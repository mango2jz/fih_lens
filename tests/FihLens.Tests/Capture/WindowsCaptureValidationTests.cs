using FihLens.Application.Capture;
using FihLens.Windows.Capture;

namespace FihLens.Tests.Capture;

public sealed class WindowsCaptureValidationTests
{
    [Fact]
    public void Topology_IsOrdinalOrderIndependent_AndIncludesEveryFullBounds()
    {
        (string Id, PhysicalPixelRect Bounds)[] displays = [("DISPLAY2", new(-1920, -100, 1920, 1080)), ("DISPLAY1", new(0, 0, 2560, 1440))];
        var stamp = CaptureValidation.TopologyStamp(displays);
        Assert.Equal(stamp, CaptureValidation.TopologyStamp(displays.Reverse()));
        Assert.NotEqual(stamp, CaptureValidation.TopologyStamp(displays.Take(1)));
        Assert.NotEqual(stamp, CaptureValidation.TopologyStamp([displays[0], (displays[1].Id, new(0, 0, 2560, 1439))]));
        Assert.NotEqual(stamp, CaptureValidation.TopologyStamp([("display2", displays[0].Bounds), displays[1]]));
    }

    [Fact]
    public void Source_RejectsIdentityBoundsOrTopologyChange()
    {
        var bounds = new PhysicalPixelRect(-1920, 0, 1920, 1080);
        var frozen = new CaptureSourceSnapshot("DISPLAY1", bounds, "topology");
        Assert.True(CaptureValidation.SourceMatches(frozen, new("DISPLAY1", bounds, "topology")));
        Assert.False(CaptureValidation.SourceMatches(frozen, new("DISPLAY2", bounds, "topology")));
        Assert.False(CaptureValidation.SourceMatches(frozen, new("DISPLAY1", new(-1919, 0, 1920, 1080), "topology")));
        Assert.False(CaptureValidation.SourceMatches(frozen, new("DISPLAY1", bounds, "changed")));
    }

    [Theory]
    [InlineData(-1920, -100, 1, 1, null)]
    [InlineData(-1, 979, 1, 1, null)]
    [InlineData(-1920, -100, 1920, 1080, null)]
    [InlineData(-1921, -100, 1, 1, RegionTextCaptureOutcome.InvalidRegion)]
    [InlineData(-1, 979, 2, 1, RegionTextCaptureOutcome.InvalidRegion)]
    [InlineData(-1920, -100, 0, 1, RegionTextCaptureOutcome.InvalidRegion)]
    public void Crop_UsesExclusivePhysicalBounds(int x, int y, int width, int height, RegionTextCaptureOutcome? expected) =>
        Assert.Equal(expected, CaptureValidation.ValidateCrop(new(-1920, -100, 1920, 1080), new(x, y, width, height), 4096));

    [Fact]
    public void Crop_RejectsOcrDimensionAndMemoryLimitBeforeAllocation()
    {
        Assert.Equal(RegionTextCaptureOutcome.TooLarge, CaptureValidation.ValidateCrop(new(0, 0, 5000, 1), new(0, 0, 4097, 1), 4096));
        Assert.Equal(RegionTextCaptureOutcome.TooLarge, CaptureValidation.ValidateCrop(new(0, 0, 10000, 10000), new(0, 0, 10000, 10000), 20000));
        Assert.Null(CaptureValidation.ValidateCrop(new(0, 0, 4096, 4096), new(0, 0, 4096, 4096), 4096));
    }

    [Theory]
    [InlineData(1920, 1080, true)]
    [InlineData(1919, 1080, false)]
    [InlineData(1920, 1079, false)]
    [InlineData(1921, 1080, false)]
    public void Frame_MustMatchBothFrozenDimensions(int width, int height, bool expected) =>
        Assert.Equal(expected, CaptureValidation.FrameMatches(new(-1920, 0, 1920, 1080), width, height));

    [Theory]
    [InlineData(null, 10L, 10000000L, false)]
    [InlineData(0L, 10L, 10000000L, false)]
    [InlineData(10L, 10L, 10000000L, false)]
    [InlineData(11L, 10L, 10000000L, false)]
    [InlineData(12L, 10L, 10000000L, false)]
    [InlineData(13L, 10L, 10000000L, true)]
    [InlineData(13L, -1L, 10000000L, false)]
    [InlineData(13L, 10L, 0L, false)]
    [InlineData(10000003L, 1000000000L, 1000000000L, true)]
    public void Freshness_RequiresStrictlyLaterTimestampBeyondQuantumMargin(long? frame, long marker, long frequency, bool expected) =>
        Assert.Equal(expected, FrameFreshness.IsAfter(frame, marker, frequency));

    [Fact]
    public void Freshness_DoesNotOverflowLargeCounters() =>
        Assert.False(FrameFreshness.IsAfter(long.MaxValue, long.MaxValue, 10000000));
}

