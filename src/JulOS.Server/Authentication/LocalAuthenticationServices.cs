using System.Globalization;
using System.Threading.RateLimiting;

using JulOS.Contracts.Errors;
using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.Server.Errors;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JulOS.Server.Authentication;

/// <summary>Registers local accounts, cookie sessions and authentication protections.</summary>
internal static class LocalAuthenticationServices
{
    internal const string LoginRateLimitPolicy = "local-authentication";

    internal static IServiceCollection AddJulOsLocalAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var localOptions = LocalAuthenticationOptions.Read(configuration);
        services.AddSingleton(localOptions);
        services.AddSingleton(TimeProvider.System);

        // Optional parent-domain scope so the session reaches web-application target subdomains
        // (see docs/WEB-APP-RENDERING.md). Unset by default, which keeps the cookies host-only.
        var cookieDomain = ReadCookieDomain(configuration);

        services
            .AddIdentity<LocalUser, LocalRole>(identity =>
            {
                identity.User.RequireUniqueEmail = false;
                identity.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-_@+";

                identity.Password.RequiredLength = 12;
                identity.Password.RequiredUniqueChars = 4;
                identity.Password.RequireDigit = true;
                identity.Password.RequireLowercase = true;
                identity.Password.RequireUppercase = true;
                identity.Password.RequireNonAlphanumeric = true;

                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = localOptions.MaximumFailedAccessAttempts;
                identity.Lockout.DefaultLockoutTimeSpan =
                    TimeSpan.FromMinutes(localOptions.LockoutMinutes);

                identity.SignIn.RequireConfirmedAccount = false;
                identity.SignIn.RequireConfirmedEmail = false;
                identity.SignIn.RequireConfirmedPhoneNumber = false;
            })
            .AddEntityFrameworkStores<CoreDbContext>()
            .AddSignInManager();

        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = ".JulOS.Session";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.IsEssential = true;
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            if (cookieDomain is not null)
            {
                cookie.Cookie.Domain = cookieDomain;
            }

            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(localOptions.SessionTimeoutMinutes);
            cookie.SlidingExpiration = true;

            cookie.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = ".JulOS.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            if (cookieDomain is not null)
            {
                options.Cookie.Domain = cookieDomain;
            }
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy<string>(
                LoginRateLimitPolicy,
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = localOptions.LoginPermitLimit,
                        Window = TimeSpan.FromSeconds(localOptions.LoginWindowSeconds),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var retryAfter))
                {
                    response.Headers["Retry-After"] = Math.Ceiling(retryAfter.TotalSeconds)
                        .ToString(CultureInfo.InvariantCulture);
                }

                var problemDetails = context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();

                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many authentication attempts.",
                        Type = "https://os.juloc.de/problems/request-rate-limited",
                        Extensions =
                        {
                            [ProblemExtensionNames.Code] = PlatformErrorCodes.RateLimited,
                            [ProblemExtensionNames.CorrelationId] = CorrelationId.Get(context.HttpContext),
                            [ProblemExtensionNames.Retryable] = true,
                        },
                    },
                }).ConfigureAwait(false);
            };
        });

        return services;
    }

    private static string? ReadCookieDomain(IConfiguration configuration)
    {
        var value = configuration["Authentication:CookieDomain"]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Contains("://", StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal)
            || value.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException(
                "Authentication:CookieDomain must be a bare cookie domain such as '.os.juloc.de'.");
        }

        return value;
    }
}
