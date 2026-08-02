using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Creates the context for reproducible EF Core migration tooling.</summary>
public sealed class CoreDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    /// <inheritdoc />
    public CoreDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("JULOS_MIGRATION_DATABASE")
            ?? "Host=127.0.0.1;Database=julos;Username=julos";

        var options = new DbContextOptionsBuilder<CoreDbContext>();
        CorePersistenceServiceCollectionExtensions.Configure(options, connectionString);

        return new CoreDbContext(options.Options);
    }
}
