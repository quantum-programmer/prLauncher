using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Pyramid.Resources;
using Pyramid.Services;
using Serilog;
using Serilog.Events;

namespace Pyramid;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            LocalizationManager.InitializeResources();
            Log.Information(AppStrings.ApplicationStarted);

            App.Services = CreateServices();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, AppStrings.ApplicationFailed);
            ShowStartupError(AppStrings.StartupErrorTitle, ex.Message);
        }
        finally
        {
            if (App.Services is IDisposable disposable)
            {
                disposable.Dispose();
            }

            Log.CloseAndFlush();
        }
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        Startup.ConfigureServices(services);
        return services.BuildServiceProvider();
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

        Log.Information("{Title}: {Message}", title, message);
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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
