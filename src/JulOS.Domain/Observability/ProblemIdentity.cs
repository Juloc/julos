using JulOS.Domain.Packages;

namespace JulOS.Domain.Observability;

/// <summary>
/// What makes two observations the same problem.
/// </summary>
/// <remarks>
/// The identity is the reporting package, the kind of condition and the stable identity
/// of the affected resource. A restart loop observed a hundred times is one problem with
/// a hundred observations, not a hundred entries an operator has to dismiss one by one.
/// </remarks>
/// <param name="SourcePackageId">The package that detected the condition.</param>
/// <param name="ProblemType">The kind of condition, for example <c>resource.unreachable</c>.</param>
/// <param name="ResourceIdentity">The stable identity of the affected resource.</param>
public readonly record struct ProblemIdentity(
    PackageId SourcePackageId,
    string ProblemType,
    string ResourceIdentity)
{
    private const int MaximumPartLength = 256;

    /// <summary>The kind of condition, for example <c>resource.unreachable</c>.</summary>
    public string ProblemType { get; } = ValidatedPart(ProblemType, nameof(ProblemType));

    /// <summary>The stable identity of the affected resource.</summary>
    public string ResourceIdentity { get; } = ValidatedPart(ResourceIdentity, nameof(ResourceIdentity));

    /// <inheritdoc />
    public override string ToString() =>
        $"{this.SourcePackageId.Value}|{this.ProblemType}|{this.ResourceIdentity}";

    private static string ValidatedPart(string value, string part)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumPartLength
            || value.Length != value.Trim().Length)
        {
            throw new DomainRuleViolationException(
                "problem.identity.invalid",
                $"'{part}' must be non-empty, at most {MaximumPartLength} characters and without surrounding whitespace.");
        }

        return value;
    }
}
