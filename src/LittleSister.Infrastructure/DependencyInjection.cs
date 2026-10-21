using LittleSister.Infrastructure.Data;
using LittleSister.Infrastructure.Live;
using LittleSister.Infrastructure.Onvif;
using LittleSister.Infrastructure.Recording;
using LittleSister.Infrastructure.Security;
using LittleSister.Infrastructure.Storage;
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
        services.AddSingleton<CameraChanges>();

        // Kameras und ONVIF
        services.AddSingleton<CameraCredentialProtector>();
        services.AddSingleton<OnvifDiscovery>();
        services.AddSingleton(sp => new OnvifClient(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<StreamProbe>();

        // Aufnahme: ein Supervisor startet die Recorder, der Ringspeicher räumt auf
        services.AddSingleton<CameraStatusRegistry>();
        services.AddSingleton<SegmentIndexer>();
        services.AddSingleton<ICameraInputFactory, RtspInputFactory>();
        services.AddSingleton<RecordingSupervisor>();
        services.AddHostedService(sp => sp.GetRequiredService<RecordingSupervisor>());
        services.AddSingleton<IFreeSpaceProvider, DriveFreeSpaceProvider>();
        services.AddSingleton<RetentionService>();
        services.AddHostedService(sp => sp.GetRequiredService<RetentionService>());

        // Live-Ansicht: go2rtc als Kindprozess, angesprochen nur über 127.0.0.1
        services.AddSingleton<LiveStatus>();
        services.AddSingleton<ILiveSourceFactory, RtspLiveSourceFactory>();
        services.AddSingleton(_ => new Go2rtcClient(new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{options.Live.ApiPort}/"),
            Timeout = TimeSpan.FromSeconds(15),
        }));
        services.AddHostedService<LiveSupervisor>();
        return services;
    }
}
