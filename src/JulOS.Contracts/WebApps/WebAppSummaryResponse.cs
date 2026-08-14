namespace JulOS.Contracts.WebApps;

/// <summary>A local web-application target the desktop can open in a window.</summary>
/// <param name="Host">The JulOS host that serves the target through the local proxy.</param>
public sealed record WebAppSummaryResponse(string Host);
