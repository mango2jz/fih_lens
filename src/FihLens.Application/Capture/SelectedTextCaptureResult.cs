using System.Text;

namespace FihLens.Application.Capture;

public enum SelectedTextCaptureOutcome
{
    Success,
    NoText,
    Unavailable,
    Unsupported,
    Denied,
    Sensitive,
    MultipleRanges,
    TooLarge,
    Timeout,
    SourceChanged,
    Failed
}

public sealed record SelectedTextCaptureResult
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private SelectedTextCaptureResult(SelectedTextCaptureOutcome outcome, string? text)
    {
        Outcome = outcome;
        Text = text;
    }

    public const int MaxTextLength = 2_000;

    public SelectedTextCaptureOutcome Outcome { get; }
    public string? Text { get; }

    public static SelectedTextCaptureResult Success(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaxTextLength)
            throw new ArgumentOutOfRangeException(nameof(text));

        try
        {
            _ = StrictUtf8.GetByteCount(text);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("Selected text contains invalid Unicode.", nameof(text));
        }

        return new(SelectedTextCaptureOutcome.Success, text);
    }

    public static SelectedTextCaptureResult Failure(SelectedTextCaptureOutcome outcome)
    {
        if (!Enum.IsDefined(outcome) || outcome == SelectedTextCaptureOutcome.Success)
            throw new ArgumentOutOfRangeException(nameof(outcome));
        return new(outcome, null);
    }
}
