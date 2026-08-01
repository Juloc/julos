using JulOS.Domain.Layouts;

namespace JulOS.Domain.Tests.Layouts;

/// <summary>Verifies the snap geometry the preview and the stored bounds both use.</summary>
[TestClass]
public sealed class SnapGeometryTests
{
    private static readonly UsableArea Area = UsableArea.Create(1920, 1040);

    [TestMethod]
    public void MaximizedFillsTheUsableArea()
    {
        var bounds = SnapGeometry.BoundsFor(WindowState.Maximized, Area);

        Assert.AreEqual(WindowBounds.Create(0, 0, 1920, 1040), bounds);
    }

    [TestMethod]
    public void TheTwoHalvesTogetherCoverTheFullWidth()
    {
        var left = SnapGeometry.BoundsFor(WindowState.SnappedLeft, Area)!.Value;
        var right = SnapGeometry.BoundsFor(WindowState.SnappedRight, Area)!.Value;

        Assert.AreEqual(0, left.X);
        Assert.AreEqual(left.Right, right.X, "A gap between the halves would show the desktop through the seam.");
        Assert.AreEqual(Area.Width, right.Right);
    }

    [TestMethod]
    public void AnOddWidthLeavesNoGap()
    {
        var odd = UsableArea.Create(1921, 1041);

        var left = SnapGeometry.BoundsFor(WindowState.SnappedLeft, odd)!.Value;
        var right = SnapGeometry.BoundsFor(WindowState.SnappedRight, odd)!.Value;

        Assert.AreEqual(left.Right, right.X);
        Assert.AreEqual(odd.Width, left.Width + right.Width);
    }

    [TestMethod]
    public void TheFourQuartersTileTheUsableAreaExactly()
    {
        var quarters = new[]
        {
            SnapGeometry.BoundsFor(WindowState.SnappedTopLeft, Area)!.Value,
            SnapGeometry.BoundsFor(WindowState.SnappedTopRight, Area)!.Value,
            SnapGeometry.BoundsFor(WindowState.SnappedBottomLeft, Area)!.Value,
            SnapGeometry.BoundsFor(WindowState.SnappedBottomRight, Area)!.Value,
        };

        var covered = quarters.Sum(quarter => (long)quarter.Width * quarter.Height);

        Assert.AreEqual((long)Area.Width * Area.Height, covered);
    }

    [TestMethod]
    public void SnappingNeverReachesBelowTheUsableArea()
    {
        foreach (var state in Enum.GetValues<WindowState>())
        {
            var bounds = SnapGeometry.BoundsFor(state, Area);

            if (bounds is not null)
            {
                Assert.IsTrue(
                    bounds.Value.Bottom <= Area.Height,
                    $"State '{state}' would place a window under the taskbar.");
            }
        }
    }

    [TestMethod]
    public void AFreeStateHasNoFixedGeometry()
    {
        Assert.IsNull(SnapGeometry.BoundsFor(WindowState.Normal, Area));
        Assert.IsNull(SnapGeometry.BoundsFor(WindowState.Minimized, Area));
    }

    [TestMethod]
    public void OnlySnappedStatesReportAsSnapped()
    {
        Assert.IsTrue(SnapGeometry.IsSnapped(WindowState.SnappedBottomRight));
        Assert.IsFalse(SnapGeometry.IsSnapped(WindowState.Maximized));
        Assert.IsTrue(SnapGeometry.OverridesBounds(WindowState.Maximized));
        Assert.IsFalse(SnapGeometry.OverridesBounds(WindowState.Normal));
    }
}
