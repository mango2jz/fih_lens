using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;

namespace Phase1NativeCapture;

public partial class App : Application
{
    private Window? window;
    private CancellationTokenSource? operation;
    private SoftwareBitmap? currentCrop;
    private string? currentOcr;
    private readonly TextBlock status = new() { Text = "Ready. Capture only this harmless fixture after authorization.", TextWrapping = TextWrapping.Wrap };
    private readonly Border fixture = new() { Width = 480, Height = 200, Background = Brush(255, 255, 255) };
    private readonly Button capture = new() { Content = "Capture fixture" };
    private readonly Button ocr = new() { Content = "OCR fixture", IsEnabled = false };
    private readonly Button copy = new() { Content = "Copy fixture result", IsEnabled = false };
    private readonly Button checks = new() { Content = "Run bounded checks (20 cycles)", IsEnabled = false };
    private readonly Button overlayCheck = new() { Content = "Check overlay retirement", IsEnabled = false };
    private readonly Button timestampCheck = new() { Content = "R7: Check timestamp-gated retirement" };
    private readonly TextBlock recognized = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly string output = EvidenceDirectory();

    public App() { InitializeComponent(); }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        BoundsSelfCheck();
        var canvas = new Canvas();
        fixture.Child = canvas;
        var text = new TextBlock { Text = "FIH LENS CAPTURE TEST\nThe quick brown fox has 42 books.", FontSize = 24, Foreground = Brush(0, 0, 0) };
        Canvas.SetLeft(text, 40); Canvas.SetTop(text, 60); canvas.Children.Add(text);
        AddMarker(canvas, 8, 8, Brush(255, 0, 0));
        AddMarker(canvas, 456, 176, Brush(0, 0, 255));
        var cancel = new Button { Content = "Cancel operation" };
        cancel.Click += (_, _) => operation?.Cancel();
        capture.Click += Capture;
        ocr.Click += Ocr;
        copy.Click += Copy;
        checks.Click += RunChecks;
        overlayCheck.Click += CheckOverlayRetirement;
        timestampCheck.Click += CheckTimestampRetirement;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { capture, ocr, cancel } };
        window = new Window { Title = "FIH Lens — owned capture fixture", Content = new StackPanel { RequestedTheme = ElementTheme.Light, Padding = new Thickness(24), Spacing = 16, Background = Brush(220, 220, 220), Children = { fixture, actions, copy, checks, overlayCheck, timestampCheck, status, recognized } } };
        window.AppWindow.ResizeClient(new SizeInt32(700, 880));
        window.Closed += (_, _) => { operation?.Cancel(); currentCrop?.Dispose(); currentCrop = null; currentOcr = null; window = null; };
        window.Activate();
    }

    private async void Capture(object sender, RoutedEventArgs args)
    {
        if (operation is not null || window is null) return;
        currentCrop?.Dispose(); currentCrop = null; currentOcr = null; recognized.Text = "";
        var owner = window;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        UpdateActions();
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            var monitor = NativeCapture.MonitorFromWindow(hwnd, 0);
            var point = new NativeCapture.Point(0, 0);
            if (monitor == 0 || !NativeCapture.ClientToScreen(hwnd, ref point)) throw new InvalidOperationException("Fixture monitor/client origin unavailable.");
            var topLeft = fixture.TransformToVisual((UIElement)owner.Content).TransformPoint(new Windows.Foundation.Point());
            var scale = fixture.XamlRoot.RasterizationScale;
            var crop = new NativeCapture.Rect(point.X + checked((int)Math.Round(topLeft.X * scale)), point.Y + checked((int)Math.Round(topLeft.Y * scale)),
                point.X + checked((int)Math.Round((topLeft.X + fixture.ActualWidth) * scale)), point.Y + checked((int)Math.Round((topLeft.Y + fixture.ActualHeight) * scale)));
            status.Text = "Capturing owned fixture…";
            using var bitmap = await NativeCapture.CropAsync(monitor, crop, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (window != owner) return;
            Require(bitmap.PixelWidth == crop.Width && bitmap.PixelHeight == crop.Height, "Crop dimensions mismatch.");
            CheckMarkers(bitmap, scale);
            currentCrop = SoftwareBitmap.Copy(bitmap);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "capture-result.json"), JsonSerializer.Serialize(new
            {
                success = true, observedUtc = DateTimeOffset.UtcNow, operatingSystem = Environment.OSVersion.VersionString,
                dotnet = Environment.Version.ToString(), sdk = "2.5.1", identity = Windows.ApplicationModel.Package.Current.Id.FullName,
                scale, width = bitmap.PixelWidth, height = bitmap.PixelHeight, bitmap.BitmapPixelFormat, bitmap.BitmapAlphaMode,
                markerAssertions = 3, pixelPersistence = false, providerCalls = 0,
                unproved = new[] { "overlay exclusion", "monitor edges", "negative/mixed-DPI capture", "OCR/clipboard", "cancellation races/repeated resource cycles" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = "PASS: exact crop and three owned fixture pixel assertions. No image saved.";

        }
        catch (OperationCanceledException) { if (window == owner) status.Text = cancellation.IsCancellationRequested ? "Canceled; no publication." : "Frame timeout; no publication."; }
        catch (Exception e)
        {
            // Persist only type/HRESULT; exception messages may contain native/private source details.
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "capture-failure.json"), JsonSerializer.Serialize(new { type = e.GetType().FullName, hresult = e.HResult, observedUtc = DateTimeOffset.UtcNow }));
            if (window == owner) status.Text = $"Capture failed: {e.GetType().Name} (0x{e.HResult:X8}).";
        }
        finally { operation = null; if (window == owner) UpdateActions(); }
    }

    private async void Ocr(object sender, RoutedEventArgs args)
    {
        if (operation is not null || window is null || currentCrop is null) return;
        var owner = window;
        using var crop = SoftwareBitmap.Copy(currentCrop);
        using var cancellation = new CancellationTokenSource();
        operation = cancellation; currentOcr = null; recognized.Text = ""; UpdateActions();
        try
        {
            status.Text = "Recognizing known fixture locally…";
            // Await native OCR to drain before releasing its bitmap, even after cancellation/close.
            var result = await FixtureOcr.RecognizeAsync(crop, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (window != owner) return;
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "ocr-result.json"), JsonSerializer.Serialize(new
            {
                success = true, observedUtc = DateTimeOffset.UtcNow, identity = Windows.ApplicationModel.Package.Current.Id.FullName,
                result.AvailableLanguages, result.RecognizerLanguage, result.MaxImageDimension, result.BlankIsEmpty,
                crop.BitmapPixelFormat, crop.BitmapAlphaMode, width = crop.PixelWidth, height = crop.PixelHeight,
                knownEnglishAssertions = 4, oversizeRejectedBeforeAllocation = true, syntheticMissingRecognizerRejected = true,
                syntheticUnavailableLanguageSelection = true, precanceledTokenRejected = true, providerCalls = 0,
                recognizedTextPersisted = false, pixelPersistence = false,
                unproved = new[] { "Vietnamese OCR", "native missing-recognizer installation", "late native OCR cancel/close races", "clipboard external-editor persistence" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            currentOcr = result.Text; recognized.Text = result.Text;
            status.Text = "PASS: English fixture, 42/books, blank image and input guards. Copy remains explicit.";
        }
        catch (OperationCanceledException) { if (window == owner) status.Text = "OCR canceled; late output discarded."; }
        catch (Exception e) { RecordFailure("ocr", e); if (window == owner) status.Text = $"OCR failed: {e.GetType().Name} (0x{e.HResult:X8})."; }
        finally { operation = null; if (window == owner) UpdateActions(); }
    }

    private void Copy(object sender, RoutedEventArgs args)
    {
        if (operation is not null || window is null || string.IsNullOrWhiteSpace(currentOcr)) return;
        try
        {
            Require(window.DispatcherQueue.HasThreadAccess, "Clipboard action must use UI thread.");
            var package = new DataPackage(); package.SetText(currentOcr);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "clipboard-result.json"), JsonSerializer.Serialize(new
            {
                success = true, observedUtc = DateTimeOffset.UtcNow, uiThread = true, flushed = true,
                textLength = currentOcr.Length, clipboardRead = false, externalEditorVerified = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = "Copied and flushed. External-editor/persistence verification still required.";
        }
        catch (Exception e) { RecordFailure("clipboard", e); status.Text = $"Copy failed: {e.GetType().Name} (0x{e.HResult:X8}); success not confirmed."; }
    }

    private void UpdateActions()
    {
        capture.IsEnabled = operation is null;
        ocr.IsEnabled = operation is null && currentCrop is not null;
        copy.IsEnabled = operation is null && !string.IsNullOrWhiteSpace(currentOcr);
        checks.IsEnabled = operation is null && currentCrop is not null;
        overlayCheck.IsEnabled = operation is null && currentCrop is not null;
        timestampCheck.IsEnabled = operation is null;
    }

    private void RecordFailure(string stage, Exception error)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, stage + "-failure.json"), JsonSerializer.Serialize(new { type = error.GetType().FullName, hresult = error.HResult, observedUtc = DateTimeOffset.UtcNow }));
    }

    private static void BoundsSelfCheck()
    {
        var monitor = new NativeCapture.Rect(-1920, 0, 0, 1200);
        NativeCapture.ValidateCrop(monitor, new(-1920, 0, -1900, 20));
        foreach (var invalid in new[] { new NativeCapture.Rect(-1, 0, 1, 20), new(-20, 0, -20, 10), new(-1921, 0, -1900, 10) })
        {
            try { NativeCapture.ValidateCrop(monitor, invalid); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Invalid crop was accepted.");
        }
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));
    private static void AddMarker(Canvas canvas, double x, double y, Brush brush)
    {
        var marker = new Border { Width = 16, Height = 16, Background = brush };
        Canvas.SetLeft(marker, x); Canvas.SetTop(marker, y); canvas.Children.Add(marker);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static string EvidenceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "docs/ARCHITECTURE.md")))
                return Path.Combine(directory.FullName, "docs/handoffs/phase1-figma-native-ui/capture-probe/runtime-r6");
        throw new InvalidOperationException("Run this bounded experiment from its repository layout.");
    }
}
