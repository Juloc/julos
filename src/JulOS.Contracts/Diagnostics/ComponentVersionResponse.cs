namespace JulOS.Contracts.Diagnostics;

/// <summary>
/// The version of one running JulOS component.
/// </summary>
/// <param name="Component">
/// The stable component name, for example <c>JulOS.Server</c>. Never a display name.
/// </param>
/// <param name="Version">
/// The semantic version the component was built from, for example <c>0.1.0</c>.
/// </param>
public sealed record ComponentVersionResponse(string Component, string Version);
