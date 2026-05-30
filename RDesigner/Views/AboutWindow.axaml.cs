using Avalonia.Controls;
using Avalonia.Input;
using RDesigner.Resources;
using System.Diagnostics;
using System.Reflection;

namespace RDesigner.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            DataContext = new AboutWindowInfo();
            AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            LocalizationManager.TryToggleLanguage(e);
        }
    }

    public sealed class AboutWindowInfo
    {
        public string Version => BuildInfo.Version;

        public string Build => BuildInfo.Build;

        public string Hash => BuildInfo.Hash;

        public string FullVersion => BuildInfo.FullVersion;

        public string AssemblyInformationalVersion { get; } =
            typeof(BuildInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? string.Empty;

        public string FileVersion { get; } =
            FileVersionInfo.GetVersionInfo(typeof(BuildInfo).Assembly.Location).FileVersion ?? string.Empty;
    }
}
