using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RDesigner.Services;
using RDesigner.ViewModels;
using RDesigner.Views;

public class Startup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<NativePostgresInstaller>();

        services.AddTransient<InstallerViewModel>();
        services.AddTransient<InstallerView>(provider => new InstallerView(provider));
        services.AddTransient<MainWindow>();
    }
}
