using LittleSister.Core.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleSister.Infrastructure.Data;

/// <summary>
/// Bringt die Datenbank beim Start auf den aktuellen Stand:
/// sichert sie vor ausstehenden Migrationen und legt die festen Rollen an.
/// </summary>
public sealed class DatabaseMigrator(
    LittleSisterDbContext db,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<LittleSisterOptions> options,
    TimeProvider time,
    ILogger<DatabaseMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0)
        {
            await BackupSqliteAsync(ct);
            logger.LogInformation("Wende {Count} Datenbank-Migration(en) an: {Migrations}", pending.Count, pending);
            await db.Database.MigrateAsync(ct);
        }

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }
    }

    /// <summary>Kopiert eine bestehende SQLite-Datei nach &lt;DataPath&gt;/backups, bevor das Schema geändert wird.</summary>
    private async Task BackupSqliteAsync(CancellationToken ct)
    {
        if (!db.Database.IsSqlite())
            return;

        var source = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrEmpty(source) || source == ":memory:" || !File.Exists(source))
            return; // Neue Installation, es gibt nichts zu sichern

        var backupDir = Path.Combine(options.Value.GetFullDataPath(), "backups");
        Directory.CreateDirectory(backupDir);
        var target = Path.Combine(backupDir, $"littlesister-{time.GetUtcNow():yyyyMMdd-HHmmss}.db");

        // VACUUM INTO erzeugt eine konsistente Kopie, auch bei aktivem WAL
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [target], ct);
        logger.LogInformation("Datenbank vor der Migration gesichert: {Backup}", target);
    }
}
