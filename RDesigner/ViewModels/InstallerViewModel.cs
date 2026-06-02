using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDesigner.Services;

namespace RDesigner.ViewModels;

public partial class InstallerViewModel : ViewModelBase
{
    private readonly NativePostgresInstaller installer;
    private readonly StringBuilder logBuilder = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallPostgresCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunPostgresWizardCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckPostgresCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string status = "Ready to install PostgreSQL 16.14";

    [ObservableProperty]
    private string logText = string.Empty;

    [ObservableProperty]
    private string windowsInstallDirectory = NativePostgresInstaller.GetDefaultWindowsInstallDir();

    public InstallerViewModel(NativePostgresInstaller installer)
    {
        this.installer = installer;
        AppendLog("Put the PostgreSQL 16.14 Windows installer into the Installers folder next to the application.");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task InstallPostgresAsync()
    {
        await RunAsync("PostgreSQL installation", log => installer.InstallAsync(WindowsInstallDirectory, log));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunPostgresWizardAsync()
    {
        await RunAsync("PostgreSQL GUI installer", log => installer.RunInteractiveWindowsInstallerAsync(WindowsInstallDirectory, log));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CheckPostgresAsync()
    {
        await RunAsync("PostgreSQL check", log => installer.CheckAsync(WindowsInstallDirectory, log));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task BrowseInstallDirectoryAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is null)
            {
                AppendLog("Main window was not found. Folder dialog cannot be opened.");
                return;
            }

            var startLocation = await desktop.MainWindow.StorageProvider.TryGetFolderFromPathAsync(WindowsInstallDirectory);
            var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select PostgreSQL installation folder",
                AllowMultiple = false,
                SuggestedStartLocation = startLocation
            });

            var folder = folders.FirstOrDefault();
            var selectedDirectory = folder?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(selectedDirectory))
            {
                WindowsInstallDirectory = selectedDirectory;
                AppendLog($"Install directory selected: {selectedDirectory}");
            }
        }
        catch (Exception ex)
        {
            Status = "Error";
            AppendLog($"Folder selection failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow?.Clipboard is null)
            {
                AppendLog("Clipboard is not available.");
                return;
            }

            await desktop.MainWindow.Clipboard.SetTextAsync(LogText);
            AppendLog("Log copied to clipboard.");
        }
        catch (Exception ex)
        {
            Status = "Error";
            AppendLog($"Copy log failed: {ex.Message}");
        }
    }

    private bool CanRun()
    {
        return !IsBusy;
    }

    private async Task RunAsync(string operationName, Func<Action<string>, Task> operation)
    {
        IsBusy = true;
        Status = operationName;
        AppendLog(string.Empty);
        AppendLog($"=== {operationName} ===");

        try
        {
            await operation(AppendLog);
            Status = "Operation completed";
            AppendLog("Done.");
        }
        catch (Exception ex)
        {
            Status = "Error";
            AppendLog(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            logBuilder.AppendLine();
        }
        else
        {
            logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        LogText = logBuilder.ToString();
    }
}
