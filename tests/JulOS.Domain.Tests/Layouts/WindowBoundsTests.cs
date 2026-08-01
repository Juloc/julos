using JulOS.Domain;
using JulOS.Domain.Layouts;

namespace JulOS.Domain.Tests.Layouts;

/// <summary>Verifies window geometry validation and clamping.</summary>
[TestClass]
public sealed class WindowBoundsTests
{
    [TestMethod]
    public void ValidBoundsAreKept()
    {
        var bounds = WindowBounds.Create(10, 20, 800, 600);

        Assert.AreEqual(10, bounds.X);
        Assert.AreEqual(20, bounds.Y);
        Assert.AreEqual(810, bounds.Right);
        Assert.AreEqual(620, bounds.Bottom);
    }

    [TestMethod]
    public void ANegativeOriginIsAllowedBecauseADraggedWindowMayOverhang()
    {
        var bounds = WindowBounds.Create(-40, -10, 800, 600);

        Assert.AreEqual(-40, bounds.X);
    }

    [TestMethod]
    public void AWindowWithNoAreaIsRejected()
    {
        foreach (var (width, height) in new[] { (0, 600), (800, 0), (-1, 600) })
        {
            var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
                () => WindowBounds.Create(0, 0, width, height));

            Assert.AreEqual("layout.bounds.not_positive", exception.Code);
        }
    }

    [TestMethod]
    public void AnAbsurdlyLargeWindowIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => WindowBounds.Create(0, 0, 100000, 600));

        Assert.AreEqual("layout.bounds.too_large", exception.Code);
    }

    [TestMethod]
    public void AnOriginFarOutsideTheDesktopIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => WindowBounds.Create(1000000, 0, 800, 600));

        Assert.AreEqual("layout.bounds.out_of_range", exception.Code);
    }

    [TestMethod]
    public void ATitleBarDraggedBelowTheDesktopIsClampedBack()
    {
        var usableArea = UsableArea.Create(1920, 1040);
        var offScreen = WindowBounds.Create(100, 5000, 800, 600);

        var clamped = offScreen.ClampToReachable(usableArea, titleBarHeight: 32);

        Assert.IsTrue(
            clamped.Y <= usableArea.Height - 32,
            "A title bar below the desktop can never be grabbed again.");
    }

    [TestMethod]
    public void ATitleBarDraggedPastTheRightEdgeIsClampedBack()
    {
        var usableArea = UsableArea.Create(1920, 1040);
        var offScreen = WindowBounds.Create(9000, 100, 800, 600);

        var clamped = offScreen.ClampToReachable(usableArea, titleBarHeight: 32);

        Assert.IsTrue(clamped.X < usableArea.Width, "Part of the window must stay reachable.");
    }

    [TestMethod]
    public void ClampingKeepsTheSizeTheUserChose()
    {
        var usableArea = UsableArea.Create(1920, 1040);
        var offScreen = WindowBounds.Create(9000, 5000, 800, 600);

        var clamped = offScreen.ClampToReachable(usableArea, titleBarHeight: 32);

        Assert.AreEqual(800, clamped.Width);
        Assert.AreEqual(600, clamped.Height);
    }

    [TestMethod]
    public void AReachableWindowIsLeftAlone()
    {
        var usableArea = UsableArea.Create(1920, 1040);
        var bounds = WindowBounds.Create(100, 100, 800, 600);

        Assert.AreEqual(bounds, bounds.ClampToReachable(usableArea, titleBarHeight: 32));
    }

    [TestMethod]
    public void AWindowIsGrownToTheApplicationMinimum()
    {
        var bounds = WindowBounds.Create(0, 0, 200, 150).AtLeast(400, 300);

        Assert.AreEqual(400, bounds.Width);
        Assert.AreEqual(300, bounds.Height);
    }
}
