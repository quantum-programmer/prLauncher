using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
//using Serilog.Sinks.Console;
using Serilog.Sinks.File;
using Serilog.Events;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RDesigner.Resources;

namespace RDesigner
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Настройка Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug() // Минимальный уровень логирования
                .WriteTo.Console() // Логи в консоль (опционально)
                .WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "RDesigner.log"), // Путь к файлу логов
                    rollingInterval: RollingInterval.Day, // Ротация логов по дням
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10 МБ
                    rollOnFileSizeLimit: true, // Создавать новый файл при превышении размера
                    retainedFileCountLimit: null, // Не удалять старые файлы логов автоматически
                    restrictedToMinimumLevel: LogEventLevel.Information, // Минимальный уровень для файла
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}" // Формат логов
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
            catch (OptionsValidationException ex)
            {
                var failure = ex.Failures.FirstOrDefault() ?? AppStrings.InvalidDatabaseSettings;
                var message = string.Format(AppStrings.DatabaseConnectionFailedFormat, failure);

                Log.Fatal(
                    ex,
                    AppStrings.InvalidDatabaseSettingsLog,
                    string.Join(" ", ex.Failures));
                ShowStartupError(AppStrings.StartupDatabaseSettingsTitle, message);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, AppStrings.ApplicationFailed);
            }
            finally
            {
                App.Host?.StopAsync().GetAwaiter().GetResult();
                App.Host?.Dispose();
                Log.CloseAndFlush(); // Закрыть и очистить логгер
            }
        }

        private static void ShowStartupError(string title, string message)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                MessageBox(IntPtr.Zero, message, title, 0x00000010);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && ShowLinuxStartupError(title, message))
            {
                return;
            }
                        
            Log.Information($"{title}: {message}");
        }

        private static bool ShowLinuxStartupError(string title, string message)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                return false;
            }

            return TryShowLinuxDialog("zenity", "--error", "--title", title, "--text", message)
                || TryShowLinuxDialog("kdialog", "--title", title, "--error", message)
                || TryShowLinuxDialog("xmessage", "-center", "-title", title, message);
        }

        private static bool TryShowLinuxDialog(string fileName, params string[] arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        UseShellExecute = false
                    }
                };

                foreach (var argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                process.Start();
                process.WaitForExit();
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, configuration) =>
                {
                    configuration.SetBasePath(AppContext.BaseDirectory);
                    configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    configuration.AddJsonFile(
                        $"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                        optional: true,
                        reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    new Startup().ConfigureServices(services, context.Configuration);
                });

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
