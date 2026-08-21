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

public sealed class LinuxCupsClientInstaller
{
    private const string DpkgPath = "/usr/bin/dpkg";
    private static readonly string[] RequiredPackagePrefixes = ["libcups2_", "cups-common_", "cups-client_"];

    public async Task EnsureInstalledAsync(Action<string> log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (!OperatingSystem.IsLinux())
            return;

        log(AppStrings.InstallerCupsCheckingLog);
        if (IsInstalled())
        {
            log(AppStrings.InstallerCupsAlreadyInstalledLog);
            return;
        }

        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "Installers", "packages", "cups");
        var packages = ResolvePackages(sourceDirectory);
        log(Format(AppStrings.InstallerCupsPackagesDirectoryLog, ("Directory", sourceDirectory)));

        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"pyramid-cups-packages-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(package, Path.Combine(stagingDirectory, Path.GetFileName(package)), overwrite: true);
            }

            await Task.Run(
                () => RunElevatedInstall(stagingDirectory, log, cancellationToken),
                cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }

        if (!IsInstalled())
            throw new InvalidOperationException(AppStrings.InstallerCupsVerificationFailed);

        log(AppStrings.InstallerCupsInstallCompletedLog);
    }

    public static void InstallFromPackages(string packagesDirectory, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesDirectory);
        ArgumentNullException.ThrowIfNull(log);

        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException();
        if (!File.Exists(DpkgPath))
            throw new FileNotFoundException(AppStrings.InstallerWineDpkgNotFound, DpkgPath);

        var packages = ResolvePackages(packagesDirectory);
        log(AppStrings.InstallerCupsInstallingLog);
        var exitCode = RunProcess(DpkgPath, new[] { "-i" }.Concat(packages).ToArray(), log);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(Format(
                AppStrings.InstallerCupsDpkgFailed,
                ("ExitCode", exitCode.ToString())));
        }
    }

    private static bool IsInstalled() => File.Exists("/usr/bin/lp") && File.Exists("/usr/bin/lpstat");

    private static string[] ResolvePackages(string packagesDirectory)
    {
        if (!Directory.Exists(packagesDirectory))
        {
            throw new DirectoryNotFoundException(Format(
                AppStrings.InstallerCupsPackagesDirectoryNotFound,
                ("Directory", packagesDirectory)));
        }

        return RequiredPackagePrefixes.Select(prefix =>
        {
            var matches = Directory.GetFiles(packagesDirectory, prefix + "*.deb", SearchOption.TopDirectoryOnly);
            if (matches.Length != 1)
            {
                throw new FileNotFoundException(Format(
                    AppStrings.InstallerCupsPackageSetInvalid,
                    ("Pattern", prefix + "*.deb")));
            }

            return Path.GetFullPath(matches[0]);
        }).ToArray();
    }

    private static void RunElevatedInstall(
        string packagesDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var maintenanceLogPath = Path.Combine(Path.GetTempPath(), $"pyramid-cups-install-{Guid.NewGuid():N}.log");
        var (fileName, arguments) = GetCurrentApplicationCommandLine(
            "--pyramid-install-cups-client",
            packagesDirectory,
            maintenanceLogPath);

        log(AppStrings.InstallerCupsInstallRequiresAdminLog);
        var exitCode = TryRunPrivilegeHelper(fileName, arguments, maintenanceLogPath, log, cancellationToken);
        TryDeleteFile(maintenanceLogPath);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(Format(
                AppStrings.InstallerCupsInstallFailed,
                ("ExitCode", exitCode.ToString())));
        }
    }

    private static int TryRunPrivilegeHelper(
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
        foreach (var helper in helpers)
        {
            var loggedLineCount = 0;
            try
            {
                TryDeleteFile(maintenanceLogPath);
                log(Format(
                    AppStrings.InstallerWinePrivilegeCommandLog,
                    ("Tool", helper.FileName),
                    ("Command", helper.FileName + " " + string.Join(" ", helper.Arguments))));

                using var process = Process.Start(CreateStartInfo(helper.FileName, helper.Arguments));
                if (process is null)
                    continue;

                helperWasStarted = true;
                while (!process.WaitForExit(500))
                {
                    ReadNewLogLines(maintenanceLogPath, ref loggedLineCount, log);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                ReadNewLogLines(maintenanceLogPath, ref loggedLineCount, log);
                if (process.ExitCode == 0)
                    return 0;

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
            throw new InvalidOperationException(AppStrings.InstallerWinePrivilegeToolNotFound);

        return 1;
    }

    private static int RunProcess(string fileName, IReadOnlyList<string> arguments, Action<string> log)
    {
        var startInfo = CreateStartInfo(fileName, arguments);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var logLock = new object();
        DataReceivedEventHandler handler = (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
                return;
            lock (logLock)
                log($"[dpkg] {eventArgs.Data}");
        };
        process.OutputDataReceived += handler;
        process.ErrorDataReceived += handler;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        ProcessLanguageEnvironment.Apply(startInfo);
        return startInfo;
    }

    private static (string FileName, string[] Arguments) GetCurrentApplicationCommandLine(params string[] arguments)
    {
        var localizedArguments = new[]
        {
            "--pyramid-language",
            LocalizationManager.CurrentLanguage.ToString()
        }.Concat(arguments).ToArray();
        var processPath = Environment.ProcessPath;
        var dllPath = Path.Combine(AppContext.BaseDirectory, "Pyramid.dll");

        if (!string.IsNullOrWhiteSpace(processPath) &&
            string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(dllPath))
        {
            return (processPath, new[] { dllPath }.Concat(localizedArguments).ToArray());
        }

        if (!string.IsNullOrWhiteSpace(processPath))
            return (processPath, localizedArguments);

        throw new InvalidOperationException(AppStrings.InstallerWineCurrentExecutableNotFound);
    }

    private static void ReadNewLogLines(string path, ref int lineCount, Action<string> log)
    {
        if (!File.Exists(path))
            return;
        try
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            foreach (var line in lines.Skip(lineCount))
                log(line);
            lineCount = lines.Length;
        }
        catch (IOException)
        {
        }
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string Format(string template, params (string Key, string Value)[] values)
    {
        foreach (var (key, value) in values)
            template = template.Replace("{" + key + "}", value, StringComparison.Ordinal);
        return template;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed record PrivilegeHelper(string FileName, string[] Arguments);
}
