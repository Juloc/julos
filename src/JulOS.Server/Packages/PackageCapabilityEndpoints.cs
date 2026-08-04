using System.Security.Claims;
using System.Text.Json;

using JulOS.Infrastructure.Packages;
using JulOS.PackageSdk;
using JulOS.Server.Authentication;
using JulOS.Server.Authorization;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Packages;

internal sealed record InvokePackageCapabilityRequest(JsonElement Payload);

internal static class PackageCapabilityEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsPackageCapabilities(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(
                "/api/v1/packages/{packageId}/capabilities/{capabilityName}/{operation}",
                InvokeAsync)
            .WithTags("Packages")
            .RequireAuthorization(
                JulOsAuthorizationPolicies.PackageRead,
                JulOsAuthorizationPolicies.AuthorizationRead)
            .RequireJulOsAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> InvokeAsync(
        HttpContext context,
        string packageId,
        string capabilityName,
        string operation,
        InvokePackageCapabilityRequest request,
        IAntiforgery antiforgery,
        PackageCapabilityAuthorizer authorizer,
        CapabilityBroker broker,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            var grant = await authorizer.AuthorizeAsync(
                packageId,
                capabilityName,
                cancellationToken).ConfigureAwait(false);
            broker.SetPackageGrants(packageId, [grant.CapabilityName]);
            var payload = request.Payload.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement(new { })
                : request.Payload;
            var response = await broker.InvokeAsync(
                packageId,
                new CapabilityRequest(
                    grant.CapabilityName,
                    grant.ContractVersion,
                    operation,
                    context.TraceIdentifier,
                    payload,
                    timeProvider.GetUtcNow().AddSeconds(10),
                    new CapabilityCallerContext(packageId, CurrentUserId(context.User))),
                cancellationToken).ConfigureAwait(false);
            return response.Succeeded
                ? Results.Json(response.Payload)
                : ProviderFailure(response.ErrorCode, response.ErrorDetail);
        }
        catch (PackageCapabilityAuthorizationException exception)
        {
            return AuthorizationFailure(exception);
        }
        catch (CapabilityUnavailableException exception)
        {
            return Results.Json(
                new
                {
                    code = "capability.unavailable",
                    detail = exception.Message,
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (CapabilityBrokerException exception)
        {
            var status = exception.Code switch
            {
                "capability.permission_denied" => StatusCodes.Status403Forbidden,
                "capability.caller_identity_mismatch" => StatusCodes.Status403Forbidden,
                "capability.user_identity_invalid" => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status400BadRequest,
            };
            return Results.Json(
                new { code = exception.Code, detail = exception.Message },
                statusCode: status);
        }
    }

    private static IResult AuthorizationFailure(
        PackageCapabilityAuthorizationException exception)
    {
        var status = exception.Code switch
        {
            "package.not_found" => StatusCodes.Status404NotFound,
            "package.capability_not_granted" => StatusCodes.Status403Forbidden,
            "package.capability_package_unavailable" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(
            new { code = exception.Code, detail = exception.Message },
            statusCode: status);
    }

    private static IResult ProviderFailure(string? code, string? detail)
    {
        var safeCode = code ?? "capability.provider_failed";
        var status = safeCode switch
        {
            "agent.not_found" => StatusCodes.Status404NotFound,
            "hostmetrics.agent_required" => StatusCodes.Status409Conflict,
            "hostmetrics.request_invalid" => StatusCodes.Status400BadRequest,
            "hostmetrics.maximum_age_invalid" => StatusCodes.Status400BadRequest,
            "hostmetrics.operation_unsupported" => StatusCodes.Status400BadRequest,
            "hostmetrics.contract_incompatible" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
        return Results.Json(
            new
            {
                code = safeCode,
                detail = detail ?? "The capability provider failed.",
            },
            statusCode: status);
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : throw new CapabilityBrokerException(
                "capability.user_identity_invalid",
                "The authenticated capability caller identity is invalid.");
    }
}
