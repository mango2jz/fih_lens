using System.ComponentModel;
using System.Runtime.InteropServices;
using FihLens.Application.Capture;

namespace FihLens.Windows.Capture;

public sealed record CaptureDisplay(string Name, CaptureSourceSnapshot Source);

public static class WindowsDisplayCatalog
{
    public static IReadOnlyList<CaptureDisplay> GetDisplays() => Enumerate().Select(x => x.Display).ToArray();

    internal sealed record NativeDisplay(nint Handle, CaptureDisplay Display);

    internal static NativeDisplay[] Enumerate()
    {
        var displays = new List<(nint Handle, string Id, PhysicalPixelRect Bounds)>();
        Exception? callbackFailure = null;
        var previous = SetThreadDpiAwarenessContext(-4);
        if (previous == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var succeeded = EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Rect bounds, nint data) =>
            {
                try
                {
                    var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), DeviceName = string.Empty };
                    if (!GetMonitorInfoW(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    displays.Add((monitor, info.DeviceName, new(info.Monitor.Left, info.Monitor.Top,
                        checked(info.Monitor.Right - info.Monitor.Left), checked(info.Monitor.Bottom - info.Monitor.Top))));
                    return true;
                }
                catch (Exception exception)
                {
                    callbackFailure ??= exception;
                    return false;
                }
            }, 0);
            if (callbackFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackFailure).Throw();
            if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SetThreadDpiAwarenessContext(previous); }

        var stamp = CaptureValidation.TopologyStamp(displays.Select(x => (x.Id, x.Bounds)));
        return displays.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => new NativeDisplay(x.Handle,
            new(x.Id, new(x.Id, x.Bounds, stamp)))).ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, ref Rect bounds, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetThreadDpiAwarenessContext(nint context);
}
