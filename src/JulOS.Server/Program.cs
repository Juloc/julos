// JulOS Server composition root.
// Local authentication protects the control plane; feature endpoints follow later work items.

using JulOS.Contracts.Diagnostics;
using JulOS.Infrastructure.Health;
using JulOS.Infrastructure.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Secrets;
using JulOS.Server;
using JulOS.Server.Auditing;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;
using JulOS.Server.Errors;
using JulOS.Server.Events;
using JulOS.Server.Layouts;
using JulOS.Server.Packages;
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

var coreDatabase = builder.Configuration.GetConnectionString(CoreDatabaseConnectionName)
    ?? throw new InvalidOperationException(
        $"The connection string '{CoreDatabaseConnectionName}' is not configured. "
        + $"Set ConnectionStrings__{CoreDatabaseConnectionName} or see deploy/compose/README.md.");

builder.Services.AddJulOsErrorHandling();
builder.Services.AddJulOsCorePersistence(coreDatabase);
builder.Services.AddJulOsLocalAuthentication(builder.Configuration);
builder.Services.AddJulOsAuthorization();
builder.Services.AddJulOsRealtimeEvents();
builder.Services.AddJulOsPackageManagement(builder.Configuration, coreDatabase);
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
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(ReadinessTag) })
    .AllowAnonymous();

app.MapJulOsLocalAuthentication();
app.MapJulOsAuthorization();
app.MapJulOsProfile();
app.MapJulOsDesktopLayouts();
app.MapJulOsPackages();
app.MapJulOsPackageUpdates();
app.MapJulOsOperations();
app.MapJulOsSecretReferences();
app.MapJulOsAudit();
app.MapJulOsRealtimeEvents();

app.MapGet(
    "/api/v1/system/version",
    () => new ComponentVersionResponse(ServerVersion.ComponentName, ServerVersion.Current))
    .RequireAuthorization(JulOsAuthorizationPolicies.SystemVersionRead);

app.MapFallback(() => TypedResults.NotFound())
    .AllowAnonymous();

ServerLog.Starting(app.Logger, ServerVersion.ComponentName, ServerVersion.Current);

await app.RunAsync().ConfigureAwait(false);

return 0;
