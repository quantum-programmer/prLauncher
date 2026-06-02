using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using RDesigner.Resources;

namespace RDesigner.Services;

public sealed class NativePostgresInstaller
{
    public const string RequiredVersion = "16.14";
    public const string DatabaseName = "OilCtrl";
    public const string SuperUser = "postgres";
    public const string SuperPassword = "j06gOuqDHwWkvpWf";
    public const int PreferredServerPort = 5433;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly string initSqlPath = Path.Combine(AppContext.BaseDirectory, "Installers", "oilctrl-init.sql");

    public async Task InstallAsync(
        string? windowsInstallDirectory,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        log(Format(AppStrings.InstallerOsLog, ("Description", RuntimeInformation.OSDescription)));
        log(Format(AppStrings.InstallerTargetPostgresVersionLog, ("Version", RequiredVersion)));

        if (OperatingSystem.IsWindows())
        {
            await InstallOnWindowsAsync(windowsInstallDirectory, log, cancellationToken);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await InstallOnLinuxAsync(log, cancellationToken);
            return;
        }

        throw new PlatformNotSupportedException(AppStrings.InstallerPlatformNotSupported);
    }

    public async Task RunInteractiveWindowsInstallerAsync(
        string? windowsInstallDirectory,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(AppStrings.InstallerInteractiveWindowsOnly);
        }

        log(Format(AppStrings.InstallerOsLog, ("Description", RuntimeInformation.OSDescription)));
        log(Format(AppStrings.InstallerTargetPostgresVersionLog, ("Version", RequiredVersion)));

        var installDir = NormalizeWindowsInstallDirectory(windowsInstallDirectory);
        var port = SelectFreeTcpPort(log);
        var dataDir = Path.Combine(installDir, "data");
        var installer = FindWindowsInstaller();
        if (installer is null)
        {
            throw new FileNotFoundException(
                AppStrings.InstallerExeNotFound);
        }

        await LogWindowsPostgresInstancesAsync(log, cancellationToken);

        log(Format(AppStrings.InstallerInstallerPathLog, ("Path", installer)));
        log(AppStrings.InstallerStartingGuiLog);
        log(AppStrings.InstallerWizardValuesLog);
        log(Format(AppStrings.InstallerWizardInstallDirLog, ("Directory", installDir)));
        log(Format(AppStrings.InstallerWizardDataDirLog, ("Directory", dataDir)));
        log(Format(AppStrings.InstallerWizardPortLog, ("Port", port.ToString())));
        log(Format(AppStrings.InstallerWizardSuperPasswordLog, ("Password", SuperPassword)));
        log(AppStrings.InstallerWizardComponentsLog);
        log(AppStrings.InstallerWizardStackBuilderLog);
        log(AppStrings.InstallerWizardPgAdminLog);

        await RunProcessAsync(
            installer,
            Array.Empty<string>(),
            log,
            cancellationToken,
            runAsAdmin: true);
    }

    public async Task CheckAsync(
        string? windowsInstallDirectory,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var port = await ResolveWindowsPortAsync(windowsInstallDirectory, log, cancellationToken);
        var psqlPath = await FindPsqlAsync(windowsInstallDirectory, cancellationToken);
        if (psqlPath is null)
        {
            log(AppStrings.InstallerPsqlNotFoundLog);
            return;
        }

        log(Format(AppStrings.InstallerPsqlPathLog, ("Path", psqlPath)));
        log(Format(AppStrings.InstallerPortLog, ("Port", port.ToString())));
        await RunProcessAsync(
            psqlPath,
            new[] { "-h", "localhost", "-p", port.ToString(), "-U", SuperUser, "-d", DatabaseName, "-c", "select version();" },
            log,
            cancellationToken,
            new Dictionary<string, string> { ["PGPASSWORD"] = SuperPassword });
    }

    private async Task InstallOnWindowsAsync(
        string? windowsInstallDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var installDir = NormalizeWindowsInstallDirectory(windowsInstallDirectory);
        var psqlPath = await FindPsqlAsync(installDir, cancellationToken);

        if (psqlPath is null)
        {
            await LogWindowsPostgresInstancesAsync(log, cancellationToken);
            var port = await SelectFreeWindowsPostgresPortAsync(log, cancellationToken);
            var dataDir = Path.Combine(installDir, "data");
            var binariesArchive = FindWindowsBinariesArchive();
            if (binariesArchive is null)
            {
                throw new FileNotFoundException(
                    AppStrings.InstallerBinariesArchiveNotFound);
            }

            log(Format(AppStrings.InstallerBinariesArchiveLog, ("Path", binariesArchive)));
            log(AppStrings.InstallerStartingSilentBinariesLog);
            var serviceName = $"postgresql-x64-16-oilctrl-{port}";

            if (Directory.Exists(installDir) && Directory.EnumerateFileSystemEntries(installDir).Any())
            {
                throw new InvalidOperationException(
                    Format(AppStrings.InstallerDirectoryNotEmpty, ("Directory", installDir)));
            }

            log(Format(AppStrings.InstallerInstallDirLog, ("Directory", installDir)));
            log(Format(AppStrings.InstallerDataDirLog, ("Directory", dataDir)));
            log(Format(AppStrings.InstallerSelectedPostgresPortLog, ("Port", port.ToString())));
            log(Format(AppStrings.InstallerWindowsServiceLog, ("ServiceName", serviceName)));
            log(AppStrings.InstallerComponentsSkippedLog);

            ExtractWindowsBinaries(binariesArchive, installDir, log);

            var binDir = Path.Combine(installDir, "bin");
            psqlPath = Path.Combine(binDir, "psql.exe");
            var initDbPath = Path.Combine(binDir, "initdb.exe");
            var pgCtlPath = Path.Combine(binDir, "pg_ctl.exe");

            if (!File.Exists(psqlPath) || !File.Exists(initDbPath) || !File.Exists(pgCtlPath))
            {
                throw new FileNotFoundException(AppStrings.InstallerRequiredExecutablesNotFound);
            }

            await InitializeWindowsDataDirectoryAsync(initDbPath, dataDir, log, cancellationToken);
            ConfigureWindowsPostgresDataDirectory(dataDir, port, log);
            await RegisterAndStartWindowsServiceAsync(pgCtlPath, serviceName, installDir, dataDir, log, cancellationToken);
        }
        else
        {
            log(Format(AppStrings.InstallerClientAlreadyExistsLog, ("Path", psqlPath)));
            var existingPort = TryReadPostgresPort(installDir);
            if (existingPort is not null)
            {
                ConfigureWindowsPostgresDataDirectory(Path.Combine(installDir, "data"), existingPort.Value, log);
            }

            var existingInstallation = await FindWindowsPostgresInstallationForDirectoryAsync(installDir, cancellationToken);
            if (existingInstallation is not null)
            {
                await EnsureWindowsServiceRunningAsync(existingInstallation.ServiceName, log, cancellationToken);
            }
            else
            {
                log(AppStrings.InstallerMatchingWindowsServiceNotFoundLog);
            }
        }

        psqlPath = await FindPsqlAsync(installDir, cancellationToken)
            ?? throw new InvalidOperationException(AppStrings.InstallerPsqlNotFoundLog);

        var installedPort = await ResolveWindowsPortAsync(installDir, log, cancellationToken);
        await InitializeDatabaseAsync(psqlPath, installedPort, log, cancellationToken);
    }

    private async Task InstallOnLinuxAsync(Action<string> log, CancellationToken cancellationToken)
    {
        var psqlPath = await FindPsqlAsync(null, cancellationToken);
        if (psqlPath is null)
        {
            log(AppStrings.InstallerLinuxPsqlNotFoundLog);
            log(AppStrings.InstallerLinuxNativeInstallInfoLog);
            log(AppStrings.InstallerLinuxRetryLog);
            return;
        }

        log(Format(AppStrings.InstallerClientFoundLog, ("Path", psqlPath)));
        await InitializeDatabaseAsync(psqlPath, PreferredServerPort, log, cancellationToken);
    }

    private async Task InitializeDatabaseAsync(string psqlPath, int port, Action<string> log, CancellationToken cancellationToken)
    {
        EnsureInitSqlExists();

        log(Format(AppStrings.InstallerUsingPostgresPortLog, ("Port", port.ToString())));
        log(Format(AppStrings.InstallerCheckingDatabaseLog, ("Database", DatabaseName)));
        var databaseExists = await QueryScalarAsync(
            psqlPath,
            port,
            "postgres",
            $"select 1 from pg_database where datname = '{DatabaseName}';",
            cancellationToken);

        if (databaseExists.Trim() != "1")
        {
            log(Format(AppStrings.InstallerCreatingDatabaseLog, ("Database", DatabaseName)));
            await RunProcessAsync(
                psqlPath,
                new[] { "-h", "localhost", "-p", port.ToString(), "-U", SuperUser, "-d", "postgres", "-c", $"CREATE DATABASE \"{DatabaseName}\";" },
                log,
                cancellationToken,
                new Dictionary<string, string> { ["PGPASSWORD"] = SuperPassword });
        }
        else
        {
            log(Format(AppStrings.InstallerDatabaseExistsLog, ("Database", DatabaseName)));
        }

        log(AppStrings.InstallerApplyingInitSqlLog);
        await RunProcessAsync(
            psqlPath,
            new[] { "-h", "localhost", "-p", port.ToString(), "-U", SuperUser, "-d", DatabaseName, "-f", initSqlPath },
            log,
            cancellationToken,
            new Dictionary<string, string> { ["PGPASSWORD"] = SuperPassword });
    }

    private static string? FindWindowsInstaller()
    {
        var installersDir = Path.Combine(AppContext.BaseDirectory, "Installers");
        if (!Directory.Exists(installersDir))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(installersDir, "postgresql-16.14-*-windows-x64.exe", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? FindWindowsBinariesArchive()
    {
        var installersDir = Path.Combine(AppContext.BaseDirectory, "Installers");
        if (!Directory.Exists(installersDir))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(installersDir, "postgresql-16.14-*-windows-x64-binaries.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void ExtractWindowsBinaries(string archivePath, string installDir, Action<string> log)
    {
        Directory.CreateDirectory(installDir);
        log(AppStrings.InstallerExtractingBinariesLog);

        using var archive = ZipFile.OpenRead(archivePath);
        var extracted = 0;
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("pgsql/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = entry.FullName["pgsql/".Length..].Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relativePath) || ShouldSkipWindowsBinaryEntry(relativePath))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(installDir, relativePath));
            var installRoot = Path.GetFullPath(installDir);
            if (!destinationPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Format(AppStrings.InstallerUnsafeArchiveEntry, ("Entry", entry.FullName)));
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
            extracted++;

            if (extracted % 100 == 0)
            {
                log(Format(AppStrings.InstallerExtractedFilesLog, ("Count", extracted.ToString())));
            }
        }

        log(Format(AppStrings.InstallerExtractionCompletedLog, ("Count", extracted.ToString())));
    }

    private static bool ShouldSkipWindowsBinaryEntry(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith("pgAdmin 4/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("StackBuilder/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("bin/stackbuilder.exe", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("StackBuilder", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("pgAdmin", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task InitializeWindowsDataDirectoryAsync(
        string initDbPath,
        string dataDir,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(dataDir) && Directory.EnumerateFileSystemEntries(dataDir).Any())
        {
            log(Format(AppStrings.InstallerDataDirectoryExistsLog, ("Directory", dataDir)));
            return;
        }

        Directory.CreateDirectory(dataDir);
        var passwordFile = Path.Combine(Path.GetTempPath(), $"oilctrl-postgres-password-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(passwordFile, SuperPassword, Utf8NoBom, cancellationToken);

        try
        {
            log(AppStrings.InstallerInitDbLog);
            await RunProcessAsync(
                initDbPath,
                new[] { "-D", dataDir, "-U", SuperUser, "--pwfile", passwordFile, "-A", "scram-sha-256", "-E", "UTF8" },
                log,
                cancellationToken);
        }
        finally
        {
            TryDeleteFile(passwordFile);
        }
    }

    private static void ConfigureWindowsPostgresDataDirectory(string dataDir, int port, Action<string> log)
    {
        var configPath = Path.Combine(dataDir, "postgresql.conf");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(Format(AppStrings.InstallerConfigNotFound, ("Path", configPath)));
        }

        log(AppStrings.InstallerConfiguringPostgresLog);
        var config = File.ReadAllText(configPath, Encoding.UTF8);
        config = Regex.Replace(config, @"(?m)^\s*#?\s*port\s*=.*$", $"port = {port}");
        config = Regex.Replace(config, @"(?m)^\s*#?\s*listen_addresses\s*=.*$", "listen_addresses = 'localhost'");
        File.WriteAllText(configPath, config, Utf8NoBom);
    }

    private static async Task RegisterAndStartWindowsServiceAsync(
        string pgCtlPath,
        string serviceName,
        string installDir,
        string dataDir,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log(AppStrings.InstallerRegisterServiceLog);
        var elevatedLogPath = Path.Combine(AppContext.BaseDirectory, "logs", "postgresql-service-install.log");
        Directory.CreateDirectory(Path.GetDirectoryName(elevatedLogPath)!);

        var scriptPath = Path.Combine(Path.GetTempPath(), $"oilctrl-postgres-service-{Guid.NewGuid():N}.ps1");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $log = '{{EscapePowerShellSingleQuotedString(elevatedLogPath)}}'
            "Registering service {{serviceName}}" | Out-File -FilePath $log -Encoding utf8
            "Granting NetworkService permissions" | Out-File -FilePath $log -Encoding utf8 -Append
            $icaclsOutput = & icacls '{{EscapePowerShellSingleQuotedString(installDir)}}' /grant 'NT AUTHORITY\NetworkService:(OI)(CI)F' /T /Q 2>&1
            $icaclsExitCode = $LASTEXITCODE
            $icaclsOutput | Out-File -FilePath $log -Encoding utf8 -Append
            if ($icaclsExitCode -ne 0) { throw "icacls failed with exit code $icaclsExitCode" }
            $registerOutput = & '{{EscapePowerShellSingleQuotedString(pgCtlPath)}}' register -N '{{EscapePowerShellSingleQuotedString(serviceName)}}' -D '{{EscapePowerShellSingleQuotedString(dataDir)}}' -S auto -U 'NT AUTHORITY\NetworkService' 2>&1
            $registerExitCode = $LASTEXITCODE
            $registerOutput | Out-File -FilePath $log -Encoding utf8 -Append
            if ($registerExitCode -ne 0) { throw "pg_ctl register failed with exit code $registerExitCode" }
            "Starting service {{serviceName}}" | Out-File -FilePath $log -Encoding utf8 -Append
            Start-Service -Name '{{EscapePowerShellSingleQuotedString(serviceName)}}'
            $service = Get-Service -Name '{{EscapePowerShellSingleQuotedString(serviceName)}}'
            $service.WaitForStatus('Running', '00:00:30')
            "Service status: $($service.Status)" | Out-File -FilePath $log -Encoding utf8 -Append
            "Service {{serviceName}} started" | Out-File -FilePath $log -Encoding utf8 -Append
            """;

        await File.WriteAllTextAsync(scriptPath, script, Utf8NoBom, cancellationToken);

        try
        {
            var result = await RunProcessAsync(
                "powershell.exe",
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
                log,
                cancellationToken,
                runAsAdmin: true,
                throwOnError: false);

            if (File.Exists(elevatedLogPath))
            {
                log(Format(AppStrings.InstallerServiceInstallationLog, ("Path", elevatedLogPath)));
                foreach (var line in File.ReadLines(elevatedLogPath).TakeLast(80))
                {
                    log($"{GetProcessLogPrefix("powershell.exe")} {line}");
                }
            }

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(Format(AppStrings.InstallerServiceRegistrationFailed, ("ExitCode", result.ExitCode.ToString())));
            }
        }
        finally
        {
            TryDeleteFile(scriptPath);
        }
    }

    private async Task<string?> FindPsqlAsync(string? windowsInstallDirectory, CancellationToken cancellationToken)
    {
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(Path.Combine(NormalizeWindowsInstallDirectory(windowsInstallDirectory), "bin", "psql.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL", "16", "bin", "psql.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PostgreSQL", "16", "bin", "psql.exe"));
            candidates.Add("psql.exe");
        }
        else
        {
            candidates.Add("/usr/bin/psql");
            candidates.Add("/usr/local/bin/psql");
            candidates.Add("psql");
        }

        foreach (var candidate in candidates)
        {
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
            {
                return candidate;
            }

            if (!Path.IsPathFullyQualified(candidate) && await CommandExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task LogWindowsPostgresInstancesAsync(Action<string> log, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var installations = await FindWindowsPostgresInstallationsAsync(cancellationToken);
        if (installations.Count == 0)
        {
            log(AppStrings.InstallerExistingServicesNotFoundLog);
        }
        else
        {
            log(AppStrings.InstallerExistingServicesLog);
            foreach (var installation in installations)
            {
                var portText = installation.Port is null
                    ? AppStrings.InstallerUnknownPort
                    : Format(AppStrings.InstallerPortValue, ("Port", installation.Port.Value.ToString()));
                log($"- {installation.ServiceName}: {installation.Version}, {portText}, {installation.InstallDirectory}");
            }
        }

        var occupiedPorts = GetOccupiedTcpPorts()
            .Where(port => port is >= 5432 and <= 5500)
            .Order()
            .ToArray();

        log(occupiedPorts.Length == 0
            ? AppStrings.InstallerNoOccupiedPortsLog
            : Format(AppStrings.InstallerOccupiedPortsLog, ("Ports", string.Join(", ", occupiedPorts))));
    }

    private static async Task<List<WindowsPostgresInstallation>> FindWindowsPostgresInstallationsAsync(CancellationToken cancellationToken)
    {
        var installations = new List<WindowsPostgresInstallation>();
        if (!OperatingSystem.IsWindows())
        {
            return installations;
        }

        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null)
        {
            return installations;
        }

        foreach (var serviceName in services.GetSubKeyNames().Where(name => name.StartsWith("postgresql", StringComparison.OrdinalIgnoreCase)))
        {
            var result = await RunProcessAsync(
                "sc.exe",
                new[] { "qc", serviceName },
                _ => { },
                cancellationToken,
                throwOnError: false);

            if (result.ExitCode != 0)
            {
                continue;
            }

            var psqlPath = TryGetPsqlPathFromServiceConfig(result.Output);
            if (psqlPath is null || !File.Exists(psqlPath))
            {
                continue;
            }

            var version = await GetPsqlVersionAsync(psqlPath, cancellationToken);
            var installDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(psqlPath)!, ".."));
            installations.Add(new WindowsPostgresInstallation(
                serviceName,
                installDirectory,
                psqlPath,
                version,
                TryReadPostgresPort(installDirectory)));
        }

        return installations;
    }

    private static async Task<WindowsPostgresInstallation?> FindWindowsPostgresInstallationForDirectoryAsync(
        string installDirectory,
        CancellationToken cancellationToken)
    {
        var normalizedInstallDirectory = installDirectory.TrimEnd('\\');
        var installations = await FindWindowsPostgresInstallationsAsync(cancellationToken);
        return installations.FirstOrDefault(item =>
            string.Equals(item.InstallDirectory.TrimEnd('\\'), normalizedInstallDirectory, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetPsqlPathFromServiceConfig(string serviceConfig)
    {
        const string marker = "BINARY_PATH_NAME";
        var line = serviceConfig
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(item => item.Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (line is null)
        {
            return null;
        }

        var firstQuote = line.IndexOf('"');
        if (firstQuote < 0)
        {
            return null;
        }

        var secondQuote = line.IndexOf('"', firstQuote + 1);
        if (secondQuote <= firstQuote)
        {
            return null;
        }

        var pgCtlPath = line.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        var binDirectory = Path.GetDirectoryName(pgCtlPath);
        return string.IsNullOrWhiteSpace(binDirectory)
            ? null
            : Path.Combine(binDirectory, "psql.exe");
    }

    private static async Task<string> GetPsqlVersionAsync(string psqlPath, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            psqlPath,
            new[] { "--version" },
            _ => { },
            cancellationToken,
            throwOnError: false);

        var versionText = result.Output.Trim();
        var parts = versionText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.LastOrDefault() ?? versionText;
    }

    public static string GetDefaultWindowsInstallDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PostgreSQL",
            "16.14-oilctrl");
    }

    private static string NormalizeWindowsInstallDirectory(string? windowsInstallDirectory)
    {
        return string.IsNullOrWhiteSpace(windowsInstallDirectory)
            ? GetDefaultWindowsInstallDir()
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(windowsInstallDirectory));
    }

    private static string GetInstallerLogPath()
    {
        var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);
        return Path.Combine(logsDirectory, "postgresql-install.log");
    }

    private static async Task<bool> CommandExistsAsync(string command, CancellationToken cancellationToken)
    {
        var locator = OperatingSystem.IsWindows() ? "where.exe" : "which";
        var result = await RunProcessAsync(locator, new[] { command }, _ => { }, cancellationToken, throwOnError: false);
        return result.ExitCode == 0;
    }

    private static async Task<bool> WindowsServiceExistsAsync(string serviceName, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "sc.exe",
            new[] { "query", serviceName },
            _ => { },
            cancellationToken,
            throwOnError: false);

        return result.ExitCode == 0;
    }

    private static async Task EnsureWindowsServiceRunningAsync(
        string serviceName,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var state = await GetWindowsServiceStateAsync(serviceName, cancellationToken);
        if (string.Equals(state, "RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            log(Format(AppStrings.InstallerServiceAlreadyRunningLog, ("ServiceName", serviceName)));
            return;
        }

        log(Format(
            AppStrings.InstallerServiceNotRunningLog,
            ("ServiceName", serviceName),
            ("State", state ?? AppStrings.InstallerUnknownPort)));
        log(AppStrings.InstallerStartServiceLog);

        var elevatedLogPath = Path.Combine(AppContext.BaseDirectory, "logs", "postgresql-service-start.log");
        Directory.CreateDirectory(Path.GetDirectoryName(elevatedLogPath)!);

        var scriptPath = Path.Combine(Path.GetTempPath(), $"oilctrl-postgres-start-{Guid.NewGuid():N}.ps1");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $log = '{{EscapePowerShellSingleQuotedString(elevatedLogPath)}}'
            "Starting service {{serviceName}}" | Out-File -FilePath $log -Encoding utf8
            Start-Service -Name '{{EscapePowerShellSingleQuotedString(serviceName)}}'
            $service = Get-Service -Name '{{EscapePowerShellSingleQuotedString(serviceName)}}'
            $service.WaitForStatus('Running', '00:00:30')
            "Service status: $($service.Status)" | Out-File -FilePath $log -Encoding utf8 -Append
            """;

        await File.WriteAllTextAsync(scriptPath, script, Utf8NoBom, cancellationToken);

        try
        {
            var result = await RunProcessAsync(
                "powershell.exe",
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
                log,
                cancellationToken,
                runAsAdmin: true,
                throwOnError: false);

            log(Format(AppStrings.InstallerServiceStartLog, ("Path", elevatedLogPath)));

            if (File.Exists(elevatedLogPath))
            {
                foreach (var line in File.ReadLines(elevatedLogPath).TakeLast(40))
                {
                    log($"{GetProcessLogPrefix("powershell.exe")} {line}");
                }
            }

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(Format(AppStrings.InstallerServiceStartFailed, ("ExitCode", result.ExitCode.ToString())));
            }
        }
        finally
        {
            TryDeleteFile(scriptPath);
        }
    }

    private static async Task<string?> GetWindowsServiceStateAsync(string serviceName, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "sc.exe",
            new[] { "query", serviceName },
            _ => { },
            cancellationToken,
            throwOnError: false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var stateLine = result.Output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains("STATE", StringComparison.OrdinalIgnoreCase));

        if (stateLine is null)
        {
            return null;
        }

        var parts = stateLine.Split(':', 2);
        return parts.Length < 2
            ? null
            : parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
    }

    private static async Task<int> ResolveWindowsPortAsync(
        string? windowsInstallDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var installDir = NormalizeWindowsInstallDirectory(windowsInstallDirectory);
        var port = TryReadPostgresPort(installDir);
        if (port is not null)
        {
            return port.Value;
        }

        var installations = await FindWindowsPostgresInstallationsAsync(cancellationToken);
        var selectedInstallation = installations.FirstOrDefault(item =>
            string.Equals(item.InstallDirectory.TrimEnd('\\'), installDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        if (selectedInstallation?.Port is not null)
        {
            return selectedInstallation.Port.Value;
        }

        log(Format(AppStrings.InstallerPortFallbackLog, ("Port", PreferredServerPort.ToString())));
        return PreferredServerPort;
    }

    private static async Task<int> SelectFreeWindowsPostgresPortAsync(Action<string> log, CancellationToken cancellationToken)
    {
        var occupied = GetOccupiedTcpPorts();
        foreach (var port in GetPreferredPostgresPorts())
        {
            var serviceName = $"postgresql-x64-16-oilctrl-{port}";

            if (occupied.Contains(port))
            {
                continue;
            }

            if (await WindowsServiceExistsAsync(serviceName, cancellationToken))
            {
                log(Format(
                    AppStrings.InstallerServiceNameExistsOnPortLog,
                    ("Port", port.ToString()),
                    ("ServiceName", serviceName)));
                continue;
            }

            if (CanBindTcpPort(port))
            {
                log(Format(AppStrings.InstallerSelectedFreeTcpPortLog, ("Port", port.ToString())));
                return port;
            }
        }

        log(AppStrings.InstallerNoFreeTcpWithServiceLog);
        throw new InvalidOperationException(AppStrings.InstallerNoFreeTcp);
    }

    private static int SelectFreeTcpPort(Action<string> log)
    {
        var occupied = GetOccupiedTcpPorts();
        foreach (var port in GetPreferredPostgresPorts())
        {
            if (occupied.Contains(port))
            {
                continue;
            }

            if (CanBindTcpPort(port))
            {
                log(Format(AppStrings.InstallerSelectedFreeTcpPortLog, ("Port", port.ToString())));
                return port;
            }
        }

        log(AppStrings.InstallerNoFreeTcpLog);
        throw new InvalidOperationException(AppStrings.InstallerNoFreeTcp);
    }

    private static IEnumerable<int> GetPreferredPostgresPorts()
    {
        return Enumerable.Range(PreferredServerPort, 5500 - PreferredServerPort + 1)
            .Concat(Enumerable.Range(15432, 100));
    }

    private static HashSet<int> GetOccupiedTcpPorts()
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        return properties
            .GetActiveTcpListeners()
            .Select(endpoint => endpoint.Port)
            .Concat(properties.GetActiveTcpConnections().Select(connection => connection.LocalEndPoint.Port))
            .ToHashSet();
    }

    private static bool CanBindTcpPort(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int? TryReadPostgresPort(string installDirectory)
    {
        var summaryPath = Path.Combine(installDirectory, "installation_summary.log");
        if (File.Exists(summaryPath))
        {
            var port = File.ReadLines(summaryPath)
                .Select(TryParsePortLine)
                .FirstOrDefault(value => value is not null);

            if (port is not null)
            {
                return port;
            }
        }

        var configPath = Path.Combine(installDirectory, "data", "postgresql.conf");
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            return File.ReadLines(configPath)
                .Select(TryParsePortLine)
                .FirstOrDefault(value => value is not null);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int? TryParsePortLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("Database Port:", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed.Split(':', 2)[1].Trim(), out var summaryPort)
                ? summaryPort
                : null;
        }

        if (!trimmed.StartsWith("port", StringComparison.OrdinalIgnoreCase) || !trimmed.Contains('='))
        {
            return null;
        }

        var value = trimmed
            .Split('=', 2)[1]
            .Split('#', 2)[0]
            .Trim();

        return int.TryParse(value, out var configPort) ? configPort : null;
    }

    private async Task<string> QueryScalarAsync(string psqlPath, int port, string database, string sql, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            psqlPath,
            new[] { "-h", "localhost", "-p", port.ToString(), "-U", SuperUser, "-d", database, "-tAc", sql },
            _ => { },
            cancellationToken,
            new Dictionary<string, string> { ["PGPASSWORD"] = SuperPassword });

        return result.Output;
    }

    private void EnsureInitSqlExists()
    {
        var directory = Path.GetDirectoryName(initSqlPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(initSqlPath))
        {
            File.WriteAllText(
                initSqlPath,
                """
                CREATE TABLE IF NOT EXISTS public."VersionInfo"
                (
                    "Version" bigint NOT NULL,
                    "AppliedOn" timestamp without time zone,
                    "Description" character varying(1024) COLLATE pg_catalog."default"
                );

                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'admin') THEN
                        CREATE ROLE admin WITH
                          LOGIN
                          NOSUPERUSER
                          INHERIT
                          NOCREATEDB
                          NOCREATEROLE
                          NOREPLICATION
                          NOBYPASSRLS
                          ENCRYPTED PASSWORD 'SCRAM-SHA-256$4096:XPe2t0Z2+Xetr89Br7Db4w==$MxCBd6ZBaQEovqc+gWfkJwkM+zPE8HoizfQ2Rb1hTvU=:kgK0h76Tb1gKWnxMmIH0w0kxL5v4m9Mx4/kqbyMMywQ=';
                    END IF;
                END
                $$;
                """,
                Utf8NoBom);
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string> log,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null,
        bool runAsAdmin = false,
        bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = runAsAdmin,
            Verb = runAsAdmin && OperatingSystem.IsWindows() ? "runas" : string.Empty,
            RedirectStandardOutput = !runAsAdmin,
            RedirectStandardError = !runAsAdmin,
            CreateNoWindow = !runAsAdmin
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null && !runAsAdmin)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        var processLogPrefix = GetProcessLogPrefix(fileName);
        log($"{processLogPrefix} > {fileName} {string.Join(" ", arguments.Select(MaskSensitiveArgument))}");

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var errors = new StringBuilder();

        if (!runAsAdmin)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                output.AppendLine(e.Data);
                log($"{processLogPrefix} {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                errors.AppendLine(e.Data);
                log($"{processLogPrefix} {e.Data}");
            };
        }

        process.Start();

        if (!runAsAdmin)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        await process.WaitForExitAsync(cancellationToken);

        var result = new ProcessResult(process.ExitCode, output.ToString(), errors.ToString());
        if (throwOnError && result.ExitCode != 0)
        {
            LogInstallerTrace(log);
            throw new InvalidOperationException(Format(
                AppStrings.InstallerCommandFailed,
                ("ExitCode", result.ExitCode.ToString()),
                ("FileName", fileName)));
        }

        return result;
    }

    private static void LogInstallerTrace(Action<string> log)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var installerLogPath = GetInstallerLogPath();
        if (!File.Exists(installerLogPath))
        {
            return;
        }

        log(Format(AppStrings.InstallerTraceTailLog, ("Path", installerLogPath)));
        foreach (var line in File.ReadLines(installerLogPath).TakeLast(80))
        {
            log($"{GetProcessLogPrefix(installerLogPath)} {line}");
        }
    }

    private static string GetProcessLogPrefix(string fileNameOrPath)
    {
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath);
        return string.IsNullOrWhiteSpace(name)
            ? "[process]"
            : $"[{name.ToLowerInvariant()}]";
    }

    private static string Format(string template, params (string Key, string Value)[] values)
    {
        foreach (var (key, value) in values)
        {
            template = template.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        return template;
    }

    private static string MaskSensitiveArgument(string argument)
    {
        return argument == SuperPassword ? "********" : argument;
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
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
            // Best-effort cleanup of temporary installer helper files.
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private sealed record WindowsPostgresInstallation(
        string ServiceName,
        string InstallDirectory,
        string PsqlPath,
        string Version,
        int? Port);
}
