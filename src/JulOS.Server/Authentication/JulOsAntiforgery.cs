using JulOS.Application.Authentication;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Authentication;

/// <summary>Owns the common antiforgery contract for authenticated mutations.</summary>
internal static class JulOsAntiforgery
{
    /// <summary>Adds discoverable JulOS antiforgery metadata to one endpoint.</summary>
    internal static RouteHandlerBuilder RequireJulOsAntiforgery(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(RequiredAntiforgeryMetadata.Instance);
    }

    /// <summary>Validates one authenticated mutation request.</summary>
    internal static async Task ValidateAsync(HttpContext context, IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(antiforgery);

        try
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException exception)
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.AntiforgeryInvalid,
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.AntiforgeryInvalid,
                exception);
        }
    }

    private sealed class RequiredAntiforgeryMetadata : IAntiforgeryMetadata
    {
        internal static RequiredAntiforgeryMetadata Instance { get; } = new();

        public bool RequiresValidation => true;
    }
}
