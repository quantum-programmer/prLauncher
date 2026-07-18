using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

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

    public async Task<ApplicationInstallResult> InstallAsync(Action<string> log, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => InstallCore(log, cancellationToken), cancellationToken);
    }

    private static ApplicationInstallResult InstallCore(Action<string> log, CancellationToken cancellationToken)
    {
        var sourceRoot = ResolveProductBundleRoot();
        var targetRoot = GetInstallRoot();

        log($"Папка исходной сборки приложений: {sourceRoot}");
        log($"Папка установки приложений: {targetRoot}");

        if (OperatingSystem.IsWindows())
        {
            RunElevatedWindowsInstall(sourceRoot, targetRoot, log, cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            RunElevatedLinuxInstall(sourceRoot, targetRoot, log, cancellationToken);
        }
        else
        {
            InstallFromBundle(sourceRoot, targetRoot, log, cancellationToken);
        }

        var oilCtrlCfgPath = Path.Combine(targetRoot, "OilCtrlCfg", Applications.First(item => item.TargetFolderName == "OilCtrlCfg").ExecutableName);
        return new ApplicationInstallResult(targetRoot, oilCtrlCfgPath);
    }

    public static void InstallFromBundle(
        string sourceRoot,
        string targetRoot,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetRoot);
        CopySharedSettings(sourceRoot, targetRoot, log);

        foreach (var application in Applications)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceDirectory = Path.Combine(sourceRoot, application.SourceFolderName);
            var targetDirectory = Path.Combine(targetRoot, application.TargetFolderName);

            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"Папка приложения не найдена: {sourceDirectory}");
            }

            log($"Установка {application.TargetFolderName}: {sourceDirectory} -> {targetDirectory}");
            CopyDirectory(sourceDirectory, targetDirectory, log, cancellationToken);
            DeleteLocalApplicationSettings(targetDirectory, log);
        }

        EnsureLinuxInstallTreePermissions(targetRoot, log);

        foreach (var application in Applications)
        {
            EnsureExecutablePermission(Path.Combine(targetRoot, application.TargetFolderName), application.ExecutableName, log);
        }
    }

    public Process LaunchOilCtrlCfg(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException($"OilCtrlCfg не найден: {executablePath}");
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

        throw new PlatformNotSupportedException("Установка приложений поддерживается только в Windows и Linux.");
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

        throw new DirectoryNotFoundException(
            "Не найдена папка поставочной сборки с приложениями ASNCtrl_Linux, R_Designer_L и OilCtrlCfg. Запускайте Pyramid из общей single-сборки.");
    }

    private static bool LooksLikeProductBundleRoot(string directory) =>
        Directory.Exists(Path.Combine(directory, "ASNCtrl_Linux"))
        && Directory.Exists(Path.Combine(directory, "R_Designer_L"))
        && Directory.Exists(Path.Combine(directory, "OilCtrlCfg"));

    private static void RunElevatedWindowsInstall(
        string sourceRoot,
        string targetRoot,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"pyramid-app-install-{Guid.NewGuid():N}.log");
        var (fileName, arguments) = GetCurrentApplicationCommandLine(
            "--pyramid-install-applications",
            sourceRoot,
            targetRoot,
            logPath);

        log("Копирование приложений в C:\\Prompribor. Требуются права администратора.");
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
            ?? throw new InvalidOperationException("Не удалось запустить elevated helper для установки приложений.");

        process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(logPath))
        {
            foreach (var line in File.ReadLines(logPath).TakeLast(120))
            {
                log(line);
            }
        }

        TryDeleteFile(logPath);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Установка приложений завершилась с кодом {process.ExitCode}.");
        }
    }

    private static void RunElevatedLinuxInstall(
        string sourceRoot,
        string targetRoot,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"pyramid-app-install-{Guid.NewGuid():N}.log");
        var (fileName, arguments) = GetCurrentApplicationCommandLine(
            "--pyramid-install-applications",
            sourceRoot,
            targetRoot,
            logPath);

        log("Копирование приложений в /opt/prompribor. Требуются права администратора.");

        var exitCode = TryRunLinuxPrivilegeHelper(fileName, arguments, log, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(logPath))
        {
            foreach (var line in File.ReadLines(logPath).TakeLast(120))
            {
                log(line);
            }
        }

        TryDeleteFile(logPath);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Установка приложений завершилась с кодом {exitCode}.");
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
            new LinuxPrivilegeHelper("fly-su", ["-d", "-c", command]),
            new LinuxPrivilegeHelper("pkexec", new[] { fileName }.Concat(arguments).ToArray())
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
        var entryAssemblyPath = Environment.ProcessPath;
        var dllPath = Path.Combine(AppContext.BaseDirectory, "Pyramid.dll");

        if (!string.IsNullOrWhiteSpace(entryAssemblyPath) &&
            string.Equals(Path.GetFileNameWithoutExtension(entryAssemblyPath), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(dllPath))
        {
            return (entryAssemblyPath, new[] { dllPath }.Concat(maintenanceArguments).ToArray());
        }

        if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return (entryAssemblyPath, maintenanceArguments);
        }

        throw new InvalidOperationException("Путь текущего приложения не найден.");
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

    private static void CopySharedSettings(string sourceRoot, string targetRoot, Action<string> log)
    {
        var sourceSettingsPath = Path.Combine(sourceRoot, "appsettings.json");
        if (!File.Exists(sourceSettingsPath))
        {
            log("Общий appsettings.json в поставочной сборке не найден. Pyramid создаст/обновит его после настройки PostgreSQL.");
            return;
        }

        var targetSettingsPath = Path.Combine(targetRoot, "appsettings.json");
        File.Copy(sourceSettingsPath, targetSettingsPath, overwrite: true);
        NormalizeSharedSettings(targetSettingsPath);
        log($"Общий appsettings.json скопирован: {targetSettingsPath}");
    }

    private static void NormalizeSharedSettings(string settingsPath)
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
        database["Port"] = database["Port"]?.GetValue<string>() is { Length: > 0 } port ? port : NativePostgresInstaller.PreferredServerPort.ToString();

        File.WriteAllText(
            settingsPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
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
        log($"Локальный appsettings.json удален из папки приложения: {localSettingsPath}");
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
            log($"Для исполняемого файла выставлены права запуска: {executablePath}");
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

        log($"Для папки установки выставлены права чтения: {targetRoot}");
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
                log($"Скопировано файлов для {Path.GetFileName(targetDirectory)}: {copiedFiles}...");
            }
        }

        log($"Установка {Path.GetFileName(targetDirectory)} завершена. Скопировано файлов: {copiedFiles}.");
    }

    private sealed record ApplicationInstallItem(string SourceFolderName, string TargetFolderName, string ExecutableName);

    private sealed record LinuxPrivilegeHelper(string FileName, IReadOnlyList<string> Arguments);
}

public sealed record ApplicationInstallResult(string InstallRoot, string OilCtrlCfgPath);
