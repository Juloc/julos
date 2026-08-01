using JulOS.Domain;
using JulOS.Domain.Applications;
using JulOS.Domain.Layouts;

namespace JulOS.Domain.Tests.Layouts;

/// <summary>Verifies the presentation state of one window.</summary>
[TestClass]
public sealed class DesktopWindowTests
{
    private static readonly UsableArea Area = UsableArea.Create(1920, 1040);

    [TestMethod]
    public void AWindowOpensInTheNormalState()
    {
        var window = NewWindow();

        Assert.AreEqual(WindowState.Normal, window.State);
        Assert.AreEqual(window.Bounds, window.RestoreBounds);
    }

    [TestMethod]
    public void MaximizingRemembersWhereToReturnTo()
    {
        var window = NewWindow();
        var original = window.Bounds;

        window.ApplyFixedState(WindowState.Maximized, Area);
        window.Restore();

        Assert.AreEqual(original, window.Bounds);
        Assert.AreEqual(WindowState.Normal, window.State);
    }

    [TestMethod]
    public void SnappingTwiceStillReturnsToTheOriginalBounds()
    {
        var window = NewWindow();
        var original = window.Bounds;

        window.ApplyFixedState(WindowState.SnappedLeft, Area);
        window.ApplyFixedState(WindowState.Maximized, Area);
        window.Restore();

        Assert.AreEqual(
            original,
            window.Bounds,
            "Going from one fixed state to another must not overwrite the remembered bounds with the snapped ones.");
    }

    [TestMethod]
    public void AFixedWindowRefusesToBeDragged()
    {
        var window = NewWindow();

        window.ApplyFixedState(WindowState.SnappedRight, Area);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => window.MoveTo(WindowBounds.Create(0, 0, 400, 300)));

        Assert.AreEqual("layout.window.bounds_not_owned", exception.Code);
    }

    [TestMethod]
    public void AStateWithoutFixedGeometryCannotBeApplied()
    {
        var window = NewWindow();

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => window.ApplyFixedState(WindowState.Normal, Area));

        Assert.AreEqual("layout.window.state_has_no_geometry", exception.Code);
    }

    [TestMethod]
    public void AMinimizedMaximizedWindowComesBackMaximized()
    {
        var window = NewWindow();

        window.ApplyFixedState(WindowState.Maximized, Area);
        window.Minimize();
        window.Unminimize(Area);

        Assert.AreEqual(
            WindowState.Maximized,
            window.State,
            "Returning it to the normal state would silently discard what the user set up.");
        Assert.AreEqual(WindowBounds.Create(0, 0, 1920, 1040), window.Bounds);
    }

    [TestMethod]
    public void AMinimizedNormalWindowComesBackWhereItWas()
    {
        var window = NewWindow();
        var original = window.Bounds;

        window.Minimize();
        window.Unminimize(Area);

        Assert.AreEqual(WindowState.Normal, window.State);
        Assert.AreEqual(original, window.Bounds);
    }

    [TestMethod]
    public void MinimizingTwiceDoesNotLoseThePreviousState()
    {
        var window = NewWindow();

        window.ApplyFixedState(WindowState.SnappedLeft, Area);
        window.Minimize();
        window.Minimize();
        window.Unminimize(Area);

        Assert.AreEqual(WindowState.SnappedLeft, window.State);
    }

    [TestMethod]
    public void OnlyAMinimizedWindowCanBeShownAgain()
    {
        var window = NewWindow();

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => window.Unminimize(Area));

        Assert.AreEqual("layout.window.not_minimized", exception.Code);
    }

    [TestMethod]
    public void DraggingUpdatesBothTheCurrentAndTheRestoreBounds()
    {
        var window = NewWindow();
        var moved = WindowBounds.Create(300, 200, 640, 480);

        window.MoveTo(moved);
        window.ApplyFixedState(WindowState.Maximized, Area);
        window.Restore();

        Assert.AreEqual(moved, window.Bounds);
    }

    private static DesktopWindow NewWindow() => DesktopWindow.Open(
        new WindowId(Guid.CreateVersion7()),
        new ApplicationDefinitionId(Guid.CreateVersion7()),
        launchTargetId: null,
        WindowBounds.Create(100, 100, 800, 600),
        zIndex: 0);
}
