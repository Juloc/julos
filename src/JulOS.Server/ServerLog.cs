namespace JulOS.Server;

/// <summary>
/// Source-generated log messages of the server host.
/// </summary>
/// <remarks>
/// Every message carries a stable event identifier, as required by the logging and
/// diagnostics rules in <c>docs/SECURITY_AND_OPERATIONS.md</c>.
/// </remarks>
internal static partial class ServerLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "{Component} {Version} is starting.")]
    internal static partial void Starting(ILogger logger, string component, string version);
}
