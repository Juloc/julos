using JulOS.Domain;
using JulOS.Domain.Observability;
using JulOS.Domain.Packages;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Observability;

/// <summary>Verifies problem deduplication and the operator lifecycle.</summary>
[TestClass]
public sealed class ProblemTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RepeatedObservationsUpdateOneProblem()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var problem = NewProblem(timeProvider);

        for (var observation = 0; observation < 99; observation++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(30));
            problem.Observe(problem.Identity, ProblemSeverity.Error, timeProvider);
        }

        Assert.AreEqual(100, problem.ObservationCount, "A restart loop is one problem, not a hundred entries to dismiss.");
        Assert.AreEqual(Start, problem.FirstDetectedAtUtc);
        Assert.AreEqual(Start.AddSeconds(99 * 30), problem.LastObservedAtUtc);
    }

    [TestMethod]
    public void AResolvedProblemReopensOnANewObservation()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var problem = NewProblem(timeProvider);

        problem.Resolve(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        problem.Observe(problem.Identity, ProblemSeverity.Error, timeProvider);

        Assert.AreEqual(ProblemState.Active, problem.State, "The condition is back; hiding it would mislead the operator.");
        Assert.IsNull(problem.ResolvedAtUtc);
        Assert.IsTrue(problem.IsOpen);
    }

    [TestMethod]
    public void AnAcknowledgedProblemStaysAcknowledgedWhenObservedAgain()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var problem = NewProblem(timeProvider);

        problem.Acknowledge(timeProvider);
        problem.Observe(problem.Identity, ProblemSeverity.Error, timeProvider);

        Assert.AreEqual(
            ProblemState.Acknowledged,
            problem.State,
            "Re-highlighting on every poll is what acknowledging is meant to stop.");
    }

    [TestMethod]
    public void ASuppressedProblemStaysSuppressedWhenObservedAgain()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var problem = NewProblem(timeProvider);

        problem.Suppress();
        problem.Observe(problem.Identity, ProblemSeverity.Critical, timeProvider);

        Assert.AreEqual(ProblemState.Suppressed, problem.State);
        Assert.IsFalse(problem.IsOpen);
    }

    [TestMethod]
    public void AnObservationOfADifferentResourceIsRejected()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var problem = NewProblem(timeProvider);
        var other = new ProblemIdentity(
            PackageId.Parse("de.juloc.julos.example"),
            "resource.unreachable",
            "resource-2");

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => problem.Observe(other, ProblemSeverity.Error, timeProvider));

        Assert.AreEqual("problem.observation.identity_mismatch", exception.Code);
    }

    [TestMethod]
    public void SeverityCanRiseWithANewObservation()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var problem = NewProblem(timeProvider);

        problem.Observe(problem.Identity, ProblemSeverity.Critical, timeProvider);

        Assert.AreEqual(ProblemSeverity.Critical, problem.Severity);
    }

    [TestMethod]
    public void OnlyAnActiveProblemCanBeAcknowledged()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var problem = NewProblem(timeProvider);

        problem.Resolve(timeProvider);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => problem.Acknowledge(timeProvider));

        Assert.AreEqual("problem.transition.invalid", exception.Code);
    }

    [TestMethod]
    public void ResolvingTwiceIsRefused()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var problem = NewProblem(timeProvider);

        problem.Resolve(timeProvider);

        Assert.ThrowsExactly<DomainRuleViolationException>(() => problem.Resolve(timeProvider));
    }

    [TestMethod]
    public void TheProblemCarriesNoUserFacingText()
    {
        var problem = NewProblem(new FakeTimeProvider(Start));

        Assert.AreEqual("problem.resource.unreachable.title", problem.TitleKey);
        Assert.IsFalse(
            typeof(Problem).GetProperties().Any(property => property.Name is "Title" or "Description"),
            "Holding the text would fix one language into the record.");
    }

    [TestMethod]
    public void TwoObservationsOfTheSameConditionShareOneIdentity()
    {
        var first = new ProblemIdentity(
            PackageId.Parse("de.juloc.julos.example"),
            "resource.unreachable",
            "resource-1");
        var second = new ProblemIdentity(
            PackageId.Parse("de.juloc.julos.example"),
            "resource.unreachable",
            "resource-1");

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void AnIdentityPartCannotBeBlank()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => new ProblemIdentity(PackageId.Parse("de.juloc.julos.example"), "  ", "resource-1"));

        Assert.AreEqual("problem.identity.invalid", exception.Code);
    }

    private static Problem NewProblem(TimeProvider timeProvider) => Problem.Detect(
        new ProblemId(Guid.CreateVersion7()),
        new ProblemIdentity(PackageId.Parse("de.juloc.julos.example"), "resource.unreachable", "resource-1"),
        ProblemSeverity.Error,
        "problem.resource.unreachable.title",
        timeProvider);
}
