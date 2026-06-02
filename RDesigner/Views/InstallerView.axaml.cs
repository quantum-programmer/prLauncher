using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pyramid.ViewModels;
using System;

namespace Pyramid.Views;

public partial class InstallerView : UserControl
{
    public InstallerView(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        DataContext = serviceProvider.GetRequiredService<InstallerViewModel>();
    }
}
