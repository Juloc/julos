using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Concurrency;
using JulOS.Application.Devices;
using JulOS.Contracts.Devices;
using JulOS.Contracts.Layouts;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Devices;

/// <summary>Stores one background-execution preference per user, application and workspace class.</summary>
/// <remarks>
/// Every query is filtered by the authenticated user, and the device key only selects which
/// of that user's devices the preference belongs to. A key owned by someone else resolves to
/// no device, which falls back to the user's shared preference rather than revealing theirs.
/// </remarks>
internal sealed class EfApplicationExecutionPreferenceService : IApplicationExecutionPreferenceService
{
    private readonly CoreDbContext context;
    private readonly IIdentifierGenerator identifiers;

    public EfApplicationExecutionPreferenceService(CoreDbContext context, IIdentifierGenerator identifiers)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.identifiers = identifiers ?? throw new ArgumentNullException(nameof(identifiers));
    }

    public async Task<ApplicationExecutionPreferenceResponse> ReadCurrentAsync(
        Guid userId,
        Guid applicationDefinitionId,
        string workspaceClass,
        string? presentedKey,
        CancellationToken cancellationToken = default)
    {
        var parsed = ParseWorkspaceClass(workspaceClass);
        var application = await this.RequireApplicationAsync(applicationDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        var deviceId = await this.ResolveDeviceAsync(userId, presentedKey, cancellationToken)
            .ConfigureAwait(false);

        var stored = await this
            .ResolveAsync(userId, applicationDefinitionId, parsed, deviceId, cancellationToken)
            .ConfigureAwait(false);

        return stored is null
            ? new ApplicationExecutionPreferenceResponse(
                applicationDefinitionId,
                workspaceClass,
                LayoutScopeNames.Shared,
                // Suspend is the documented default. An application that has never been
                // configured never gets to keep running just because it can.
                BackgroundModeNames.Suspend,
                application.SurfaceSupportsKeepActive,
                Revision: 0)
            : ToResponse(stored, workspaceClass, application.SurfaceSupportsKeepActive);
    }

    public async Task<ApplicationExecutionPreferenceWriteResult> WriteCurrentAsync(
        Guid userId,
        Guid applicationDefinitionId,
        string workspaceClass,
        string? presentedKey,
        UpdateApplicationExecutionPreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsed = ParseWorkspaceClass(workspaceClass);
        var mode = ParseBackgroundMode(request.BackgroundMode);
        var application = await this.RequireApplicationAsync(applicationDefinitionId, cancellationToken)
            .ConfigureAwait(false);

        if (mode == BackgroundMode.KeepSurfaceActive && !application.SurfaceSupportsKeepActive)
        {
            // Storing a mode the manifest does not declare would leave the stored preference
            // and the resolved Surface state disagreeing, which section 11 requires to map
            // exactly.
            throw new ApplicationExecutionPreferenceException(
                ClientDeviceErrorCodes.BackgroundModeUnsupported,
                "The application does not declare that it can stay active in the background.");
        }

        var deviceId = await this.ResolveDeviceAsync(userId, presentedKey, cancellationToken)
            .ConfigureAwait(false);
        // A write targets exactly one scope. Falling back to the shared row here would
        // silently rewrite the user's other devices when they configured only this one.
        var row = await this
            .FindInScopeAsync(userId, applicationDefinitionId, parsed, deviceId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            if (request.ExpectedRevision is not null and not 0)
            {
                throw new ConcurrencyConflictException(
                    currentRevision: 0,
                    new InvalidOperationException("The execution preference does not exist yet."));
            }

            row = new ApplicationExecutionPreferenceRow
            {
                Id = this.identifiers.Create(),
                OwnerUserId = userId,
                ApplicationDefinitionId = applicationDefinitionId,
                WorkspaceClass = parsed,
                ClientDeviceId = deviceId,
                BackgroundMode = mode,
                Revision = 1,
            };
            _ = this.context.ApplicationExecutionPreferences.Add(row);
            _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new ApplicationExecutionPreferenceWriteResult(
                ToResponse(row, workspaceClass, application.SurfaceSupportsKeepActive),
                Created: true);
        }

        if (request.ExpectedRevision is int expected && row.Revision != expected)
        {
            throw new ConcurrencyConflictException(
                row.Revision,
                new InvalidOperationException("The execution preference changed concurrently."));
        }

        row.BackgroundMode = mode;
        row.Revision = checked(row.Revision + 1);
        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ApplicationExecutionPreferenceWriteResult(
            ToResponse(row, workspaceClass, application.SurfaceSupportsKeepActive),
            Created: false);
    }

    private async Task<ApplicationDefinitionRow> RequireApplicationAsync(
        Guid applicationDefinitionId,
        CancellationToken cancellationToken)
    {
        return await this.context.ApplicationDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == applicationDefinitionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ApplicationExecutionPreferenceException(
                ClientDeviceErrorCodes.NotFound,
                "The application does not exist.");
    }

    /// <summary>
    /// Resolves which device a preference belongs to.
    /// </summary>
    /// <remarks>
    /// A preference is device-scoped when this browser is a registered device of the
    /// authenticated user, and shared otherwise. The device never widens what the caller
    /// can reach: it only narrows which of the caller's own rows answer.
    /// </remarks>
    private async Task<Guid?> ResolveDeviceAsync(
        Guid userId,
        string? presentedKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presentedKey))
        {
            return null;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey)));
        var device = await this.context.ClientDevices
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.OwnerUserId == userId && row.ClientInstanceKeyHash == hash,
                cancellationToken)
            .ConfigureAwait(false);
        return device?.Id;
    }

    /// <summary>Finds the row for exactly one scope, without falling back to the other.</summary>
    private Task<ApplicationExecutionPreferenceRow?> FindInScopeAsync(
        Guid userId,
        Guid applicationDefinitionId,
        WorkspaceClass workspaceClass,
        Guid? deviceId,
        CancellationToken cancellationToken) =>
        this.context.ApplicationExecutionPreferences
            .SingleOrDefaultAsync(
                row => row.OwnerUserId == userId
                    && row.ApplicationDefinitionId == applicationDefinitionId
                    && row.WorkspaceClass == workspaceClass
                    && row.ClientDeviceId == deviceId,
                cancellationToken);

    private async Task<ApplicationExecutionPreferenceRow?> ResolveAsync(
        Guid userId,
        Guid applicationDefinitionId,
        WorkspaceClass workspaceClass,
        Guid? deviceId,
        CancellationToken cancellationToken)
    {
        // A device-scoped preference answers ahead of the shared one, so a phone can keep a
        // chat application awake without changing what the same user gets on a desktop.
        if (deviceId is Guid device)
        {
            var scoped = await this.context.ApplicationExecutionPreferences
                .SingleOrDefaultAsync(
                    row => row.OwnerUserId == userId
                        && row.ApplicationDefinitionId == applicationDefinitionId
                        && row.WorkspaceClass == workspaceClass
                        && row.ClientDeviceId == device,
                    cancellationToken)
                .ConfigureAwait(false);
            if (scoped is not null)
            {
                return scoped;
            }
        }

        return await this.context.ApplicationExecutionPreferences
            .SingleOrDefaultAsync(
                row => row.OwnerUserId == userId
                    && row.ApplicationDefinitionId == applicationDefinitionId
                    && row.WorkspaceClass == workspaceClass
                    && row.ClientDeviceId == null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ApplicationExecutionPreferenceResponse ToResponse(
        ApplicationExecutionPreferenceRow row,
        string workspaceClass,
        bool supportsKeepActive) => new(
        row.ApplicationDefinitionId,
        workspaceClass,
        row.ClientDeviceId is null ? LayoutScopeNames.Shared : LayoutScopeNames.Device,
        row.BackgroundMode == BackgroundMode.KeepSurfaceActive
            ? BackgroundModeNames.KeepSurfaceActive
            : BackgroundModeNames.Suspend,
        supportsKeepActive,
        row.Revision);

    private static WorkspaceClass ParseWorkspaceClass(string value) => value switch
    {
        WorkspaceClassNames.Phone => WorkspaceClass.Phone,
        WorkspaceClassNames.Tablet => WorkspaceClass.Tablet,
        WorkspaceClassNames.DesktopSingle => WorkspaceClass.DesktopSingle,
        WorkspaceClassNames.DesktopMulti => WorkspaceClass.DesktopMulti,
        _ => throw new ApplicationExecutionPreferenceException(
            ClientDeviceErrorCodes.WorkspaceClassInvalid,
            "The workspace class is not supported."),
    };

    private static BackgroundMode ParseBackgroundMode(string value) => value switch
    {
        BackgroundModeNames.Suspend => BackgroundMode.Suspend,
        BackgroundModeNames.KeepSurfaceActive => BackgroundMode.KeepSurfaceActive,
        _ => throw new ApplicationExecutionPreferenceException(
            ClientDeviceErrorCodes.BackgroundModeUnsupported,
            "The background mode is not supported."),
    };
}
