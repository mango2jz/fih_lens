using FihLens.WinUI.Windowing;

namespace FihLens.Tests.WinUI;

public sealed class PopupPlacementTests
{
    [Theory]
    [InlineData(1, 400, 325)]
    [InlineData(1.25, 500, 406)]
    [InlineData(1.5, 600, 488)]
    [InlineData(2, 800, 650)]
    public void ConvertsClientSizeUsingNativeRounding(double scale, int width, int height)
    {
        var result = PopupPlacement.Calculate(new PixelRect(0, 0, 2000, 1500), 100, 200, 400, 325, scale, 6, 6);

        Assert.Equal(width, result.ClientWidth);
        Assert.Equal(height, result.ClientHeight);
        Assert.Equal(new PixelRect(100, 200, width + 6, height + 6), result.OuterBounds);
    }

    [Theory]
    // Left, above and below primary; work-area offsets also model taskbars on each side.
    [InlineData(-1880, 0, 1880, 1080, -50, 1079, -406, 749)]
    [InlineData(0, -1160, 1920, 1160, 1919, -1, 1514, -331)]
    [InlineData(0, 1120, 1920, 1040, -1, 1000, 0, 1120)]
    [InlineData(0, 40, 1920, 1040, -20, -20, 0, 40)]
    [InlineData(0, 0, 1880, 1080, 1900, 1100, 1474, 749)]
    public void ClampsOuterBoundsInsideDesktopWorkArea(
        int x, int y, int width, int height, int preferredX, int preferredY, int expectedX, int expectedY)
    {
        var work = new PixelRect(x, y, width, height);
        var result = PopupPlacement.Calculate(work, preferredX, preferredY, 400, 325, 1, 6, 6);

        Assert.Equal(new PixelRect(expectedX, expectedY, 406, 331), result.OuterBounds);
        AssertContained(work, result.OuterBounds);
    }

    [Fact]
    public void ExpandedAt200PercentMatchesMeasured565DipClientAndDoesNotAddDisplayOrigin()
    {
        var work = new PixelRect(2048, 0, 1920, 1136);
        var result = PopupPlacement.Calculate(work, 2605, 240, 400, 569, 2, 6, 6);

        Assert.Equal(800, result.ClientWidth);
        Assert.Equal(1130, result.ClientHeight);
        Assert.Equal(565, result.ClientHeight / 2d);
        Assert.Equal(new PixelRect(2605, 0, 806, 1136), result.OuterBounds);
        AssertContained(work, result.OuterBounds);
    }

    [Fact]
    public void UndersizedWorkAreaClampsBothClientDimensionsUsingMeasuredOverhead()
    {
        var work = new PixelRect(-300, -200, 200, 100);
        var result = PopupPlacement.Calculate(work, 0, 0, 400, 569, 1.5, 12, 8);

        Assert.Equal(188, result.ClientWidth);
        Assert.Equal(92, result.ClientHeight);
        Assert.Equal(work, result.OuterBounds);
    }

    [Fact]
    public void ExtremeValidDesktopCoordinatesDoNotOverflow()
    {
        var work = new PixelRect(int.MinValue, int.MaxValue - 1000, 1000, 1000);
        var result = PopupPlacement.Calculate(work, int.MaxValue, int.MinValue, 400, 325, 1, 6, 6);

        Assert.Equal(int.MinValue + 594, result.OuterBounds.X);
        Assert.Equal(int.MaxValue - 1000, result.OuterBounds.Y);
        AssertContained(work, result.OuterBounds);
    }

    [Fact]
    public void ExactMidpointsRoundToEvenInBothDirections()
    {
        var result = PopupPlacement.Calculate(new PixelRect(0, 0, 100, 100), 0, 0, 10.5, 11.5, 1, 0, 0);

        Assert.Equal(new PixelRect(0, 0, 10, 12), result.OuterBounds);
    }

    [Theory]
    [InlineData(0, 325, 1)]
    [InlineData(-1, 325, 1)]
    [InlineData(double.NaN, 325, 1)]
    [InlineData(400, double.PositiveInfinity, 1)]
    [InlineData(400, 325, 0)]
    [InlineData(400, 325, -1)]
    [InlineData(400, 325, double.NaN)]
    [InlineData(400, 325, double.PositiveInfinity)]
    [InlineData(double.MaxValue, 325, 2)]
    [InlineData(2147483648d, 325, 1)]
    [InlineData(0.1, 325, 1)]
    public void RejectsInvalidOrUnrepresentableClientSize(double width, double height, double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PopupPlacement.Calculate(new PixelRect(0, 0, 2000, 1500), 0, 0, width, height, scale, 6, 6));
    }

    [Theory]
    [InlineData(0, 0, 0, 100, 0, 0)]
    [InlineData(0, 0, 100, -1, 0, 0)]
    [InlineData(int.MaxValue, 0, 1, 100, 0, 0)]
    [InlineData(0, int.MaxValue, 100, 1, 0, 0)]
    [InlineData(0, 0, 100, 100, -1, 0)]
    [InlineData(0, 0, 100, 100, 0, -1)]
    [InlineData(0, 0, 6, 100, 6, 6)]
    [InlineData(0, 0, 100, 6, 6, 6)]
    public void RejectsInvalidWorkAreaOrUnusableMeasuredOverhead(int x, int y, int width, int height, int extraWidth, int extraHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PopupPlacement.Calculate(new PixelRect(x, y, width, height), 0, 0, 400, 325, 1, extraWidth, extraHeight));
    }

    private static void AssertContained(PixelRect work, PixelRect bounds)
    {
        Assert.True(bounds.X >= work.X && bounds.Y >= work.Y);
        Assert.True((long)bounds.X + bounds.Width <= (long)work.X + work.Width);
        Assert.True((long)bounds.Y + bounds.Height <= (long)work.Y + work.Height);
    }
}
