namespace JulOS.Domain.Applications;

/// <summary>
/// The identity a package gives an application, unique within that package.
/// </summary>
/// <remarks>
/// The stable key is what a layout, a launcher entry and a permission assignment refer
/// to, so it must survive every rename. A display name is a localized label and is never
/// an identity: renaming an application must not orphan a stored window or a granted
/// permission.
/// </remarks>
public readonly record struct ApplicationStableKey
{
    private const int MaximumLength = 64;

    private ApplicationStableKey(string value) => this.Value = value;

    /// <summary>The key value.</summary>
    public string Value { get; }

    /// <summary>Reads a package-declared stable key.</summary>
    /// <param name="value">Lower-case letters, digits, hyphens and dots, starting with a letter.</param>
    /// <exception cref="DomainRuleViolationException">The value is not a valid stable key.</exception>
    public static ApplicationStableKey Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new DomainRuleViolationException(
                "application.stable_key.invalid",
                "An application stable key starts with a lower-case letter and continues with lower-case letters, digits, hyphens or dots.");
        }

        return new ApplicationStableKey(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength || !char.IsAsciiLetterLower(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            var allowed = char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '-' or '.';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
