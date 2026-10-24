namespace FihLens.WinUI.Presentation;

public enum SecondaryActivationResult { Completed, TimedOut, Failed }
public enum ActivationLaunchDecision { Primary, Redirected, TimedOut, Failed }
public enum ActivationRequest { Open, Pending, Closing, Ignored }

public sealed class SingleInstanceActivationPolicy
{
    private bool _ready;
    private bool _stopping;
    private bool _pendingOpen;
    private bool _openScheduled;

    public ActivationRequest RequestOpen()
    {
        if (_stopping) return _ready ? ActivationRequest.Closing : ActivationRequest.Ignored;
        if (!_ready) { _pendingOpen = true; return ActivationRequest.Pending; }
        if (_openScheduled) return ActivationRequest.Pending;
        _openScheduled = true;
        return ActivationRequest.Open;
    }

    public bool MarkReady()
    {
        if (_stopping || _ready) return false;
        _ready = true;
        bool pending = _pendingOpen;
        _pendingOpen = false;
        _openScheduled = pending;
        return pending;
    }

    public void BeginShutdown()
    {
        _stopping = true;
        _pendingOpen = false;
        _openScheduled = false;
    }

    public void Retire()
    {
        BeginShutdown();
        _ready = false;
    }

    public bool RunOpen(Action open)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (_stopping || !_openScheduled) return false;
        try
        {
            open();
            return true;
        }
        finally { _openScheduled = false; }
    }

    public void CancelScheduledOpen() => _openScheduled = false;

    public bool RunClosing(Action showClosingStatus)
    {
        ArgumentNullException.ThrowIfNull(showClosingStatus);
        if (!_stopping || !_ready) return false;
        showClosingStatus();
        return true;
    }

    public static ActivationLaunchDecision Decide(bool isPrimary, Func<SecondaryActivationResult> redirect)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        if (isPrimary) return ActivationLaunchDecision.Primary;
        return redirect() switch
        {
            SecondaryActivationResult.Completed => ActivationLaunchDecision.Redirected,
            SecondaryActivationResult.TimedOut => ActivationLaunchDecision.TimedOut,
            _ => ActivationLaunchDecision.Failed
        };
    }

    public static bool ShouldShowStartupFailure(ActivationLaunchDecision decision) =>
        decision is ActivationLaunchDecision.TimedOut or ActivationLaunchDecision.Failed;
}
