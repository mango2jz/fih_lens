using FihLens.Application.Capture;
using FihLens.WinUI.Presentation;

namespace FihLens.Tests.WinUI;

public sealed class SelectionGeometryTests
{
    [Theory]
    [InlineData(-90, -40, -20, 30)]
    [InlineData(-20, -40, -90, 30)]
    [InlineData(-20, 30, -90, -40)]
    [InlineData(-90, 30, -20, -40)]
    public void DragNormalizesAllDirectionsWithoutChangingPixels(int x1, int y1, int x2, int y2) =>
        Assert.Equal(new PhysicalPixelRect(-90, -40, 70, 70), SelectionGeometry.Drag(new(-100, -50, 200, 150), x1, y1, x2, y2));

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void EndpointConversionUsesFrozenOriginOnce(double scale)
    {
        Assert.Equal(-1920 + (int)Math.Round(40 * scale, MidpointRounding.ToEven),
            SelectionGeometry.ToDesktopPixel(40, scale, -1920));
        Assert.Equal(-1920, SelectionGeometry.ToDesktopPixel(0, scale, -1920));
    }

    [Fact]
    public void RejectsInvalidZeroOutsideAndOverflowGeometry()
    {
        var bounds = new PhysicalPixelRect(0, 0, 100, 100);
        Assert.Null(SelectionGeometry.Drag(bounds, 0, 0, 0, 10));
        Assert.Null(SelectionGeometry.Drag(bounds, 0, 0, 101, 10));
        Assert.Null(SelectionGeometry.Adjust(bounds, new(0, 0, 1, 1), int.MaxValue, 0, false));
        Assert.Null(SelectionGeometry.Adjust(bounds, new(0, 0, 1, 1), -1, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => SelectionGeometry.ToDesktopPixel(double.NaN, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SelectionGeometry.ToDesktopPixel(1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SelectionGeometry.ToDesktopPixel(int.MaxValue, 2, 0));
        Assert.Equal(bounds, SelectionGeometry.Drag(bounds, 0, 0, 100, 100));
    }
}
