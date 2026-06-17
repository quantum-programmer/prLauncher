using Avalonia.Controls;
using Avalonia.Input;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Pyramid.Resources;

namespace Pyramid.Views
{
    public partial class MainWindow : Window
    {        
        public MainWindow()
        {            
            InitializeComponent();
            AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private async void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F1)
            {
                e.Handled = true;
                var aboutWindow = new AboutWindow();
                await aboutWindow.ShowDialog(this);
                return;
            }

            if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                OpenLogsDirectory();
                return;
            }

            LocalizationManager.TryToggleLanguage(e);
        }

        private static void OpenLogsDirectory()
        {
            var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logsDirectory);

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = logsDirectory,
                        UseShellExecute = true
                    });
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        UseShellExecute = false,
                        ArgumentList = { logsDirectory }
                    });
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "open",
                        UseShellExecute = false,
                        ArgumentList = { logsDirectory }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, AppStrings.LogsDirectoryOpenFailed, logsDirectory);
            }
        }
    }
}
