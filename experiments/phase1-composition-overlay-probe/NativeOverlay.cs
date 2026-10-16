using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Phase1CompositionOverlayProbe;

internal sealed class NativeOverlay
{
    private readonly Native.WndProc callback;
    private readonly Action<string, Exception> failure;
    private readonly Action<string> changed;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly string className = "FihLensCompositionProbe_" + Guid.NewGuid().ToString("N");
    private ushort atom;
    private Compositor? compositor;
    private DesktopWindowTarget? target;
    private ContainerVisual? root;
    private readonly List<SpriteVisual> visuals = [];
    private readonly List<CompositionColorBrush> brushes = [];
    internal nint Hwnd { get; private set; }
    internal bool DestroyAcknowledged { get; private set; }
    internal int HolePresses, PointerMoves, PointerReleases;
    internal Bounds Monitor { get; }
    internal Bounds Hole { get; }
    private bool dragging;
    internal Action? SampleRequested;

    internal NativeOverlay(Bounds monitor, Bounds hole, Action<string, Exception> failure, Action<string> changed)
    {
        Monitor = monitor; Hole = hole; this.failure = failure; this.changed = changed; callback = Procedure;
    }

    internal unsafe void Initialize(nint owner, bool inject)
    {
        CheckThread();
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().EnsureSystemDispatcherQueue();
        var wc = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = callback,
            Instance = Native.GetModuleHandleW(null), ClassName = className };
        atom = Native.RegisterClassExW(ref wc);
        if (atom == 0) throw Native.Error();
        Hwnd = Native.CreateWindowExW(0x00200080, className, "FIH Lens composition probe — S samples six owned points; Escape closes; drag clear hole",
            0x80000000, Monitor.Left, Monitor.Top, Monitor.Width, Monitor.Height, owner, 0, wc.Instance, 0);
        if (Hwnd == 0) throw Native.Error();
        if (inject) throw new InvalidOperationException("Injected boundary: HWND created, no compositor/target/activation.");
        if (Native.ClientBounds(Hwnd) != Monitor) throw new InvalidOperationException("Native client does not match frozen physical monitor.");
        compositor = new Compositor();
        nint interop = 0, abi = 0;
        try
        {
            var iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(((WinRT.IWinRTObject)compositor).NativeObject.ThisPtr, in iid, out interop));
            // IUnknown slots 0..2, then CreateDesktopWindowTarget(HWND, BOOL, out target).
            var create = (delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)(*(nint**)interop)[3];
            Marshal.ThrowExceptionForHR(create(interop, Hwnd, 0, &abi));
            target = WinRT.MarshalInterface<DesktopWindowTarget>.FromAbi(abi);
        }
        finally { if (abi != 0) Marshal.Release(abi); if (interop != 0) Marshal.Release(interop); }
        root = compositor.CreateContainerVisual(); root.Size = new Vector2(Monitor.Width, Monitor.Height); target.Root = root;
        int x = Hole.Left - Monitor.Left, y = Hole.Top - Monitor.Top, w = Hole.Width, h = Hole.Height;
        Add(0, 0, Monitor.Width, y, Color.FromArgb(140, 0, 0, 0));
        Add(0, y, x, h, Color.FromArgb(140, 0, 0, 0));
        Add(x + w, y, Monitor.Width - x - w, h, Color.FromArgb(140, 0, 0, 0));
        Add(0, y + h, Monitor.Width, Monitor.Height - y - h, Color.FromArgb(140, 0, 0, 0));
        var yellow = Color.FromArgb(255, 255, 200, 0);
        Add(x, y, w, 2, yellow); Add(x, y + h - 2, w, 2, yellow);
        Add(x, y, 2, h, yellow); Add(x + w - 2, y, 2, h, yellow);
        Native.ShowWindow(Hwnd, 5);
        if (!Native.SetForegroundWindow(Hwnd)) throw new InvalidOperationException("Foreground request denied.");
        changed("overlay initialized");
    }

    private void Add(int x, int y, int w, int h, Color color)
    {
        if (w <= 0 || h <= 0) throw new InvalidOperationException("Invalid composition geometry.");
        var brush = compositor!.CreateColorBrush(color); brushes.Add(brush);
        var visual = compositor.CreateSpriteVisual(); visuals.Add(visual);
        visual.Brush = brush; visual.Offset = new Vector3(x, y, 0); visual.Size = new Vector2(w, h); root!.Children.InsertAtTop(visual);
    }

    private nint Procedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case 0x84: return 1; // Uniform HTCLIENT even where composition has no visual.
                case 0x14: return 1; // No GDI erase/background surface.
                case 0x100:
                    if (wParam == 0x1B) { Close(); return 0; }
                    if (wParam == 0x53) { SampleRequested?.Invoke(); return 0; }
                    break;
                case 0x201:
                    var x = Monitor.Left + (short)((long)lParam & 65535);
                    var y = Monitor.Top + (short)(((long)lParam >> 16) & 65535);
                    if (Hole.Inside(x, y)) { HolePresses++; dragging = true; Native.SetCapture(hwnd); changed("hole press"); }
                    return 0;
                case 0x200: if (dragging) PointerMoves++; return 0;
                case 0x202:
                    if (dragging) { dragging = false; PointerReleases++; Native.ReleaseCapture(); changed("hole drag released"); } return 0;
                case 0x10: Close(); return 0;
                case 0x82:
                    Hwnd = 0; DestroyAcknowledged = true; changed("WM_NCDESTROY acknowledged");
                    break;
            }
        }
        catch (Exception error) { SafeFailure("WndProc", error); }
        return Native.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    internal bool Close()
    {
        CheckThread();
        if (Hwnd != 0 && !Native.DestroyWindow(Hwnd)) { SafeFailure("DestroyWindow", Native.Error()); return false; }
        // Actual WM_NCDESTROY has acknowledged callback retirement before releasing its root.
        if (Hwnd != 0) { SafeFailure("DestroyWindow acknowledgement", new InvalidOperationException()); return false; }
        var detached = Release("target detach", () => { if (target is not null) target.Root = null; });
        var cleared = Release("root children clear", () => root?.Children.RemoveAll());
        foreach (var visual in visuals.ToArray()) if (Release("visual dispose", visual.Dispose)) visuals.Remove(visual);
        foreach (var brush in brushes.ToArray()) if (Release("brush dispose", brush.Dispose)) brushes.Remove(brush);
        if (detached && cleared && visuals.Count == 0 && Release("root dispose", () => root?.Dispose())) root = null;
        if (detached && root is null && Release("target dispose", () => target?.Dispose())) target = null;
        if (root is null && target is null && brushes.Count == 0 && Release("compositor dispose", () => compositor?.Dispose())) compositor = null;
        if (atom != 0)
        {
            if (Native.UnregisterClassW(className, Native.GetModuleHandleW(null))) atom = 0;
            else SafeFailure("UnregisterClass", Native.Error());
        }
        if (atom != 0 || root is not null || target is not null || compositor is not null || visuals.Count != 0 || brushes.Count != 0) return false;
        SampleRequested = null;
        GC.KeepAlive(callback);
        changed("cleanup completed");
        return atom == 0;
    }
    private bool Release(string stage, Action action) { try { action(); return true; } catch (Exception error) { SafeFailure(stage, error); return false; } }
    private void SafeFailure(string stage, Exception error) { try { failure(stage, error); } catch { } }
    private void CheckThread() { if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI thread required."); }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; internal readonly Bounds Bounds => new(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass
    {
        internal uint Size, Style; internal WndProc Procedure; internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background; internal string? MenuName; internal string ClassName; internal nint SmallIcon;
    }
    internal static Exception Error() => new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    internal static Bounds ClientBounds(nint hwnd)
    {
        if (!GetClientRect(hwnd, out var r)) throw Error();
        var p = new Point(); if (!ClientToScreen(hwnd, ref p)) throw Error();
        return new(p.X, p.Y, checked(p.X + r.Right), checked(p.Y + r.Bottom));
    }
    internal static Bounds MonitorBounds(nint hwnd)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(hwnd, 2), ref info)) throw Error(); return info.Monitor.Bounds;
    }
    [DllImport("user32.dll", SetLastError = true)] internal static extern ushort RegisterClassExW(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool UnregisterClassW(string name, nint instance);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowExW(uint extended, string name, string title, uint style, int x, int y, int w, int h, nint owner, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] internal static extern nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
}
