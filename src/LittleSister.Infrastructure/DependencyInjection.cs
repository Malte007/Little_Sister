using LittleSister.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LittleSister.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registriert Optionen und Datenbank je nach konfiguriertem Provider.</summary>
    public static IServiceCollection AddLittleSisterInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(LittleSisterOptions.SectionName);
        services.Configure<LittleSisterOptions>(section);
        var options = section.Get<LittleSisterOptions>() ?? new LittleSisterOptions();

        Directory.CreateDirectory(options.GetFullDataPath());

        services.AddDbContext<LittleSisterDbContext>(db =>
        {
            switch (options.Database.Provider.ToLowerInvariant())
            {
                case "sqlite":
                    db.UseSqlite(options.Database.ConnectionString
                        ?? $"Data Source={Path.Combine(options.GetFullDataPath(), "littlesister.db")}");
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Datenbank-Provider '{options.Database.Provider}' wird noch nicht unterstützt. Möglich: Sqlite.");
            }
        });

        services.AddScoped<DatabaseMigrator>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
