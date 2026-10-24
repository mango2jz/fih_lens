namespace FihLens.WinUI.Presentation;

public enum ShellCommand { Open, Capture, Exit }
public enum RegionCaptureRequestOutcome { Started, Busy, ModalBlocked, Unavailable, Stopping }
public enum LauncherCloseAction { Hide, KeepVisible, BeginExit, ObserveExit, RetryExit }

/// <summary>Product decisions shared by launcher and native shell callbacks.</summary>
public sealed class BackgroundShellController(Action open, Func<RegionCaptureRequestOutcome> capture, Action exit)
{
    private readonly Action _open = open ?? throw new ArgumentNullException(nameof(open));
    private readonly Func<RegionCaptureRequestOutcome> _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    private readonly Action _exit = exit ?? throw new ArgumentNullException(nameof(exit));

    public bool IsStopping { get; private set; }
    public bool ExitPending { get; private set; }
    public bool ExitFailed { get; private set; }
    public bool HotkeyAvailable { get; private set; }
    public bool TrayAvailable { get; private set; }
    public int? HotkeyErrorCode { get; private set; }
    public int? TrayErrorCode { get; private set; }

    public RegionCaptureRequestOutcome? Execute(ShellCommand command)
    {
        switch (command)
        {
            case ShellCommand.Open:
                _open();
                return null;
            case ShellCommand.Capture when !IsStopping:
                return _capture();
            case ShellCommand.Exit:
                RequestExit();
                return null;
            default:
                return RegionCaptureRequestOutcome.Stopping;
        }
    }

    public LauncherCloseAction OnClose(bool trayAvailable, bool modalPending)
    {
        if (ExitPending) return LauncherCloseAction.ObserveExit;
        if (ExitFailed) return LauncherCloseAction.RetryExit;
        if (IsStopping) return LauncherCloseAction.KeepVisible;
        if (!trayAvailable) return LauncherCloseAction.BeginExit;
        return modalPending ? LauncherCloseAction.KeepVisible : LauncherCloseAction.Hide;
    }

    public void RequestExit()
    {
        if (ExitPending) return;
        IsStopping = true;
        ExitPending = true;
        ExitFailed = false;
        try { _exit(); }
        catch
        {
            ExitPending = false;
            ExitFailed = true;
            throw;
        }
    }

    public void CompleteExit(bool complete)
    {
        ExitPending = false;
        ExitFailed = !complete;
    }

    public bool RetryShell(Action retryHotkey, Action retryTray)
    {
        if (IsStopping) return false;
        if (!HotkeyAvailable || HotkeyErrorCode is not null) retryHotkey();
        if (!TrayAvailable || TrayErrorCode is not null) retryTray();
        return true;
    }

    public void SetAvailability(bool hotkeyAvailable, int? hotkeyErrorCode, bool trayAvailable, int? trayErrorCode)
    {
        HotkeyAvailable = hotkeyAvailable;
        HotkeyErrorCode = hotkeyErrorCode;
        TrayAvailable = trayAvailable;
        TrayErrorCode = trayErrorCode;
    }
}
