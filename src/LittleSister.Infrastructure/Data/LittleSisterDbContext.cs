using LittleSister.Core.Cameras;
using LittleSister.Core.Recording;
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
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<Segment> Segments => Set<Segment>();

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
            e.Property(s => s.MinFreeGb).HasDefaultValue(10.0);
        });

        builder.Entity<Camera>(e =>
        {
            e.ToTable("Cameras");
            e.Property(c => c.Name).HasMaxLength(100);
            e.Property(c => c.MainStreamUrl).HasMaxLength(1000);
            e.Property(c => c.SubStreamUrl).HasMaxLength(1000);
            e.Property(c => c.Username).HasMaxLength(200);
            e.Property(c => c.OnvifAddress).HasMaxLength(1000);
            e.Property(c => c.Manufacturer).HasMaxLength(200);
            e.Property(c => c.Model).HasMaxLength(200);
            e.Ignore(c => c.QuotaBytes);
        });

        builder.Entity<Segment>(e =>
        {
            e.ToTable("Segments");
            e.Property(s => s.RelativePath).HasMaxLength(500);
            e.Property(s => s.StorageRoot).HasMaxLength(1000);
            e.Ignore(s => s.FullPath);
            // Ringspeicher und Zeitleiste suchen immer je Kamera nach Zeit
            e.HasIndex(s => new { s.CameraId, s.StartUtc });
            e.HasIndex(s => s.StartUtc);
            e.HasOne<Camera>().WithMany().HasForeignKey(s => s.CameraId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite kann DateTimeOffset nicht sortieren; daher als UTC-Ticks speichern
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
