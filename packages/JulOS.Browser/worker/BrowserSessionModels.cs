namespace JulOS.Browser.Worker;

/// <summary>Browser profile modes accepted by the Browser package.</summary>
internal static class BrowserSessionProfileModes
{
    internal const string Temporary = "temporary";
    internal const string Persistent = "persistent";
    internal const string Application = "application";
}

/// <summary>Opaque Browser request carried inside the generic interactive-session envelope.</summary>
/// <param name="InitialUrl">Absolute HTTP or HTTPS start URL.</param>
/// <param name="ProfileMode">Browser profile mode.</param>
/// <param name="ProfileId">Retained Browser profile identity when required by the mode.</param>
internal sealed record BrowserSessionRequest(
    string InitialUrl,
    string ProfileMode,
    Guid? ProfileId);
