// JulOS Server composition root.
// Authentication and feature endpoints are wired by later work items.

using JulOS.Contracts.Diagnostics;
using JulOS.Infrastructure.Health;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Server;
using JulOS.Server.Errors;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

if (HealthProbeCommand.IsRequested(args))
{
    return await HealthProbeCommand.RunAsync(args).ConfigureAwait(false);
}

var builder = WebApplication.CreateBuilder(args);

if (DatabaseMigrationCommand.IsRequested(args))
{
    return await DatabaseMigrationCommand
        .RunAsync(builder.Configuration)
        .ConfigureAwait(false);
}

const string ReadinessTag = "ready";
const string CoreDatabaseConnectionName = "CoreDatabase";

// The control plane cannot operate without its database, so a missing connection
// string stops startup instead of producing a server that fails on first use.
var coreDatabase = builder.Configuration.GetConnectionString(CoreDatabaseConnectionName)
    ?? throw new InvalidOperationException(
        $"The connection string '{CoreDatabaseConnectionName}' is not configured. "
        + $"Set ConnectionStrings__{CoreDatabaseConnectionName} or see deploy/compose/README.md.");

builder.Services.AddJulOsErrorHandling();
builder.Services.AddJulOsCorePersistence(coreDatabase);

builder.Services
    .AddHealthChecks()
    .AddTypeActivatedCheck<PostgreSqlHealthCheck>(
        name: "core-database",
        failureStatus: HealthStatus.Unhealthy,
        tags: [ReadinessTag],
        args: [coreDatabase]);

var app = builder.Build();

app.UseJulOsErrorHandling();

// Liveness answers whether the process itself is running, so it registers no
// dependency check. A failing dependency must not cause a restart loop.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(ReadinessTag) });

// Diagnostics. API-004 attaches the authorization policy once roles exist.
app.MapGet(
    "/api/v1/system/version",
    () => new ComponentVersionResponse(ServerVersion.ComponentName, ServerVersion.Current));

ServerLog.Starting(app.Logger, ServerVersion.ComponentName, ServerVersion.Current);

await app.RunAsync().ConfigureAwait(false);

return 0;
