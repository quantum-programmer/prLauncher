using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using RDesigner.Views;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RDesigner.Resources;

namespace RDesigner
{
    public partial class App : Application
    {
        public static IHost? Host { get; set; }
        public IServiceProvider _serviceProvider = null!;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            LocalizationManager.InitializeResources();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            _serviceProvider = Host?.Services
                ?? throw new InvalidOperationException("Application host is not initialized.");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    Content = _serviceProvider.GetRequiredService<InstallerView>()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
