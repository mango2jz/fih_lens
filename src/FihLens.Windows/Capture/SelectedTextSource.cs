using System.Diagnostics;
using System.Runtime.InteropServices;
using FihLens.Application.Capture;
using FihLens.Windows.Windowing;

namespace FihLens.Windows.Capture;

/// <summary>Borrowed invocation-time identity; it never owns either HWND.</summary>
public readonly record struct SelectedTextSource(
    nint SourceWindow, uint ProcessId, uint ThreadId, nint FocusedWindow,
    long InvocationId, long MonotonicTimestamp, long ObserverEpoch, bool SnapshotAvailable)
{
    public WindowFocusSnapshot Origin => new(SourceWindow, ProcessId, ThreadId);
}

/// <summary>UI-thread owner of app-lifetime focus observers and the single UIA MTA lane.</summary>
public sealed class WindowsSelectedTextCapture
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectFocus = 0x8005;
    private const uint WineventOutOfContext = 0x0000;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly WinEventProc _callback;
    private readonly SelectedTextCaptureWorker _worker = new();
    private nint _foregroundHook;
    private nint _focusHook;
    private long _epoch;
    private long _invocation;
    private volatile bool _observing;
    private volatile bool _stopped;

    public WindowsSelectedTextCapture()
    {
        _callback = OnWinEvent;
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _callback, 0, 0, WineventOutOfContext);
        if (_foregroundHook == 0) return;
        _focusHook = SetWinEventHook(EventObjectFocus, EventObjectFocus, 0, _callback, 0, 0, WineventOutOfContext);
        if (_focusHook == 0) _ = TryRetireObservers();
        else _observing = true;
    }

    public bool IsAvailable => !_stopped && _observing && _foregroundHook != 0 && _focusHook != 0;
    public bool IsBusy => _worker.IsBusy;
    public Task DrainCompletion => _worker.DrainCompletion;

    public SelectedTextSource CaptureSource()
    {
        EnsureOwnerThread();
        long timestamp = Stopwatch.GetTimestamp();
        long epoch = Interlocked.Read(ref _epoch);
        nint source = GetForegroundWindow();
        uint thread = GetWindowThreadProcessId(source, out uint process);
        nint focus = 0;
        bool available = IsAvailable && TryCaptureIdentity(source, process, thread, out focus);
        available &= epoch == Interlocked.Read(ref _epoch);
        return new(source, process, thread, focus, Interlocked.Increment(ref _invocation),
            timestamp, epoch, available);
    }

    public SelectedTextCaptureLease CreateSource(SelectedTextSource source) => new(this, source, _worker);

    public void Stop()
    {
        EnsureOwnerThread();
        if (_stopped) return;
        _stopped = true;
        _observing = false;
        _worker.Stop();
    }

    public Task DrainAsync() => _worker.DrainCompletion;

    /// <summary>Retries retained hook retirement; call only on the registering UI thread.</summary>
    public bool TryRetireObservers()
    {
        EnsureOwnerThread();
        _observing = false;
        bool foreground = _foregroundHook == 0 || UnhookWinEvent(_foregroundHook);
        if (foreground) _foregroundHook = 0;
        bool focus = _focusHook == 0 || UnhookWinEvent(_focusHook);
        if (focus) _focusHook = 0;
        return foreground && focus;
    }

    internal bool Validate(SelectedTextSource source, out SelectedTextCaptureOutcome outcome)
    {
        EnsureOwnerThread();
        outcome = SelectedTextCaptureOutcome.SourceChanged;
        if (!source.SnapshotAvailable) { outcome = SelectedTextCaptureOutcome.Unavailable; return false; }
        var current = CaptureSourceWithoutInvocation();
        if (IdentityChanged(source, current) || !current.SnapshotAvailable) return false;
        if (Stopwatch.GetTimestamp() >= source.MonotonicTimestamp + (long)(1500 * Stopwatch.Frequency / 1000d))
        {
            outcome = SelectedTextCaptureOutcome.Timeout;
            return false;
        }
        outcome = default;
        return true;
    }

    internal bool ValidateForOffer(SelectedTextSource source, out SelectedTextCaptureOutcome outcome)
    {
        EnsureOwnerThread();
        var current = CaptureSourceWithoutInvocation();
        if (IdentityChanged(source, current) || (source.SnapshotAvailable && !current.SnapshotAvailable))
        {
            outcome = SelectedTextCaptureOutcome.SourceChanged;
            return false;
        }
        outcome = default;
        return true;
    }

    internal bool IsSourceCurrent(SelectedTextSource source)
    {
        if (!source.SnapshotAvailable || !IsAvailable) return false;
        var current = CaptureSourceWithoutInvocation();
        return current.SnapshotAvailable && !IdentityChanged(source, current);
    }

    private SelectedTextSource CaptureSourceWithoutInvocation()
    {
        nint source = GetForegroundWindow();
        uint thread = GetWindowThreadProcessId(source, out uint process);
        nint focus = 0;
        bool available = IsAvailable && TryCaptureIdentity(source, process, thread, out focus);
        return new(source, process, thread, focus, 0, Stopwatch.GetTimestamp(), Interlocked.Read(ref _epoch), available);
    }

    private static bool TryCaptureIdentity(nint source, uint process, uint thread, out nint focus)
    {
        focus = 0;
        if (source == 0 || !IsWindow(source) || process == 0 || thread == 0 || process == Environment.ProcessId ||
            !TryGetFocusedWindow(thread, out focus) || focus == 0 || !TryGetFocusedWindow(thread, out nint focusAfter) ||
            focusAfter != focus || GetForegroundWindow() != source ||
            GetWindowThreadProcessId(source, out uint afterProcess) != thread || afterProcess != process || !IsWindow(source))
            return false;
        return true;
    }

    private static bool IdentityChanged(SelectedTextSource source, SelectedTextSource current) =>
        source.ObserverEpoch != current.ObserverEpoch ||
        (source.SourceWindow != 0 && current.SourceWindow != 0 && source.SourceWindow != current.SourceWindow) ||
        (source.ProcessId != 0 && current.ProcessId != 0 && source.ProcessId != current.ProcessId) ||
        (source.ThreadId != 0 && current.ThreadId != 0 && source.ThreadId != current.ThreadId) ||
        (source.FocusedWindow != 0 && current.FocusedWindow != 0 && source.FocusedWindow != current.FocusedWindow);

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (_observing) Interlocked.Increment(ref _epoch);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Selected-text observers belong to their registering UI thread.");
    }

    private static bool TryGetFocusedWindow(uint threadId, out nint focused)
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        bool success = GetGUIThreadInfo(threadId, ref info);
        focused = success ? info.Focus : 0;
        return success;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint eventThread, uint eventTime);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWinEvent(nint hook);
}

public sealed class SelectedTextCaptureLease : ISelectedTextCaptureSource
{
    private readonly WindowsSelectedTextCapture _owner;
    private readonly SelectedTextSource _source;
    private readonly SelectedTextCaptureWorker _worker;
    private Task _drainCompletion = Task.CompletedTask;
    private int _started;
    internal SelectedTextCaptureLease(WindowsSelectedTextCapture owner, SelectedTextSource source, SelectedTextCaptureWorker worker)
        => (_owner, _source, _worker) = (owner, source, worker);
    public Task<SelectedTextCaptureResult> CaptureAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return Task.FromResult(SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Unavailable));
        return _worker.CaptureAsync(_owner, _source, cancellationToken, task => _drainCompletion = task);
    }
    public bool ValidateForCommit(out SelectedTextCaptureOutcome outcome) => _owner.Validate(_source, out outcome);
    public bool ValidateForOffer(out SelectedTextCaptureOutcome outcome) => _owner.ValidateForOffer(_source, out outcome);
    public WindowFocusSnapshot Origin => _source.Origin;
    public Task DrainCompletion => _drainCompletion;
}
