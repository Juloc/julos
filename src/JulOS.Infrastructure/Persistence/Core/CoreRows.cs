using JulOS.Domain.Agents;
using JulOS.Domain.Applications;
using JulOS.Domain.Layouts;
using JulOS.Domain.Observability;
using JulOS.Domain.Packages;
using JulOS.Domain.Permissions;
using JulOS.Domain.Primitives;
using JulOS.Domain.Sessions;

namespace JulOS.Infrastructure.Persistence.Core;

// These types are the relational storage shape only. Domain behavior remains in
// JulOS.Domain; no rule is reimplemented here.
internal sealed class PackageInstallationRow
{
    internal Guid Id { get; set; }

    internal required string PackageId { get; set; }

    internal PackageInstallationState State { get; set; }

    internal int Revision { get; set; }

    internal string? FaultCode { get; set; }

    internal string? FaultDetail { get; set; }

    internal DateTimeOffset? FaultedAtUtc { get; set; }

    internal static PackageInstallationRow FromDomain(PackageInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        return new PackageInstallationRow
        {
            Id = installation.Id.Value,
            PackageId = installation.PackageId.Value,
            State = installation.State,
            Revision = installation.Revision.Value,
            FaultCode = installation.FaultCode,
            FaultDetail = installation.FaultDetail,
            FaultedAtUtc = installation.FaultedAtUtc,
        };
    }
}

internal sealed class ApplicationDefinitionRow
{
    internal Guid Id { get; set; }

    internal required string OwningPackageId { get; set; }

    internal required string StableKey { get; set; }

    internal required string DisplayNameKey { get; set; }

    internal ApplicationInstancePolicy InstancePolicy { get; set; }

    internal int DefaultWidth { get; set; }

    internal int DefaultHeight { get; set; }

    internal int MinimumWidth { get; set; }

    internal int MinimumHeight { get; set; }

    internal bool IsEnabled { get; set; }

    internal int Revision { get; set; }

    internal List<ApplicationViewportRow> SupportedViewports { get; } = [];

    internal static ApplicationDefinitionRow FromDomain(ApplicationDefinition application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var row = new ApplicationDefinitionRow
        {
            Id = application.Id.Value,
            OwningPackageId = application.OwningPackageId.Value,
            StableKey = application.StableKey.Value,
            DisplayNameKey = application.DisplayNameKey.Value,
            InstancePolicy = application.InstancePolicy,
            DefaultWidth = application.WindowSize.DefaultWidth,
            DefaultHeight = application.WindowSize.DefaultHeight,
            MinimumWidth = application.WindowSize.MinimumWidth,
            MinimumHeight = application.WindowSize.MinimumHeight,
            IsEnabled = application.IsEnabled,
            Revision = application.Revision.Value,
        };

        foreach (var viewportClass in application.SupportedViewportClasses)
        {
            row.SupportedViewports.Add(new ApplicationViewportRow
            {
                ApplicationDefinitionId = application.Id.Value,
                ViewportClass = viewportClass,
            });
        }

        return row;
    }
}

internal sealed class ApplicationViewportRow
{
    internal Guid ApplicationDefinitionId { get; set; }

    internal ViewportClass ViewportClass { get; set; }
}

internal sealed class LaunchTargetRow
{
    internal Guid Id { get; set; }

    internal Guid ApplicationDefinitionId { get; set; }

    internal required string OwningPackageId { get; set; }

    internal required string ExternalIdentity { get; set; }

    internal required string DisplayName { get; set; }

    internal LaunchTargetApprovalState ApprovalState { get; set; }

    internal DateTimeOffset FirstObservedAtUtc { get; set; }

    internal DateTimeOffset LastObservedAtUtc { get; set; }

    internal DateTimeOffset? ApprovedAtUtc { get; set; }

    internal Guid? ApprovedByUserId { get; set; }

    internal int Revision { get; set; }

    internal static LaunchTargetRow FromDomain(LaunchTarget target, Guid? approvedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        return new LaunchTargetRow
        {
            Id = target.Id.Value,
            ApplicationDefinitionId = target.ApplicationDefinitionId.Value,
            OwningPackageId = target.OwningPackageId.Value,
            ExternalIdentity = target.ExternalIdentity.Value,
            DisplayName = target.DisplayName,
            ApprovalState = target.ApprovalState,
            FirstObservedAtUtc = target.FirstObservedAtUtc,
            LastObservedAtUtc = target.LastObservedAtUtc,
            ApprovedAtUtc = target.ApprovedAtUtc,
            ApprovedByUserId = approvedByUserId,
            Revision = target.Revision.Value,
        };
    }
}

internal sealed class DesktopLayoutRow
{
    internal Guid Id { get; set; }

    internal Guid UserId { get; set; }

    internal ViewportClass ViewportClass { get; set; }

    internal required string Name { get; set; }

    internal bool IsDefault { get; set; }

    internal int Revision { get; set; }

    internal DateTimeOffset UpdatedAtUtc { get; set; }

    internal List<DesktopWindowRow> Windows { get; } = [];

    internal List<WidgetPlacementRow> Widgets { get; } = [];

    internal static DesktopLayoutRow FromDomain(
        DesktopLayout layout,
        Guid userId,
        string name,
        bool isDefault,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var row = new DesktopLayoutRow
        {
            Id = layout.Id.Value,
            UserId = EntityIdentifier.Validated(userId),
            ViewportClass = layout.ViewportClass,
            Name = name,
            IsDefault = isDefault,
            Revision = layout.Revision.Value,
            UpdatedAtUtc = updatedAtUtc,
        };

        foreach (var window in layout.Windows)
        {
            row.Windows.Add(DesktopWindowRow.FromDomain(window, layout.Id.Value, updatedAtUtc));
        }

        foreach (var widget in layout.Widgets)
        {
            row.Widgets.Add(WidgetPlacementRow.FromDomain(widget, layout.Id.Value));
        }

        return row;
    }
}

internal sealed class DesktopWindowRow
{
    internal Guid Id { get; set; }

    internal Guid DesktopLayoutId { get; set; }

    internal Guid ApplicationDefinitionId { get; set; }

    internal Guid? LaunchTargetId { get; set; }

    internal WindowState State { get; set; }

    internal int X { get; set; }

    internal int Y { get; set; }

    internal int Width { get; set; }

    internal int Height { get; set; }

    internal int RestoreX { get; set; }

    internal int RestoreY { get; set; }

    internal int RestoreWidth { get; set; }

    internal int RestoreHeight { get; set; }

    internal int ZIndex { get; set; }

    internal Guid? SessionReferenceId { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset UpdatedAtUtc { get; set; }

    internal int Revision { get; set; }

    internal static DesktopWindowRow FromDomain(
        DesktopWindow window,
        Guid desktopLayoutId,
        DateTimeOffset observedAtUtc,
        Guid? sessionReferenceId = null,
        int revision = 1)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new DesktopWindowRow
        {
            Id = window.Id.Value,
            DesktopLayoutId = EntityIdentifier.Validated(desktopLayoutId),
            ApplicationDefinitionId = window.ApplicationId.Value,
            LaunchTargetId = window.LaunchTargetId?.Value,
            State = window.State,
            X = window.Bounds.X,
            Y = window.Bounds.Y,
            Width = window.Bounds.Width,
            Height = window.Bounds.Height,
            RestoreX = window.RestoreBounds.X,
            RestoreY = window.RestoreBounds.Y,
            RestoreWidth = window.RestoreBounds.Width,
            RestoreHeight = window.RestoreBounds.Height,
            ZIndex = window.ZIndex,
            SessionReferenceId = sessionReferenceId,
            CreatedAtUtc = observedAtUtc,
            UpdatedAtUtc = observedAtUtc,
            Revision = JulOS.Domain.Primitives.Revision.From(revision).Value,
        };
    }
}

internal sealed class WidgetPlacementRow
{
    internal Guid Id { get; set; }

    internal Guid DesktopLayoutId { get; set; }

    internal required string WidgetKey { get; set; }

    internal int Column { get; set; }

    internal int Row { get; set; }

    internal int WidthUnits { get; set; }

    internal int HeightUnits { get; set; }

    internal int Revision { get; set; }

    internal static WidgetPlacementRow FromDomain(
        WidgetPlacement placement,
        Guid desktopLayoutId,
        int revision = 1)
    {
        ArgumentNullException.ThrowIfNull(placement);

        return new WidgetPlacementRow
        {
            Id = placement.Id.Value,
            DesktopLayoutId = EntityIdentifier.Validated(desktopLayoutId),
            WidgetKey = placement.WidgetKey,
            Column = placement.Column,
            Row = placement.Row,
            WidthUnits = placement.WidthUnits,
            HeightUnits = placement.HeightUnits,
            Revision = JulOS.Domain.Primitives.Revision.From(revision).Value,
        };
    }
}

internal sealed class SessionReferenceRow
{
    internal Guid Id { get; set; }

    internal required string OwningPackageId { get; set; }

    internal required string SessionKind { get; set; }

    internal required string TargetReference { get; set; }

    internal Guid UserId { get; set; }

    internal SessionState State { get; set; }

    internal SessionLifecyclePolicy LifecyclePolicy { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset? ConnectedAtUtc { get; set; }

    internal DateTimeOffset LastActivityAtUtc { get; set; }

    internal DateTimeOffset? ExpiresAtUtc { get; set; }

    internal DateTimeOffset? EndedAtUtc { get; set; }

    internal string? FailureCode { get; set; }

    internal int Revision { get; set; }

    internal static SessionReferenceRow FromDomain(
        SessionReference session,
        PackageId owningPackageId,
        Guid userId,
        DateTimeOffset lastActivityAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new SessionReferenceRow
        {
            Id = session.Id.Value,
            OwningPackageId = owningPackageId.Value,
            SessionKind = session.Request.Kind,
            TargetReference = session.Request.TargetReference,
            UserId = EntityIdentifier.Validated(userId),
            State = session.State,
            LifecyclePolicy = session.LifecyclePolicy,
            CreatedAtUtc = session.CreatedAtUtc,
            ConnectedAtUtc = session.ConnectedAtUtc,
            LastActivityAtUtc = lastActivityAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            EndedAtUtc = session.EndedAtUtc,
            FailureCode = session.FailureCode?.Value,
            Revision = session.Revision.Value,
        };
    }
}

internal sealed class AgentRow
{
    internal Guid Id { get; set; }

    internal required string Name { get; set; }

    internal required string MachineIdentity { get; set; }

    internal required string OperatingSystem { get; set; }

    internal required string Architecture { get; set; }

    internal required string Version { get; set; }

    internal AgentConnectionState State { get; set; }

    internal DateTimeOffset EnrolledAtUtc { get; set; }

    internal DateTimeOffset? LastSeenAtUtc { get; set; }

    internal DateTimeOffset? RevokedAtUtc { get; set; }

    internal int Revision { get; set; }

    internal static AgentRow FromDomain(Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return new AgentRow
        {
            Id = agent.Id.Value,
            Name = agent.Name,
            MachineIdentity = agent.MachineIdentity.Value,
            OperatingSystem = agent.OperatingSystem,
            Architecture = agent.Architecture,
            Version = agent.Version,
            State = agent.State,
            EnrolledAtUtc = agent.EnrolledAtUtc,
            LastSeenAtUtc = agent.LastSeen?.AtUtc,
            RevokedAtUtc = agent.RevokedAtUtc,
            Revision = agent.Revision.Value,
        };
    }
}

internal sealed class AgentCapabilityRow
{
    internal Guid Id { get; set; }

    internal Guid AgentId { get; set; }

    internal required string CapabilityName { get; set; }

    internal int CapabilityVersion { get; set; }

    internal bool Enabled { get; set; }

    internal int MetadataVersion { get; set; }

    internal required string Metadata { get; set; }

    internal DateTimeOffset ObservedAtUtc { get; set; }

    internal int Revision { get; set; }

    internal static AgentCapabilityRow FromDomain(AgentCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        return new AgentCapabilityRow
        {
            Id = capability.Id.Value,
            AgentId = capability.AgentId.Value,
            CapabilityName = capability.Name.Value,
            CapabilityVersion = capability.Version.Value,
            Enabled = capability.Enabled,
            MetadataVersion = capability.MetadataVersion.Value,
            Metadata = capability.Metadata.Value,
            ObservedAtUtc = capability.ObservedAtUtc,
            Revision = capability.Revision.Value,
        };
    }
}

internal sealed class ProblemRow
{
    internal Guid Id { get; set; }

    internal required string SourcePackageId { get; set; }

    internal required string ProblemType { get; set; }

    internal required string StableResourceIdentity { get; set; }

    internal ProblemSeverity Severity { get; set; }

    internal ProblemState State { get; set; }

    internal required string TitleKey { get; set; }

    internal DateTimeOffset FirstDetectedAtUtc { get; set; }

    internal DateTimeOffset LastObservedAtUtc { get; set; }

    internal DateTimeOffset? AcknowledgedAtUtc { get; set; }

    internal Guid? AcknowledgedByUserId { get; set; }

    internal DateTimeOffset? ResolvedAtUtc { get; set; }

    internal int ObservationCount { get; set; }

    internal int Revision { get; set; }

    internal static ProblemRow FromDomain(Problem problem, Guid? acknowledgedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return new ProblemRow
        {
            Id = problem.Id.Value,
            SourcePackageId = problem.Identity.SourcePackageId.Value,
            ProblemType = problem.Identity.ProblemType,
            StableResourceIdentity = problem.Identity.ResourceIdentity,
            Severity = problem.Severity,
            State = problem.State,
            TitleKey = problem.TitleKey,
            FirstDetectedAtUtc = problem.FirstDetectedAtUtc,
            LastObservedAtUtc = problem.LastObservedAtUtc,
            AcknowledgedAtUtc = problem.AcknowledgedAtUtc,
            AcknowledgedByUserId = acknowledgedByUserId,
            ResolvedAtUtc = problem.ResolvedAtUtc,
            ObservationCount = problem.ObservationCount,
            Revision = problem.Revision.Value,
        };
    }
}

internal sealed class NotificationRow
{
    internal Guid Id { get; set; }

    internal Guid UserId { get; set; }

    internal string? SourcePackageId { get; set; }

    internal ProblemSeverity Severity { get; set; }

    internal required string TitleKey { get; set; }

    internal required string DeduplicationKey { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset? ReadAtUtc { get; set; }

    internal string? ActionLink { get; set; }

    internal static NotificationRow FromDomain(
        Notification notification,
        Guid userId,
        PackageId? sourcePackageId = null,
        string? actionLink = null)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new NotificationRow
        {
            Id = notification.Id.Value,
            UserId = EntityIdentifier.Validated(userId),
            SourcePackageId = sourcePackageId?.Value,
            Severity = notification.Severity,
            TitleKey = notification.TitleKey,
            DeduplicationKey = notification.DeduplicationKey,
            CreatedAtUtc = notification.CreatedAtUtc,
            ReadAtUtc = notification.ReadAtUtc,
            ActionLink = actionLink,
        };
    }
}

internal sealed class AuditEventRow
{
    internal Guid Id { get; set; }

    internal DateTimeOffset OccurredAtUtc { get; set; }

    internal Guid? UserId { get; set; }

    internal Guid? AgentId { get; set; }

    internal string? SourcePackageId { get; set; }

    internal required string Action { get; set; }

    internal required string TargetType { get; set; }

    internal required string TargetId { get; set; }

    internal AuditOutcome Outcome { get; set; }

    internal required string CorrelationId { get; set; }

    internal string? RemoteAddress { get; set; }

    internal required string Summary { get; set; }

    internal required string SafeDetails { get; set; }

    internal static AuditEventRow FromDomain(
        AuditEvent auditEvent,
        Guid? userId = null,
        Guid? agentId = null,
        PackageId? sourcePackageId = null,
        string? remoteAddress = null,
        string? summary = null)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        return new AuditEventRow
        {
            Id = auditEvent.Id.Value,
            OccurredAtUtc = auditEvent.OccurredAtUtc,
            UserId = userId,
            AgentId = agentId,
            SourcePackageId = sourcePackageId?.Value,
            Action = auditEvent.Action,
            TargetType = auditEvent.TargetType,
            TargetId = auditEvent.TargetId,
            Outcome = auditEvent.Outcome,
            CorrelationId = auditEvent.CorrelationId,
            RemoteAddress = remoteAddress,
            Summary = summary ?? auditEvent.Action,
            SafeDetails = auditEvent.SafeDetails,
        };
    }
}

internal sealed class PermissionAssignmentRow
{
    internal Guid Id { get; set; }

    internal PermissionSubjectKind SubjectKind { get; set; }

    internal Guid SubjectId { get; set; }

    internal required string Permission { get; set; }

    internal PermissionScopeKind ScopeKind { get; set; }

    internal string? ScopeId { get; set; }

    internal DateTimeOffset GrantedAtUtc { get; set; }

    internal Guid GrantedByUserId { get; set; }

    internal static PermissionAssignmentRow FromDomain(
        PermissionAssignment assignment,
        Guid grantedByUserId)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return new PermissionAssignmentRow
        {
            Id = assignment.Id.Value,
            SubjectKind = assignment.Subject.Kind,
            SubjectId = assignment.Subject.Id.Value,
            Permission = assignment.Permission.Value,
            ScopeKind = assignment.Scope.Kind,
            ScopeId = assignment.Scope.ScopeId,
            GrantedAtUtc = assignment.GrantedAtUtc,
            GrantedByUserId = EntityIdentifier.Validated(grantedByUserId),
        };
    }
}
