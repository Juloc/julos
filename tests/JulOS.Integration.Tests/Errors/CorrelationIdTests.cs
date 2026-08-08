using JulOS.Server.Errors;

using Microsoft.AspNetCore.Http;

namespace JulOS.Integration.Tests.Errors;

/// <summary>Verifies which caller-supplied correlation identifiers are safe to echo.</summary>
[TestClass]
public sealed class CorrelationIdTests
{
    [TestMethod]
    public void GetReturnsTheValueSetByTheMiddleware()
    {
        var context = new DefaultHttpContext();
        CorrelationId.Set(context, "abc-123_XYZ");

        Assert.AreEqual("abc-123_XYZ", CorrelationId.Get(context));
    }

    [TestMethod]
    public void GetSanitizesTheDefaultKestrelTraceIdentifierFormat()
    {
        // Kestrel's default TraceIdentifier joins a connection ID and a request ordinal
        // with a colon, which downstream consumers such as the audit correlation
        // identifier reject. This reproduces that shape without the middleware having run.
        var context = new DefaultHttpContext { TraceIdentifier = "0HN7VJ8Q2K9RO:00000001" };

        var correlationId = CorrelationId.Get(context);

        Assert.IsFalse(correlationId.Contains(':', StringComparison.Ordinal));
        Assert.IsTrue(correlationId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
    }
    [TestMethod]
    public void AnUnreservedValueIsAccepted()
    {
        Assert.AreEqual("abc-123_XYZ", CorrelationId.Accept("abc-123_XYZ"));
    }

    [TestMethod]
    public void AMissingValueProducesAGeneratedOne()
    {
        Assert.IsTrue(Guid.TryParse(CorrelationId.Accept(null), out _));
        Assert.IsTrue(Guid.TryParse(CorrelationId.Accept(string.Empty), out _));
    }

    [TestMethod]
    public void AValueWithReservedCharactersIsReplaced()
    {
        foreach (var unsafeValue in new[] { "with space", "line\nbreak", "semi;colon", "quote\"mark", "sl/ash" })
        {
            Assert.AreNotEqual(unsafeValue, CorrelationId.Accept(unsafeValue), $"'{unsafeValue}' must not be echoed.");
        }
    }

    [TestMethod]
    public void AnOverlongValueIsReplaced()
    {
        var overlong = new string('a', 65);

        Assert.AreNotEqual(overlong, CorrelationId.Accept(overlong));
    }

    [TestMethod]
    public void AValueAtTheLengthLimitIsStillAccepted()
    {
        var atLimit = new string('a', 64);

        Assert.AreEqual(atLimit, CorrelationId.Accept(atLimit));
    }
}
