using JulOS.Infrastructure.Identifiers;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Infrastructure.Tests.Identifiers;

/// <summary>Verifies the core identifier generator.</summary>
[TestClass]
public sealed class TimeOrderedIdentifierGeneratorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void EveryIdentifierIsUnique()
    {
        var generator = new TimeOrderedIdentifierGenerator(new FakeTimeProvider(Start));

        var identifiers = Enumerable.Range(0, 1000).Select(_ => generator.Create()).ToHashSet();

        Assert.AreEqual(1000, identifiers.Count, "Identifiers generated within the same instant must still differ.");
    }

    [TestMethod]
    public void NoIdentifierIsEmpty()
    {
        var generator = new TimeOrderedIdentifierGenerator(new FakeTimeProvider(Start));

        Assert.AreNotEqual(Guid.Empty, generator.Create());
    }

    [TestMethod]
    public void LaterIdentifiersSortAfterEarlierOnes()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var generator = new TimeOrderedIdentifierGenerator(timeProvider);

        var earlier = generator.Create();
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var later = generator.Create();

        Assert.IsLessThan(0, earlier.CompareTo(later), "Time ordering is what keeps the primary key index compact.");
    }

    [TestMethod]
    public void TheTimeSourceIsRequired()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new TimeOrderedIdentifierGenerator(null!));
    }
}
