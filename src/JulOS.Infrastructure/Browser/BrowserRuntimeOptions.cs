using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Browser;

/// <summary>Trusted immutable Browser runtime configuration.</summary>
internal sealed record BrowserRuntimeOptions(string? Image)
{
    internal bool IsConfigured => this.Image is not null;

    internal static BrowserRuntimeOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var image = configuration["Browser:Runtime:Image"]
            ?? Environment.GetEnvironmentVariable("JULOS_BROWSER_RUNTIME_IMAGE");
        if (string.IsNullOrWhiteSpace(image))
        {
            return new BrowserRuntimeOptions(null);
        }

        image = image.Trim();
        const string marker = "@sha256:";
        var markerIndex = image.LastIndexOf(marker, StringComparison.Ordinal);
        var digest = markerIndex < 1 ? string.Empty : image[(markerIndex + marker.Length)..];
        if (digest.Length != 64
            || digest.Any(character => !char.IsAsciiHexDigit(character) || char.IsAsciiLetterUpper(character)))
        {
            throw new InvalidOperationException(
                "Browser:Runtime:Image must be an immutable lowercase sha256 image reference.");
        }

        return new BrowserRuntimeOptions(image);
    }
}
