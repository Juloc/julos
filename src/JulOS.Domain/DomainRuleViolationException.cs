namespace JulOS.Domain;

/// <summary>
/// A refused domain operation.
/// </summary>
/// <remarks>
/// The domain refuses an operation by throwing rather than by returning a neutral
/// result, because a caller that ignores a returned failure produces exactly the
/// silent invalid state the platform rules forbid. <see cref="Code"/> is stable and
/// is what an API response and a log entry carry; the message explains the case.
/// </remarks>
public sealed class DomainRuleViolationException : Exception
{
    /// <summary>Creates the exception for one stable rule code.</summary>
    /// <param name="code">A stable code such as <c>package.transition.invalid</c>.</param>
    /// <param name="message">A message that explains the refusal and contains no secret.</param>
    public DomainRuleViolationException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        this.Code = code;
    }

    /// <summary>Creates the exception for one stable rule code and an underlying cause.</summary>
    /// <param name="code">A stable code such as <c>package.transition.invalid</c>.</param>
    /// <param name="message">A message that explains the refusal and contains no secret.</param>
    /// <param name="innerException">The cause to preserve.</param>
    public DomainRuleViolationException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        this.Code = code;
    }

    /// <summary>The stable code identifying which rule refused the operation.</summary>
    public string Code { get; }
}
