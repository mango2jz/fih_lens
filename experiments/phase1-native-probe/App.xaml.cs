using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Phase1NativeProbe;

public partial class App : Application
{
    private Window? window;
    private readonly string output = Path.GetFullPath(Environment.GetEnvironmentVariable("FIHLENS_PROBE_OUTPUT")
        ?? "docs/handoffs/phase1-figma-native-ui/native-resource-probe");

    public App()
    {
        Directory.CreateDirectory(output);
        UnhandledException += (_, e) => File.WriteAllText(Path.Combine(output, "unhandled.txt"), e.Exception.ToString());
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!MatchesTint([24, 24, 24, 239], 3, Windows.UI.Color.FromArgb(255, 26, 26, 26))
            || !MatchesTint([230, 230, 230, 239], 3, Windows.UI.Color.FromArgb(255, 245, 245, 245))
            || MatchesTint([24, 24, 24, 239], 3, Windows.UI.Color.FromArgb(255, 245, 245, 245))
            || MatchesTint([0, 0, 0, 0], 3, Windows.UI.Color.FromArgb(255, 26, 26, 26)))
            throw new InvalidOperationException("Premultiplied tint self-check failed.");
        window = new Window { Title = "FIH Lens — isolated native resource probe" };
        var panel = new StackPanel { Spacing = 8, Width = 600 };
        var requireScale200 = Environment.GetEnvironmentVariable("FIHLENS_PROBE_REQUIRE_SCALE200") == "1";
        DisplayArea? targetDisplay = null;
        if (requireScale200)
        {
            var displays = DisplayArea.FindAll();
            var nonprimaryCount = 0;
            for (var index = 0; index < displays.Count; index++)
                if (!displays[index].IsPrimary) { targetDisplay = displays[index]; nonprimaryCount++; }
            if (nonprimaryCount != 1 || targetDisplay is null) throw new InvalidOperationException("200% probe requires exactly one active nonprimary display.");
            if (targetDisplay.OuterBounds.Width != 1920 || targetDisplay.OuterBounds.Height != 1200)
                throw new InvalidOperationException("Unique nonprimary display does not match the observed 1920x1200 fixture display.");
            window.AppWindow.Move(new PointInt32(targetDisplay.OuterBounds.X + 20, targetDisplay.OuterBounds.Y + 20));
        }
        window.Content = panel;
        panel.Loaded += async (_, _) =>
        {
            try
            {
                if (requireScale200)
                {
                    for (var attempt = 0; panel.XamlRoot.RasterizationScale != 2 && attempt < 60; attempt++) await Task.Delay(50);
                    if (panel.XamlRoot.RasterizationScale != 2) throw new InvalidOperationException("Selected display did not provide actual XamlRoot scale 2.0.");
                    window.AppWindow.ResizeClient(new SizeInt32(1200, 960));
                    var work = targetDisplay!.WorkArea;
                    var bounds = targetDisplay.OuterBounds;
                    var size = window.AppWindow.Size;
                    if (size.Width > work.Width || size.Height > work.Height) throw new InvalidOperationException("Native gallery cannot fit selected display work area.");
                    if (work.X < bounds.X || work.Y < bounds.Y || work.X + work.Width > bounds.X + bounds.Width || work.Y + work.Height > bounds.Y + bounds.Height)
                        throw new InvalidOperationException("Native work area is not desktop-relative within the selected display.");
                    // Actual SDK 2.5.1 reports secondary WorkArea in desktop coordinates.
                    window.AppWindow.Move(new PointInt32(work.X + Math.Clamp(20, 0, work.Width - size.Width), work.Y + Math.Clamp(20, 0, work.Height - size.Height)));
                }
                var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
                if (Environment.GetEnvironmentVariable("FIHLENS_PROBE_REQUIRE_HIGH_CONTRAST") == "1" && !highContrast)
                    throw new InvalidOperationException("Genuine Windows high contrast is required for this run.");
                var systemWindow = SystemColor(5);
                var systemWindowText = SystemColor(8);
                var systemHighlight = SystemColor(13);
                var results = new List<object>();
                var surface = new Border { Style = Style("Phase1SurfaceStyle") };
                var content = new StackPanel { Spacing = 8 };
                surface.Child = content;
                panel.Children.Add(surface);
                foreach (var kind in new[] { "Title", "Translation", "Body", "Caption", "Label" })
                    content.Children.Add(new TextBlock { Text = $"{kind}: FIH Lens • Tiếng Việt", Style = Style($"Phase1{kind}TextStyle") });
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var kind in new[] { "Action", "Primary", "Ghost" })
                    buttons.Children.Add(new Button { Content = kind, Style = Style($"Phase1{kind}ButtonStyle") });
                content.Children.Add(buttons);
                foreach (var kind in new[] { "", "Success", "Error" })
                    content.Children.Add(new Border { Style = Style($"Phase1{kind}StatusStyle"), Child = new TextBlock { Text = $"{kind} status", Style = Style("Phase1BodyTextStyle") } });
                var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                foreach (var kind in new[] { "Close", "Copy", "Bookmark", "ChevronDown", "ChevronUp", "Check", "Error", "Retry", "Scan" })
                {
                    var icon = new BitmapIcon { Style = Style($"Phase1{kind}IconStyle") };
                    icons.Children.Add(icon);
                }
                content.Children.Add(icons);
                var scale100References = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                var scale200References = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                if (requireScale200)
                {
                    var referenceOverlay = new Grid();
                    referenceOverlay.Children.Add(scale100References);
                    referenceOverlay.Children.Add(scale200References);
                    content.Children.Add(referenceOverlay);
                    foreach (BitmapIcon original in icons.Children)
                    {
                        var name = Path.GetFileName(original.UriSource.AbsolutePath);
                        var kind = Path.GetFileNameWithoutExtension(name);
                        scale100References.Children.Add(new BitmapIcon { Style = Style($"Phase1{kind}IconStyle"), UriSource = new Uri($"ms-appx:///Assets/Scale100Reference/{name}") });
                        scale200References.Children.Add(new BitmapIcon { Style = Style($"Phase1{kind}IconStyle"), UriSource = new Uri($"ms-appx:///Assets/Scale200Reference/{name}") });
                    }
                }
                var svgSource = new SvgImageSource();
                var svgOpened = false;
                string? svgError = null;
                svgSource.Opened += (_, _) => svgOpened = true;
                svgSource.OpenFailed += (_, e) => svgError = e.Status.ToString();
                svgSource.UriSource = new Uri("ms-appx:///Assets/Phase1/Close.svg");
                var svgImage = new Image { Source = svgSource, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
                content.Children.Add(new TextBlock { Text = "Original SVG: fixed source stroke", Style = Style("Phase1CaptionTextStyle") });
                content.Children.Add(svgImage);
                var transitions = new[] { ElementTheme.Light, ElementTheme.Dark, ElementTheme.Light };
                for (var transition = 0; transition < transitions.Length; transition++)
                {
                    var theme = transitions[transition];
                    panel.RequestedTheme = theme;
                    panel.UpdateLayout();
                    await Task.Delay(250);
                    if (requireScale200 && (panel.XamlRoot.RasterizationScale != 2 || DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.None)?.DisplayId.Value != targetDisplay!.DisplayId.Value))
                        throw new InvalidOperationException($"Native DPI/display changed during the 200% run: scale={panel.XamlRoot.RasterizationScale}, target={targetDisplay!.DisplayId.Value}, actual={DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.None)?.DisplayId.Value}, outer=({targetDisplay.OuterBounds.X},{targetDisplay.OuterBounds.Y},{targetDisplay.OuterBounds.Width},{targetDisplay.OuterBounds.Height}), work=({targetDisplay.WorkArea.X},{targetDisplay.WorkArea.Y},{targetDisplay.WorkArea.Width},{targetDisplay.WorkArea.Height}), position=({window.AppWindow.Position.X},{window.AppWindow.Position.Y}), size=({window.AppWindow.Size.Width},{window.AppWindow.Size.Height})");
                    if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast != highContrast)
                        throw new InvalidOperationException("Windows high-contrast mode changed during this run.");
                    if (!svgOpened || svgError is not null) throw new InvalidOperationException($"Original SVG did not load: {svgError}");
                    if (panel.ActualTheme != theme) throw new InvalidOperationException("Native root did not switch theme.");
                    var filename = transition == 2 ? "light-return.png" : $"{theme.ToString().ToLowerInvariant()}.png";
                    var backgroundPixel = await Render(panel, filename);
                    var expectedSurface = ((SolidColorBrush)surface.Background).Color;
                    var requiredSurface = highContrast ? systemWindow.ToString() : (theme == ElementTheme.Light ? "#FFFAFAFA" : "#FF2C2C2C");
                    if (expectedSurface.ToString() != requiredSurface) throw new InvalidOperationException("Retained surface did not resolve the new theme resource.");
                    if (backgroundPixel != expectedSurface.ToString()) throw new InvalidOperationException($"Rendered background {backgroundPixel} differs from native brush {expectedSurface}");
                    var iconResults = new List<object>();
                    foreach (BitmapIcon icon in icons.Children)
                    {
                        var bitmap = new RenderTargetBitmap();
                        await bitmap.RenderAsync(icon);
                        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                        var visible = 0;
                        var antialiased = 0;
                        var foregroundPixels = 0;
                        var meaningfulCoveragePixels = 0;
                        var tintedCoveragePixels = 0;
                        var expectedColor = ((SolidColorBrush)icon.Foreground).Color;
                        var requiredColor = icon.UriSource.AbsolutePath.EndsWith("/Scan.png", StringComparison.Ordinal)
                            ? (highContrast ? systemHighlight.ToString() : (theme == ElementTheme.Light ? "#FF005FB8" : "#FF60CDFF"))
                            : (highContrast ? systemWindowText.ToString() : (theme == ElementTheme.Light ? "#FF1A1A1A" : "#FFF5F5F5"));
                        if (expectedColor.ToString() != requiredColor) throw new InvalidOperationException($"Retained icon did not switch theme: {icon.UriSource}");
                        for (var i = 3; i < pixels.Length; i += 4)
                        {
                            if (pixels[i] > 0) visible++;
                            if (pixels[i] > 0 && pixels[i] < 255) antialiased++;
                            if (pixels[i] == 255 && pixels[i - 1] == expectedColor.R && pixels[i - 2] == expectedColor.G && pixels[i - 3] == expectedColor.B) foregroundPixels++;
                            if (pixels[i] >= 64) meaningfulCoveragePixels++;
                            if (MatchesTint(pixels, i, expectedColor)) tintedCoveragePixels++;
                        }
                        if (visible == 0 || antialiased == 0 || meaningfulCoveragePixels == 0 || tintedCoveragePixels != meaningfulCoveragePixels || visible == pixels.Length / 4) throw new InvalidOperationException($"Empty, opaque, untinted or un-antialiased native icon: {icon.UriSource}");
                        var selected200 = false;
                        var differsFrom100 = false;
                        if (requireScale200)
                        {
                            var index = icons.Children.IndexOf(icon);
                            var reference200 = new RenderTargetBitmap();
                            var reference100 = new RenderTargetBitmap();
                            await reference200.RenderAsync(scale200References.Children[index]);
                            await reference100.RenderAsync(scale100References.Children[index]);
                            var reference200Pixels = (await reference200.GetPixelsAsync()).ToArray();
                            var reference100Pixels = (await reference100.GetPixelsAsync()).ToArray();
                            selected200 = pixels.AsSpan().SequenceEqual(reference200Pixels);
                            differsFrom100 = !pixels.AsSpan().SequenceEqual(reference100Pixels);
                            if (bitmap.PixelWidth != 40 || bitmap.PixelHeight != 40 || !selected200 || !differsFrom100) throw new InvalidOperationException($"Qualified 200% original artwork selection failed: {icon.UriSource}");
                        }
                        iconResults.Add(new { uri = icon.UriSource.ToString(), visiblePixels = visible, alphaEdgePixels = antialiased, foregroundPixels, meaningfulCoveragePixels, tintedCoveragePixels, foreground = expectedColor.ToString(), nativePixelWidth = bitmap.PixelWidth, nativePixelHeight = bitmap.PixelHeight, selected200, differsFrom100 });
                    }
                    var svgBitmap = new RenderTargetBitmap();
                    await svgBitmap.RenderAsync(svgImage);
                    var svgPixels = (await svgBitmap.GetPixelsAsync()).ToArray();
                    var fixedStrokePixels = 0;
                    for (var i = 3; i < svgPixels.Length; i += 4)
                        if (svgPixels[i] == 255 && svgPixels[i - 1] == 26 && svgPixels[i - 2] == 26 && svgPixels[i - 3] == 26) fixedStrokePixels++;
                    if (fixedStrokePixels == 0) throw new InvalidOperationException("Original SVG did not render its fixed #1A1A1A stroke.");
                    results.Add(new { transition, screenshot = filename, requestedTheme = theme.ToString(), actualTheme = panel.ActualTheme.ToString(), retainedControls = true, rasterizationScale = panel.XamlRoot.RasterizationScale, width = panel.ActualWidth, height = panel.ActualHeight, surface = expectedSurface.ToString(), renderedSurfacePixel = backgroundPixel, icons = iconResults, originalSvg = new { opened = svgOpened, fixedStrokePixels, stroke = "#FF1A1A1A", followsTheme = false } });
                }
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { success = true, highContrast, requireScale200, targetDisplay = targetDisplay is null ? null : new { id = targetDisplay.DisplayId.Value, targetDisplay.IsPrimary, outer = new { targetDisplay.OuterBounds.X, targetDisplay.OuterBounds.Y, targetDisplay.OuterBounds.Width, targetDisplay.OuterBounds.Height }, work = new { targetDisplay.WorkArea.X, targetDisplay.WorkArea.Y, targetDisplay.WorkArea.Width, targetDisplay.WorkArea.Height }, windowPosition = new { window.AppWindow.Position.X, window.AppWindow.Position.Y }, windowSize = new { window.AppWindow.Size.Width, window.AppWindow.Size.Height } }, systemPalette = new { window = systemWindow.ToString(), windowText = systemWindowText.ToString(), highlight = systemHighlight.ToString() }, themes = results }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "failure.txt"), e.ToString()); }
            finally { window.Close(); }
        };
        window.Activate();
    }

    private Style Style(string key) => Resources[key] as Style ?? throw new InvalidOperationException($"Missing native style: {key}");

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetSysColor(int index);

    private static Windows.UI.Color SystemColor(int index)
    {
        var colorRef = GetSysColor(index);
        return Windows.UI.Color.FromArgb(255, (byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16));
    }

    // A thin antialiased source can have no alpha-255 pixels at its native scale.
    private static bool MatchesTint(byte[] pixels, int alphaIndex, Windows.UI.Color color)
    {
        var alpha = pixels[alphaIndex];
        return alpha >= 64
            && Math.Abs(pixels[alphaIndex - 1] - color.R * alpha / 255.0) <= 1
            && Math.Abs(pixels[alphaIndex - 2] - color.G * alpha / 255.0) <= 1
            && Math.Abs(pixels[alphaIndex - 3] - color.B * alpha / 255.0) <= 1;
    }

    private async Task<string> Render(UIElement element, string filename)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        if (pixels.Length == 0) throw new InvalidOperationException("Native render returned no pixels.");
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var file = File.Create(Path.Combine(output, filename));
        await stream.AsStreamForRead().CopyToAsync(file);
        var offset = ((2 * bitmap.PixelWidth) + bitmap.PixelWidth / 2) * 4;
        return $"#{pixels[offset + 3]:X2}{pixels[offset + 2]:X2}{pixels[offset + 1]:X2}{pixels[offset]:X2}";
    }
}
