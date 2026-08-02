// JulOS Server composition root.
// Local authentication protects the control plane; feature endpoints follow later work items.

using JulOS.Contracts.Diagnostics;
using JulOS.Infrastructure.Health;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Secrets;
using JulOS.Server;
using JulOS.Server.Auditing;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;
using JulOS.Server.Errors;
using JulOS.Server.Events;
using JulOS.Server.Profile;
using JulOS.Server.Operations;
using JulOS.Server.Secrets;

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
builder.Services.AddJulOsLocalAuthentication(builder.Configuration);
builder.Services.AddJulOsAuthorization();
builder.Services.AddJulOsRealtimeEvents();
var secretOptions = SecretReferenceOptions.Read(builder.Configuration);
builder.Services.AddJulOsSecretReferences(
    secretOptions.ActiveKeyId,
    secretOptions.KeyRingPath,
    secretOptions.LeaseLifetime);

builder.Services
    .AddHealthChecks()
    .AddTypeActivatedCheck<PostgreSqlHealthCheck>(
        name: "core-database",
        failureStatus: HealthStatus.Unhealthy,
        tags: [ReadinessTag],
        args: [coreDatabase]);

var app = builder.Build();

app.UseJulOsErrorHandling();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Liveness answers whether the process itself is running, so it registers no
// dependency check. A failing dependency must not cause a restart loop.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(ReadinessTag) })
    .AllowAnonymous();

app.MapJulOsLocalAuthentication();
app.MapJulOsAuthorization();
app.MapJulOsProfile();
app.MapJulOsOperations();
app.MapJulOsSecretReferences();
app.MapJulOsAudit();
app.MapJulOsRealtimeEvents();

app.MapGet(
    "/api/v1/system/version",
    () => new ComponentVersionResponse(ServerVersion.ComponentName, ServerVersion.Current))
    .RequireAuthorization(JulOsAuthorizationPolicies.SystemVersionRead);

// An unknown path has no protected resource behind it. Keep the platform's
// common 404 problem response instead of challenging an unauthenticated caller.
app.MapFallback(() => TypedResults.NotFound())
    .AllowAnonymous();

ServerLog.Starting(app.Logger, ServerVersion.ComponentName, ServerVersion.Current);

await app.RunAsync().ConfigureAwait(false);

return 0;
