using JulOS.Domain.Primitives;

namespace JulOS.Domain.Tests.Primitives;

/// <summary>Verifies the concurrency revision value.</summary>
[TestClass]
public sealed class RevisionTests
{
    [TestMethod]
    public void ANewRecordStartsAtTheInitialRevision()
    {
        Assert.AreEqual(1, Revision.Initial.Value);
    }

    [TestMethod]
    public void AnAcceptedUpdateMovesToTheNextRevision()
    {
        Assert.AreEqual(Revision.From(2), Revision.Initial.Next());
    }

    [TestMethod]
    public void ARevisionBelowTheInitialOneIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Revision.From(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Revision.From(-1));
    }

    [TestMethod]
    public void AStaleRevisionCompareesOlderThanTheStoredOne()
    {
        var stale = Revision.Initial;
        var stored = stale.Next();

        Assert.IsTrue(stale < stored, "A revision an update was based on must be recognisable as older.");
        Assert.IsTrue(stored > stale);
        Assert.IsFalse(stale == stored);
    }

    [TestMethod]
    public void TwoRevisionsWithTheSameValueAreEqual()
    {
        Assert.AreEqual(Revision.From(7), Revision.From(7));
    }

    [TestMethod]
    public void OverflowIsReportedRatherThanWrappingToAnOlderRevision()
    {
        var last = Revision.From(int.MaxValue);

        Assert.ThrowsExactly<OverflowException>(() => last.Next());
    }
}
