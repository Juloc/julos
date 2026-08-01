using JulOS.Domain;
using JulOS.Domain.Applications;
using JulOS.Domain.Layouts;
using JulOS.Domain.Primitives;

namespace JulOS.Domain.Tests.Layouts;

/// <summary>Verifies the stored desktop, its stacking order and its widget grid.</summary>
[TestClass]
public sealed class DesktopLayoutTests
{
    private static readonly int[] TwoInOrder = [0, 1];

    private static readonly int[] ThreeInOrder = [0, 1, 2];

    private static readonly int[] FiveInOrder = [0, 1, 2, 3, 4];

    [TestMethod]
    public void MobileAndDesktopLayoutsAreSeparateRecords()
    {
        var desktop = DesktopLayout.Create(NewLayoutId(), ViewportClass.Desktop);
        var mobile = DesktopLayout.Create(NewLayoutId(), ViewportClass.Mobile);

        desktop.AddWindow(NewWindow());

        Assert.AreEqual(1, desktop.Windows.Count);
        Assert.AreEqual(0, mobile.Windows.Count, "Arranging a wide screen must not overwrite the phone layout.");
        Assert.AreNotEqual(desktop.ViewportClass, mobile.ViewportClass);
    }

    [TestMethod]
    public void ZOrderIsAGapFreeSequenceWithoutDuplicates()
    {
        var layout = DesktopLayout.Create(NewLayoutId(), ViewportClass.Desktop);

        for (var index = 0; index < 5; index++)
        {
            layout.AddWindow(NewWindow());
        }

        var indices = layout.Windows.Select(window => window.ZIndex).ToArray();

        CollectionAssert.AreEqual(FiveInOrder, indices);
        Assert.AreEqual(indices.Length, indices.Distinct().Count(), "A duplicate z-index makes a click land on an arbitrary window.");
    }

    [TestMethod]
    public void FocusingRaisesAWindowToTheFrontAndRenumbersTheRest()
    {
        var layout = DesktopLayout.Create(NewLayoutId(), ViewportClass.Desktop);
        var first = NewWindow();
        var second = NewWindow();
        var third = NewWindow();

        layout.AddWindow(first);
        layout.AddWindow(second);
        layout.AddWindow(third);

        layout.Focus(first.Id);

        Assert.AreEqual(first.Id, layout.FrontWindow!.Id);
        CollectionAssert.AreEqual(ThreeInOrder, layout.Windows.Select(window => window.ZIndex).ToArray());
        Assert.AreEqual(2, first.ZIndex);
    }

    [TestMethod]
    public void ClosingAWindowLeavesNoGapInTheStack()
    {
        var layout = DesktopLayout.Create(NewLayoutId(), ViewportClass.Desktop);
        var first = NewWindow();
        var second = NewWindow();
        var third = NewWindow();

        layout.AddWindow(first);
        layout.AddWindow(second);
        layout.AddWindow(third);

        layout.RemoveWindow(second.Id);

        CollectionAssert.AreEqual(TwoInOrder, layout.Windows.Select(window => window.ZIndex).ToArray());
    }

    [TestMethod]
    public void TheSameWindowCannotBeAddedTwice()
    {
        var layout = DesktopLayout.Create(NewLayoutId(), ViewportClass.Desktop);
        var window = NewWindow();

        layout.AddWindow(window);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => layout.AddWindow(window));

        Assert.AreEqual("layout.window.already_open", exception.Code);
    }

    [TestMethod]
    public void FocusingAWindowThatIsNotOpenFails()
    {
        var layout = DesktopLayout.Create(NewLayoutId(), ViewportClass.Desktop);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => layout.Focus(new WindowId(Guid.CreateVersion7())));

        Assert.AreEqual("layout.window.not_open", exception.Code);
    }

    [TestMethod]
    public void EveryChangeMovesTheRevision()
    {
        var layout = DesktopLayout.Create(NewLayoutId(), ViewportClass.Desktop);
        var before = layout.Revision;

        layout.AddWindow(NewWindow());

        Assert.IsTrue(layout.Revision > before);
    }

    [TestMethod]
    public void OverlappingWidgetsAreRejected()
    {
        var layout = DesktopLayout.Create(NewLayoutId(), ViewportClass.Desktop);

        layout.AddWidget(WidgetPlacement.Place(NewWidgetId(), "host.cpu", 0, 0, 2, 2));

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => layout.AddWidget(WidgetPlacement.Place(NewWidgetId(), "host.memory", 1, 1, 2, 2)));

        Assert.AreEqual("layout.widget.overlaps", exception.Code);
    }

    [TestMethod]
    public void AdjacentWidgetsAreAccepted()
    {
        var layout = DesktopLayout.Create(NewLayoutId(), ViewportClass.Desktop);

        layout.AddWidget(WidgetPlacement.Place(NewWidgetId(), "host.cpu", 0, 0, 2, 2));
        layout.AddWidget(WidgetPlacement.Place(NewWidgetId(), "host.memory", 2, 0, 2, 2));

        Assert.AreEqual(2, layout.Widgets.Count);
    }

    [TestMethod]
    public void AWidgetOutsideTheGridIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => WidgetPlacement.Place(NewWidgetId(), "host.cpu", -1, 0, 2, 2));

        Assert.AreEqual("layout.widget.outside_grid", exception.Code);
    }

    [TestMethod]
    public void AWidgetWithNoAreaIsRejected()
    {
        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(
            () => WidgetPlacement.Place(NewWidgetId(), "host.cpu", 0, 0, 0, 2));

        Assert.AreEqual("layout.widget.size_not_positive", exception.Code);
    }

    private static DesktopLayoutId NewLayoutId() => new(Guid.CreateVersion7());

    private static WidgetPlacementId NewWidgetId() => new(Guid.CreateVersion7());

    private static DesktopWindow NewWindow() => DesktopWindow.Open(
        new WindowId(Guid.CreateVersion7()),
        new ApplicationDefinitionId(Guid.CreateVersion7()),
        launchTargetId: null,
        WindowBounds.Create(100, 100, 800, 600),
        zIndex: 0);
}
