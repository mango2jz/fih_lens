using FihLens.Application.Capture;
using FihLens.Core.Enums;

namespace FihLens.Tests.Capture;

public sealed class RegionTextCaptureContractTests
{
    [Fact]
    public void RequestPreservesNegativeDesktopOriginAndExactPixels()
    {
        var source = new CaptureSourceSnapshot("display-2", new(-1920, -200, 1920, 1200), "sorted-catalog-stamp");
        var crop = new PhysicalPixelRect(-1920, -200, 1920, 1200);
        var request = new RegionTextCaptureRequest(source, crop);
        Assert.Same(source, request.Source);
        Assert.Equal(crop, request.Crop);
        Assert.Equal(Language.English, request.SourceLanguage);
    }

    [Fact]
    public void RequestCarriesSelectedSourceAndRejectsAutomaticOrUndefinedLanguage()
    {
        var source = new CaptureSourceSnapshot("display", new(0, 0, 100, 100), "stamp");
        var crop = new PhysicalPixelRect(1, 1, 20, 20);
        Assert.Equal(Language.Japanese, new RegionTextCaptureRequest(source, crop, Language.Japanese).SourceLanguage);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionTextCaptureRequest(source, crop, Language.Auto));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionTextCaptureRequest(source, crop, (Language)99));
    }

    [Theory]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, -1)]
    [InlineData(-1, 0, 20, 20)]
    [InlineData(90, 0, 20, 20)]
    [InlineData(0, 90, 20, 20)]
    [InlineData(int.MaxValue, 0, 1, 1)]
    public void InvalidCropIsRejectedWithoutClipping(int x, int y, int width, int height)
    {
        var source = new CaptureSourceSnapshot("display", new(0, 0, 100, 100), "stamp");
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionTextCaptureRequest(source, new(x, y, width, height)));
    }

    [Fact]
    public void InvalidSourceAndDefaultBoundsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new CaptureSourceSnapshot(" ", new(0, 0, 1, 1), "stamp"));
        Assert.Throws<ArgumentException>(() => new CaptureSourceSnapshot("display", new(0, 0, 1, 1), " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureSourceSnapshot("display", default, "stamp"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureSourceSnapshot("display", new(0, int.MaxValue, 1, 1), "stamp"));
    }

    [Fact]
    public void ResultCannotMislabelEmptyTextOrAttachTextToFailure()
    {
        Assert.Throws<ArgumentException>(() => RegionTextCaptureResult.Success(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.Success));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegionTextCaptureResult.Failure((RegionTextCaptureOutcome)999));
        foreach (var outcome in Enum.GetValues<RegionTextCaptureOutcome>().Where(x => x != RegionTextCaptureOutcome.Success))
            Assert.Null(RegionTextCaptureResult.Failure(outcome).Text);
        Assert.Equal(" original text ", RegionTextCaptureResult.Success(" original text ").Text);
    }
}
