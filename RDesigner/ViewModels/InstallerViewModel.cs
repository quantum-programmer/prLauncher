using System;
using System.Text;
using System.Threading.Tasks;
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
    [NotifyCanExecuteChangedFor(nameof(CheckPostgresCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string status = "Ready to install PostgreSQL 16.2";

    [ObservableProperty]
    private string logText = string.Empty;

    public InstallerViewModel(NativePostgresInstaller installer)
    {
        this.installer = installer;
        AppendLog("Put the PostgreSQL 16.2 Windows installer into the Installers folder next to the application.");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task InstallPostgresAsync()
    {
        await RunAsync("PostgreSQL installation", log => installer.InstallAsync(log));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CheckPostgresAsync()
    {
        await RunAsync("PostgreSQL check", log => installer.CheckAsync(log));
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
