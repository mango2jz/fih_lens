using System.Runtime.InteropServices;

namespace FihLens.Windows.Windowing;

public readonly record struct WindowFocusSnapshot(nint Window, uint ProcessId, uint ThreadId);

/// <summary>Borrowed foreground handles are never owned or destroyed by the caller.</summary>
public static class WindowFocus
{
    public static WindowFocusSnapshot Capture()
    {
        nint window = GetForegroundWindow();
        uint threadId = GetWindowThreadProcessId(window, out uint processId);
        return new WindowFocusSnapshot(window, processId, threadId);
    }

    public static bool IsForeground(nint window) => window != 0 && GetForegroundWindow() == window;

    // Windows may deny foreground restoration; never retry or force input/thread attachment.
    public static bool TryRestore(WindowFocusSnapshot previous)
    {
        if (previous.Window == 0 || previous.ProcessId == 0 || previous.ThreadId == 0 || !IsWindow(previous.Window))
            return false;
        uint threadId = GetWindowThreadProcessId(previous.Window, out uint processId);
        return processId == previous.ProcessId && threadId == previous.ThreadId && SetForegroundWindow(previous.Window);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
