using JulOS.Domain.Applications;
using JulOS.Domain.Primitives;

namespace JulOS.Domain.Devices;

/// <summary>
/// What one user wants to happen to one application's surface when it leaves the visible
/// foreground, in one workspace class.
/// </summary>
/// <remarks>
/// <para>
/// The preference belongs to the user, never to the application: <c>docs/MOBILE_PWA.md</c>
/// section 11 states an application can neither request nor persist a mode for itself.
/// </para>
/// <para>
/// A null <see cref="ClientDeviceId"/> is the user's shared preference for that workspace
/// class; a device-scoped row overrides it for that one device.
/// </para>
/// </remarks>
public sealed class ApplicationExecutionPreference
{
    private ApplicationExecutionPreference(
        Guid ownerUserId,
        ApplicationDefinitionId applicationDefinitionId,
        WorkspaceClass workspaceClass,
        ClientDeviceId? clientDeviceId,
        BackgroundMode backgroundMode)
    {
        this.OwnerUserId = ownerUserId;
        this.ApplicationDefinitionId = applicationDefinitionId;
        this.WorkspaceClass = workspaceClass;
        this.ClientDeviceId = clientDeviceId;
        this.BackgroundMode = backgroundMode;
        this.Revision = Revision.Initial;
    }

    /// <summary>The user the preference belongs to.</summary>
    public Guid OwnerUserId { get; }

    /// <summary>The application the preference applies to.</summary>
    public ApplicationDefinitionId ApplicationDefinitionId { get; }

    /// <summary>The workspace class the preference applies to.</summary>
    public WorkspaceClass WorkspaceClass { get; }

    /// <summary>The device this preference is private to, or null for the shared preference.</summary>
    public ClientDeviceId? ClientDeviceId { get; }

    /// <summary>The chosen background behaviour.</summary>
    public BackgroundMode BackgroundMode { get; private set; }

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Creates a preference.</summary>
    /// <exception cref="DomainRuleViolationException">The preference does not identify a user.</exception>
    public static ApplicationExecutionPreference Create(
        Guid ownerUserId,
        ApplicationDefinitionId applicationDefinitionId,
        WorkspaceClass workspaceClass,
        ClientDeviceId? clientDeviceId,
        BackgroundMode backgroundMode)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "client_device.not_owned",
                "An execution preference belongs to an authenticated user.");
        }

        return new ApplicationExecutionPreference(
            ownerUserId,
            applicationDefinitionId,
            workspaceClass,
            clientDeviceId,
            backgroundMode);
    }

    /// <summary>Changes the background behaviour.</summary>
    public void ChangeBackgroundMode(BackgroundMode backgroundMode)
    {
        this.BackgroundMode = backgroundMode;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Restores a persisted preference without advancing its revision.</summary>
    public static ApplicationExecutionPreference Restore(
        Guid ownerUserId,
        ApplicationDefinitionId applicationDefinitionId,
        WorkspaceClass workspaceClass,
        ClientDeviceId? clientDeviceId,
        BackgroundMode backgroundMode,
        Revision revision) =>
        new(ownerUserId, applicationDefinitionId, workspaceClass, clientDeviceId, backgroundMode)
        {
            Revision = revision,
        };
}
