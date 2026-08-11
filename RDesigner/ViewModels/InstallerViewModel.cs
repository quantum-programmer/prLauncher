using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pyramid.Resources;
using Pyramid.Services;
using Serilog;

namespace Pyramid.ViewModels;

public partial class InstallerViewModel : ViewModelBase
{
    private readonly NativePostgresInstaller installer;
    private readonly LinuxWineInstaller wineInstaller;
    private readonly ProductApplicationInstaller applicationInstaller;
    private readonly object logLock = new();
    private readonly StringBuilder logBuilder = new();
    private Func<string> statusProvider = () => AppStrings.InstallerReadyStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallPostgresCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallArmCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenServerInstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackToWelcomeCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunPostgresWizardCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckPostgresCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string status = AppStrings.InstallerReadyStatus;

    [ObservableProperty]
    private string logText = string.Empty;

    [ObservableProperty]
    private string windowsInstallDirectory = NativePostgresInstaller.GetDefaultWindowsInstallDir();

    [ObservableProperty]
    private bool isWelcomeVisible = true;

    [ObservableProperty]
    private bool isServerInstallVisible;

    [ObservableProperty]
    private bool isArmInstallProgressVisible;

    [ObservableProperty]
    private bool isArmInstallCompletedVisible;

    [ObservableProperty]
    private bool isArmInstallCancelledVisible;

    [ObservableProperty]
    private bool isArmInstallSystemCancelledVisible;

    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsLinux => OperatingSystem.IsLinux();

    public InstallerViewModel(
        NativePostgresInstaller installer,
        LinuxWineInstaller wineInstaller,
        ProductApplicationInstaller applicationInstaller)
    {
        this.installer = installer;
        this.wineInstaller = wineInstaller;
        this.applicationInstaller = applicationInstaller;
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        SetStatus(() => AppStrings.InstallerReadyStatus);
        AppendLog(AppStrings.InstallerPutInstallerLog);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void OpenServerInstall()
    {
        IsWelcomeVisible = false;
        IsServerInstallVisible = true;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void BackToWelcome()
    {
        IsServerInstallVisible = false;
        IsWelcomeVisible = true;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task InstallPostgresAsync()
    {
        await RunAsync(() => AppStrings.InstallerInstallOperation, log => installer.InstallAsync(WindowsInstallDirectory, log));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task InstallArmAsync()
    {
        ApplicationInstallResult? installResult = null;
        var installCompleted = false;
        var reinstallExisting = false;
        ResetArmInstallIndicator();

        if (applicationInstaller.IsArmInstalled())
        {
            var installRoot = applicationInstaller.GetArmInstallRoot();
            AppendLog(AppStrings.InstallerArmAlreadyInstalledLog.Replace("{Directory}", installRoot, StringComparison.Ordinal));
            reinstallExisting = await ConfirmReinstallArmAsync(installRoot);
            if (!reinstallExisting)
            {
                AppendLog(AppStrings.InstallerArmInstallCancelledLog);
                IsArmInstallCancelledVisible = true;
                return;
            }
        }

        IsArmInstallProgressVisible = true;

        await RunAsync(
            () => AppStrings.InstallerArmInstallOperation,
            async log =>
            {
                var postgresPort = await installer.InstallAsync(WindowsInstallDirectory, log);
                await wineInstaller.InstallAsync(log, ConfirmReinstallWineAsync);
                installResult = await applicationInstaller.InstallAsync(postgresPort, reinstallExisting, log);
                installCompleted = true;
            });

        IsArmInstallProgressVisible = false;

        if (installResult is null)
        {
            return;
        }

        if (installCompleted)
        {
            IsArmInstallCompletedVisible = true;
        }

        if (await ConfirmLaunchOilCtrlCfgAsync())
        {
            try
            {
                var process = applicationInstaller.LaunchOilCtrlCfg(installResult.OilCtrlCfgPath);
                await Task.Delay(1500);

                if (process.HasExited)
                {
                    AppendLog(AppStrings.InstallerOilCtrlCfgExitedLog
                        .Replace("{ExitCode}", process.ExitCode.ToString(), StringComparison.Ordinal)
                        .Replace("{Path}", installResult.OilCtrlCfgPath, StringComparison.Ordinal));
                }
                else
                {
                    AppendLog(AppStrings.InstallerOilCtrlCfgStartedLog.Replace("{Path}", installResult.OilCtrlCfgPath, StringComparison.Ordinal));
                }
            }
            catch (Exception ex)
            {
                SetStatus(() => AppStrings.InstallerErrorStatus);
                Log.Error(ex, "OilCtrlCfg launch failed.");
                AppendLog(AppStrings.InstallerOilCtrlCfgLaunchFailedLog.Replace("{Message}", ex.Message, StringComparison.Ordinal));
            }
        }
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
            Log.Error(ex, AppStrings.InstallerFolderSelectionFailedLog.Replace("{Message}", ex.Message, StringComparison.Ordinal));
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
            Log.Error(ex, AppStrings.InstallerCopyLogFailedLog.Replace("{Message}", ex.Message, StringComparison.Ordinal));
            AppendLog(AppStrings.InstallerCopyLogFailedLog.Replace("{Message}", ex.Message, StringComparison.Ordinal));
        }
    }

    private bool CanRun()
    {
        return !IsBusy;
    }

    private void ResetArmInstallIndicator()
    {
        IsArmInstallProgressVisible = false;
        IsArmInstallCompletedVisible = false;
        IsArmInstallCancelledVisible = false;
        IsArmInstallSystemCancelledVisible = false;
    }

    private static async Task<bool> ConfirmLaunchOilCtrlCfgAsync()
    {
        return await ShowConfirmationDialogAsync(
            "Pyramid",
            AppStrings.InstallerLaunchOilCtrlCfgQuestion,
            AppStrings.InstallerYesButton,
            AppStrings.InstallerNoButton);
    }

    private static async Task<bool> ConfirmReinstallArmAsync(string installRoot)
    {
        return await ShowConfirmationDialogAsync(
            "Pyramid",
            AppStrings.InstallerArmReinstallQuestion
                .Replace("{Directory}", installRoot, StringComparison.Ordinal)
                .Replace("{NewLine}", Environment.NewLine, StringComparison.Ordinal),
            AppStrings.InstallerArmReinstallButton,
            AppStrings.InstallerCancelButton);
    }

    private static async Task<bool> ConfirmReinstallWineAsync(string installedVersion)
    {
        return await ShowConfirmationDialogAsync(
            "Pyramid",
            AppStrings.InstallerWineReinstallQuestion
                .Replace("{Version}", installedVersion, StringComparison.Ordinal)
                .Replace("{NewLine}", Environment.NewLine, StringComparison.Ordinal),
            AppStrings.InstallerYesButton,
            AppStrings.InstallerNoButton);
    }

    private static async Task<bool> ShowConfirmationDialogAsync(string title, string message, string yesText, string noText)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            return false;
        }

        var result = false;
        var window = new Window
        {
            Title = title,
            Width = 460,
            Height = 210,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var yesButton = new Button
        {
            Content = yesText,
            MinWidth = 120,
            Height = 34,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        yesButton.Click += (_, _) =>
        {
            result = true;
            window.Close();
        };

        var noButton = new Button
        {
            Content = noText,
            MinWidth = 90,
            Height = 34,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        noButton.Click += (_, _) =>
        {
            result = false;
            window.Close();
        };

        window.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = 15
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { yesButton, noButton }
                }
            }
        };

        await window.ShowDialog(desktop.MainWindow);
        return result;
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
        catch (InstallerSystemCancelledException ex)
        {
            SetStatus(() => AppStrings.InstallerSystemCancelledStatus);
            IsArmInstallProgressVisible = false;
            IsArmInstallSystemCancelledVisible = true;
            Log.Warning(ex, "Installer operation was cancelled by system validation.");
            AppendLog(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus(() => AppStrings.InstallerErrorStatus);
            Log.Error(ex, "Installer operation failed.");
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
                Log.Information("{InstallerMessage}", message);
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
