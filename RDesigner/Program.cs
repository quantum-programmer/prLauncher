using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using Pyramid.Resources;
using Pyramid.Security;
using Pyramid.Services;

namespace Pyramid
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            if (TryRunMaintenanceCommand(args, out var exitCode))
            {
                Environment.ExitCode = exitCode;
                return;
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "Pyramid.log"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: null,
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            try
            {
                LocalizationManager.InitializeResources();
                Log.Information(AppStrings.ApplicationStarted);

                App.Host = CreateHostBuilder(args).Build();
                App.Host.Start();
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, AppStrings.ApplicationFailed);
            }
            finally
            {
                App.Host?.StopAsync().GetAwaiter().GetResult();
                App.Host?.Dispose();
                Log.CloseAndFlush();
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, configuration) =>
                {
                    var appBase = AppContext.BaseDirectory;
                    var sharedSettings = Path.GetFullPath(Path.Combine(appBase, "..", "appsettings.json"));

                    configuration.SetBasePath(appBase);
                    configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    configuration.AddJsonFile(
                        $"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                        optional: true,
                        reloadOnChange: true);

                    if (File.Exists(sharedSettings))
                    {
                        configuration.AddJsonFile(sharedSettings, optional: true, reloadOnChange: true);
                    }
                })
                .ConfigureServices((context, services) =>
                {
                    new Startup().ConfigureServices(services, context.Configuration);
                });

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        private static bool TryRunMaintenanceCommand(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args.Length == 3 &&
                string.Equals(args[0], "--pyramid-save-database-credential", StringComparison.Ordinal))
            {
                try
                {
                    var credentialId = args[1];
                    var passwordFile = args[2];
                    var password = File.ReadAllText(passwordFile);
                    DatabaseCredentialProvider.Create().SavePassword(credentialId, password);
                    return true;
                }
                catch
                {
                    exitCode = 1;
                    return true;
                }
            }

            if (args.Length == 4 &&
                string.Equals(args[0], "--pyramid-install-applications", StringComparison.Ordinal))
            {
                try
                {
                    var sourceRoot = args[1];
                    var targetRoot = args[2];
                    var logPath = args[3];
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

                    using var writer = new StreamWriter(logPath, append: false);
                    ProductApplicationInstaller.InstallFromBundle(
                        sourceRoot,
                        targetRoot,
                        message =>
                        {
                            writer.WriteLine(message);
                            writer.Flush();
                        });
                    return true;
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(args[3], ex.Message + Environment.NewLine);
                    }
                    catch
                    {
                        // Nothing else can be reported from the maintenance process.
                    }

                    exitCode = 1;
                    return true;
                }
            }

            return false;
        }
    }
}
