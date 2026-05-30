using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using RDesigner.ViewModels;
using System;

namespace RDesigner.Views;

public partial class InstallerView : UserControl
{
    public InstallerView(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        DataContext = serviceProvider.GetRequiredService<InstallerViewModel>();
    }
}
