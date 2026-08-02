using System.Security.Claims;

using JulOS.Application.Authorization;
using JulOS.Contracts.Authorization;
using JulOS.Domain;
using JulOS.Domain.Packages;
using JulOS.Domain.Permissions;
using JulOS.Server.Authentication;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Authorization;

/// <summary>Maps versioned administrator role and permission endpoints.</summary>
internal static class AuthorizationEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsAuthorization(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/v1/authorization")
            .WithTags("Authorization");

        group.MapGet("/roles", ListRolesAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationRead);
        group.MapPost("/roles", CreateRoleAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();
        group.MapPut("/roles/{roleId:guid}", UpdateRoleAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();
        group.MapDelete("/roles/{roleId:guid}", DeleteRoleAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();

        group.MapGet("/roles/{roleId:guid}/members", ListRoleMembersAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationRead);
        group.MapPost("/roles/{roleId:guid}/members/{userId:guid}", AddRoleMemberAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();
        group.MapDelete("/roles/{roleId:guid}/members/{userId:guid}", RemoveRoleMemberAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();

        group.MapGet("/assignments", ListAssignmentsAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationRead);
        group.MapPost("/assignments", GrantPermissionAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();
        group.MapDelete("/assignments/{assignmentId:guid}", RevokePermissionAsync)
            .RequireAuthorization(JulOsAuthorizationPolicies.AuthorizationManage)
            .RequireJulOsAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> ListRolesAsync(
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        var roles = await administration.ListRolesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(roles.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> CreateRoleAsync(
        HttpContext context,
        CreateAuthorizationRoleRequest request,
        IAntiforgery antiforgery,
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(request);
        var role = await administration
            .CreateRoleAsync(request.Name, request.Description, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/authorization/roles/{role.Id}", ToResponse(role));
    }

    private static async Task<IResult> UpdateRoleAsync(
        HttpContext context,
        Guid roleId,
        UpdateAuthorizationRoleRequest request,
        IAntiforgery antiforgery,
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(request);
        var role = await administration
            .UpdateRoleAsync(
                roleId,
                request.Name,
                request.Description,
                request.Revision,
                cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(role));
    }

    private static async Task<IResult> DeleteRoleAsync(
        HttpContext context,
        Guid roleId,
        int revision,
        IAntiforgery antiforgery,
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        await administration.DeleteRoleAsync(roleId, revision, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListRoleMembersAsync(
        Guid roleId,
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        var members = await administration
            .ListRoleMembersAsync(roleId, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(members.Select(member => new AuthorizationRoleMemberResponse(
            member.UserId,
            member.UserName,
            member.DisplayName)).ToArray());
    }

    private static async Task<IResult> AddRoleMemberAsync(
        HttpContext context,
        Guid roleId,
        Guid userId,
        IAntiforgery antiforgery,
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        await administration.AddRoleMemberAsync(roleId, userId, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveRoleMemberAsync(
        HttpContext context,
        Guid roleId,
        Guid userId,
        IAntiforgery antiforgery,
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        await administration.RemoveRoleMemberAsync(roleId, userId, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListAssignmentsAsync(
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        var assignments = await administration
            .ListPermissionAssignmentsAsync(cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(assignments.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> GrantPermissionAsync(
        HttpContext context,
        GrantPermissionRequest request,
        IAntiforgery antiforgery,
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var assignment = await administration.GrantPermissionAsync(
                ParseSubject(request.SubjectType, request.SubjectId),
                PermissionName.Parse(request.Permission),
                ParseScope(request.ScopeType, request.ScopeId),
                CurrentUserId(context.User),
                cancellationToken).ConfigureAwait(false);

            var response = ToResponse(assignment);
            return TypedResults.Created(
                $"/api/v1/authorization/assignments/{response.AssignmentId}",
                response);
        }
        catch (DomainRuleViolationException exception)
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.InvalidAssignment,
                exception);
        }
    }

    private static async Task<IResult> RevokePermissionAsync(
        HttpContext context,
        Guid assignmentId,
        IAntiforgery antiforgery,
        IAuthorizationAdministration administration,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            await administration.RevokePermissionAsync(
                new PermissionAssignmentId(assignmentId),
                cancellationToken).ConfigureAwait(false);
            return TypedResults.NoContent();
        }
        catch (ArgumentException exception)
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.InvalidAssignment,
                exception);
        }
    }

    private static AuthorizationRoleResponse ToResponse(AuthorizationRole role) => new(
        role.Id,
        role.Name,
        role.Description,
        role.IsSystemRole,
        role.Revision);

    private static PermissionAssignmentResponse ToResponse(StoredPermissionAssignment stored) => new(
        stored.Assignment.Id.Value,
        stored.Assignment.Subject.Kind == PermissionSubjectKind.User
            ? AuthorizationSubjectTypes.User
            : AuthorizationSubjectTypes.Role,
        stored.Assignment.Subject.Id.Value,
        stored.Assignment.Permission.Value,
        stored.Assignment.Scope.Kind switch
        {
            PermissionScopeKind.Global => AuthorizationScopeTypes.Global,
            PermissionScopeKind.Package => AuthorizationScopeTypes.Package,
            PermissionScopeKind.Resource => AuthorizationScopeTypes.Resource,
            _ => throw new InvalidOperationException("Unknown permission scope kind."),
        },
        stored.Assignment.Scope.ScopeId,
        stored.Assignment.GrantedAtUtc,
        stored.GrantedByUserId);

    private static PermissionSubject ParseSubject(string subjectType, Guid subjectId)
    {
        var kind = subjectType switch
        {
            AuthorizationSubjectTypes.User => PermissionSubjectKind.User,
            AuthorizationSubjectTypes.Role => PermissionSubjectKind.Role,
            _ => throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.InvalidAssignment),
        };

        try
        {
            return new PermissionSubject(kind, new PermissionSubjectId(subjectId));
        }
        catch (ArgumentException exception)
        {
            throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.InvalidAssignment,
                exception);
        }
    }

    private static PermissionScope ParseScope(string scopeType, string? scopeId)
    {
        return scopeType switch
        {
            AuthorizationScopeTypes.Global when scopeId is null => PermissionScope.Global,
            AuthorizationScopeTypes.Package when scopeId is not null =>
                PermissionScope.ForPackage(PackageId.Parse(scopeId)),
            AuthorizationScopeTypes.Resource when scopeId is not null =>
                PermissionScope.ForResource(PermissionResourceId.Parse(scopeId)),
            _ => throw new AuthorizationAdministrationException(
                AuthorizationAdministrationFailureReason.InvalidAssignment),
        };
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : throw new InvalidOperationException("The authenticated principal has no valid user identifier.");
    }
}
