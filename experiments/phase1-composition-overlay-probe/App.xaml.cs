using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Phase1CompositionOverlayProbe;

public partial class App : Application
{
    private Window? owner;
    private NativeOverlay? overlay;
    private DispatcherTimer? timer;
    private readonly Border fixture = new() { Width = 480, Height = 180, Background = Brush(255, 255, 255) };
    private readonly Border live = new() { Width = 32, Height = 32, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Background = Brush(0, 255, 0) };
    private readonly TextBlock counter = new() { Margin = new Thickness(24), FontSize = 24, Foreground = Brush(0, 0, 0) };
    private readonly TextBlock status = new() { Text = "Ready: explicit calibration required. No startup screen reads.", TextWrapping = TextWrapping.Wrap };
    private readonly ProbePolicy policy = new();
    private readonly ProbeFailureLog failures = new();
    private readonly string output = FindOutput();
    private Bounds ownerClient, source, monitor, hole;
    private (int X, int Y)[]? points;
    private double scale;
    private int ticks, attempts, reads, fixturePresses, overlayGeneration, run;
    private string phase = "idle", verdict = "not run";
    private bool ownerClosed, timerStopped, injectedCleanupAcknowledged;
    private int lastHolePresses, lastPointerMoves, lastPointerReleases;
    private bool lastDestroyAcknowledged;
    private string stage = "startup";

    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var colors = new Grid(); colors.ColumnDefinitions.Add(new()); colors.ColumnDefinitions.Add(new());
        colors.Children.Add(new Border { Background = Brush(255, 0, 0), Height = 24, VerticalAlignment = VerticalAlignment.Bottom });
        var blue = new Border { Background = Brush(0, 0, 255), Height = 24, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(blue, 1); colors.Children.Add(blue);
        Grid.SetColumnSpan(counter, 2); colors.Children.Add(counter); Grid.SetColumnSpan(live, 2); colors.Children.Add(live); fixture.Child = colors;
        fixture.PointerPressed += (_, _) => { fixturePresses++; Save(); };
        var calibrate = new Button { Content = "Calibrate six owned fixture pixels" }; calibrate.Click += (_, _) => Calibrate();
        var show = new Button { Content = "Show native composition overlay" }; show.Click += (_, _) => Show(false);
        var inject = new Button { Content = "Inject failure after native HWND creation" }; inject.Click += (_, _) => Show(true);
        var retry = new Button { Content = "Retry owned overlay cleanup" }; retry.Click += (_, _) => Retire();
        owner = new Window { Title = "FIH Lens — native composition overlay fixture", Content = new StackPanel { Padding = new Thickness(24), Spacing = 12, Background = Brush(220, 220, 220), Children = {
            fixture, calibrate, show, inject, retry, new TextBlock { Text = "Calibrate first. Show overlay, press S to sample. Click/drag clear hole; Escape closes. No OCR, provider, clipboard or images.", TextWrapping = TextWrapping.Wrap }, status } } };
        owner.AppWindow.ResizeClient(new SizeInt32(760, 570));
        owner.Closed += (_, _) => { ownerClosed = true; phase = "idle"; timer!.Stop(); timerStopped = true; Retire(); Save(); };
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (phase != "idle" && ticks % 2 == 1) Sample();
            ticks++; counter.Text = $"LIVE FIXTURE\nCounter: {ticks}";
            live.Background = ticks / 2 % 2 == 0 ? Brush(0, 255, 0) : Brush(255, 0, 255);
        };
        timer.Start(); owner.Activate();
    }

    private void Freeze()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner!);
        ownerClient = Native.ClientBounds(hwnd); monitor = Native.MonitorBounds(hwnd); scale = fixture.XamlRoot.RasterizationScale;
        Require(double.IsFinite(scale) && scale > 0, "Invalid scale.");
        var location = fixture.TransformToVisual((UIElement)owner!.Content).TransformPoint(new Windows.Foundation.Point());
        int x = ownerClient.Left + Pixel(location.X * scale), y = ownerClient.Top + Pixel(location.Y * scale);
        int w = Pixel(fixture.ActualWidth * scale), h = Pixel(fixture.ActualHeight * scale), inset = Pixel(8 * scale);
        source = new(x, y, x + w, y + h); hole = new(x + inset, y + inset, x + w - inset, y + h - inset);
        Require(monitor.Contains(source), "Fixture outside frozen monitor.");
        points = [(x + w / 4, y + h - Pixel(12 * scale)), (x + w * 3 / 4, y + h - Pixel(12 * scale)),
            (x + w / 2, y + h - Pixel(48 * scale)), (x + w - Pixel(32 * scale), y + Pixel(32 * scale)),
            (hole.Left, y + h / 2), (x + Pixel(3 * scale), y + h / 2)];
        Require(points.Take(4).All(p => hole.Inside(p.X, p.Y)) && !hole.Inside(points[5].X, points[5].Y) && points.All(p => source.Inside(p.X, p.Y)), "Sample point policy invalid.");
    }
    private void Calibrate()
    {
        if (overlay is not null || ownerClosed) return;
        policy.ResetCalibration(); phase = "idle"; verdict = "not run"; attempts = 0; run++;
        try { stage = "calibration geometry"; Freeze(); phase = "baseline"; status.Text = "Calibrating six fixed owned coordinates; keep fixture foreground and unobscured."; Save(); }
        catch (Exception error) { Fail(stage, error); }
    }
    private void Show(bool inject)
    {
        if (ownerClosed || overlay is not null) return;
        phase = "idle"; policy.ResetRun(); verdict = "not run"; fixturePresses = 0; attempts = 0; overlayGeneration++;
        try
        {
            stage = "frozen fixture validation";
            Require(policy.Calibrated, "Calibrate both live colors first."); Validate(false);
            stage = inject ? "injected initialization boundary" : "native composition initialization";
            overlay = new(monitor, hole, Fail, NativeChanged);
            overlay.SampleRequested = () =>
            {
                policy.ResetRun(); verdict = "sampling"; attempts = 0; run++; phase = "overlay"; Save();
            };
            overlay.Initialize(WinRT.Interop.WindowNative.GetWindowHandle(owner!), inject);
            status.Text = "Native overlay active. S samples; click and drag the clear hole; Escape closes."; Save();
        }
        catch (Exception error)
        {
            Fail(stage, error);
            var candidate = overlay;
            Retire();
            injectedCleanupAcknowledged = inject && candidate is not null && candidate.DestroyAcknowledged && candidate.Hwnd == 0 && overlay is null;
            Save();
        }
    }
    private void Validate(bool samplingOverlay)
    {
        Require(owner is not null && !ownerClosed && points is not null, "Missing live owner.");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner!);
        Require(Native.ClientBounds(hwnd) == ownerClient && Native.MonitorBounds(hwnd) == monitor && fixture.XamlRoot.RasterizationScale == scale, "Frozen owner/source/scale changed.");
        var location = fixture.TransformToVisual((UIElement)owner!.Content).TransformPoint(new Windows.Foundation.Point());
        Require(source == new Bounds(ownerClient.Left + Pixel(location.X * scale), ownerClient.Top + Pixel(location.Y * scale), ownerClient.Left + Pixel((location.X + fixture.ActualWidth) * scale), ownerClient.Top + Pixel((location.Y + fixture.ActualHeight) * scale)), "Fixture layout changed.");
        Require(points!.All(p => source.Inside(p.X, p.Y) && monitor.Inside(p.X, p.Y)), "Points no longer owned.");
        var foreground = samplingOverlay ? overlay?.Hwnd ?? 0 : hwnd;
        Require(foreground != 0 && Native.GetForegroundWindow() == foreground, "Foreground changed.");
        if (samplingOverlay) Require(overlay is not null && Native.ClientBounds(foreground) == monitor && overlay.Monitor == monitor && overlay.Hole == hole, "Overlay geometry changed.");
    }
    private void Sample()
    {
        string current = phase; attempts++;
        try
        {
            Validate(current == "overlay"); reads++;
            var colors = ScreenPixelProbe.Read(points!); Validate(current == "overlay");
            var livePhase = ticks / 2 % 2;
            if (current == "baseline") { policy.Calibrate(colors, livePhase); if (policy.Calibrated) phase = "idle"; }
            else { policy.Accept(colors, livePhase); if (policy.Passed) phase = "idle"; }
            if (attempts >= 12) phase = "idle";
            verdict = current == "baseline" ? policy.Calibrated ? "calibrated" : "inconclusive calibration"
                : policy.Passed ? "PASS owned fixture pixels only" : phase == "idle" ? "inconclusive overlay samples" : "sampling";
            Append("screen-pixel-samples.jsonl", new { overlayGeneration, run, phase = current, attempt = attempts, ticks, livePhase,
                points = points!.Select(p => new { p.X, p.Y }), colorRefs = colors, reads, verdict });
            Require(!failures.RecordingFailed, "Prior diagnostic recording failed; restart for fresh evidence.");
            status.Text = verdict; Save();
        }
        catch (Exception error) { if (current == "baseline") policy.ResetCalibration(); Fail("known-point sample or recording", error); }
    }
    private void Retire()
    {
        phase = "idle";
        if (overlay is not null && overlay.Close()) overlay = null;
        Save();
    }
    private void NativeChanged(string nativeStage)
    {
        if (overlay is not null)
        {
            lastHolePresses = overlay.HolePresses; lastPointerMoves = overlay.PointerMoves;
            lastPointerReleases = overlay.PointerReleases; lastDestroyAcknowledged = overlay.DestroyAcknowledged;
            if (nativeStage == "cleanup completed") { phase = "idle"; overlay = null; }
        }
        Save();
    }
    private void Fail(string failureStage, Exception error)
    {
        policy.ResetRun(); phase = "idle"; verdict = "inconclusive"; status.Text = "Inconclusive: " + failureStage;
        failures.Record(failureStage, error, (failure, first) => Append("failures.jsonl", new { overlayGeneration, run, failure, first }), () => Save(false));
    }
    private void Append(string file, object value)
    {
        Directory.CreateDirectory(output); File.AppendAllText(Path.Combine(output, file), JsonSerializer.Serialize(value) + Environment.NewLine);
    }
    private void Save(bool recordFailure = true)
    {
        try
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "state.json"), JsonSerializer.Serialize(new { overlayGeneration, run, phase, verdict,
                policy.Passed, policy.Calibrated, attempts, reads, ticks, scale, source, monitor, hole, ownerClient,
                fixturePresses, holePresses = overlay?.HolePresses ?? lastHolePresses, pointerMoves = overlay?.PointerMoves ?? lastPointerMoves, pointerReleases = overlay?.PointerReleases ?? lastPointerReleases,
                nativeHwnd = overlay?.Hwnd.ToInt64(), destroyAcknowledged = overlay?.DestroyAcknowledged ?? lastDestroyAcknowledged,
                injectedCleanupAcknowledged, ownerClosed, timerStopped, failures.First, failures.RecordingFailed }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (recordFailure) { Fail("state recording", error); }
    }
    private static int Pixel(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));
    private static string FindOutput()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs"))) directory = directory.Parent;
        return directory is null ? Path.Combine(ApplicationDataPath(), "composition-overlay-probe")
            : Path.Combine(directory.FullName, "docs", "handoffs", "phase1-figma-native-ui", "composition-overlay-probe", "native-run");
    }
    private static string ApplicationDataPath() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}
