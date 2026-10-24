namespace FihLens.Application.Capture;

public static class SelectedTextCapturePolicy
{
    public static bool CanOfferRegion(SelectedTextCaptureOutcome outcome) => outcome is
        SelectedTextCaptureOutcome.NoText or
        SelectedTextCaptureOutcome.Unavailable or
        SelectedTextCaptureOutcome.Unsupported or
        SelectedTextCaptureOutcome.Denied or
        SelectedTextCaptureOutcome.MultipleRanges or
        SelectedTextCaptureOutcome.TooLarge or
        SelectedTextCaptureOutcome.Timeout or
        SelectedTextCaptureOutcome.Failed;
}
