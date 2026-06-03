using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RDesigner.Resources;
using RDesigner.Services;

namespace RDesigner.ViewModels;

public partial class InstallerViewModel : ViewModelBase
{
    private readonly NativePostgresInstaller installer;
    private readonly object logLock = new();
    private readonly StringBuilder logBuilder = new();
    private Func<string> statusProvider = () => AppStrings.InstallerReadyStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallPostgresCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunPostgresWizardCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckPostgresCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string status = AppStrings.InstallerReadyStatus;

    [ObservableProperty]
    private string logText = string.Empty;

    [ObservableProperty]
    private string windowsInstallDirectory = NativePostgresInstaller.GetDefaultWindowsInstallDir();

    public InstallerViewModel(NativePostgresInstaller installer)
    {
        this.installer = installer;
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        SetStatus(() => AppStrings.InstallerReadyStatus);
        AppendLog(AppStrings.InstallerPutInstallerLog);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task InstallPostgresAsync()
    {
        await RunAsync(() => AppStrings.InstallerInstallOperation, log => installer.InstallAsync(WindowsInstallDirectory, log));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunPostgresWizardAsync()
    {
        await RunAsync(() => AppStrings.InstallerWizardOperation, log => installer.RunInteractiveWindowsInstallerAsync(WindowsInstallDirectory, log));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CheckPostgresAsync()
    {
        await RunAsync(() => AppStrings.InstallerCheckOperation, log => installer.CheckAsync(WindowsInstallDirectory, log));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task BrowseInstallDirectoryAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is null)
            {
                AppendLog(AppStrings.InstallerMainWindowNotFoundLog);
                return;
            }

            var startLocation = await desktop.MainWindow.StorageProvider.TryGetFolderFromPathAsync(WindowsInstallDirectory);
            var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = AppStrings.InstallerSelectPostgresFolderTitle,
                AllowMultiple = false,
                SuggestedStartLocation = startLocation
            });

            var folder = folders.FirstOrDefault();
            var selectedDirectory = folder?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(selectedDirectory))
            {
                WindowsInstallDirectory = selectedDirectory;
                AppendLog(AppStrings.InstallerDirectorySelectedLog.Replace("{Directory}", selectedDirectory, StringComparison.Ordinal));
            }
        }
        catch (Exception ex)
        {
            SetStatus(() => AppStrings.InstallerErrorStatus);
            AppendLog(AppStrings.InstallerFolderSelectionFailedLog.Replace("{Message}", ex.Message, StringComparison.Ordinal));
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
                AppendLog(AppStrings.InstallerClipboardUnavailableLog);
                return;
            }

            await desktop.MainWindow.Clipboard.SetTextAsync(LogText);
            AppendLog(AppStrings.InstallerLogCopiedLog);
        }
        catch (Exception ex)
        {
            SetStatus(() => AppStrings.InstallerErrorStatus);
            AppendLog(AppStrings.InstallerCopyLogFailedLog.Replace("{Message}", ex.Message, StringComparison.Ordinal));
        }
    }

    private bool CanRun()
    {
        return !IsBusy;
    }

    private async Task RunAsync(Func<string> operationNameProvider, Func<Action<string>, Task> operation)
    {
        IsBusy = true;
        SetStatus(operationNameProvider);
        var operationName = operationNameProvider();
        AppendLog(string.Empty);
        AppendLog($"=== {operationName} ===");

        try
        {
            await operation(AppendLog);
            SetStatus(() => AppStrings.InstallerCompletedStatus);
            AppendLog(AppStrings.InstallerDoneLog);
        }
        catch (Exception ex)
        {
            SetStatus(() => AppStrings.InstallerErrorStatus);
            AppendLog(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendLog(string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(message));
            return;
        }

        lock (logLock)
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

    private void SetStatus(Func<string> provider)
    {
        statusProvider = provider;
        Status = statusProvider();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Status = statusProvider();
    }
}
