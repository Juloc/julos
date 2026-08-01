using JulOS.Domain.Sessions;

namespace JulOS.Domain.Tests.Sessions;

/// <summary>Verifies the stable code recorded for an abnormal disconnect or end.</summary>
[TestClass]
public sealed class SessionFailureCodeTests
{
    [TestMethod]
    public void AStableDottedCodeIsAccepted()
    {
        var code = new SessionFailureCode("session.failure.connection_lost");

        Assert.AreEqual("session.failure.connection_lost", code.Value);
        Assert.AreEqual("session.failure.connection_lost", code.ToString());
    }

    [TestMethod]
    public void AnEmptyCodeIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SessionFailureCode(string.Empty));
    }

    [TestMethod]
    public void AWhitespaceCodeIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SessionFailureCode("   "));
    }
}
