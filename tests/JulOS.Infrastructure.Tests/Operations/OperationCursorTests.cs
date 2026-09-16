using JulOS.Application.Operations;
using JulOS.Infrastructure.Operations;

namespace JulOS.Infrastructure.Tests.Operations;

/// <summary>MOB-008: the opaque continuation of one Operation Center page.</summary>
[TestClass]
public sealed class OperationCursorTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OperationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-03-01T08:00:00Z", null);

    [TestMethod]
    public void ACursorRoundTripsTheSortTuple()
    {
        var fingerprint = OperationCursor.Fingerprint(new OperationQuery(Owner));

        var read = OperationCursor.Read(OperationCursor.Write(Created, OperationId, fingerprint), fingerprint);

        Assert.IsNotNull(read);
        Assert.AreEqual(Created, read.Value.CreatedAtUtc);
        Assert.AreEqual(OperationId, read.Value.OperationId);
    }

    [TestMethod]
    public void NoCursorMeansTheFirstPage()
    {
        Assert.IsNull(OperationCursor.Read(null, "x"));
        Assert.IsNull(OperationCursor.Read("   ", "x"));
    }

    [TestMethod]
    public void ACursorCannotBeReusedAgainstDifferentFilters()
    {
        var failed = OperationCursor.Fingerprint(
            new OperationQuery(Owner, States: [OperationState.Failed]));
        var everything = OperationCursor.Fingerprint(new OperationQuery(Owner));
        var cursor = OperationCursor.Write(Created, OperationId, failed);

        // Continuing a filtered page into an unfiltered list would silently skip whatever
        // sorted between them, so the cursor refuses rather than returning a wrong page.
        var failure = Assert.ThrowsExactly<OperationFailureException>(
            () => OperationCursor.Read(cursor, everything));
        Assert.AreEqual(OperationFailureReason.Invalid, failure.Reason);
    }

    [TestMethod]
    public void AnotherUsersCursorDoesNotContinueThisUsersList()
    {
        var mine = OperationCursor.Fingerprint(new OperationQuery(Owner));
        var theirs = OperationCursor.Fingerprint(
            new OperationQuery(Guid.Parse("33333333-3333-3333-3333-333333333333")));

        Assert.AreNotEqual(mine, theirs);
        _ = Assert.ThrowsExactly<OperationFailureException>(
            () => OperationCursor.Read(OperationCursor.Write(Created, OperationId, theirs), mine));
    }

    [TestMethod]
    public void AStateFilterFingerprintsTheSameWhateverOrderItArrivesIn()
    {
        var first = OperationCursor.Fingerprint(
            new OperationQuery(Owner, States: [OperationState.Failed, OperationState.Queued]));
        var second = OperationCursor.Fingerprint(
            new OperationQuery(Owner, States: [OperationState.Queued, OperationState.Failed]));

        Assert.AreEqual(first, second, "The same filter set must continue its own pages.");
    }

    [TestMethod]
    public void ATamperedCursorIsRefusedRatherThanChangingThePage()
    {
        var fingerprint = OperationCursor.Fingerprint(new OperationQuery(Owner));

        foreach (var cursor in new[] { "not-base64-url!!", "", "AAAA", "Zm9vfGJhcg" })
        {
            if (cursor.Length == 0)
            {
                Assert.IsNull(OperationCursor.Read(cursor, fingerprint));
                continue;
            }

            _ = Assert.ThrowsExactly<OperationFailureException>(
                () => OperationCursor.Read(cursor, fingerprint),
                $"'{cursor}' should have been refused");
        }
    }
}
