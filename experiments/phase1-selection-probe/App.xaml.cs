using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace Phase1SelectionProbe;

public partial class App : Application
{
    private Window? owner;
    private Window? overlay;
    private DispatcherTimer? timer;
    private readonly TextBlock counter = new() { FontSize = 28, Foreground = Brush(255, 0, 0, 0) };
    private readonly TextBlock status = new() { Text = "Ready. No screen reads until explicit pixel calibration; no OCR or provider calls.", TextWrapping = TextWrapping.Wrap };
    private readonly Border fixture = new() { Width = 480, Height = 180, Background = Brush(255, 255, 255, 255) };
    private readonly Button show = new() { Content = "Show transparent probe" };
    private readonly Button baseline = new() { Content = "Calibrate known fixture pixels only" };
    private readonly Border livePatch = new() { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16), Background = Brush(255, 0, 255, 0) };
    private (int X, int Y)[]? samplePoints;
    private uint[]? baselineMarkers;
    private uint? baselineStroke, overlayStroke;
    private readonly uint?[] baselineLive = new uint?[2];
    private readonly bool[] overlayLive = new bool[2];
    private string samplePhase = "idle";
    private int sampleAttempts, screenDcReadCalls;
    private bool empiricalScreenSampleProofPassed;
    private TextBlock? overlaySampleStatus;
    private bool CalibrationReady => baselineMarkers is not null && baselineLive.All(c => c is not null) && baselineLive[0] != baselineLive[1];
    private Rect frozenOwnerClient;
    private Rect sampledMonitor, sampledHole;
    private TransparentBackdrop? backdrop;
    private Rect monitorBounds;
    private Rect holeBounds;
    private Rect clientBounds;
    private Rect taskbarBefore;
    private double scale;
    private int ticks, holePresses, overlayCloses;
    private bool geometryMatched, ownerClosed, timerStopped;
    private string closeReason = "none";
    private readonly ProbeFailureLog failures = new();
    private readonly string output = FindOutput();

    public App() { InitializeComponent(); }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Require(Contains(new Rect(-1920, 0, 0, 1080), new Rect(-1900, 20, -1500, 200)), "Negative-origin geometry self-check failed.");
        Require(!Contains(new Rect(0, 0, 1920, 1080), new Rect(-1, 0, 200, 100)), "Outside geometry self-check failed.");
        Require(Pixel(400 * 1.25) == 500 && Pixel(400 * 2d) == 800, "Scale self-check failed.");
        var colors = new Grid();
        colors.ColumnDefinitions.Add(new ColumnDefinition()); colors.ColumnDefinitions.Add(new ColumnDefinition());
        colors.Children.Add(new Border { Background = Brush(255, 255, 0, 0), Height = 24, VerticalAlignment = VerticalAlignment.Bottom });
        var blue = new Border { Background = Brush(255, 0, 0, 255), Height = 24, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(blue, 1); colors.Children.Add(blue);
        Grid.SetColumnSpan(counter, 2); counter.Margin = new Thickness(24); colors.Children.Add(counter);
        Grid.SetColumnSpan(livePatch, 2); colors.Children.Add(livePatch); fixture.Child = colors;
        show.Click += ShowOverlay;
        baseline.Click += BeginBaseline;
        owner = new Window { Title = "FIH Lens — live transparent selection fixture", Content = new StackPanel { Padding = new Thickness(24), Spacing = 16, Background = Brush(255, 220, 220, 220), Children = { fixture, baseline, show, status } } };
        owner.AppWindow.ResizeClient(new SizeInt32(700, 480));
        owner.Closed += OwnerClosed;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += Tick; timer.Start();
        owner.Activate();
    }

    private void Tick(object? sender, object args)
    {
        if (samplePhase != "idle" && ticks % 2 == 1) SamplePixels();
        ticks++; counter.Text = $"LIVE FIXTURE\nCounter: {ticks}";
        livePatch.Background = ticks / 2 % 2 == 0 ? Brush(255, 0, 255, 0) : Brush(255, 255, 0, 255);
    }

    private void BeginBaseline(object sender, RoutedEventArgs args)
    {
        if (owner is null || overlay is not null) return;
        baselineMarkers = null; baselineStroke = null; overlayStroke = null; Array.Clear(baselineLive); Array.Clear(overlayLive);
        empiricalScreenSampleProofPassed = false; sampleAttempts = 0;
        try
        {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        frozenOwnerClient = ClientBounds(hwnd);
        scale = fixture.XamlRoot.RasterizationScale;
        var origin = new Point(); Require(ClientToScreen(hwnd, ref origin), "Fixture origin unavailable.");
        var location = fixture.TransformToVisual((UIElement)owner.Content).TransformPoint(new Windows.Foundation.Point());
        var x = origin.X + Pixel(location.X * scale); var y = origin.Y + Pixel(location.Y * scale);
        var w = Pixel(fixture.ActualWidth * scale); var h = Pixel(fixture.ActualHeight * scale);
        sampledHole = new Rect(x, y, x + w, y + h);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Require(GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitorInfo) && Contains(monitorInfo.Monitor, sampledHole), "Fixture monitor unavailable.");
        sampledMonitor = monitorInfo.Monitor;
        samplePoints = [(x + w / 4, y + h - Pixel(12 * scale)), (x + w * 3 / 4, y + h - Pixel(12 * scale)),
            (x + w / 2, y + h - Pixel(48 * scale)), (x + w - Pixel(32 * scale), y + Pixel(32 * scale)), (x + 1, y + h / 2)];
        samplePhase = "baseline"; status.Text = "Calibrating only five owned fixture coordinates; keep fixture unobscured.";
        }
        catch (Exception error) { samplePhase = "idle"; RecordFailure("pixel calibration setup inconclusive", error); status.Text = "Inconclusive fixture geometry."; }
    }

    private void BeginOverlaySamples(object sender, RoutedEventArgs args)
    {
        ResetOverlayProof();
        if (!CalibrationReady)
        { status.Text = "Inconclusive: calibrate both distinct live fixture colors first."; return; }
        sampleAttempts = 0; samplePhase = "overlay";
    }

    private void ResetOverlayProof()
    {
        ScreenPixelProbe.ResetProof(overlayLive, ref overlayStroke, ref empiricalScreenSampleProofPassed);
    }

    private void SamplePixels()
    {
        var phase = samplePhase; sampleAttempts++;
        try
        {
            Require(owner is not null && samplePoints is not null, "Sample owner missing.");
            var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner!);
            var expectedForeground = phase == "overlay" && overlay is not null ? WinRT.Interop.WindowNative.GetWindowHandle(overlay) : ownerHwnd;
            void Validate()
            {
                Require(GetForegroundWindow() == expectedForeground && ClientBounds(ownerHwnd).Equals(frozenOwnerClient), "Foreground or fixture geometry changed.");
                Require(fixture.XamlRoot.RasterizationScale == scale, "Fixture scale changed.");
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                Require(GetMonitorInfo(MonitorFromWindow(ownerHwnd, 2), ref info), "Monitor unavailable.");
                Require(info.Monitor.Equals(sampledMonitor), "Frozen monitor changed.");
                var origin = new Point(); Require(ClientToScreen(ownerHwnd, ref origin), "Fixture origin unavailable.");
                var location = fixture.TransformToVisual((UIElement)owner!.Content).TransformPoint(new Windows.Foundation.Point());
                Require(sampledHole.Equals(new Rect(origin.X + Pixel(location.X * scale), origin.Y + Pixel(location.Y * scale),
                    origin.X + Pixel((location.X + fixture.ActualWidth) * scale), origin.Y + Pixel((location.Y + fixture.ActualHeight) * scale))), "Fixture layout changed.");
                Require(samplePoints!.All(p => p.X > info.Monitor.Left && p.X < info.Monitor.Right && p.Y > info.Monitor.Top && p.Y < info.Monitor.Bottom), "Sample outside monitor.");
                if (phase == "overlay") Require(overlay is not null && geometryMatched && info.Monitor.Equals(monitorBounds) && ClientBounds(expectedForeground).Equals(monitorBounds)
                    && samplePoints!.All(p => p.X > holeBounds.Left && p.X < holeBounds.Right && p.Y > holeBounds.Top && p.Y < holeBounds.Bottom), "Overlay geometry changed.");
            }
            Validate(); screenDcReadCalls++;
            var colors = ScreenPixelProbe.Read(samplePoints!); Validate();
            var livePhase = ticks / 2 % 2;
            if (phase == "baseline")
            {
                baselineMarkers ??= colors[..3];
                baselineStroke ??= colors[4];
                Require(baselineMarkers.SequenceEqual(colors[..3]) && baselineMarkers.Distinct().Count() == 3, "Static calibration changed or unsupported.");
                if (baselineLive[livePhase] is uint prior) Require(prior == colors[3], "Live calibration unstable.");
                baselineLive[livePhase] = colors[3];
                if (baselineLive.All(c => c is not null) && baselineLive[0] != baselineLive[1]) samplePhase = "idle";
            }
            else
            {
                var markersAndControl = ScreenPixelProbe.MarkersAndControlMatch(colors, baselineMarkers!, baselineStroke!.Value, baselineLive, overlayStroke);
                if (markersAndControl) overlayStroke ??= colors[4];
                if (markersAndControl && colors[3] == baselineLive[livePhase]) overlayLive[livePhase] = true;
                empiricalScreenSampleProofPassed = overlayLive.All(v => v);
                if (empiricalScreenSampleProofPassed) samplePhase = "idle";
            }
            Directory.CreateDirectory(output);
            File.AppendAllText(Path.Combine(output, "screen-pixel-samples.jsonl"), JsonSerializer.Serialize(new { phase, attempt = sampleAttempts, ticks, livePhase,
                points = samplePoints!.Select(p => new { p.X, p.Y }), colorRefs = colors, screenDcReadCalls, empiricalScreenSampleProofPassed }) + Environment.NewLine);
            if (sampleAttempts >= 12) { samplePhase = "idle"; if (!empiricalScreenSampleProofPassed) ResetOverlayProof(); }
            status.Text = samplePhase != "idle" ? "Sampling known fixture pixels only; bounded attempts remain."
                : phase == "baseline" && CalibrationReady ? "Calibration ready; open overlay, then explicitly sample fixture pixels." : empiricalScreenSampleProofPassed ? "PASS: host-specific owned fixture pixel samples only." : "Inconclusive pixel samples; retry explicitly.";
            if (overlaySampleStatus is not null) overlaySampleStatus.Text = status.Text;
            Save();
        }
        catch (Exception error) { samplePhase = "idle"; ResetOverlayProof(); if (phase == "baseline") baselineMarkers = null; RecordFailure("screen pixel sample inconclusive", error); status.Text = "Inconclusive: screen pixels unavailable or geometry changed."; if (overlaySampleStatus is not null) overlaySampleStatus.Text = status.Text; Save(); }
    }

    private void ShowOverlay(object sender, RoutedEventArgs args)
    {
        if (owner is null || overlay is not null) return;
        ResetOverlayProof();
        samplePhase = "idle";
        var stage = "fixture geometry";
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            var monitor = MonitorFromWindow(hwnd, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Require(GetMonitorInfo(monitor, ref info), "Monitor geometry unavailable."); monitorBounds = info.Monitor;
            taskbarBefore = TaskbarBounds();
            var origin = new Point(); Require(ClientToScreen(hwnd, ref origin), "Fixture origin unavailable.");
            scale = fixture.XamlRoot.RasterizationScale;
            var location = fixture.TransformToVisual((UIElement)owner.Content).TransformPoint(new Windows.Foundation.Point());
            holeBounds = new Rect(origin.X + Pixel(location.X * scale), origin.Y + Pixel(location.Y * scale),
                origin.X + Pixel((location.X + fixture.ActualWidth) * scale), origin.Y + Pixel((location.Y + fixture.ActualHeight) * scale));
            Require(Contains(monitorBounds, holeBounds), "Fixture must fit the selected display.");
            geometryMatched = false; holePresses = 0; closeReason = "open";
            var canvas = new Canvas { Background = Brush(0, 0, 0, 0), IsHitTestVisible = false };
            stage = "overlay window constructor";
            overlay = new Window();
            overlay.Closed += OverlayClosed;
            stage = "overlay title and content";
            overlay.Title = "FIH Lens — transparent selection probe"; overlay.Content = canvas;
            stage = "backdrop constructor";
            backdrop = new TransparentBackdrop();
            stage = "window backdrop assignment"; overlay.SystemBackdrop = backdrop;
            stage = "overlapped presenter configuration";
            var presenter = (OverlappedPresenter)overlay.AppWindow.Presenter;
            presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
            stage = "overlay resize client";
            overlay.AppWindow.ResizeClient(new SizeInt32(monitorBounds.Width, monitorBounds.Height));
            stage = "overlay initial move";
            overlay.AppWindow.Move(new PointInt32(monitorBounds.Left, monitorBounds.Top));
            canvas.Loaded += OverlayLoaded;
            canvas.KeyDown += KeyDown;
            stage = "overlay activate";
            overlay.Activate(); show.IsEnabled = overlay is null;
        }
        catch (Exception e) { Fail(stage is "window backdrop assignment" or "overlay activate" ? backdrop?.FailureStage ?? stage : stage, e); }
    }

    private void OverlayLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
            Require(overlay is not null, "Overlay missing.");
            var current = overlay!;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(current);
            clientBounds = ClientBounds(hwnd);
            // Borderless overlapped windows may retain invisible non-client margins; measure them.
            current.AppWindow.Move(new PointInt32(current.AppWindow.Position.X + monitorBounds.Left - clientBounds.Left,
                current.AppWindow.Position.Y + monitorBounds.Top - clientBounds.Top));
            clientBounds = ClientBounds(hwnd);
            Require(clientBounds.Equals(monitorBounds), "Client bounds do not match frozen monitor.");
            Require(TaskbarBounds().Equals(taskbarBefore), "Taskbar bounds changed.");
            scale = ((Canvas)current.Content).XamlRoot.RasterizationScale;
            Require(double.IsFinite(scale) && scale > 0, "Invalid actual scale.");
            var canvas = (Canvas)current.Content;
            var left = (holeBounds.Left - monitorBounds.Left) / scale;
            var top = (holeBounds.Top - monitorBounds.Top) / scale;
            var width = holeBounds.Width / scale; var height = holeBounds.Height / scale;
            var fullWidth = monitorBounds.Width / scale; var fullHeight = monitorBounds.Height / scale;
            Dim(0, 0, fullWidth, top); Dim(0, top, left, height);
            Dim(left + width, top, fullWidth - left - width, height); Dim(0, top + height, fullWidth, fullHeight - top - height);
            var hole = new Border { Width = width, Height = height, Background = Brush(0, 0, 0, 0), BorderBrush = Brush(255, 255, 200, 0), BorderThickness = new Thickness(2) };
            Canvas.SetLeft(hole, left); Canvas.SetTop(hole, top); canvas.Children.Add(hole);
            hole.PointerPressed += HolePressed;
            var cancel = new Button { Content = "Cancel overlay (Escape)", MinWidth = 200 };
            cancel.Click += Cancel; Canvas.SetLeft(cancel, 20); Canvas.SetTop(cancel, 20); canvas.Children.Add(cancel);
            var sample = new Button { Content = "Sample known fixture pixels only" };
            sample.Click += BeginOverlaySamples; Canvas.SetLeft(sample, 20); Canvas.SetTop(sample, 64); canvas.Children.Add(sample);
            overlaySampleStatus = new TextBlock { Text = "Pixel sampling requires explicit button action.", Foreground = Brush(255, 255, 255, 255), Width = 400, TextWrapping = TextWrapping.Wrap };
            Canvas.SetLeft(overlaySampleStatus, 20); Canvas.SetTop(overlaySampleStatus, 108); canvas.Children.Add(overlaySampleStatus);
            canvas.IsHitTestVisible = true; geometryMatched = true; cancel.Focus(FocusState.Programmatic);
            status.Text = "Overlay open. Counter must remain live; click its clear hole, then Escape or Cancel."; Save();
            void Dim(double x, double y, double w, double h)
            {
                var dim = new Border { Width = w, Height = h, Background = Brush(140, 0, 0, 0) };
                Canvas.SetLeft(dim, x); Canvas.SetTop(dim, y); canvas.Children.Add(dim);
            }
        }
        catch (Exception e) { Fail("geometry", e); }
    }

    private void HolePressed(object sender, PointerRoutedEventArgs args) { holePresses++; args.Handled = true; Save(); }
    private void Cancel(object sender, RoutedEventArgs args) => CloseOverlay("cancel");
    private void KeyDown(object sender, KeyRoutedEventArgs args) { if (args.Key == VirtualKey.Escape) { args.Handled = true; CloseOverlay("escape"); } }
    private void CloseOverlay(string reason)
    {
        if (overlay is null) return;
        var current = overlay; closeReason = reason;
        var closeReturned = false;
        try { current.Close(); closeReturned = true; }
        catch (Exception e) { RecordFailure("overlay close", e); }
        finally { if (closeReturned && ReferenceEquals(overlay, current)) RetireOverlay(current); }
    }
    private void OverlayClosed(object sender, WindowEventArgs args)
    {
        overlayCloses++;
        RetireOverlay((Window)sender);
    }
    private void RetireOverlay(Window closed)
    {
        closed.Closed -= OverlayClosed;
        try
        {
            if (closed.Content is Canvas canvas)
            {
                canvas.Loaded -= OverlayLoaded; canvas.KeyDown -= KeyDown;
                foreach (var child in canvas.Children) { if (child is Border border) border.PointerPressed -= HolePressed; if (child is Button button) { button.Click -= Cancel; button.Click -= BeginOverlaySamples; } }
            }
        }
        catch (Exception e) { RecordFailure("overlay handler cleanup", e); }
        finally
        {
            try { closed.SystemBackdrop = null; }
            catch (Exception e) { RecordFailure("window backdrop clear", e); }
            finally { if (ReferenceEquals(overlay, closed)) overlay = null; show.IsEnabled = true; }
        }
        samplePhase = "idle"; overlaySampleStatus = null; status.Text = $"Overlay closed ({closeReason}); clear-hole presses: {holePresses}."; Save();
    }

    private void OwnerClosed(object sender, WindowEventArgs args)
    {
        ownerClosed = true;
        try { CloseOverlay("owner close"); }
        finally
        {
            try
            {
                if (timer is not null) { timer.Stop(); timerStopped = true; }
            }
            catch (Exception e) { RecordFailure("owner timer stop", e); }
            finally
            {
                if (timer is not null) timer.Tick -= Tick;
                timer = null; show.Click -= ShowOverlay; baseline.Click -= BeginBaseline;
                if (owner is not null) owner.Closed -= OwnerClosed;
                owner = null; Save();
            }
        }
    }

    private void Fail(string stage, Exception e)
    {
        ResetOverlayProof();
        try { RecordFailure(stage, e); }
        finally { CloseOverlay("failure"); }
        status.Text = $"FAIL {stage}: {e.GetType().Name} (0x{e.HResult:X8}); no geometry accepted." + (failures.RecordingFailed ? " Diagnostic recording failed." : "");
    }

    private void RecordFailure(string stage, Exception e)
    {
        failures.Record(stage, e, (failure, first) =>
        {
            Directory.CreateDirectory(output);
            var metadata = JsonSerializer.Serialize(new { stage = failure.Stage, type = failure.Type, hresult = failure.HResult,
                backdropStage = backdrop?.Stage, backdropFailureStage = backdrop?.FailureStage, backdropCleanupFailures = backdrop?.CleanupFailures,
                systemQueuePresentBefore = backdrop?.SystemQueuePresentBefore, systemQueuePresentAfter = backdrop?.SystemQueuePresentAfter,
                systemDispatcherQueuePresent = Windows.System.DispatcherQueue.GetForCurrentThread() is not null });
            if (first) File.WriteAllText(Path.Combine(output, "failure.json"), metadata);
            else File.AppendAllText(Path.Combine(output, "additional-failures.jsonl"), metadata + Environment.NewLine);
        });
    }

    private void Save()
    {
        try
        {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { observedUtc = DateTimeOffset.UtcNow,
            package = Windows.ApplicationModel.Package.Current.Id.FullName, scale, monitorBounds, holeBounds, clientBounds, geometryMatched,
            taskbarBefore, taskbarAfter = TaskbarBounds(), ticks, holePresses, overlayCloses, closeReason, ownerClosed, timerStopped,
            backdropConnected = backdrop?.Connected, backdropDisconnected = backdrop?.Disconnected, backdropResourcesDisposed = backdrop?.ResourcesDisposed,
            backdropStage = backdrop?.Stage, backdropFailureStage = backdrop?.FailureStage,
            systemQueuePresentBefore = backdrop?.SystemQueuePresentBefore, systemQueuePresentAfter = backdrop?.SystemQueuePresentAfter,
            firstFailure = failures.First, diagnosticRecordingFailed = failures.RecordingFailed, backdropCleanupFailures = backdrop?.CleanupFailures,
            wgcFullImageCaptureCalls = 0, screenDcReadCalls, sampleAttempts, samplePhase, empiricalScreenSampleProofPassed,
            ocrCalls = 0, providerCalls = 0, actualDesktopTransparencyVerified = false,
            note = "Transparency and live counter require external visual observation. Geometry alone does not prove alpha." }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        catch (Exception e)
        {
            samplePhase = "idle"; ResetOverlayProof(); RecordFailure("result recording", e);
            // Verdict updates are best effort after IO failure; never recurse into recording.
            try { status.Text = "Inconclusive: recording failed."; } catch { }
            try { if (overlaySampleStatus is not null) overlaySampleStatus.Text = "Inconclusive: recording failed."; } catch { }
        }
    }

    private static string FindOutput()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return Path.Combine(directory.FullName, "docs/handoffs/phase1-figma-native-ui/selection-probe/runtime-r4"); directory = directory.Parent; }
        throw new InvalidOperationException("Repository evidence directory unavailable.");
    }
    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));
    private static int Pixel(double value) => checked((int)Math.Round(value));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static bool Contains(Rect outer, Rect inner) => inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom && inner.Width > 0 && inner.Height > 0;
    private static Rect ClientBounds(nint hwnd) { Require(GetClientRect(hwnd, out var rect), "Client rect unavailable."); var point = new Point(); Require(ClientToScreen(hwnd, ref point), "Client origin unavailable."); return new Rect(point.X, point.Y, checked(point.X + rect.Width), checked(point.Y + rect.Height)); }
    private static Rect TaskbarBounds() { var hwnd = FindWindow("Shell_TrayWnd", null); if (hwnd == 0) return default; Require(GetWindowRect(hwnd, out var rect), "Taskbar bounds unavailable."); return rect; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private record struct Rect(int Left, int Top, int Right, int Bottom) { public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string name, string? title);
}

