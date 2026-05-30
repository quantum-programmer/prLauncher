using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RDesigner.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace RDesigner.Views;

public partial class MainView : UserControl
{
    public IServiceProvider _serviceProvider;
    //public MainView(MainViewModel viewModel)
    public MainView(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        InitializeComponent();
        DataContext = _serviceProvider.GetRequiredService<MainViewModel>();
    }
}