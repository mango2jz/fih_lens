using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;

namespace Phase1CompositionOverlayProbe;

internal static class ScreenPixelProbe
{
    internal static uint[] Read((int X, int Y)[] points)
    {
        if (!AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), -4))
            throw new InvalidOperationException("Per-monitor V2 coordinates required.");
        var dc = GetDC(0);
        if (dc == 0) throw new InvalidOperationException("Screen DC unavailable.");
        Exception? first = null;
        uint[]? result = null;
        try
        {
            if (GetMapMode(dc) != 1) throw new InvalidOperationException("MM_TEXT required.");
            result = points.Select(p =>
            {
                var color = GetPixel(dc, p.X, p.Y);
                if (color == uint.MaxValue) throw new InvalidOperationException("Screen pixel unavailable.");
                return color;
            }).ToArray();
        }
        catch (Exception error) { first = error; }
        finally { if (ReleaseDC(0, dc) != 1) first ??= new InvalidOperationException("Screen DC release failed."); }
        if (first is not null) ExceptionDispatchInfo.Capture(first).Throw();
        return result!;
    }
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
    [DllImport("gdi32.dll")] private static extern int GetMapMode(nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
}

