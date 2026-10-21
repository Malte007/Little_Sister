using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LittleSister.Infrastructure.Data;

/// <summary>Wird nur von "dotnet ef" zum Erzeugen von Migrationen genutzt.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LittleSisterDbContext>
{
    public LittleSisterDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LittleSisterDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options);
}
