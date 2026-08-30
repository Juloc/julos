// JulOS Server composition root.
// Local authentication protects the control plane; feature endpoints follow later work items.

using JulOS.Contracts.Diagnostics;
using JulOS.Infrastructure.Agents;
using JulOS.Infrastructure.Authorization;
using JulOS.Infrastructure.Health;
using JulOS.Infrastructure.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Remote;
using JulOS.Infrastructure.Secrets;
using JulOS.Server;
using JulOS.Server.Agents;
using JulOS.Server.Applications;
using JulOS.Server.Auditing;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;
using JulOS.Server.Errors;
using JulOS.Server.Events;
using JulOS.Server.Layouts;
using JulOS.Server.Operations;
using JulOS.Server.Packages;
using JulOS.Server.Profile;
using JulOS.Server.Remote;
using JulOS.Server.SafeMode;
using JulOS.Server.Secrets;
using JulOS.Server.Security;
using JulOS.Server.WebApps;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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
var coreDatabase = CoreDatabaseConfiguration.Read(builder.Configuration);

builder.Services.AddJulOsErrorHandling();
builder.Services.AddJulOsCorePersistence(coreDatabase);
builder.Services.AddJulOsRemoteOrchestration(builder.Configuration);
builder.Services.AddJulOsWebAppProxy(builder.Configuration);
builder.Services.AddHostedService<RemoteSessionProvisioningWorker>();
builder.Services.AddHostedService<RemoteSessionLifecycleWorker>();
builder.Services.AddJulOsAgentControl();
builder.Services.AddJulOsLocalAuthentication(builder.Configuration);
builder.Services.AddJulOsAuthorization();
builder.Services.AddSingleton(SafeModeState.Read(builder.Configuration));
builder.Services.AddJulOsRealtimeEvents();
builder.Services.AddJulOsPackageManagement(builder.Configuration, coreDatabase);
var secretOptions = SecretReferenceOptions.Read(builder.Configuration);
builder.Services.AddSingleton(secretOptions);
builder.Services.AddJulOsSecretReferences(
    secretOptions.ActiveKeyId,
    secretOptions.KeyRingPath,
    secretOptions.LeaseLifetime);
var dataProtectionKeyRingPath =
    builder.Configuration["DataProtection:KeyRingPath"];
if (string.IsNullOrWhiteSpace(dataProtectionKeyRingPath))
{
    dataProtectionKeyRingPath = JulOsDataProtection.KeyRingPath;
}

builder.Services
    .AddDataProtection()
    .SetApplicationName("JulOS")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyRingPath));
builder.Services.AddSingleton<JulOsDataProtectionKeyProvider>();
builder.Services.AddSingleton<
    IConfigureOptions<KeyManagementOptions>,
    JulOsDataProtectionOptions>();

builder.Services
    .AddHealthChecks()
    .AddTypeActivatedCheck<PostgreSqlHealthCheck>(
        name: "core-database",
        failureStatus: HealthStatus.Unhealthy,
        tags: [ReadinessTag],
        args: [coreDatabase]);

var app = builder.Build();

if (coreDatabase.Provider == CoreDatabaseProvider.Sqlite)
{
    await CoreDatabaseMigrator.MigrateAsync(coreDatabase).ConfigureAwait(false);
}

// Grant the administrator role any permission added to the catalog since setup
// completed (for example after an upgrade). This is best-effort: a database that
// is not reachable at startup must not stop the host, and the seeder is
// idempotent, so it re-runs harmlessly on every boot.
try
{
    await SystemAuthorizationReconciler
        .ReconcileAdministratorPermissionsAsync(app.Services)
        .ConfigureAwait(false);
}
catch (Exception exception)
{
    ServerLog.AdministratorPermissionReconciliationSkipped(app.Logger, exception);
}

app.UseJulOsErrorHandling();
app.UseJulOsAgentProtocol();
app.UseDefaultFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseWebSockets();
app.UseAuthentication();
app.UseJulOsWebAppProxy();

// Requests that matched no endpoint return 404 here — before the authorization
// fallback policy would turn them into 401, and before endpoint execution. This
// replaces a routed MapFallback whose catch-all pattern suppressed sibling
// parameter routes (for example package enable/disable/remove) in the endpoint
// route table. The web-app proxy runs first so proxied target hosts are still
// handled, and UseStatusCodePages renders the JulOS problem shape for the 404.
app.Use(async (context, next) =>
{
    if (context.GetEndpoint() is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next(context).ConfigureAwait(false);
});

app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets()
    .AllowAnonymous();

// The Shell service worker is served here rather than through the fingerprinted
// static-asset pipeline: a service-worker registration cannot consume the
// compressed, immutable-cached asset response, and the worker must stay
// no-cache so browsers can detect a new build. Served uncompressed from the web
// root with the header that lets a /sw.js worker take the whole-origin scope.
app.MapGet("/sw.js", async (HttpContext context, IWebHostEnvironment environment) =>
{
    var webRoot = environment.WebRootPath
        ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    var serviceWorkerPath = Path.Combine(webRoot, "service-worker.js");
    if (!File.Exists(serviceWorkerPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/javascript";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["Service-Worker-Allowed"] = "/";
    await context.Response.SendFileAsync(serviceWorkerPath).ConfigureAwait(false);
}).AllowAnonymous();

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
app.MapJulOsApplications();
app.MapJulOsPackages();
app.MapJulOsPackageCapabilities();
app.MapJulOsPackageUpdates();
app.MapJulOsOperations();
app.MapJulOsSecretReferences();
app.MapJulOsSafeMode();
app.MapJulOsAudit();
app.MapJulOsAgents();
app.MapJulOsRealtimeEvents();
app.MapJulOsRemoteProviderEvents();
app.MapJulOsRemoteDisplay();
app.MapJulOsWebApps();

app.MapGet(
    "/api/v1/system/version",
    () => new ComponentVersionResponse(ServerVersion.ComponentName, ServerVersion.Current))
    .RequireAuthorization(JulOsAuthorizationPolicies.SystemVersionRead);

ServerLog.Starting(app.Logger, ServerVersion.ComponentName, ServerVersion.Current);

await app.RunAsync().ConfigureAwait(false);

return 0;
