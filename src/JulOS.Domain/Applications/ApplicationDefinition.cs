using JulOS.Domain.Packages;
using JulOS.Domain.Primitives;

namespace JulOS.Domain.Applications;

/// <summary>
/// One application a package registers with the desktop.
/// </summary>
/// <remarks>
/// Identity is the owning package plus the stable key. Everything a user reads is a
/// localization key, so the record holds no display text and a rename cannot change
/// what a stored window, launcher entry or permission refers to.
/// </remarks>
public sealed class ApplicationDefinition
{
    private readonly HashSet<ViewportClass> supportedViewportClasses;

    private ApplicationDefinition(
        ApplicationDefinitionId id,
        PackageId owningPackageId,
        ApplicationStableKey stableKey,
        LocalizationKey displayNameKey,
        ApplicationInstancePolicy instancePolicy,
        WindowSizeConstraints windowSize,
        IEnumerable<ViewportClass> supportedViewportClasses)
    {
        this.Id = id;
        this.OwningPackageId = owningPackageId;
        this.StableKey = stableKey;
        this.DisplayNameKey = displayNameKey;
        this.InstancePolicy = instancePolicy;
        this.WindowSize = windowSize;
        this.supportedViewportClasses = [.. supportedViewportClasses];
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated identity of this registration.</summary>
    public ApplicationDefinitionId Id { get; }

    /// <summary>The package that owns and registered the application.</summary>
    public PackageId OwningPackageId { get; }

    /// <summary>The package-declared key that identifies the application inside its package.</summary>
    public ApplicationStableKey StableKey { get; }

    /// <summary>The localization key of the name shown to the user. Never the name itself.</summary>
    public LocalizationKey DisplayNameKey { get; private set; }

    /// <summary>How many windows of the application may exist at the same time.</summary>
    public ApplicationInstancePolicy InstancePolicy { get; }

    /// <summary>The declared default and minimum window size.</summary>
    public WindowSizeConstraints WindowSize { get; }

    /// <summary>The viewport classes the application is usable in.</summary>
    public IReadOnlyCollection<ViewportClass> SupportedViewportClasses => this.supportedViewportClasses;

    /// <summary>Whether the application is currently offered to users.</summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Registers an application.</summary>
    /// <exception cref="DomainRuleViolationException">No viewport class is supported.</exception>
    public static ApplicationDefinition Register(
        ApplicationDefinitionId id,
        PackageId owningPackageId,
        ApplicationStableKey stableKey,
        LocalizationKey displayNameKey,
        ApplicationInstancePolicy instancePolicy,
        WindowSizeConstraints windowSize,
        IEnumerable<ViewportClass> supportedViewportClasses)
    {
        ArgumentNullException.ThrowIfNull(supportedViewportClasses);

        var supported = supportedViewportClasses.ToArray();

        if (supported.Length == 0)
        {
            throw new DomainRuleViolationException(
                "application.viewport.none_supported",
                "An application that supports no viewport class could never be opened.");
        }

        return new ApplicationDefinition(
            id,
            owningPackageId,
            stableKey,
            displayNameKey,
            instancePolicy,
            windowSize,
            supported);
    }

    /// <summary>Returns whether the application can be opened in the given viewport class.</summary>
    public bool SupportsViewport(ViewportClass viewportClass) =>
        this.supportedViewportClasses.Contains(viewportClass);

    /// <summary>
    /// Points the application at a different localization key. Identity is unaffected,
    /// which is the whole reason the record stores a key rather than a name.
    /// </summary>
    public void RenameTo(LocalizationKey displayNameKey)
    {
        this.DisplayNameKey = displayNameKey;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Stops offering the application without removing it or its stored state.</summary>
    public void Disable()
    {
        this.IsEnabled = false;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Offers the application again.</summary>
    public void Enable()
    {
        this.IsEnabled = true;
        this.Revision = this.Revision.Next();
    }
}
