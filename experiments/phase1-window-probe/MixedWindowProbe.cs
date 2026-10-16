using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Phase1WindowProbe;

public partial class App
{
    private nint priorForegroundToRestore;
    private async Task RunMixed()
    {
        var previousForeground = GetForegroundWindow();
        priorForegroundToRestore = previousForeground;
        var previousRectValid = GetWindowRect(previousForeground, out var previousRect);
        var displays = DisplayArea.FindAll();
        Require(displays.Count >= 2, "Mixed display experiment needs two actual displays.");
        var metadata = new List<object>();
        for (var i = 0; i < displays.Count; i++)
            metadata.Add(new { id = displays[i].DisplayId.Value, displays[i].IsPrimary, outer = Rect(displays[i].OuterBounds), rawWorkArea = Rect(displays[i].WorkArea) });
        File.WriteAllText(Path.Combine(output, "displays.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        var launcher = await Open("E1 mixed display launcher", false);
        var second = await Open("E1 mixed independent session", true);
        var transient = await Open("E1 mixed transient", true);
        var firstId = transient.AppWindow.Id.Value;
        var results = new List<object>();
        for (var i = 0; i < displays.Count; i++)
        {
            results.Add(await MeasureOnDisplay(transient, displays[i], 325, $"display-{i}-compact.png", previousForeground, previousRectValid ? previousRect : null));
            results.Add(await MeasureOnDisplay(transient, displays[i], 569, $"display-{i}-expanded.png", previousForeground, previousRectValid ? previousRect : null));
        }
        // Revisit the first real display to observe reverse DPI transition on the same root.
        results.Add(await MeasureOnDisplay(transient, displays[0], 325, "return-first-display.png", previousForeground, previousRectValid ? previousRect : null));
        transient.Close();
        await Task.Delay(100);
        Require(closed.Contains(transient) && !closed.Contains(launcher) && !closed.Contains(second), "Mixed transient lifetime damaged another window.");
        var reopened = await Open("E1 mixed reopened transient", true);
        Require(reopened.AppWindow.Id.Value != firstId, "Mixed reopen reused closed native identity.");
        results.Add(await MeasureOnDisplay(reopened, displays[displays.Count - 1], 325, "reopened-last-display.png", previousForeground, previousRectValid ? previousRect : null));
        reopened.Close();
        await Task.Delay(100);
        Require(!closed.Contains(second) && second.AppWindow.IsVisible && !closed.Contains(launcher) && launcher.AppWindow.IsVisible, "Mixed close/reopen did not retain other windows.");
        second.Close();
        await Task.Delay(100);
        Require(!closed.Contains(launcher), "Mixed independent close stopped launcher.");
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            success = true, observedUtc = DateTimeOffset.UtcNow, operatingSystem = Environment.OSVersion.VersionString,
            dotnet = Environment.Version.ToString(), architecture = RuntimeInformation.ProcessArchitecture.ToString(), windowsAppSdkPackage = "2.5.1",
            highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast,
            createdWindows = owned.Count, closedTransientsBeforeLauncher = closed.Count, launcherAliveUntilFinalCleanup = true,
            priorForeground = previousForeground.ToInt64(), priorBounds = previousRectValid ? NativeRect(previousRect) : null,
            restorationEvidence = "foreground-restoration.json: one best-effort attempt after all owned windows close",
            displays = metadata, windows = results,
            unproved = new[] { "desktop screenshot/native shadow fidelity", "keyboard/Narrator", "100/125/150/200 full matrix unless separately observed", "live user scale-setting change on retained root" }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task<object> MeasureOnDisplay(Window window, DisplayArea display, int requestedHeightDip, string screenshot, nint prior, NativeBounds? priorBounds)
    {
        var root = (Border)window.Content;
        var appWindow = window.AppWindow;
        var outer = display.OuterBounds;
        var raw = display.WorkArea;
        var added = new RectInt32(raw.X + outer.X, raw.Y + outer.Y, raw.Width, raw.Height);
        var rawFits = Contains(outer, raw);
        var addedFits = Contains(outer, added);
        Require(rawFits || addedFits, "Neither raw nor origin-added work area belongs to display.");
        Require(rawFits != addedFits || (raw.X == added.X && raw.Y == added.Y), "Ambiguous secondary-display WorkArea coordinate interpretation.");
        var work = rawFits ? raw : added;
        var semantics = rawFits && (outer.X != 0 || outer.Y != 0) ? "observed-desktop-coordinates-contrary-to-docs-relative-wording" : "display-relative-plus-outer-origin";
        appWindow.Move(new PointInt32(work.X + 20, work.Y + 20));
        await Task.Delay(250);
        Require(DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.None).DisplayId.Value == display.DisplayId.Value, "Window move did not select requested actual display.");
        var scale = root.XamlRoot.RasterizationScale;
        var overheadWidth = appWindow.Size.Width - appWindow.ClientSize.Width;
        var overheadHeight = appWindow.Size.Height - appWindow.ClientSize.Height;
        var clientWidth = Math.Min((int)Math.Round(400 * scale), work.Width - overheadWidth);
        var clientHeight = Math.Min((int)Math.Round(requestedHeightDip * scale), work.Height - overheadHeight);
        Require(clientWidth > 0 && clientHeight > 0, "No usable work-area client size.");
        appWindow.ResizeClient(new SizeInt32(clientWidth, clientHeight));
        appWindow.Move(new PointInt32(work.X + (work.Width - appWindow.Size.Width) / 2, work.Y + (work.Height - appWindow.Size.Height) / 2));
        window.Activate();
        await Task.Delay(250);
        root.UpdateLayout();
        Require(DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.None).DisplayId.Value == display.DisplayId.Value, "Placement chose wrong actual display.");
        Require(Contains(work, new RectInt32(appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height)), "Measured outer window escaped actual work area.");
        Require(appWindow.ClientSize.Width == clientWidth && appWindow.ClientSize.Height == clientHeight, "Mixed client sizing mismatch.");
        Require(Math.Abs(root.ActualWidth - clientWidth / scale) <= 1 && Math.Abs(root.ActualHeight - clientHeight / scale) <= 1, "Mixed effective XAML sizing mismatch.");
        Require(root.XamlRoot.ContentIslandEnvironment.AppWindowId.Value == appWindow.Id.Value, "Mixed root association mismatch.");
        Require(GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(window), out var native), "Own native rectangle unavailable.");
        Require(native.Left == appWindow.Position.X && native.Top == appWindow.Position.Y && native.Right - native.Left == appWindow.Size.Width && native.Bottom - native.Top == appWindow.Size.Height, "AppWindow geometry differs from own GetWindowRect.");
        var observedForeground = GetForegroundWindow();
        var foregroundOwn = observedForeground == WinRT.Interop.WindowNative.GetWindowHandle(window);
        var overlap = priorBounds is { } p && native.Left < p.Right && native.Right > p.Left && native.Top < p.Bottom && native.Bottom > p.Top;
        await RenderMixedRoot(root, screenshot);
        return new
        {
            title = window.Title, appWindowId = appWindow.Id.Value, displayId = display.DisplayId.Value, display.IsPrimary,
            scale, requestedWidthDip = 400, requestedHeightDip, actualWidthDip = root.ActualWidth, actualHeightDip = root.ActualHeight,
            clientPhysical = new { appWindow.ClientSize.Width, appWindow.ClientSize.Height },
            outerPhysical = NativeRect(native), outerMinusClientPhysical = new { width = overheadWidth, height = overheadHeight },
            rawWorkArea = Rect(raw), originAddedCandidate = Rect(added), selectedDesktopWorkArea = Rect(work), displayBounds = Rect(outer), workAreaSemantics = semantics,
            heightClamped = clientHeight < (int)Math.Round(requestedHeightDip * scale), widthClamped = clientWidth < (int)Math.Round(400 * scale),
            foregroundOwn, observedForeground = observedForeground.ToInt64(), priorForeground = prior.ToInt64(), overlapsPriorForegroundBounds = overlap,
            screenshot, withinWorkArea = true, xamlRootAssociated = true
        };
    }

    private async Task RenderMixedRoot(FrameworkElement root, string filename)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        var buffer = await bitmap.GetPixelsAsync();
        var pixels = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(buffer);
        Require(pixels.Length > 0, "Mixed own-root render empty.");
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var file = File.Create(Path.Combine(output, filename));
        await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(file);
    }

    private static bool Contains(RectInt32 a, RectInt32 b) => b.Width > 0 && b.Height > 0 && b.X >= a.X && b.Y >= a.Y && (long)b.X + b.Width <= (long)a.X + a.Width && (long)b.Y + b.Height <= (long)a.Y + a.Height;
    private static object NativeRect(NativeBounds b) => new { x = b.Left, y = b.Top, width = b.Right - b.Left, height = b.Bottom - b.Top };
    [StructLayout(LayoutKind.Sequential)] private struct NativeBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeBounds bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
}
