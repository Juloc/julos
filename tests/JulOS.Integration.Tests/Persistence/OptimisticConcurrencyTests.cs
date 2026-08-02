using JulOS.Application.Concurrency;
using JulOS.Domain.Packages;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Integration.Tests.Persistence;

/// <summary>Verifies that a stale mutation cannot overwrite a newer stored revision.</summary>
[TestClass]
public sealed class OptimisticConcurrencyTests
{
    [TestMethod]
    public async Task AStaleUpdateFailsWithTheCurrentRevision()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;

        await using (var setup = new CoreDbContext(options))
        {
            await setup.Database.MigrateAsync();

            var installation = PackageInstallation.BeginInstallation(
                PackageInstallationId.From(EntityIdentifier.From(Guid.NewGuid())),
                PackageId.From("com.julos.concurrency-test"));

            setup.PackageInstallations.Add(PackageInstallationRow.FromDomain(installation));
            await setup.SaveChangesAsync();
        }

        await using var firstWriter = new CoreDbContext(options);
        await using var staleWriter = new CoreDbContext(options);

        var firstRow = await firstWriter.PackageInstallations.SingleAsync();
        var staleRow = await staleWriter.PackageInstallations.SingleAsync();

        firstRow.State = PackageInstallationState.Installed;
        firstRow.Revision = 2;
        await firstWriter.SaveChangesAsync();

        staleRow.State = PackageInstallationState.Installed;
        staleRow.Revision = 2;

        var conflict = await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(
            async () => await staleWriter.SaveChangesAsync());

        Assert.AreEqual(2, conflict.CurrentRevision);

        await using var verification = new CoreDbContext(options);
        var stored = await verification.PackageInstallations.SingleAsync();

        Assert.AreEqual(2, stored.Revision);
        Assert.AreEqual(PackageInstallationState.Installed, stored.State);
    }
}
