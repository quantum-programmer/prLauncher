using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RDesigner.Services;

public sealed class NativePostgresInstaller
{
    public const string RequiredVersion = "16.2";
    public const string DatabaseName = "OilCtrl";
    public const string SuperUser = "postgres";
    public const string SuperPassword = "j06gOuqDHwWkvpWf";
    public const int ServerPort = 5433;

    private readonly string initSqlPath = Path.Combine(AppContext.BaseDirectory, "Installers", "oilctrl-init.sql");

    public async Task InstallAsync(Action<string> log, CancellationToken cancellationToken = default)
    {
        log($"OS: {RuntimeInformation.OSDescription}");
        log($"Target PostgreSQL version: {RequiredVersion}");

        if (OperatingSystem.IsWindows())
        {
            await InstallOnWindowsAsync(log, cancellationToken);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await InstallOnLinuxAsync(log, cancellationToken);
            return;
        }

        throw new PlatformNotSupportedException("Only Windows and Linux are supported.");
    }

    public async Task CheckAsync(Action<string> log, CancellationToken cancellationToken = default)
    {
        var psqlPath = await FindPsqlAsync(cancellationToken);
        if (psqlPath is null)
        {
            log("psql was not found. PostgreSQL is not installed or its bin directory is not in PATH.");
            return;
        }

        log($"psql: {psqlPath}");
        await RunProcessAsync(
            psqlPath,
            new[] { "-h", "localhost", "-p", ServerPort.ToString(), "-U", SuperUser, "-d", DatabaseName, "-c", "select version();" },
            log,
            cancellationToken,
            new Dictionary<string, string> { ["PGPASSWORD"] = SuperPassword });
    }

    private async Task InstallOnWindowsAsync(Action<string> log, CancellationToken cancellationToken)
    {
        var psqlPath = await FindPsqlAsync(cancellationToken);

        if (psqlPath is null)
        {
            var installer = FindWindowsInstaller();
            if (installer is null)
            {
                throw new FileNotFoundException(
                    "PostgreSQL 16.2 installer was not found. Put postgresql-16.2-*-windows-x64.exe into the Installers folder next to the application.");
            }

            log($"Installer: {installer}");
            log("Starting unattended PostgreSQL installation. Administrator privileges are required.");

            var installDir = GetWindowsInstallDir();

            await RunProcessAsync(
                installer,
                new[]
                {
                    "--mode", "unattended",
                    "--unattendedmodeui", "minimal",
                    "--superpassword", SuperPassword,
                    "--serverport", ServerPort.ToString(),
                    "--servicename", "postgresql-x64-16-oilctrl",
                    "--prefix", installDir
                },
                log,
                cancellationToken,
                runAsAdmin: true);
        }
        else
        {
            log($"PostgreSQL client already exists: {psqlPath}");
        }

        psqlPath = await FindPsqlAsync(cancellationToken)
            ?? throw new InvalidOperationException("psql was not found after installation.");

        await InitializeDatabaseAsync(psqlPath, log, cancellationToken);
    }

    private async Task InstallOnLinuxAsync(Action<string> log, CancellationToken cancellationToken)
    {
        var psqlPath = await FindPsqlAsync(cancellationToken);
        if (psqlPath is null)
        {
            log("psql was not found.");
            log("Linux native installation depends on the target distribution. For production, place approved offline PostgreSQL 16.2 packages into Installers/linux and finalize an Astra/Debian/RHEL-specific installation flow.");
            log("After PostgreSQL is installed, run initialization from this launcher again.");
            return;
        }

        log($"PostgreSQL client found: {psqlPath}");
        await InitializeDatabaseAsync(psqlPath, log, cancellationToken);
    }

    private async Task InitializeDatabaseAsync(string psqlPath, Action<string> log, CancellationToken cancellationToken)
    {
        EnsureInitSqlExists();

        log($"Checking database {DatabaseName}.");
        var databaseExists = await QueryScalarAsync(
            psqlPath,
            "postgres",
            $"select 1 from pg_database where datname = '{DatabaseName}';",
            cancellationToken);

        if (databaseExists.Trim() != "1")
        {
            log($"Creating database {DatabaseName}.");
            await RunProcessAsync(
                psqlPath,
                new[] { "-h", "localhost", "-p", ServerPort.ToString(), "-U", SuperUser, "-d", "postgres", "-c", $"CREATE DATABASE \"{DatabaseName}\";" },
                log,
                cancellationToken,
                new Dictionary<string, string> { ["PGPASSWORD"] = SuperPassword });
        }
        else
        {
            log($"Database {DatabaseName} already exists.");
        }

        log("Applying oilctrl-init.sql.");
        await RunProcessAsync(
            psqlPath,
            new[] { "-h", "localhost", "-p", ServerPort.ToString(), "-U", SuperUser, "-d", DatabaseName, "-f", initSqlPath },
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
            .EnumerateFiles(installersDir, "postgresql-16.2-*-windows-x64.exe", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private async Task<string?> FindPsqlAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(Path.Combine(GetWindowsInstallDir(), "bin", "psql.exe"));
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

    private static string GetWindowsInstallDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PostgreSQL",
            "16-oilctrl");
    }

    private static async Task<bool> CommandExistsAsync(string command, CancellationToken cancellationToken)
    {
        var locator = OperatingSystem.IsWindows() ? "where.exe" : "which";
        var result = await RunProcessAsync(locator, new[] { command }, _ => { }, cancellationToken, throwOnError: false);
        return result.ExitCode == 0;
    }

    private async Task<string> QueryScalarAsync(string psqlPath, string database, string sql, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            psqlPath,
            new[] { "-h", "localhost", "-p", ServerPort.ToString(), "-U", SuperUser, "-d", database, "-tAc", sql },
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
                Encoding.UTF8);
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

        log($"> {fileName} {string.Join(" ", arguments.Select(MaskSensitiveArgument))}");

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var errors = new StringBuilder();

        if (!runAsAdmin)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                output.AppendLine(e.Data);
                log(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                errors.AppendLine(e.Data);
                log(e.Data);
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
            throw new InvalidOperationException($"Command failed with exit code {result.ExitCode}: {fileName}");
        }

        return result;
    }

    private static string MaskSensitiveArgument(string argument)
    {
        return argument == SuperPassword ? "********" : argument;
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
