using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pyramid.Resources;

namespace Pyramid.Services;

public sealed class LinuxWineInstaller
{
    private const string DpkgPath = "/usr/bin/dpkg";

    private static readonly string[][] PackageStages =
    [
        ["libc6-i386_"],
        ["lib32gcc-s1_", "lib32tinfo6_", "lib32ncurses6_", "lib32stdc++6_", "libcapi20-3_", "libz-mingw-w64_"],
        ["ia32-libs_"],
        ["wine-gecko_", "wine-mono_", "wine_"]
    ];

    public async Task InstallAsync(
        Action<string> log,
        Func<string, Task<bool>>? confirmReinstall = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        log(AppStrings.InstallerWineCheckingLog);
        var installedVersion = await TryGetWineVersionAsync(cancellationToken);
        var reinstallExisting = false;
        if (!string.IsNullOrWhiteSpace(installedVersion))
        {
            log(Format(AppStrings.InstallerWineAlreadyInstalledLog, ("Version", installedVersion)));
            reinstallExisting = confirmReinstall is not null && await confirmReinstall(installedVersion);
            if (!reinstallExisting)
            {
                log(AppStrings.InstallerWineReinstallDeclinedLog);
                return;
            }
        }

        var packagesDirectory = Path.Combine(AppContext.BaseDirectory, "Installers", "packages", "wine");
        var packageStages = ResolvePackageStages(packagesDirectory);
        log(Format(AppStrings.InstallerWinePackagesDirectoryLog, ("Directory", packagesDirectory)));

        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pyramid-wine-packages-{Guid.NewGuid():N}");
        try
        {
            log(Format(AppStrings.InstallerWineStagingPackagesLog, ("Directory", stagingDirectory)));
            await Task.Run(
                () => StagePackages(packageStages, stagingDirectory, log, cancellationToken),
                cancellationToken);
            await Task.Run(
                () => RunElevatedInstall(stagingDirectory, reinstallExisting, log, cancellationToken),
                cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }

        installedVersion = await TryGetWineVersionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            throw new InvalidOperationException(Format(
                AppStrings.InstallerWineInstallFailedWithExitCode,
                ("ExitCode", "0")));
        }

        log(Format(AppStrings.InstallerWineInstallCompletedLog, ("Version", installedVersion)));
    }

    public static void InstallFromPackages(string packagesDirectory, bool reinstallExisting, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesDirectory);
        ArgumentNullException.ThrowIfNull(log);

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        if (!File.Exists(DpkgPath))
        {
            throw new FileNotFoundException(AppStrings.InstallerWineDpkgNotFound, DpkgPath);
        }

        var stages = ResolvePackageStages(packagesDirectory);
        if (reinstallExisting)
        {
            log(AppStrings.InstallerWineRemovingLog);
            var removalResult = RunProcess(
                DpkgPath,
                ["--purge", "wine", "wine-gecko", "wine-mono"],
                log);
            if (removalResult != 0)
            {
                throw new InvalidOperationException(Format(
                    AppStrings.InstallerWineRemovalFailed,
                    ("ExitCode", removalResult.ToString())));
            }

            log(AppStrings.InstallerWineRemovalCompletedLog);
        }

        for (var index = 0; index < stages.Count; index++)
        {
            var stageNumber = index + 1;
            log(Format(
                AppStrings.InstallerWineInstallingStageLog,
                ("Stage", stageNumber.ToString()),
                ("TotalStages", stages.Count.ToString())));

            var result = RunProcess(DpkgPath, new[] { "-i" }.Concat(stages[index]).ToArray(), log);
            if (result != 0)
            {
                throw new InvalidOperationException(Format(
                    AppStrings.InstallerWineDpkgFailed,
                    ("Stage", stageNumber.ToString()),
                    ("ExitCode", result.ToString())));
            }
        }
    }

    private static IReadOnlyList<string[]> ResolvePackageStages(string packagesDirectory)
    {
        if (!Directory.Exists(packagesDirectory))
        {
            throw new DirectoryNotFoundException(Format(
                AppStrings.InstallerWinePackagesDirectoryNotFound,
                ("Directory", packagesDirectory)));
        }

        return PackageStages
            .Select(stage => stage.Select(pattern => ResolvePackage(packagesDirectory, pattern)).ToArray())
            .ToArray();
    }

    private static string ResolvePackage(string packagesDirectory, string fileNamePrefix)
    {
        var pattern = fileNamePrefix + "*.deb";
        var matches = Directory.GetFiles(packagesDirectory, pattern, SearchOption.TopDirectoryOnly);
        if (matches.Length == 0)
        {
            throw new FileNotFoundException(Format(
                AppStrings.InstallerWinePackageNotFound,
                ("Pattern", pattern)));
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(Format(
                AppStrings.InstallerWineMultiplePackagesFound,
                ("Pattern", pattern)));
        }

        return Path.GetFullPath(matches[0]);
    }

    private static void StagePackages(
        IReadOnlyList<string[]> packageStages,
        string stagingDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDirectory);
        var packages = packageStages.SelectMany(stage => stage).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        for (var index = 0; index < packages.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = packages[index];
            var destinationPath = Path.Combine(stagingDirectory, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destinationPath, overwrite: true);
            log(Format(
                AppStrings.InstallerWineStagedPackageLog,
                ("Current", (index + 1).ToString()),
                ("Total", packages.Length.ToString()),
                ("Package", Path.GetFileName(sourcePath))));
        }
    }

    private static void RunElevatedInstall(
        string packagesDirectory,
        bool reinstallExisting,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"pyramid-wine-install-{Guid.NewGuid():N}.log");
        var (fileName, arguments) = GetCurrentApplicationCommandLine(
            reinstallExisting ? "--pyramid-reinstall-wine" : "--pyramid-install-wine",
            packagesDirectory,
            logPath);

        log(AppStrings.InstallerWineInstallRequiresAdminLog);
        var exitCode = TryRunLinuxPrivilegeHelper(fileName, arguments, logPath, log, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        TryDeleteFile(logPath);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(Format(
                AppStrings.InstallerWineInstallFailedWithExitCode,
                ("ExitCode", exitCode.ToString())));
        }
    }

    private static int TryRunLinuxPrivilegeHelper(
        string fileName,
        IReadOnlyList<string> arguments,
        string maintenanceLogPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var command = string.Join(" ", new[] { fileName }.Concat(arguments).Select(ShellQuote));
        var helpers = new[]
        {
            new PrivilegeHelper("pkexec", new[] { fileName }.Concat(arguments).ToArray()),
            new PrivilegeHelper("fly-su", ["-d", "-p", "100", "-c", command])
        };

        var helperWasStarted = false;
        var loggedLineCount = 0;
        foreach (var helper in helpers)
        {
            try
            {
                loggedLineCount = 0;
                TryDeleteFile(maintenanceLogPath);
                log(Format(
                    AppStrings.InstallerWinePrivilegeCommandLog,
                    ("Tool", helper.FileName),
                    ("Command", helper.FileName + " " + string.Join(" ", helper.Arguments))));
                using var process = Process.Start(CreateStartInfo(helper.FileName, helper.Arguments));
                if (process is null)
                {
                    continue;
                }

                helperWasStarted = true;
                while (!process.WaitForExit(500))
                {
                    ReadNewLogLines(maintenanceLogPath, ref loggedLineCount, log);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                ReadNewLogLines(maintenanceLogPath, ref loggedLineCount, log);
                cancellationToken.ThrowIfCancellationRequested();

                if (process.ExitCode == 0)
                {
                    return 0;
                }

                log(Format(
                    AppStrings.InstallerWinePrivilegeHelperFailedLog,
                    ("Tool", helper.FileName),
                    ("ExitCode", process.ExitCode.ToString())));
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                log(Format(
                    AppStrings.InstallerWinePrivilegeHelperErrorLog,
                    ("Tool", helper.FileName),
                    ("Message", ex.Message)));
            }
        }

        if (!helperWasStarted)
        {
            throw new InvalidOperationException(AppStrings.InstallerWinePrivilegeToolNotFound);
        }

        return 1;
    }

    private static void ReadNewLogLines(string logPath, ref int loggedLineCount, Action<string> log)
    {
        if (!File.Exists(logPath))
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(logPath, Encoding.UTF8);
        }
        catch (IOException)
        {
            return;
        }

        foreach (var line in lines.Skip(loggedLineCount))
        {
            log(line);
        }

        loggedLineCount = lines.Length;
    }

    private static async Task<string?> TryGetWineVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wine",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");
            ProcessLanguageEnvironment.Apply(startInfo);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await standardOutput).Trim();
            var error = (await standardError).Trim();

            return process.ExitCode == 0
                ? string.IsNullOrWhiteSpace(output) ? error : output
                : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static int RunProcess(string fileName, IReadOnlyList<string> arguments, Action<string> log)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ProcessLanguageEnvironment.Apply(startInfo);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        var logLock = new object();
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                lock (logLock)
                {
                    log($"[dpkg] {eventArgs.Data}");
                }
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                lock (logLock)
                {
                    log($"[dpkg] {eventArgs.Data}");
                }
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static (string FileName, string[] Arguments) GetCurrentApplicationCommandLine(params string[] maintenanceArguments)
    {
        var localizedArguments = new[]
        {
            "--pyramid-language",
            LocalizationManager.CurrentLanguage.ToString()
        }.Concat(maintenanceArguments).ToArray();

        var entryAssemblyPath = Environment.ProcessPath;
        var dllPath = Path.Combine(AppContext.BaseDirectory, "Pyramid.dll");
        if (!string.IsNullOrWhiteSpace(entryAssemblyPath) &&
            string.Equals(Path.GetFileNameWithoutExtension(entryAssemblyPath), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(dllPath))
        {
            return (entryAssemblyPath, new[] { dllPath }.Concat(localizedArguments).ToArray());
        }

        if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return (entryAssemblyPath, localizedArguments);
        }

        throw new InvalidOperationException(AppStrings.InstallerWineCurrentExecutableNotFound);
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ProcessLanguageEnvironment.Apply(startInfo);

        return startInfo;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string Format(string template, params (string Key, string Value)[] values)
    {
        foreach (var (key, value) in values)
        {
            template = template.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        return template;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private sealed record PrivilegeHelper(string FileName, string[] Arguments);
}
