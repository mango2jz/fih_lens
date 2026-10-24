using FihLens.WinUI.Presentation;
using Microsoft.Windows.AppLifecycle;
using FihLens.Windows.Startup;

namespace FihLens.WinUI;

internal static class SingleInstanceActivation
{
    internal const string InstanceKey = "FIHLens.Main";
    private static readonly object Gate = new();
    private static readonly SingleInstanceActivationPolicy Policy = new();
    private static AppInstance? _instance;
    private static AppInstance? _registration;
    private static Func<Action, bool>? _dispatch;
    private static Action? _reopen;
    private static Action? _showClosingStatus;

    internal static void Initialize(AppInstance instance)
    {
        _instance = instance;
        instance.Activated += OnActivated;
    }

    internal static void RetainRegistration(AppInstance registration) => _registration = registration;

    internal static void Attach(Func<Action, bool> dispatch, Action reopen, Action showClosingStatus)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(reopen);
        ArgumentNullException.ThrowIfNull(showClosingStatus);
        bool open;
        lock (Gate)
        {
            _dispatch = dispatch;
            _reopen = reopen;
            _showClosingStatus = showClosingStatus;
            open = Policy.MarkReady();
        }
        if (open) DispatchOpen(dispatch, reopen);
    }

    internal static void BeginShutdown()
    {
        lock (Gate) Policy.BeginShutdown();
    }

    internal static void Detach()
    {
        lock (Gate)
        {
            if (_instance is not null) _instance.Activated -= OnActivated;
            _instance = null;
            _dispatch = null;
            _reopen = null;
            _showClosingStatus = null;
            Policy.Retire();
        }
    }

    internal static void ShowStartupFailure(bool timedOut) => StartupDiagnostic.ShowError(
        timedOut
            ? "FIH Lens is already running, but did not respond in time. Try again after it finishes starting or closing."
            : "FIH Lens could not open its existing session. Try again after it finishes starting or closing.",
        "FIH Lens");

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        Func<Action, bool>? dispatch;
        Action? reopen;
        Action? closingStatus;
        ActivationRequest request;
        lock (Gate)
        {
            request = Policy.RequestOpen();
            dispatch = _dispatch;
            reopen = _reopen;
            closingStatus = _showClosingStatus;
        }
        if (request is ActivationRequest.Pending or ActivationRequest.Ignored || dispatch is null) return;
        if (request == ActivationRequest.Closing)
        {
            DispatchSafely(dispatch, () => RunClosing(closingStatus!));
            return;
        }
        if (reopen is null) return;
        DispatchOpen(dispatch, reopen);
    }

    private static void DispatchOpen(Func<Action, bool> dispatch, Action reopen) => DispatchSafely(
        dispatch, () => RunOpen(reopen), CancelScheduledOpen);

    private static void DispatchSafely(Func<Action, bool> dispatch, Action callback, Action? rejected = null)
    {
        try
        {
            if (dispatch(callback)) return;
            rejected?.Invoke();
        }
        catch (Exception exception)
        {
            rejected?.Invoke();
            System.Diagnostics.Debug.WriteLine($"Activation dispatch exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
        }
    }

    private static void CancelScheduledOpen()
    {
        lock (Gate) Policy.CancelScheduledOpen();
    }

    private static void RunOpen(Action reopen)
    {
        try { lock (Gate) Policy.RunOpen(reopen); }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Activation reopen exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
        }
    }

    private static void RunClosing(Action showClosingStatus)
    {
        try { lock (Gate) Policy.RunClosing(showClosingStatus); }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Activation closing status exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
        }
    }
}
