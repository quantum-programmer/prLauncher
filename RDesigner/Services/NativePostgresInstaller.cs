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
    private const string LinuxClusterNamePrefix = "oilctrl";
    private const string LinuxPostgresVersion = "16";

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
        var port = await ResolvePostgresPortAsync(windowsInstallDirectory, log, cancellationToken);
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
        await EnsureAstraOrDebianLinuxAsync(log, cancellationToken);

        var psqlPath = await FindPsqlAsync(null, cancellationToken);
        if (psqlPath is null)
        {
            log(AppStrings.InstallerLinuxPsqlNotFoundLog);
            await InstallLinuxDebPackagesAsync(log, cancellationToken);
            psqlPath = await FindPsqlAsync(null, cancellationToken);
        }

        if (psqlPath is null)
        {
            throw new InvalidOperationException(AppStrings.InstallerPsqlNotFoundLog);
        }

        log(Format(AppStrings.InstallerClientFoundLog, ("Path", psqlPath)));

        var port = await SelectFreeLinuxPostgresPortAsync(log, cancellationToken);
        var clusterName = GetLinuxClusterName(port);
        log(Format(AppStrings.InstallerSelectedPostgresPortLog, ("Port", port.ToString())));
        log(Format(AppStrings.InstallerLinuxClusterLog, ("Cluster", $"{LinuxPostgresVersion}/{clusterName}")));
        await CreateAndStartLinuxClusterAsync(clusterName, port, log, cancellationToken);

        await EnsureLinuxPostgresPasswordAsync(port, log, cancellationToken);
        await InitializeDatabaseAsync(psqlPath, port, log, cancellationToken);
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

    private static async Task EnsureAstraOrDebianLinuxAsync(Action<string> log, CancellationToken cancellationToken)
    {
        var osRelease = await ReadLinuxOsReleaseAsync(cancellationToken);
        var id = GetOsReleaseValue(osRelease, "ID");
        var idLike = GetOsReleaseValue(osRelease, "ID_LIKE");
        var versionId = GetOsReleaseValue(osRelease, "VERSION_ID");

        log(Format(
            AppStrings.InstallerLinuxDistributionLog,
            ("Id", id ?? "unknown"),
            ("Version", versionId ?? "unknown")));

        var isAstraOrDebian =
            ContainsOsToken(id, "astra") ||
            ContainsOsToken(id, "debian") ||
            ContainsOsToken(idLike, "astra") ||
            ContainsOsToken(idLike, "debian");

        if (!isAstraOrDebian)
        {
            throw new PlatformNotSupportedException(AppStrings.InstallerLinuxDistributionNotSupported);
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadLinuxOsReleaseAsync(CancellationToken cancellationToken)
    {
        const string osReleasePath = "/etc/os-release";
        if (!File.Exists(osReleasePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in await File.ReadAllLinesAsync(osReleasePath, cancellationToken))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || !trimmed.Contains('='))
            {
                continue;
            }

            var parts = trimmed.Split('=', 2);
            result[parts[0]] = UnquoteOsReleaseValue(parts[1]);
        }

        return result;
    }

    private static string? GetOsReleaseValue(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : null;
    }

    private static string UnquoteOsReleaseValue(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return value;
    }

    private static bool ContainsOsToken(string? value, string token)
    {
        return value?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, token, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static async Task InstallLinuxDebPackagesAsync(Action<string> log, CancellationToken cancellationToken)
    {
        var packagesDirectory = FindLinuxDebPackagesDirectory();
        if (packagesDirectory is null)
        {
            throw new FileNotFoundException(AppStrings.InstallerLinuxDebPackagesNotFound);
        }

        log(Format(AppStrings.InstallerLinuxDebPackagesLog, ("Directory", packagesDirectory)));
        await RunLinuxPrivilegedScriptAsync(
            $$"""
            set -eu
            export DEBIAN_FRONTEND=noninteractive
            cd {{ShellQuote(packagesDirectory)}}
            apt-get install -y --no-install-recommends ./*.deb
            """,
            log,
            cancellationToken);
    }

    private static string? FindLinuxDebPackagesDirectory()
    {
        var installersDir = Path.Combine(AppContext.BaseDirectory, "Installers");
        if (!Directory.Exists(installersDir))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(installersDir, "*", SearchOption.TopDirectoryOnly)
            .Prepend(installersDir)
            .Where(HasRequiredLinuxDebPackages)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool HasRequiredLinuxDebPackages(string directory)
    {
        return Directory.EnumerateFiles(directory, "postgresql-client-common_*_all.deb").Any() &&
               Directory.EnumerateFiles(directory, "postgresql-common_*_all.deb").Any() &&
               Directory.EnumerateFiles(directory, "postgresql-client-16_*_amd64.deb").Any() &&
               Directory.EnumerateFiles(directory, "postgresql-16_*_amd64.deb").Any() &&
               Directory.EnumerateFiles(directory, "libpq5_*_amd64.deb").Any();
    }

    private static async Task<LinuxPostgresCluster?> FindExistingLinuxOilCtrlClusterAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "pg_lsclusters",
            new[] { "--no-header" },
            _ => { },
            cancellationToken,
            throwOnError: false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        return result.Output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(TryParseLinuxClusterLine)
            .Where(cluster => cluster is not null)
            .Cast<LinuxPostgresCluster>()
            .Where(cluster =>
                string.Equals(cluster.Version, LinuxPostgresVersion, StringComparison.Ordinal) &&
                cluster.Name.StartsWith($"{LinuxClusterNamePrefix}-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(cluster => cluster.Port)
            .FirstOrDefault();
    }

    private static LinuxPostgresCluster? TryParseLinuxClusterLine(string line)
    {
        var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (columns.Length < 3 || !int.TryParse(columns[2], out var port))
        {
            return null;
        }

        return new LinuxPostgresCluster(columns[0], columns[1], port);
    }

    private static async Task<int> SelectFreeLinuxPostgresPortAsync(Action<string> log, CancellationToken cancellationToken)
    {
        var occupied = GetOccupiedTcpPorts();
        foreach (var port in GetPreferredPostgresPorts())
        {
            var clusterName = GetLinuxClusterName(port);
            var serviceName = GetLinuxServiceName(clusterName);

            if (occupied.Contains(port))
            {
                continue;
            }

            if (await LinuxClusterOrServiceExistsAsync(clusterName, serviceName, cancellationToken))
            {
                log(Format(
                    AppStrings.InstallerLinuxClusterNameExistsOnPortLog,
                    ("Port", port.ToString()),
                    ("Cluster", $"{LinuxPostgresVersion}/{clusterName}"),
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

    private static async Task<bool> LinuxClusterOrServiceExistsAsync(
        string clusterName,
        string serviceName,
        CancellationToken cancellationToken)
    {
        var clustersResult = await RunProcessAsync(
            "pg_lsclusters",
            new[] { "--no-header" },
            _ => { },
            cancellationToken,
            throwOnError: false);

        if (clustersResult.ExitCode == 0 &&
            clustersResult.Output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(TryParseLinuxClusterLine)
                .Any(cluster =>
                    cluster is not null &&
                    string.Equals(cluster.Version, LinuxPostgresVersion, StringComparison.Ordinal) &&
                    string.Equals(cluster.Name, clusterName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (Directory.Exists($"/etc/postgresql/{LinuxPostgresVersion}/{clusterName}"))
        {
            return true;
        }

        // postgresql@.service is a template unit, so "systemctl status postgresql@x"
        // can look loaded even when the cluster does not exist.
        var result = await RunProcessAsync(
            "systemctl",
            new[] { "is-enabled", serviceName },
            _ => { },
            cancellationToken,
            throwOnError: false);

        var state = result.Output.Trim();
        return string.Equals(state, "enabled", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, "linked", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CreateAndStartLinuxClusterAsync(
        string clusterName,
        int port,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        await RunLinuxPrivilegedScriptAsync(
            $$"""
            set -eu
            if ! pg_lsclusters --no-header | awk '$1 == "{{LinuxPostgresVersion}}" && $2 == "{{clusterName}}" { found = 1 } END { exit found ? 0 : 1 }'; then
              pg_createcluster {{LinuxPostgresVersion}} {{clusterName}} --port {{port}}
            fi
            sed -i -E "s/^[#[:space:]]*port[[:space:]]*=.*/port = {{port}}/" /etc/postgresql/{{LinuxPostgresVersion}}/{{clusterName}}/postgresql.conf
            sed -i -E "s/^[#[:space:]]*listen_addresses[[:space:]]*=.*/listen_addresses = 'localhost'/" /etc/postgresql/{{LinuxPostgresVersion}}/{{clusterName}}/postgresql.conf
            {{CreateLinuxSystemdServiceScript(LinuxPostgresVersion, clusterName)}}
            systemctl restart {{ShellQuote(GetLinuxServiceName(clusterName))}}
            systemctl enable {{ShellQuote(GetLinuxServiceName(clusterName))}}
            """,
            log,
            cancellationToken);
    }

    private static async Task EnsureLinuxClusterRunningAsync(
        LinuxPostgresCluster cluster,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log(Format(AppStrings.InstallerStartLinuxServiceLog, ("ServiceName", GetLinuxServiceName(cluster.Name))));
        await RunLinuxPrivilegedScriptAsync(
            $$"""
            set -eu
            {{CreateLinuxSystemdServiceScript(cluster.Version, cluster.Name)}}
            systemctl restart {{ShellQuote(GetLinuxServiceName(cluster.Name))}}
            systemctl enable {{ShellQuote(GetLinuxServiceName(cluster.Name))}}
            """,
            log,
            cancellationToken);
    }

    private static async Task EnsureLinuxPostgresPasswordAsync(int port, Action<string> log, CancellationToken cancellationToken)
    {
        log(AppStrings.InstallerLinuxSettingPasswordLog);
        await RunLinuxPrivilegedScriptAsync(
            $$"""
            set -eu
            su - postgres -c {{ShellQuote($"psql -p {port} -d postgres -v ON_ERROR_STOP=1 -c \"ALTER USER postgres WITH PASSWORD '{EscapeSqlLiteral(SuperPassword)}';\"")}}
            """,
            log,
            cancellationToken);
    }

    private static async Task RunLinuxPrivilegedScriptAsync(string script, Action<string> log, CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"oilctrl-postgres-linux-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(scriptPath, script.ReplaceLineEndings("\n"), Utf8NoBom, cancellationToken);

        try
        {
            var privilegedRunners = await FindLinuxPrivilegedRunnersAsync(scriptPath, cancellationToken);
            if (privilegedRunners.Count == 0)
            {
                throw new InvalidOperationException(AppStrings.InstallerLinuxPrivilegeToolNotFound);
            }

            ProcessResult? lastResult = null;
            foreach (var privilegedRunner in privilegedRunners)
            {
                var result = await RunProcessAsync(
                    privilegedRunner.FileName,
                    privilegedRunner.Arguments,
                    log,
                    cancellationToken,
                    throwOnError: false);

                if (result.ExitCode == 0)
                {
                    return;
                }

                lastResult = result;
                log(Format(
                    AppStrings.InstallerLinuxPrivilegeRunnerFailedLog,
                    ("Tool", privilegedRunner.FileName),
                    ("ExitCode", result.ExitCode.ToString())));
            }

            throw new InvalidOperationException(Format(
                AppStrings.InstallerCommandFailed,
                ("ExitCode", (lastResult?.ExitCode ?? -1).ToString()),
                ("FileName", privilegedRunners[^1].FileName)));
        }
        finally
        {
            TryDeleteFile(scriptPath);
        }
    }

    private static async Task<List<LinuxPrivilegedRunner>> FindLinuxPrivilegedRunnersAsync(
        string scriptPath,
        CancellationToken cancellationToken)
    {
        var runners = new List<LinuxPrivilegedRunner>();
        var scriptCommand = $"sh {ShellQuote(scriptPath)}";

        if (await CommandExistsAsync("fly-su", cancellationToken))
        {
            runners.Add(new LinuxPrivilegedRunner("fly-su", new[] { "-d", "-c", scriptCommand }));
        }

        if (await CommandExistsAsync("pkexec", cancellationToken))
        {
            runners.Add(new LinuxPrivilegedRunner("pkexec", new[] { "sh", scriptPath }));
        }

        if (await CommandExistsAsync("sudo", cancellationToken))
        {
            runners.Add(new LinuxPrivilegedRunner("sudo", new[] { "-E", "sh", scriptPath }));
        }

        return runners;
    }

    private static async Task<int> ResolvePostgresPortAsync(
        string? windowsInstallDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return await ResolveWindowsPortAsync(windowsInstallDirectory, log, cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            var cluster = await FindExistingLinuxOilCtrlClusterAsync(cancellationToken);
            if (cluster is not null)
            {
                return cluster.Port;
            }
        }

        log(Format(AppStrings.InstallerPortFallbackLog, ("Port", PreferredServerPort.ToString())));
        return PreferredServerPort;
    }

    private static string GetLinuxClusterName(int port)
    {
        return $"{LinuxClusterNamePrefix}-{port}";
    }

    private static string GetLinuxServiceName(string clusterName)
    {
        return $"oilctrl-postgresql-{clusterName}.service";
    }

    private static string CreateLinuxSystemdServiceScript(string version, string clusterName)
    {
        var servicePath = ShellQuote($"/etc/systemd/system/{GetLinuxServiceName(clusterName)}");
        var syslogIdentifier = $"oilctrl-postgresql-{version}-{clusterName}";
        var pidFile = $"/run/postgresql/{version}-{clusterName}.pid";

        return $$"""
        cat > {{servicePath}} <<'OILCTRL_POSTGRESQL_SERVICE'
        [Unit]
        Description=OilCtrl PostgreSQL Cluster {{version}}/{{clusterName}}
        AssertPathExists=/etc/postgresql/{{version}}/{{clusterName}}/postgresql.conf
        RequiresMountsFor=/etc/postgresql/{{version}}/{{clusterName}} /var/lib/postgresql/{{version}}/{{clusterName}}
        After=network.target

        [Service]
        Type=forking
        ExecStart=/usr/bin/pg_ctlcluster --skip-systemctl-redirect {{version}} {{clusterName}} start
        TimeoutStartSec=infinity
        ExecStop=/usr/bin/pg_ctlcluster --skip-systemctl-redirect -m fast {{version}} {{clusterName}} stop
        TimeoutStopSec=1h
        ExecReload=/usr/bin/pg_ctlcluster --skip-systemctl-redirect {{version}} {{clusterName}} reload
        PIDFile={{pidFile}}
        SyslogIdentifier={{syslogIdentifier}}
        OOMScoreAdjust=-900

        [Install]
        WantedBy=multi-user.target
        OILCTRL_POSTGRESQL_SERVICE
        systemctl daemon-reload
        """;
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

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
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

    private readonly record struct LinuxPrivilegedRunner(string FileName, IReadOnlyList<string> Arguments);

    private sealed record LinuxPostgresCluster(string Version, string Name, int Port);
}
