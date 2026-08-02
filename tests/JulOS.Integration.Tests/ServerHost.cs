using JulOS.Application.Concurrency;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JulOS.Integration.Tests;

/// <summary>Creates the real Server host without requiring a live database.</summary>
internal sealed class ServerHost : WebApplicationFactory<Program>
{
    private readonly bool includeConcurrencyConflictEndpoint;

    internal ServerHost(bool includeConcurrencyConflictEndpoint = false)
    {
        this.includeConcurrencyConflictEndpoint = includeConcurrencyConflictEndpoint;
    }

    // No TCP listener exists on the discard port. The value is safe to commit
    // and makes an accidental database access fail immediately.
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=9;Database=julos_tests;Username=julos;Password=test-only;Timeout=1;Command Timeout=1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:CoreDatabase", UnreachableDatabase);
        builder.UseEnvironment("Production");

        if (this.includeConcurrencyConflictEndpoint)
        {
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, ConcurrencyConflictEndpointStartupFilter>());
        }
    }

    private sealed class ConcurrencyConflictEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return application =>
            {
                next(application);
                application.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Path == "/__tests/concurrency-conflict")
                    {
                        throw new ConcurrencyConflictException(
                            currentRevision: 7,
                            new InvalidOperationException("Test-only persistence conflict."));
                    }

                    await nextMiddleware().ConfigureAwait(false);
                });
            };
        }
    }
}
