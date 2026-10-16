namespace FihLens.Application.Capture;

public interface IRegionTextCapture
{
    Task<RegionTextCaptureResult> CaptureAsync(RegionTextCaptureRequest request,
        CancellationToken cancellationToken = default);
}

public enum RegionTextCaptureOutcome
{
    Success, NoText, InvalidRegion, SourceChanged, Unsupported, Denied,
    RecognizerUnavailable, TooLarge, Timeout, Failed
}

public sealed record RegionTextCaptureResult
{
    private RegionTextCaptureResult(RegionTextCaptureOutcome outcome, string? text)
    {
        Outcome = outcome;
        Text = text;
    }

    public RegionTextCaptureOutcome Outcome { get; }
    public string? Text { get; }

    public static RegionTextCaptureResult Success(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new(RegionTextCaptureOutcome.Success, text);
    }

    public static RegionTextCaptureResult Failure(RegionTextCaptureOutcome outcome)
    {
        if (!Enum.IsDefined(outcome) || outcome == RegionTextCaptureOutcome.Success)
            throw new ArgumentOutOfRangeException(nameof(outcome));
        return new(outcome, null);
    }
}
