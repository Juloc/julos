// JulOS Server composition root.
// Authentication, persistence and feature endpoints are wired by later work items.

using JulOS.Infrastructure.Health;
using JulOS.Server;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

if (HealthProbeCommand.IsRequested(args))
{
    return await HealthProbeCommand.RunAsync(args).ConfigureAwait(false);
}

const string ReadinessTag = "ready";
const string CoreDatabaseConnectionName = "CoreDatabase";

var builder = WebApplication.CreateBuilder(args);

// The control plane cannot operate without its database, so a missing connection
// string stops startup instead of producing a server that fails on first use.
var coreDatabase = builder.Configuration.GetConnectionString(CoreDatabaseConnectionName)
    ?? throw new InvalidOperationException(
        $"The connection string '{CoreDatabaseConnectionName}' is not configured. "
        + $"Set ConnectionStrings__{CoreDatabaseConnectionName} or see deploy/compose/README.md.");

builder.Services
    .AddHealthChecks()
    .AddTypeActivatedCheck<PostgreSqlHealthCheck>(
        name: "core-database",
        failureStatus: HealthStatus.Unhealthy,
        tags: [ReadinessTag],
        args: [coreDatabase]);

var app = builder.Build();

// Liveness answers whether the process itself is running, so it registers no
// dependency check. A failing dependency must not cause a restart loop.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(ReadinessTag) });

await app.RunAsync().ConfigureAwait(false);

return 0;
