using JulOS.Infrastructure.Persistence.Core;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Tests.Persistence;

[TestClass]
public sealed class CoreDatabaseConfigurationTests
{
    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [TestMethod]
    public void DefaultsToSqliteWhenNothingIsConfigured()
    {
        var result = CoreDatabaseConfiguration.Read(Configuration([]));

        Assert.AreEqual(CoreDatabaseProvider.Sqlite, result.Provider);
        StringAssert.Contains(result.ConnectionString, "julos.db");
    }

    [TestMethod]
    public void SelectsPostgreSqlWhenOnlyAConnectionStringIsConfigured()
    {
        var result = CoreDatabaseConfiguration.Read(Configuration(new()
        {
            ["ConnectionStrings:CoreDatabase"] = "Host=postgres;Database=julos",
        }));

        Assert.AreEqual(CoreDatabaseProvider.PostgreSql, result.Provider);
        Assert.AreEqual("Host=postgres;Database=julos", result.ConnectionString);
    }

    [TestMethod]
    public void HonoursExplicitSqliteProviderWithoutConnectionString()
    {
        var result = CoreDatabaseConfiguration.Read(Configuration(new()
        {
            ["Database:Provider"] = "sqlite",
        }));

        Assert.AreEqual(CoreDatabaseProvider.Sqlite, result.Provider);
        StringAssert.Contains(result.ConnectionString, "julos.db");
    }

    [TestMethod]
    public void HonoursExplicitSqliteProviderWithConnectionString()
    {
        var result = CoreDatabaseConfiguration.Read(Configuration(new()
        {
            ["Database:Provider"] = "sqlite",
            ["ConnectionStrings:CoreDatabase"] = "Data Source=/data/custom.db;Cache=Shared",
        }));

        Assert.AreEqual(CoreDatabaseProvider.Sqlite, result.Provider);
        Assert.AreEqual("Data Source=/data/custom.db;Cache=Shared", result.ConnectionString);
    }

    [TestMethod]
    public void HonoursExplicitPostgreSqlProviderWithConnectionString()
    {
        var result = CoreDatabaseConfiguration.Read(Configuration(new()
        {
            ["Database:Provider"] = "postgresql",
            ["ConnectionStrings:CoreDatabase"] = "Host=postgres;Database=julos",
        }));

        Assert.AreEqual(CoreDatabaseProvider.PostgreSql, result.Provider);
        Assert.AreEqual("Host=postgres;Database=julos", result.ConnectionString);
    }

    [TestMethod]
    public void ThrowsWhenPostgreSqlIsRequestedWithoutAConnectionString()
    {
        var configuration = Configuration(new()
        {
            ["Database:Provider"] = "postgres",
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => CoreDatabaseConfiguration.Read(configuration));
    }

    [TestMethod]
    public void ThrowsOnUnknownProvider()
    {
        var configuration = Configuration(new()
        {
            ["Database:Provider"] = "mysql",
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => CoreDatabaseConfiguration.Read(configuration));
    }
}
