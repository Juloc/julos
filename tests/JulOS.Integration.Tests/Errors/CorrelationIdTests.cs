using JulOS.Server.Errors;

namespace JulOS.Integration.Tests.Errors;

/// <summary>Verifies which caller-supplied correlation identifiers are safe to echo.</summary>
[TestClass]
public sealed class CorrelationIdTests
{
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
