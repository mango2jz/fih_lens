using FihLens.Application.Capture;

namespace FihLens.Tests.Application;

public sealed class SelectedTextCaptureTests
{
    [Theory]
    [InlineData(SelectedTextCaptureOutcome.NoText, true)]
    [InlineData(SelectedTextCaptureOutcome.Unavailable, true)]
    [InlineData(SelectedTextCaptureOutcome.Unsupported, true)]
    [InlineData(SelectedTextCaptureOutcome.Denied, true)]
    [InlineData(SelectedTextCaptureOutcome.MultipleRanges, true)]
    [InlineData(SelectedTextCaptureOutcome.TooLarge, true)]
    [InlineData(SelectedTextCaptureOutcome.Timeout, true)]
    [InlineData(SelectedTextCaptureOutcome.Failed, true)]
    [InlineData(SelectedTextCaptureOutcome.Success, false)]
    [InlineData(SelectedTextCaptureOutcome.Sensitive, false)]
    [InlineData(SelectedTextCaptureOutcome.SourceChanged, false)]
    public void RegionOfferPolicyMatchesCaptureOutcome(SelectedTextCaptureOutcome outcome, bool expected) =>
        Assert.Equal(expected, SelectedTextCapturePolicy.CanOfferRegion(outcome));

    [Fact]
    public void UnknownOutcomeCannotOfferRegion() =>
        Assert.False(SelectedTextCapturePolicy.CanOfferRegion((SelectedTextCaptureOutcome)int.MaxValue));

    [Fact]
    public void SuccessPreservesRawTextAtTheLimit()
    {
        var text = "  " + new string('a', SelectedTextCaptureResult.MaxTextLength - 2);

        var result = SelectedTextCaptureResult.Success(text);

        Assert.Equal(SelectedTextCaptureOutcome.Success, result.Outcome);
        Assert.Same(text, result.Text);
    }

    [Fact]
    public void SuccessAcceptsWellFormedSurrogatePairs()
    {
        var result = SelectedTextCaptureResult.Success("漢😀");

        Assert.Equal("漢😀", result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SuccessRejectsBlankText(string text) =>
        Assert.Throws<ArgumentException>(() => SelectedTextCaptureResult.Success(text));

    [Fact]
    public void SuccessRejectsTextOverLimitAndMalformedUtf16()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelectedTextCaptureResult.Success(new string('a', SelectedTextCaptureResult.MaxTextLength + 1)));
        Assert.Throws<ArgumentException>(() => SelectedTextCaptureResult.Success("\ud800"));
    }

    [Fact]
    public void FailureCarriesNoTextAndRejectsSuccessOrUnknownOutcomes()
    {
        var result = SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Denied);

        Assert.Equal(SelectedTextCaptureOutcome.Denied, result.Outcome);
        Assert.Null(result.Text);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Success));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelectedTextCaptureResult.Failure((SelectedTextCaptureOutcome)int.MaxValue));
    }
}
