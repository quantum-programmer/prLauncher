using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pyramid.Resources;

namespace Pyramid.Services;

public sealed class ProductApplicationInstaller
{
    private const string WindowsInstallRoot = @"C:\Prompribor";
    private const string LinuxInstallRoot = "/opt/prompribor";
    private const string PostgresCredentialId = "OilCtrl.PostgreSQL.Postgres";

    private static readonly ApplicationInstallItem[] Applications =
    [
        new("ASNCtrl_Linux", "AsnCtrl", OperatingSystem.IsWindows() ? "ARM.exe" : "ARM"),
        new("R_Designer_L", "RDesigner", OperatingSystem.IsWindows() ? "RDesigner.exe" : "RDesigner"),
        new("OilCtrlCfg", "OilCtrlCfg", OperatingSystem.IsWindows() ? "OilCtrlCfg.exe" : "OilCtrlCfg")
    ];

    public async Task<ApplicationInstallResult> InstallAsync(int postgresPort, bool reinstallExisting, Action<string> log, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => InstallCore(postgresPort, reinstallExisting, log, cancellationToken), cancellationToken);
    }

    public bool IsArmInstalled() => IsArmInstalled(GetInstallRoot());

    public string GetArmInstallRoot() => GetInstallRoot();

    private static ApplicationInstallResult InstallCore(int postgresPort, bool reinstallExisting, Action<string> log, CancellationToken cancellationToken)
    {
        var sourceRoot = ResolveProductBundleRoot();
        var targetRoot = GetInstallRoot();

        log(Format(AppStrings.InstallerProductSourceBundleRootLog, ("Directory", sourceRoot)));
        log(Format(AppStrings.InstallerProductInstallRootLog, ("Directory", targetRoot)));

        if (OperatingSystem.IsWindows())
        {
            RunElevatedWindowsInstall(sourceRoot, targetRoot, postgresPort, reinstallExisting, log, cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            RunElevatedLinuxInstall(sourceRoot, targetRoot, postgresPort, reinstallExisting, log, cancellationToken);
        }
        else
        {
            InstallFromBundle(sourceRoot, targetRoot, postgresPort, reinstallExisting, log, cancellationToken);
        }

        var oilCtrlCfgPath = Path.Combine(targetRoot, "OilCtrlCfg", Applications.First(item => item.TargetFolderName == "OilCtrlCfg").ExecutableName);
        return new ApplicationInstallResult(targetRoot, oilCtrlCfgPath);
    }

    public static void InstallFromBundle(
        string sourceRoot,
        string targetRoot,
        int postgresPort,
        bool reinstallExisting,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        if (reinstallExisting && Directory.Exists(targetRoot))
        {
            log(Format(AppStrings.InstallerProductRemovingPreviousInstallLog, ("Directory", targetRoot)));
            RemoveManagedApplicationFiles(targetRoot, log, cancellationToken);
        }

        Directory.CreateDirectory(targetRoot);
        CopySharedSettings(sourceRoot, targetRoot, postgresPort, log);

        foreach (var application in Applications)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceDirectory = Path.Combine(sourceRoot, application.SourceFolderName);
            var targetDirectory = Path.Combine(targetRoot, application.TargetFolderName);

            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException(Format(AppStrings.InstallerProductApplicationFolderNotFound, ("Directory", sourceDirectory)));
            }

            log(Format(
                AppStrings.InstallerProductInstallingApplicationLog,
                ("Application", application.TargetFolderName),
                ("Source", sourceDirectory),
                ("Target", targetDirectory)));
            CopyDirectory(sourceDirectory, targetDirectory, log, cancellationToken);
            DeleteLocalApplicationSettings(targetDirectory, log);
        }

        EnsureLinuxInstallTreePermissions(targetRoot, log);

        foreach (var application in Applications)
        {
            EnsureExecutablePermission(Path.Combine(targetRoot, application.TargetFolderName), application.ExecutableName, log);
        }
    }

    private static void RemoveManagedApplicationFiles(
        string targetRoot,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        foreach (var application in Applications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var applicationDirectory = Path.Combine(targetRoot, application.TargetFolderName);
            if (!Directory.Exists(applicationDirectory))
            {
                continue;
            }

            log(Format(
                AppStrings.InstallerProductRemovingApplicationLog,
                ("Application", application.TargetFolderName),
                ("Directory", applicationDirectory)));
            Directory.Delete(applicationDirectory, recursive: true);
        }

        var sharedSettingsPath = Path.Combine(targetRoot, "appsettings.json");
        if (File.Exists(sharedSettingsPath))
        {
            File.Delete(sharedSettingsPath);
        }
    }

    public Process LaunchOilCtrlCfg(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(Format(AppStrings.InstallerProductOilCtrlCfgNotFound, ("Path", executablePath)));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = OperatingSystem.IsWindows()
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("OilCtrlCfg process was not started.");
    }

    private static string GetInstallRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsInstallRoot;
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxInstallRoot;
        }

        throw new PlatformNotSupportedException(AppStrings.InstallerProductPlatformNotSupported);
    }

    private static bool IsArmInstalled(string targetRoot)
    {
        if (!Directory.Exists(targetRoot))
        {
            return false;
        }

        return File.Exists(Path.Combine(targetRoot, "appsettings.json"))
            || Applications.Any(application =>
                Directory.Exists(Path.Combine(targetRoot, application.TargetFolderName))
                || File.Exists(Path.Combine(targetRoot, application.TargetFolderName, application.ExecutableName)));
    }

    private static string ResolveProductBundleRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var runtimeFolder = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        var repositoryRoot = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", ".."));
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDirectory, "..")),
            baseDirectory,
            Path.Combine(repositoryRoot, "publish", "single", runtimeFolder, "self-contained debug single"),
            Path.Combine(repositoryRoot, "publish", "single", runtimeFolder, "self-contained release single")
        };

        foreach (var candidate in candidates)
        {
            if (LooksLikeProductBundleRoot(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(AppStrings.InstallerProductBundleRootNotFound);
    }

    private static bool LooksLikeProductBundleRoot(string directory) =>
        Directory.Exists(Path.Combine(directory, "ASNCtrl_Linux"))
        && Directory.Exists(Path.Combine(directory, "R_Designer_L"))
        && Directory.Exists(Path.Combine(directory, "OilCtrlCfg"));

    private static void RunElevatedWindowsInstall(
        string sourceRoot,
        string targetRoot,
        int postgresPort,
        bool reinstallExisting,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"pyramid-app-install-{Guid.NewGuid():N}.log");
        var (fileName, arguments) = GetCurrentApplicationCommandLine(
            "--pyramid-install-applications",
            sourceRoot,
            targetRoot,
            postgresPort.ToString(),
            reinstallExisting ? "true" : "false",
            logPath);

        log(AppStrings.InstallerProductWindowsCopyRequiresAdminLog);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(AppStrings.InstallerProductElevatedHelperStartFailed);

        process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(logPath))
        {
            foreach (var line in File.ReadLines(logPath, Encoding.UTF8).TakeLast(120))
            {
                log(line);
            }
        }

        TryDeleteFile(logPath);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(Format(AppStrings.InstallerProductInstallFailedWithExitCode, ("ExitCode", process.ExitCode.ToString())));
        }
    }

    private static void RunElevatedLinuxInstall(
        string sourceRoot,
        string targetRoot,
        int postgresPort,
        bool reinstallExisting,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"pyramid-app-install-{Guid.NewGuid():N}.log");
        var (fileName, arguments) = GetCurrentApplicationCommandLine(
            "--pyramid-install-applications",
            sourceRoot,
            targetRoot,
            postgresPort.ToString(),
            reinstallExisting ? "true" : "false",
            logPath);

        log(AppStrings.InstallerProductLinuxCopyRequiresAdminLog);

        var exitCode = TryRunLinuxPrivilegeHelper(fileName, arguments, log, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(logPath))
        {
            foreach (var line in File.ReadLines(logPath, Encoding.UTF8).TakeLast(120))
            {
                log(line);
            }
        }

        TryDeleteFile(logPath);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(Format(AppStrings.InstallerProductInstallFailedWithExitCode, ("ExitCode", exitCode.ToString())));
        }
    }

    private static int TryRunLinuxPrivilegeHelper(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var command = string.Join(" ", new[] { fileName }.Concat(arguments).Select(ShellQuote));
        var helpers = new[]
        {
            new LinuxPrivilegeHelper("pkexec", new[] { fileName }.Concat(arguments).ToArray()),
            new LinuxPrivilegeHelper("fly-su", ["-d", "-p", "100", "-c", command])
        };

        foreach (var helper in helpers)
        {
            try
            {
                log($"[{helper.FileName}] > {helper.FileName} {string.Join(" ", helper.Arguments)}");
                using var process = Process.Start(CreateStartInfo(helper.FileName, helper.Arguments));
                if (process is null)
                {
                    continue;
                }

                process.WaitForExit();
                cancellationToken.ThrowIfCancellationRequested();

                if (process.ExitCode == 0)
                {
                    return 0;
                }

                log($"Privilege elevation helper {helper.FileName} failed with exit code {process.ExitCode}. Trying next helper.");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                log($"Privilege elevation helper {helper.FileName} failed: {ex.Message}");
            }
        }

        return 1;
    }

    private static (string FileName, string[] Arguments) GetCurrentApplicationCommandLine(params string[] maintenanceArguments)
    {
        var localizedMaintenanceArguments = new[]
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
            return (entryAssemblyPath, new[] { dllPath }.Concat(localizedMaintenanceArguments).ToArray());
        }

        if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return (entryAssemblyPath, localizedMaintenanceArguments);
        }

        throw new InvalidOperationException(AppStrings.InstallerProductCurrentExecutableNotFound);
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

        return startInfo;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

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

    private static void CopySharedSettings(string sourceRoot, string targetRoot, int postgresPort, Action<string> log)
    {
        var sourceSettingsPath = Path.Combine(sourceRoot, "appsettings.json");
        if (!File.Exists(sourceSettingsPath))
        {
            log(AppStrings.InstallerProductSharedSettingsMissingLog);
            return;
        }

        var targetSettingsPath = Path.Combine(targetRoot, "appsettings.json");
        File.Copy(sourceSettingsPath, targetSettingsPath, overwrite: true);
        NormalizeSharedSettings(targetSettingsPath, postgresPort);
        log(Format(AppStrings.InstallerProductSharedSettingsCopiedLog, ("Path", targetSettingsPath)));
    }

    private static void NormalizeSharedSettings(string settingsPath, int postgresPort)
    {
        JsonObject root;
        if (File.Exists(settingsPath))
        {
            root = JsonNode.Parse(File.ReadAllText(settingsPath, Encoding.UTF8)) as JsonObject
                ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (root["Database"] is not JsonObject database)
        {
            database = new JsonObject();
            root["Database"] = database;
        }

        database["Host"] = database["Host"]?.GetValue<string>() is { Length: > 0 } host ? host : "localhost";
        database["Username"] = "postgres";
        database["Password"] = string.Empty;
        database["CredentialID"] = PostgresCredentialId;
        database["DBName"] = database["DBName"]?.GetValue<string>() is { Length: > 0 } dbName ? dbName : "OilCtrl";
        database["Port"] = postgresPort.ToString();

        ConfigurationFileSafety.WriteAllTextAtomic(
            settingsPath,
            root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }),
            new UTF8Encoding(false));
    }

    private static void DeleteLocalApplicationSettings(string targetDirectory, Action<string> log)
    {
        var localSettingsPath = Path.Combine(targetDirectory, "appsettings.json");
        if (!File.Exists(localSettingsPath))
        {
            return;
        }

        File.Delete(localSettingsPath);
        log(Format(AppStrings.InstallerProductLocalSettingsDeletedLog, ("Path", localSettingsPath)));
    }

    private static void EnsureExecutablePermission(string targetDirectory, string executableName, Action<string> log)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var executablePath = Path.Combine(targetDirectory, executableName);
        if (!File.Exists(executablePath))
        {
            return;
        }

        using var process = Process.Start(CreateStartInfo("chmod", ["+x", executablePath]));
        process?.WaitForExit();

        if (process?.ExitCode == 0)
        {
            log(Format(AppStrings.InstallerProductExecutablePermissionSetLog, ("Path", executablePath)));
        }
    }

    private static void EnsureLinuxInstallTreePermissions(string targetRoot, Action<string> log)
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists(targetRoot))
        {
            return;
        }

        const UnixFileMode directoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        const UnixFileMode fileMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead |
            UnixFileMode.OtherRead;

        File.SetUnixFileMode(targetRoot, directoryMode);

        foreach (var directory in Directory.EnumerateDirectories(targetRoot, "*", SearchOption.AllDirectories))
        {
            File.SetUnixFileMode(directory, directoryMode);
        }

        foreach (var file in Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories))
        {
            File.SetUnixFileMode(file, fileMode);
        }

        log(Format(AppStrings.InstallerProductInstallTreePermissionsSetLog, ("Directory", targetRoot)));
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        var copiedFiles = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);

            copiedFiles++;
            if (copiedFiles % 100 == 0)
            {
                log(Format(
                    AppStrings.InstallerProductCopiedFilesProgressLog,
                    ("Application", Path.GetFileName(targetDirectory)),
                    ("Count", copiedFiles.ToString())));
            }
        }

        log(Format(
            AppStrings.InstallerProductInstallCompletedLog,
            ("Application", Path.GetFileName(targetDirectory)),
            ("Count", copiedFiles.ToString())));
    }

    private static string Format(string template, params (string Name, string Value)[] values)
    {
        foreach (var (name, value) in values)
        {
            template = template.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        return template;
    }

    private sealed record ApplicationInstallItem(string SourceFolderName, string TargetFolderName, string ExecutableName);

    private sealed record LinuxPrivilegeHelper(string FileName, IReadOnlyList<string> Arguments);
}

public sealed record ApplicationInstallResult(string InstallRoot, string OilCtrlCfgPath);
