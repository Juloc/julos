using JulOS.Domain.Sessions;

namespace JulOS.Domain.Tests.Sessions;

/// <summary>Verifies the identity of one session reference.</summary>
[TestClass]
public sealed class SessionReferenceIdTests
{
    [TestMethod]
    public void AGeneratedValueIsAccepted()
    {
        var value = Guid.CreateVersion7();

        Assert.AreEqual(value, new SessionReferenceId(value).Value);
    }

    [TestMethod]
    public void AnEmptyValueIsRejectedBecauseItIdentifiesNothing()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SessionReferenceId(Guid.Empty));
    }
}
