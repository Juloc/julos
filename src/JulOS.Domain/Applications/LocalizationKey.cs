namespace JulOS.Domain.Applications;

/// <summary>
/// A reference into a package's localization bundle.
/// </summary>
/// <remarks>
/// An application stores keys rather than text. Storing the text would fix one language
/// into the record and break the rule that no user-facing string is hard-coded, so the
/// domain never holds a display name at all — only the key the client resolves.
/// </remarks>
public readonly record struct LocalizationKey
{
    private const int MaximumLength = 128;

    private LocalizationKey(string value) => this.Value = value;

    /// <summary>The key value.</summary>
    public string Value { get; }

    /// <summary>Reads a localization key.</summary>
    /// <param name="value">Letters, digits, dots, hyphens and underscores.</param>
    /// <exception cref="DomainRuleViolationException">The value is not a valid localization key.</exception>
    public static LocalizationKey Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new DomainRuleViolationException(
                "application.localization_key.invalid",
                "A localization key contains only letters, digits, dots, hyphens and underscores, and no whitespace.");
        }

        return new LocalizationKey(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
