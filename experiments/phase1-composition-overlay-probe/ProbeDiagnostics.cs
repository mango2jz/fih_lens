namespace Phase1CompositionOverlayProbe;

internal sealed record ProbeFailure(string Stage, string Type, int HResult);

internal sealed class ProbeFailureLog
{
    public ProbeFailure? First { get; private set; }
    public bool RecordingFailed { get; private set; }

    public void Record(string stage, Exception error, Action<ProbeFailure, bool> write, Action? persist = null)
    {
        var failure = new ProbeFailure(stage, error.GetType().Name, error.HResult);
        var first = First is null;
        First ??= failure;
        try { write(failure, first); }
        catch { RecordingFailed = true; }
        try { persist?.Invoke(); }
        catch (Exception recordingError)
        {
            RecordingFailed = true;
            Record("state recording", recordingError, write);
        }
    }

}

