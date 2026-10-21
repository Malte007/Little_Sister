using LittleSister.Core.Settings;
using LittleSister.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LittleSister.Infrastructure.Data;

/// <summary>Zentraler Datenbankkontext inklusive der Identity-Tabellen.</summary>
public sealed class LittleSisterDbContext(DbContextOptions<LittleSisterDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SystemSettings>(e =>
        {
            e.ToTable("SystemSettings");
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.Language).HasMaxLength(10);
            e.Property(s => s.TimeZoneId).HasMaxLength(100);
            e.Property(s => s.RecordingsPath).HasMaxLength(1000);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite kann DateTimeOffset nicht sortieren; daher als UTC-Ticks speichern
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
