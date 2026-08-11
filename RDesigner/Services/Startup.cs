using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pyramid.Services;
using Pyramid.ViewModels;
using Pyramid.Views;

public class Startup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<NativePostgresInstaller>();
        services.AddSingleton<LinuxWineInstaller>();
        services.AddSingleton<ProductApplicationInstaller>();

        services.AddTransient<InstallerViewModel>();
        services.AddTransient<InstallerView>(provider => new InstallerView(provider));
        services.AddTransient<MainWindow>();
    }
}
