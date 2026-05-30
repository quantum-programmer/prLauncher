using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RDesigner.Configuration;
using RDesigner.Services;
using RDesigner.ViewModels;
using RDesigner.Views;

public class Startup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        services.AddSingleton(provider =>
        {
            var databaseOptions = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            return Npgsql.NpgsqlDataSource.Create(databaseOptions.CreateConnectionString());
        });

        services.AddSingleton<IDBService, PostgresDBService>();
        services.AddSingleton<NativePostgresInstaller>();

        services.AddTransient<MainViewModel>();
        services.AddTransient<MainView>(provider => new MainView(provider));
        services.AddTransient<InstallerViewModel>();
        services.AddTransient<InstallerView>(provider => new InstallerView(provider));

        services.AddTransient<MainWindow>();
    }
}
