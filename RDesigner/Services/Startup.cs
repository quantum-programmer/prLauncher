using Microsoft.Extensions.DependencyInjection;
using Pyramid.ViewModels;
using Pyramid.Views;

namespace Pyramid.Services;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<NativePostgresInstaller>();
        services.AddTransient<InstallerViewModel>();
        services.AddTransient<InstallerView>(provider => new InstallerView(provider));
        services.AddTransient<MainWindow>();
    }
}
