using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using RDesigner.Resources;

namespace RDesigner;

public partial class CreateReportView : Window
{
    public CreateReportView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        LocalizationManager.TryToggleLanguage(e);
    }
}
