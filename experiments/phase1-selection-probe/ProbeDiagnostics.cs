namespace Phase1SelectionProbe;

internal sealed record ProbeFailure(string Stage, string Type, int HResult);

internal sealed class ProbeFailureLog
{
    public ProbeFailure? First { get; private set; }
    public bool RecordingFailed { get; private set; }

    public void Record(string stage, Exception error, Action<ProbeFailure, bool> write)
    {
        var failure = new ProbeFailure(stage, error.GetType().Name, error.HResult);
        var first = First is null;
        First ??= failure;
        try { write(failure, first); }
        catch { RecordingFailed = true; }
    }

    public static Exception? Release(string stage, Action release, Exception? first, List<ProbeFailure> failures)
    {
        try { release(); }
        catch (Exception error)
        {
            first ??= error;
            if (failures.Count < 8) failures.Add(new ProbeFailure(stage, error.GetType().Name, error.HResult));
        }
        return first;
    }
}
