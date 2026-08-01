using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests;

/// <summary>
/// Starts the real JulOS Server host in memory.
/// </summary>
/// <remarks>
/// The host requires a core database connection string before it starts, so the factory
/// supplies one that points nowhere. Nothing under test opens a connection; the readiness
/// endpoint would, and it is covered by the development stack instead.
/// </remarks>
internal sealed class ServerHost : WebApplicationFactory<Program>
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=julos;Username=julos;Password=not-used";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:CoreDatabase", UnreachableDatabase);
        builder.UseEnvironment("Production");
    }
}
