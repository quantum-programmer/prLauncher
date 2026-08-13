using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Pyramid.Resources;
using Pyramid.Security;

namespace Pyramid.Services;

public sealed class NativePostgresInstaller
{
    public const string RequiredVersion = "16.14";
    public const string DatabaseName = "OilCtrl";
    public const string SuperUser = "postgres";
    public const string SuperPassword = "j06gOuqDHwWkvpWf";
    public const int PreferredServerPort = 5433;
    private const string PostgresCredentialId = "OilCtrl.PostgreSQL.Postgres";
    private const string OilCtrlAdministratorUser = "administrator";
    private const string OilCtrlAdministratorPassword = "administrator";
    private const string OilCtrlAdministratorDisplayName = "Администратор";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly string[] OilCtrlApplicationRoles = ["reader", "operator", "master", "configurator", "admin"];
    private const string LinuxClusterNamePrefix = "oilctrl";
    private const string LinuxPostgresVersion = "16";

    private readonly string initSqlPath = Path.Combine(AppContext.BaseDirectory, "Installers", "oilctrl-init.sql");

    public async Task<int> InstallAsync(
        string? windowsInstallDirectory,
        Action<string> log,
        bool reinstallExisting = false,
        CancellationToken cancellationToken = default)
    {
        log(Format(AppStrings.InstallerOsLog, ("Description", RuntimeInformation.OSDescription)));
        log(Format(AppStrings.InstallerTargetPostgresVersionLog, ("Version", RequiredVersion)));

        if (OperatingSystem.IsWindows())
        {
            return await InstallOnWindowsAsync(windowsInstallDirectory, reinstallExisting, log, cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            return await InstallOnLinuxAsync(log, cancellationToken);
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

    private async Task<int> InstallOnWindowsAsync(
        string? windowsInstallDirectory,
        bool reinstallExisting,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var installDir = NormalizeWindowsInstallDirectory(windowsInstallDirectory);
        if (reinstallExisting && WindowsInstallDirectoryContainsPostgres(installDir))
        {
            await BackupAndRemoveWindowsPostgresAsync(installDir, log, cancellationToken);
        }

        EnsureWindowsInstallDirectoryDoesNotContainPostgres(installDir);
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
                throw new InstallerSystemCancelledException(
                    Format(AppStrings.InstallerDirectoryNotEmpty, ("Directory", installDir)));
            }

            log(Format(AppStrings.InstallerInstallDirLog, ("Directory", installDir)));
            log(Format(AppStrings.InstallerDataDirLog, ("Directory", dataDir)));
            log(Format(AppStrings.InstallerSelectedPostgresPortLog, ("Port", port.ToString())));
            log(Format(AppStrings.InstallerWindowsServiceLog, ("ServiceName", serviceName)));
            log(AppStrings.InstallerComponentsSkippedLog);

            var binDir = Path.Combine(installDir, "bin");
            psqlPath = Path.Combine(binDir, "psql.exe");
            var initDbPath = Path.Combine(binDir, "initdb.exe");
            var pgCtlPath = Path.Combine(binDir, "pg_ctl.exe");

            await RunElevatedWindowsBinariesInstallAsync(binariesArchive, installDir, port, log, cancellationToken);

            if (!File.Exists(psqlPath) || !File.Exists(initDbPath) || !File.Exists(pgCtlPath))
            {
                throw new FileNotFoundException(AppStrings.InstallerRequiredExecutablesNotFound);
            }
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
        return installedPort;
    }

    private async Task<int> InstallOnLinuxAsync(Action<string> log, CancellationToken cancellationToken)
    {
        await EnsureAstraOrDebianLinuxAsync(log, cancellationToken);
        await RunLinuxSetupScriptsAsync(log, cancellationToken);

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
        return port;
    }

    private static void EnsureWindowsInstallDirectoryDoesNotContainPostgres(string installDir)
    {
        if (!WindowsInstallDirectoryContainsPostgres(installDir))
        {
            return;
        }

        throw new InstallerSystemCancelledException(
            Format(AppStrings.InstallerPostgresAlreadyExistsInDirectory, ("Directory", installDir)));
    }

    private static bool WindowsInstallDirectoryContainsPostgres(string installDir)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(installDir))
        {
            return false;
        }

        var binDirectory = Path.Combine(installDir, "bin");
        var dataDirectory = Path.Combine(installDir, "data");
        return File.Exists(Path.Combine(binDirectory, "postgres.exe")) ||
               File.Exists(Path.Combine(binDirectory, "psql.exe")) ||
               File.Exists(Path.Combine(binDirectory, "pg_ctl.exe")) ||
               Directory.Exists(dataDirectory) && Directory.EnumerateFileSystemEntries(dataDirectory).Any();
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

        await EnsurePostgresCredentialExistsAsync(log, cancellationToken);
        await EnsureOilCtrlAdministratorRoleAsync(psqlPath, port, log, cancellationToken);
        EnsureSharedAppSettingsExists(port);

        log(AppStrings.InstallerApplyingInitSqlLog);
        await RunProcessAsync(
            psqlPath,
            new[] { "-h", "localhost", "-p", port.ToString(), "-U", SuperUser, "-d", DatabaseName, "-f", initSqlPath },
            log,
            cancellationToken,
            new Dictionary<string, string> { ["PGPASSWORD"] = SuperPassword });
    }

    private static async Task EnsurePostgresCredentialExistsAsync(Action<string> log, CancellationToken cancellationToken)
    {
        var credentialProvider = DatabaseCredentialProvider.Create();
        string? existingPassword = null;
        try
        {
            existingPassword = credentialProvider.GetPassword(PostgresCredentialId);
        }
        catch (InvalidOperationException)
        {
            // Existing credential was created by an older provider format.
            // Recreate it with the current platform credential provider.
        }

        if (string.Equals(existingPassword, SuperPassword, StringComparison.Ordinal))
        {
            return;
        }

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            await SavePostgresCredentialWithElevatedHelperAsync(log, cancellationToken);
            return;
        }

        credentialProvider.SavePassword(PostgresCredentialId, SuperPassword);
    }

    private static async Task SavePostgresCredentialWithElevatedHelperAsync(
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var passwordPath = Path.Combine(Path.GetTempPath(), $"oilctrl-postgres-credential-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(passwordPath, SuperPassword, Utf8NoBom, cancellationToken);

        try
        {
            var (fileName, arguments) = GetCurrentApplicationCommandLine(
                "--pyramid-save-database-credential",
                PostgresCredentialId,
                passwordPath);

            ProcessResult result;
            if (OperatingSystem.IsWindows())
            {
                result = await RunProcessAsync(
                    fileName,
                    arguments,
                    log,
                    cancellationToken,
                    runAsAdmin: true,
                    throwOnError: false);
            }
            else if (OperatingSystem.IsLinux())
            {
                var command = string.Join(" ", new[] { fileName }.Concat(arguments).Select(ShellQuote));
                await RunLinuxPrivilegedScriptAsync(command, log, cancellationToken);
                result = new ProcessResult(0, string.Empty, string.Empty);
            }
            else
            {
                throw new PlatformNotSupportedException("Machine database credential creation is supported only on Windows and Linux.");
            }

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException("Failed to create machine database credential. Run Pyramid with administrator privileges and repeat installation.");
            }
        }
        finally
        {
            TryDeleteFile(passwordPath);
        }
    }

    private static (string FileName, IReadOnlyList<string> Arguments) GetCurrentApplicationCommandLine(params string[] maintenanceArguments)
    {
        var localizedMaintenanceArguments = new[]
        {
            "--pyramid-language",
            LocalizationManager.CurrentLanguage.ToString()
        }.Concat(maintenanceArguments).ToArray();

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            var entryAssemblyPath = Environment.ProcessPath;
            var currentDirectory = AppContext.BaseDirectory;
            var dllPath = Path.Combine(currentDirectory, "Pyramid.dll");

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
        }

        throw new InvalidOperationException("Current application executable path was not found.");
    }

    private static async Task EnsureOilCtrlAdministratorRoleAsync(
        string psqlPath,
        int port,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"oilctrl-administrator-role-{Guid.NewGuid():N}.sql");
        try
        {
            await File.WriteAllTextAsync(
                scriptPath,
                $$"""
                DO $$
                BEGIN
                    {{CreateOilCtrlApplicationRolesSql()}}

                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{OilCtrlAdministratorUser}}') THEN
                        CREATE ROLE {{OilCtrlAdministratorUser}} WITH
                          LOGIN
                          NOSUPERUSER
                          INHERIT
                          NOCREATEDB
                          NOCREATEROLE
                          NOREPLICATION
                          NOBYPASSRLS;
                    END IF;

                    ALTER ROLE {{OilCtrlAdministratorUser}} WITH PASSWORD '{{EscapeSqlLiteral(OilCtrlAdministratorPassword)}}';
                    COMMENT ON ROLE {{OilCtrlAdministratorUser}} IS '{{EscapeSqlLiteral(OilCtrlAdministratorDisplayName)}}';
                    GRANT admin TO {{OilCtrlAdministratorUser}};
                END
                $$;
                """,
                Utf8NoBom,
                cancellationToken);

            await RunProcessAsync(
                psqlPath,
                new[] { "-h", "localhost", "-p", port.ToString(), "-U", SuperUser, "-d", "postgres", "-f", scriptPath },
                log,
                cancellationToken,
                new Dictionary<string, string> { ["PGPASSWORD"] = SuperPassword });
        }
        finally
        {
            TryDeleteFile(scriptPath);
        }
    }

    private static string CreateOilCtrlApplicationRolesSql()
    {
        var builder = new StringBuilder();
        foreach (var role in OilCtrlApplicationRoles)
        {
            builder.AppendLine($$"""
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{role}}') THEN
                        CREATE ROLE {{role}} WITH
                          NOLOGIN
                          NOSUPERUSER
                          INHERIT
                          NOCREATEDB
                          NOCREATEROLE
                          NOREPLICATION
                          NOBYPASSRLS;
                    END IF;

                """);
        }

        return builder.ToString().TrimEnd();
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

    public static void InstallWindowsBinariesFromMaintenance(
        string archivePath,
        string installDir,
        int port,
        Action<string> log)
    {
        var dataDir = Path.Combine(installDir, "data");
        var serviceName = $"postgresql-x64-16-oilctrl-{port}";

        if (Directory.Exists(installDir) && Directory.EnumerateFileSystemEntries(installDir).Any())
        {
            throw new InvalidOperationException(
                Format(AppStrings.InstallerDirectoryNotEmpty, ("Directory", installDir)));
        }

        ExtractWindowsBinaries(archivePath, installDir, log);

        var binDir = Path.Combine(installDir, "bin");
        var psqlPath = Path.Combine(binDir, "psql.exe");
        var initDbPath = Path.Combine(binDir, "initdb.exe");
        var pgCtlPath = Path.Combine(binDir, "pg_ctl.exe");

        if (!File.Exists(psqlPath) || !File.Exists(initDbPath) || !File.Exists(pgCtlPath))
        {
            throw new FileNotFoundException(AppStrings.InstallerRequiredExecutablesNotFound);
        }

        PrepareWindowsDataDirectoryAcl(dataDir, log);
        InitializeWindowsDataDirectoryAsync(initDbPath, dataDir, log, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ConfigureWindowsPostgresDataDirectory(dataDir, port, log);
        RegisterAndStartWindowsServiceAsync(pgCtlPath, serviceName, installDir, dataDir, log, CancellationToken.None, runAsAdmin: false)
            .GetAwaiter()
            .GetResult();
    }

    private static async Task BackupAndRemoveWindowsPostgresAsync(
        string installDir,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var backupDirectory = Path.GetFullPath(@"C:\Prompribor");
        if (string.Equals(
                installDir.TrimEnd(Path.DirectorySeparatorChar),
                backupDirectory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallerSystemCancelledException(AppStrings.InstallerWindowsPostgresUnsafeReinstallDirectory);
        }

        log(Format(AppStrings.InstallerWindowsPostgresReinstallDetectedLog, ("Directory", installDir)));
        log(AppStrings.InstallerWindowsPostgresReinstallRequiresAdminLog);

        var logPath = Path.Combine(Path.GetTempPath(), $"pyramid-postgres-reinstall-{Guid.NewGuid():N}.log");
        var (fileName, arguments) = GetCurrentApplicationCommandLine(
            "--pyramid-reinstall-postgres",
            installDir,
            backupDirectory,
            logPath);

        var result = await RunProcessAsync(
            fileName,
            arguments.ToArray(),
            log,
            cancellationToken,
            runAsAdmin: true,
            throwOnError: false);

        if (File.Exists(logPath))
        {
            foreach (var line in File.ReadLines(logPath, Encoding.UTF8))
            {
                log(line);
            }
        }

        TryDeleteFile(logPath);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                Format(AppStrings.InstallerWindowsPostgresReinstallFailed, ("ExitCode", result.ExitCode.ToString())));
        }
    }

    public static void BackupAndRemoveWindowsPostgresFromMaintenance(
        string installDir,
        string backupDirectory,
        Action<string> log)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(AppStrings.InstallerInteractiveWindowsOnly);
        }

        installDir = Path.GetFullPath(installDir);
        backupDirectory = Path.GetFullPath(backupDirectory);
        if (string.Equals(
                installDir.TrimEnd(Path.DirectorySeparatorChar),
                backupDirectory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(AppStrings.InstallerWindowsPostgresUnsafeReinstallDirectory);
        }

        Directory.CreateDirectory(backupDirectory);
        var services = FindWindowsPostgresServicesForDirectory(installDir);
        TryBackupWindowsDatabase(installDir, backupDirectory, services, log);
        StopAndDeleteWindowsPostgresServices(services, log);
        StopWindowsPostgresServer(installDir, log);
        RemoveWindowsPostgresDirectory(installDir, log);
    }

    private static void TryBackupWindowsDatabase(
        string installDir,
        string backupDirectory,
        IReadOnlyList<string> services,
        Action<string> log)
    {
        log(AppStrings.InstallerWindowsPostgresBackupPreparingLog);
        var binDirectory = Path.Combine(installDir, "bin");
        var dataDirectory = Path.Combine(installDir, "data");
        var pgDumpPath = Path.Combine(binDirectory, "pg_dump.exe");
        var pgCtlPath = Path.Combine(binDirectory, "pg_ctl.exe");
        var partialBackupPath = string.Empty;

        try
        {
            if (!File.Exists(pgDumpPath))
            {
                throw new FileNotFoundException(
                    Format(AppStrings.InstallerWindowsPostgresBackupToolNotFound, ("Path", pgDumpPath)));
            }

            var port = TryReadPostgresPort(installDir)
                ?? services.Select(TryReadPortFromServiceName).FirstOrDefault(value => value.HasValue)
                ?? PreferredServerPort;

            var serverRunning = false;
            foreach (var serviceName in services)
            {
                if (GetWindowsServiceStateCode(serviceName) == 4)
                {
                    serverRunning = true;
                    break;
                }

                log(Format(
                    AppStrings.InstallerWindowsPostgresServiceStartingForBackupLog,
                    ("ServiceName", serviceName)));
                RunProcessAsync(
                        "sc.exe",
                        new[] { "start", serviceName },
                        log,
                        CancellationToken.None,
                        throwOnError: false,
                        outputEncoding: GetWindowsOemEncoding())
                    .GetAwaiter()
                    .GetResult();

                if (WaitForWindowsServiceState(serviceName, 4, TimeSpan.FromSeconds(30)))
                {
                    serverRunning = true;
                    break;
                }
            }

            if (!serverRunning && File.Exists(pgCtlPath) && Directory.Exists(dataDirectory))
            {
                log(AppStrings.InstallerWindowsPostgresManualStartForBackupLog);
                var startResult = RunProcessAsync(
                        pgCtlPath,
                        new[] { "start", "-D", dataDirectory, "-w", "-t", "30" },
                        log,
                        CancellationToken.None,
                        throwOnError: false,
                        outputEncoding: GetWindowsOemEncoding())
                    .GetAwaiter()
                    .GetResult();
                serverRunning = startResult.ExitCode == 0;
            }

            if (!serverRunning)
            {
                throw new InvalidOperationException(AppStrings.InstallerWindowsPostgresCouldNotStartForBackup);
            }

            var backupPath = GetUniqueDatabaseBackupPath(backupDirectory);
            partialBackupPath = backupPath + ".partial";
            var dumpResult = RunProcessAsync(
                    pgDumpPath,
                    new[]
                    {
                        "-h", "localhost",
                        "-p", port.ToString(CultureInfo.InvariantCulture),
                        "-U", SuperUser,
                        "-d", DatabaseName,
                        "-F", "c",
                        "-f", partialBackupPath
                    },
                    log,
                    CancellationToken.None,
                    new Dictionary<string, string> { ["PGPASSWORD"] = SuperPassword },
                    throwOnError: false,
                    outputEncoding: GetWindowsAnsiEncoding())
                .GetAwaiter()
                .GetResult();

            if (dumpResult.ExitCode != 0 || !File.Exists(partialBackupPath))
            {
                var reason = string.IsNullOrWhiteSpace(dumpResult.Error)
                    ? Format(AppStrings.InstallerWindowsPostgresBackupExitCode, ("ExitCode", dumpResult.ExitCode.ToString()))
                    : dumpResult.Error.Trim();
                throw new InvalidOperationException(reason);
            }

            File.Move(partialBackupPath, backupPath);
            partialBackupPath = string.Empty;
            log(Format(AppStrings.InstallerWindowsPostgresBackupCompletedLog, ("Path", backupPath)));
        }
        catch (Exception ex)
        {
            log(Format(
                AppStrings.InstallerWindowsPostgresBackupFailedContinuingLog,
                ("Reason", ex.Message)));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(partialBackupPath))
            {
                TryDeleteFile(partialBackupPath);
            }
        }
    }

    private static IReadOnlyList<string> FindWindowsPostgresServicesForDirectory(string installDir)
    {
        var result = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null)
        {
            return result;
        }

        var normalizedInstallDir = installDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var serviceName in services.GetSubKeyNames()
                     .Where(name => name.StartsWith("postgresql", StringComparison.OrdinalIgnoreCase)))
        {
            using var service = services.OpenSubKey(serviceName);
            var imagePath = service?.GetValue("ImagePath") as string;
            if (!string.IsNullOrWhiteSpace(imagePath) &&
                imagePath.Contains(normalizedInstallDir, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(serviceName);
            }
        }

        return result;
    }

    private static void StopAndDeleteWindowsPostgresServices(
        IReadOnlyList<string> services,
        Action<string> log)
    {
        foreach (var serviceName in services)
        {
            if (GetWindowsServiceStateCode(serviceName) is not null and not 1)
            {
                log(Format(AppStrings.InstallerWindowsPostgresStoppingServiceLog, ("ServiceName", serviceName)));
                RunProcessAsync(
                        "sc.exe",
                        new[] { "stop", serviceName },
                        log,
                        CancellationToken.None,
                        throwOnError: false,
                        outputEncoding: GetWindowsAnsiEncoding())
                    .GetAwaiter()
                    .GetResult();
                WaitForWindowsServiceState(serviceName, 1, TimeSpan.FromSeconds(30));
            }

            log(Format(AppStrings.InstallerWindowsPostgresDeletingServiceLog, ("ServiceName", serviceName)));
            var deleteResult = RunProcessAsync(
                    "sc.exe",
                    new[] { "delete", serviceName },
                    log,
                    CancellationToken.None,
                    throwOnError: false,
                    outputEncoding: GetWindowsOemEncoding())
                .GetAwaiter()
                .GetResult();
            if (deleteResult.ExitCode != 0 && GetWindowsServiceStateCode(serviceName) is not null)
            {
                throw new InvalidOperationException(Format(
                    AppStrings.InstallerWindowsPostgresDeleteServiceFailed,
                    ("ServiceName", serviceName),
                    ("ExitCode", deleteResult.ExitCode.ToString())));
            }
        }
    }

    private static void StopWindowsPostgresServer(string installDir, Action<string> log)
    {
        var pgCtlPath = Path.Combine(installDir, "bin", "pg_ctl.exe");
        var dataDirectory = Path.Combine(installDir, "data");
        if (!File.Exists(pgCtlPath) || !Directory.Exists(dataDirectory))
        {
            return;
        }

        var status = RunProcessAsync(
                pgCtlPath,
                new[] { "status", "-D", dataDirectory },
                _ => { },
                CancellationToken.None,
                throwOnError: false,
                outputEncoding: GetWindowsAnsiEncoding())
            .GetAwaiter()
            .GetResult();
        if (status.ExitCode != 0)
        {
            return;
        }

        log(AppStrings.InstallerWindowsPostgresStoppingServerLog);
        var stopResult = RunProcessAsync(
                pgCtlPath,
                new[] { "stop", "-D", dataDirectory, "-m", "fast", "-w", "-t", "30" },
                log,
                CancellationToken.None,
                throwOnError: false,
                outputEncoding: GetWindowsAnsiEncoding())
            .GetAwaiter()
            .GetResult();
        if (stopResult.ExitCode != 0)
        {
            throw new InvalidOperationException(Format(
                AppStrings.InstallerWindowsPostgresStopServerFailed,
                ("ExitCode", stopResult.ExitCode.ToString())));
        }
    }

    private static void RemoveWindowsPostgresDirectory(string installDir, Action<string> log)
    {
        if (!Directory.Exists(installDir))
        {
            return;
        }

        var root = Path.GetPathRoot(installDir);
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(installDir.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(AppStrings.InstallerWindowsPostgresUnsafeReinstallDirectory);
        }

        log(Format(AppStrings.InstallerWindowsPostgresRemovingDirectoryLog, ("Directory", installDir)));
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                Directory.Delete(installDir, recursive: true);
                log(AppStrings.InstallerWindowsPostgresPreviousInstallRemovedLog);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                Thread.Sleep(500);
            }
        }

        throw new IOException(
            Format(AppStrings.InstallerWindowsPostgresRemoveDirectoryFailed, ("Reason", lastError?.Message ?? string.Empty)),
            lastError);
    }

    private static int? GetWindowsServiceStateCode(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var managerHandle = OpenSCManager(null, null, ScManagerConnect);
        if (managerHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var serviceHandle = OpenService(managerHandle, serviceName, ServiceQueryStatus);
            if (serviceHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return QueryServiceStatusEx(
                    serviceHandle,
                    ScStatusProcessInfo,
                    out var status,
                    Marshal.SizeOf<ServiceStatusProcess>(),
                    out _)
                    ? (int)status.CurrentState
                    : null;
            }
            finally
            {
                CloseServiceHandle(serviceHandle);
            }
        }
        finally
        {
            CloseServiceHandle(managerHandle);
        }
    }

    private static bool WaitForWindowsServiceState(string serviceName, int expectedState, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (GetWindowsServiceStateCode(serviceName) == expectedState)
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }

    private static int? TryReadPortFromServiceName(string serviceName)
    {
        var match = Regex.Match(serviceName, @"-(\d{4,5})$");
        return match.Success && int.TryParse(match.Groups[1].Value, out var port) ? port : null;
    }

    private static string GetUniqueDatabaseBackupPath(string backupDirectory)
    {
        var baseName = $"OilCtrl-backup-{DateTime.Now:yyyyMMdd-HHmmss}";
        var path = Path.Combine(backupDirectory, baseName + ".backup");
        for (var suffix = 2; File.Exists(path) || File.Exists(path + ".partial"); suffix++)
        {
            path = Path.Combine(backupDirectory, $"{baseName}-{suffix}.backup");
        }

        return path;
    }

    private static async Task RunElevatedWindowsBinariesInstallAsync(
        string archivePath,
        string installDir,
        int port,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log(AppStrings.InstallerWindowsBinariesInstallRequiresAdminLog);
        var logPath = Path.Combine(Path.GetTempPath(), $"pyramid-postgres-binaries-install-{Guid.NewGuid():N}.log");
        var (fileName, arguments) = GetCurrentApplicationCommandLine(
            "--pyramid-install-postgres-binaries",
            archivePath,
            installDir,
            port.ToString(),
            logPath);

        var result = await RunProcessAsync(
            fileName,
            arguments.ToArray(),
            log,
            cancellationToken,
            runAsAdmin: true,
            throwOnError: false);

        if (File.Exists(logPath))
        {
            foreach (var line in File.ReadLines(logPath, Encoding.UTF8).TakeLast(160))
            {
                log(line);
            }
        }

        TryDeleteFile(logPath);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                Format(AppStrings.InstallerWindowsBinariesInstallFailed, ("ExitCode", result.ExitCode.ToString())));
        }
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

    private static void PrepareWindowsDataDirectoryAcl(string dataDir, Action<string> log)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(dataDir);
        var currentUserSid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(currentUserSid))
        {
            throw new InvalidOperationException(AppStrings.InstallerCurrentWindowsUserSidNotFound);
        }

        log(Format(AppStrings.InstallerPreparingWindowsDataDirectoryAclLog, ("Directory", dataDir)));

        RunIcaclsOrThrow(log, dataDir, "/inheritance:r");
        RunIcaclsOrThrow(
            log,
            dataDir,
            "/grant:r",
            $"*{currentUserSid}:(OI)(CI)F",
            "*S-1-5-18:(OI)(CI)F",
            "*S-1-5-32-544:(OI)(CI)F");
    }

    private static void RunIcaclsOrThrow(Action<string> log, string targetPath, params string[] arguments)
    {
        var allArguments = new[] { targetPath }.Concat(arguments).ToArray();
        var processLogPrefix = GetProcessLogPrefix("icacls.exe");
        var result = RunProcessAsync(
                "icacls.exe",
                allArguments,
                message =>
                {
                    if (message.StartsWith($"{processLogPrefix} >", StringComparison.Ordinal))
                    {
                        log(message);
                    }
                },
                CancellationToken.None,
                throwOnError: false)
            .GetAwaiter()
            .GetResult();

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(Format(
                AppStrings.InstallerWindowsDataDirectoryAclFailed,
                ("Directory", targetPath),
                ("ExitCode", result.ExitCode.ToString())));
        }
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
                cancellationToken,
                outputEncoding: GetWindowsAnsiEncoding());
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
        config = SetPostgresConfigValue(config, "lc_messages", "'C'");
        File.WriteAllText(configPath, config, Utf8NoBom);
    }

    private static string SetPostgresConfigValue(string config, string name, string value)
    {
        var pattern = $@"(?m)^\s*#?\s*{Regex.Escape(name)}\s*=.*$";
        var replacement = $"{name} = {value}";
        if (Regex.IsMatch(config, pattern))
        {
            return Regex.Replace(config, pattern, replacement);
        }

        return config.TrimEnd() + Environment.NewLine + replacement + Environment.NewLine;
    }

    private static async Task RegisterAndStartWindowsServiceAsync(
        string pgCtlPath,
        string serviceName,
        string installDir,
        string dataDir,
        Action<string> log,
        CancellationToken cancellationToken,
        bool runAsAdmin = true)
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
                runAsAdmin: runAsAdmin,
                throwOnError: false);

            if (File.Exists(elevatedLogPath))
            {
                log(Format(AppStrings.InstallerServiceInstallationLog, ("Path", elevatedLogPath)));
                foreach (var line in File.ReadLines(elevatedLogPath, Encoding.UTF8).TakeLast(80))
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
        return @"C:\Prompribor\PostgreSQL";
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
                foreach (var line in File.ReadLines(elevatedLogPath, Encoding.UTF8).TakeLast(40))
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

        var packagesRoot = Path.Combine(installersDir, "packages");
        var preferredDirectories = Directory.Exists(packagesRoot)
            ? Directory.EnumerateDirectories(packagesRoot, "*", SearchOption.AllDirectories).Prepend(packagesRoot)
            : Enumerable.Empty<string>();

        var fallbackDirectories = Directory
            .EnumerateDirectories(installersDir, "*", SearchOption.AllDirectories)
            .Prepend(installersDir)
            .Where(path => !path.StartsWith(packagesRoot, StringComparison.OrdinalIgnoreCase));

        return preferredDirectories
            .Concat(fallbackDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(HasRequiredLinuxDebPackages)
            .OrderByDescending(path => path.StartsWith(packagesRoot, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool HasRequiredLinuxDebPackages(string directory)
    {
        return HasNonEmptyDeb(directory, "postgresql-client-common_*_all.deb") &&
               HasNonEmptyDeb(directory, "postgresql-common_*_all.deb") &&
               HasNonEmptyDeb(directory, "postgresql-client-16_*_amd64.deb") &&
               HasNonEmptyDeb(directory, "postgresql-16_*_amd64.deb") &&
               HasNonEmptyDeb(directory, "libpq5_*_amd64.deb");
    }

    private static bool HasNonEmptyDeb(string directory, string pattern)
    {
        return Directory
            .EnumerateFiles(directory, pattern)
            .Any(path => new FileInfo(path).Length > 0);
    }

    private static async Task RunLinuxSetupScriptsAsync(Action<string> log, CancellationToken cancellationToken)
    {
        var scriptsDirectory = Path.Combine(AppContext.BaseDirectory, "Installers", "sh");
        if (!Directory.Exists(scriptsDirectory))
        {
            return;
        }

        var scripts = Directory
            .EnumerateFiles(scriptsDirectory, "*.sh", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var script in scripts)
        {
            log(Format(AppStrings.InstallerLinuxSetupScriptLog, ("Path", script)));
            await RunLinuxPrivilegedScriptAsync(
                $$"""
                set -eu
                sh {{ShellQuote(script)}}
                """,
                log,
                cancellationToken);
        }
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
            if grep -Eq "^[#[:space:]]*lc_messages[[:space:]]*=" /etc/postgresql/{{LinuxPostgresVersion}}/{{clusterName}}/postgresql.conf; then
              sed -i -E "s/^[#[:space:]]*lc_messages[[:space:]]*=.*/lc_messages = 'C'/" /etc/postgresql/{{LinuxPostgresVersion}}/{{clusterName}}/postgresql.conf
            else
              printf "\nlc_messages = 'C'\n" >> /etc/postgresql/{{LinuxPostgresVersion}}/{{clusterName}}/postgresql.conf
            fi
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

        if (await CommandExistsAsync("pkexec", cancellationToken))
        {
            runners.Add(new LinuxPrivilegedRunner("pkexec", new[] { "sh", scriptPath }));
        }

        if (await CommandExistsAsync("fly-su", cancellationToken))
        {
            runners.Add(new LinuxPrivilegedRunner("fly-su", new[] { "-d", "-p", "100", "-c", scriptCommand }));
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
                """,
                Utf8NoBom);
        }
    }

    private static void EnsureSharedAppSettingsExists(int port)
    {
        var sharedRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        if (!LooksLikeSingleBundleRoot(sharedRoot))
        {
            return;
        }

        var sharedSettingsPath = Path.Combine(sharedRoot, "appsettings.json");
        var directory = Path.GetDirectoryName(sharedSettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        JsonObject root;
        if (File.Exists(sharedSettingsPath))
        {
            root = JsonNode.Parse(File.ReadAllText(sharedSettingsPath, Encoding.UTF8)) as JsonObject
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

        database["Host"] = "localhost";
        database["Username"] = SuperUser;
        database["Password"] = string.Empty;
        database["CredentialID"] = PostgresCredentialId;
        database["DBName"] = DatabaseName;
        database["Port"] = port.ToString();

        ConfigurationFileSafety.WriteAllTextAtomic(
            sharedSettingsPath,
            root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }),
            Utf8NoBom);
    }

    private static bool LooksLikeSingleBundleRoot(string directory)
    {
        return Directory.Exists(Path.Combine(directory, "Pyramid"))
            && Directory.Exists(Path.Combine(directory, "OilCtrlCfg"))
            && Directory.Exists(Path.Combine(directory, "ASNCtrl_Linux"))
            && Directory.Exists(Path.Combine(directory, "R_Designer_L"));
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string> log,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null,
        bool runAsAdmin = false,
        bool throwOnError = true,
        Encoding? outputEncoding = null)
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

        if (!runAsAdmin && outputEncoding is not null)
        {
            startInfo.StandardOutputEncoding = outputEncoding;
            startInfo.StandardErrorEncoding = outputEncoding;
        }

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
        if (!runAsAdmin)
        {
            process.WaitForExit();
        }

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

    private static Encoding GetWindowsAnsiEncoding()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Encoding.UTF8;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (ArgumentException)
        {
            return Encoding.Default;
        }
    }

    private static Encoding GetWindowsOemEncoding()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Encoding.UTF8;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (ArgumentException)
        {
            return Encoding.Default;
        }
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

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr serviceManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr service,
        int infoLevel,
        out ServiceStatusProcess status,
        int bufferSize,
        out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
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
