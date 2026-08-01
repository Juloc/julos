namespace JulOS.Domain.Sessions;

/// <summary>
/// A stable code recorded when a session reference disconnects or ends abnormally.
/// </summary>
/// <remarks>
/// The code is dotted and stable, for example <c>session.failure.connection_lost</c>, so a
/// client and a log entry can rely on its value across releases. It names a cause, never a
/// protocol or transport.
/// </remarks>
public readonly record struct SessionFailureCode(string Value)
{
    /// <summary>The wrapped code value.</summary>
    public string Value { get; } = ValidatedValue(Value);

    /// <inheritdoc />
    public override string ToString() => this.Value;

    private static string ValidatedValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value;
    }
}
