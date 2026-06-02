using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Pyramid.Resources;
using Pyramid.Views;

namespace Pyramid;

public partial class App : Application
{
    public static IServiceProvider? Services { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LocalizationManager.InitializeResources();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var serviceProvider = Services
            ?? throw new InvalidOperationException("Application services are not initialized.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = serviceProvider.GetRequiredService<MainWindow>();
            desktop.MainWindow.Content = serviceProvider.GetRequiredService<InstallerView>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
