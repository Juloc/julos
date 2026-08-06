using System.Security.Claims;

using JulOS.Application.Authorization;
using JulOS.Domain.Permissions;

using Microsoft.AspNetCore.Authorization;

namespace JulOS.Server.Authorization;

/// <summary>Evaluates persisted direct and role grants with the pure Domain evaluator.</summary>
internal sealed class PermissionAuthorizationHandler(
    IPermissionAssignmentReader assignmentReader)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var identifier = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(identifier, out var userId) || userId == Guid.Empty)
        {
            return;
        }

        var evaluation = await assignmentReader
            .ReadForUserAsync(userId)
            .ConfigureAwait(false);

        foreach (var subject in evaluation.Subjects)
        {
            if (PermissionEvaluator.Grants(
                evaluation.Assignments,
                subject,
                requirement.Permission,
                requirement.Target))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}
