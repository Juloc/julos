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

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Administrator permission reconciliation was skipped at startup.")]
    internal static partial void AdministratorPermissionReconciliationSkipped(
        ILogger logger,
        Exception exception);
}
