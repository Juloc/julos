using System.Globalization;
using System.Security.Cryptography;

using JulOS.Application.Concurrency;
using JulOS.Contracts.Agents;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Integration.Tests;

/// <summary>Creates the real Server host with deterministic test configuration.</summary>
internal sealed class ServerHost : WebApplicationFactory<Program>
{
    // No TCP listener exists on the discard port. The value is safe to commit
    // and makes an accidental database access fail immediately.
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=9;Database=julos_tests;Username=julos;Password=test-only;Timeout=1;Command Timeout=1";

    private static readonly Lazy<string> SecretKeyRingPath = new(CreateSecretKeyRing);

    private readonly string connectionString;
    private readonly bool includeConcurrencyConflictEndpoint;
    private readonly IReadOnlyDictionary<string, string?> settings;
    private readonly Action<IServiceCollection>? configureServices;

    internal ServerHost(bool includeConcurrencyConflictEndpoint = false)
        : this(UnreachableDatabase, includeConcurrencyConflictEndpoint, settings: null, configureServices: null)
    {
    }

    internal ServerHost(IReadOnlyDictionary<string, string?> settings)
        : this(UnreachableDatabase, includeConcurrencyConflictEndpoint: false, settings, configureServices: null)
    {
    }

    internal ServerHost(
        string connectionString,
        IReadOnlyDictionary<string, string?>? settings = null)
        : this(connectionString, includeConcurrencyConflictEndpoint: false, settings, configureServices: null)
    {
    }

    internal ServerHost(
        string connectionString,
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection> configureServices)
        : this(connectionString, includeConcurrencyConflictEndpoint: false, settings, configureServices)
    {
    }

    private ServerHost(
        string connectionString,
        bool includeConcurrencyConflictEndpoint,
        IReadOnlyDictionary<string, string?>? settings,
        Action<IServiceCollection>? configureServices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        this.connectionString = connectionString;
        this.includeConcurrencyConflictEndpoint = includeConcurrencyConflictEndpoint;
        this.settings = settings ?? new Dictionary<string, string?>();
        this.configureServices = configureServices;
    }

    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add(
            AgentProtocolContract.HeaderName,
            AgentProtocolContract.CurrentVersion.ToString(CultureInfo.InvariantCulture));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:CoreDatabase", this.connectionString);
        builder.UseSetting("Secrets:ActiveKeyId", "primary");
        builder.UseSetting("Secrets:KeyRingPath", SecretKeyRingPath.Value);
        builder.UseSetting("Secrets:LeaseLifetimeSeconds", "300");
        builder.UseEnvironment("Production");

        foreach (var setting in this.settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        if (this.configureServices is not null)
        {
            builder.ConfigureServices(this.configureServices);
        }

        if (this.includeConcurrencyConflictEndpoint)
        {
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<AuthorizationOptions>(
                    options => options.FallbackPolicy = null);
                services.AddSingleton<IStartupFilter, ConcurrencyConflictEndpointStartupFilter>();
            });
        }
    }

    private static string CreateSecretKeyRing()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "julos-integration-tests",
            $"secret-key-ring-{Environment.ProcessId}");
        Directory.CreateDirectory(path);
        var keyPath = Path.Combine(path, "primary.key");
        if (!File.Exists(keyPath))
        {
            File.WriteAllText(
                keyPath,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        }

        return path;
    }

    private sealed class ConcurrencyConflictEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return application =>
            {
                next(application);
                application.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet(
                        "/__tests/concurrency-conflict",
                        (HttpContext _) => throw new ConcurrencyConflictException(
                            currentRevision: 7,
                            new InvalidOperationException("Test-only persistence conflict.")))
                        .AllowAnonymous();
                });
            };
        }
    }
}
