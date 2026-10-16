using FihLens.Application.Capture;
using FihLens.Core.Enums;
using FihLens.WinUI.ViewModels;

namespace FihLens.Tests.WinUI;

public sealed class SelectionOverlayViewModelTests
{
    private static SelectionOverlayViewModel Create() => new(new("display", new(-100, 0, 100, 100), "stamp"));

    [Fact]
    public void ConfirmationCarriesSelectedOcrLanguage()
    {
        var vm = new SelectionOverlayViewModel(new("display", new(0, 0, 100, 100), "stamp"), Language.Japanese);
        vm.CreateKeyboardSelection();
        Assert.Equal(Language.Japanese, vm.Confirm()!.SourceLanguage);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SelectionOverlayViewModel(new("display", new(0, 0, 100, 100), "stamp"), Language.Auto));
    }

    [Fact]
    public void DepartureRemainsRejectedAfterPointerReentersUntilNewGesture()
    {
        var vm = Create();
        vm.BeginDrag(-90, 10);
        vm.MoveDrag(5, 50);
        vm.EndDrag(-50, 50);
        Assert.False(vm.CanConfirm);
        Assert.Null(vm.Confirm());
        Assert.Contains("Cross-display", vm.Status);
        vm.BeginDrag(-90, 10);
        vm.EndDrag(-50, 50);
        Assert.True(vm.CanConfirm);
        Assert.Equal("40 × 40 px", vm.Dimensions);
        Assert.Equal(new PhysicalPixelRect(-90, 10, 40, 40), vm.Confirm()!.Crop);
        Assert.Null(vm.Confirm());
    }

    [Fact]
    public void KeyboardCreatesMovesResizesAndRejectsInvalidAdjustmentWithoutClipping()
    {
        var vm = Create();
        vm.CreateKeyboardSelection();
        vm.AdjustKeyboard(10, 20, false);
        vm.AdjustKeyboard(29, 39, true);
        Assert.Equal(new PhysicalPixelRect(-90, 20, 30, 40), vm.Selection);
        vm.AdjustKeyboard(100, 0, false);
        Assert.Equal(new PhysicalPixelRect(-90, 20, 30, 40), vm.Selection);
        Assert.Contains("rejected", vm.Status);
        var frozen = vm.Confirm();
        vm.AdjustKeyboard(1, 1, false);
        Assert.Equal(frozen!.Crop, vm.Selection);
        Assert.Same(vm.Source, frozen.Source);
    }

    [Fact]
    public void ZeroSizeActiveDragAndCanceledSelectionCannotConfirm()
    {
        var vm = Create();
        vm.BeginDrag(-90, 10);
        vm.MoveDrag(-80, 20);
        Assert.Null(vm.Confirm());
        vm.EndDrag(-90, 10);
        Assert.Null(vm.Confirm());
        vm.CreateKeyboardSelection();
        vm.Cancel();
        vm.CreateKeyboardSelection();
        Assert.Null(vm.Selection);
        Assert.Null(vm.Confirm());
    }
}
