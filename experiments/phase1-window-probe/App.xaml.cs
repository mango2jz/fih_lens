using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Phase1WindowProbe;

public partial class App : Application
{
    private readonly List<Window> owned = [];
    private readonly HashSet<Window> closed = [];
    private readonly string output = Path.GetFullPath(Environment.GetEnvironmentVariable("FIHLENS_WINDOW_PROBE_OUTPUT")
        ?? "docs/handoffs/phase1-figma-native-ui/window-probe");

    public App()
    {
        Directory.CreateDirectory(output);
        UnhandledException += (_, e) => File.WriteAllText(Path.Combine(output, "unhandled.txt"), e.Exception.ToString());
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("FIHLENS_WINDOW_PROBE_MIXED") == "content")
            {
                await RunContent();
                return;
            }
            if (Environment.GetEnvironmentVariable("FIHLENS_WINDOW_PROBE_MIXED") == "activation")
            {
                await RunActivation();
                return;
            }
            if (Environment.GetEnvironmentVariable("FIHLENS_WINDOW_PROBE_MIXED") == "1")
            {
                await RunMixed();
                return;
            }
            var launcher = await Open("E1 isolated launcher", false);
            var second = await Open("E1 independent second session", true);
            var results = new List<object>();
            var first = await Open("E1 compact transient 1", true);
            var firstId = first.AppWindow.Id.Value;
            results.Add(await Measure(first, 325, "compact.png"));
            first.Close();
            await Task.Delay(100);
            Require(closed.Contains(first), "First transient Closed event missing.");
            Require(!closed.Contains(launcher) && !closed.Contains(second), "Closing first transient closed another window.");
            Require(launcher.AppWindow.IsVisible && second.AppWindow.IsVisible, "Surviving windows are not visible.");

            var reopened = await Open("E1 compact transient 2", true);
            results.Add(await Measure(reopened, 325, "reopened.png"));
            results.Add(await Measure(reopened, 569, "expanded.png"));
            Require(firstId != reopened.AppWindow.Id.Value, "Reopen did not create a new top-level window.");
            reopened.Close();
            await Task.Delay(100);
            Require(!closed.Contains(launcher) && !closed.Contains(second), "Reopened transient close damaged other sessions.");
            // Mutating surviving XAML after both transient closes proves roots remain usable.
            ((TextBlock)((StackPanel)((Border)second.Content).Child).Children[0]).Text = "Second session remains usable";
            results.Add(await Measure(second, 325, "second-session-after-close.png"));
            second.Close();
            await Task.Delay(100);
            Require(!closed.Contains(launcher) && launcher.AppWindow.IsVisible, "Second session close stopped launcher.");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                success = true,
                observedUtc = DateTimeOffset.UtcNow,
                operatingSystem = Environment.OSVersion.VersionString,
                dotnet = Environment.Version.ToString(),
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                windowsAppSdkPackage = "2.5.1",
                highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast,
                createdWindows = owned.Count,
                closedTransientsBeforeLauncher = closed.Count,
                launcherAliveUntilFinalCleanup = true,
                windows = results,
                unproved = new[] { "external-app activation/focus restoration", "mixed DPI and live DPI changes", "negative-origin physical monitor", "small/portrait work area", "native border/shadow visual fidelity", "keyboard/Narrator" }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "failure.txt"), e.ToString()); }
        finally
        {
            foreach (var window in owned.AsEnumerable().Reverse())
                if (!closed.Contains(window)) window.Close();
            File.WriteAllText(Path.Combine(output, "cleanup.json"), JsonSerializer.Serialize(new { created = owned.Count, closed = closed.Count, allClosed = owned.Count == closed.Count }));
            if (priorForegroundToRestore != 0)
            {
                var accepted = SetForegroundWindow(priorForegroundToRestore);
                // Last-window close may end the dispatcher; record synchronously before returning.
                File.WriteAllText(Path.Combine(output, "foreground-restoration.json"), JsonSerializer.Serialize(new
                {
                    prior = priorForegroundToRestore.ToInt64(), accepted,
                    observed = GetForegroundWindow().ToInt64(), restored = GetForegroundWindow() == priorForegroundToRestore,
                    allOwnedWindowsClosedBeforeAttempt = owned.Count == closed.Count
                }));
            }
        }
    }

    private async Task<Window> Open(string title, bool compact)
    {
        var window = new Window { Title = title };
        owned.Add(window);
        window.Closed += (_, _) => closed.Add(window);
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "E1 window feasibility fixture", Style = Style("Phase1LabelTextStyle") });
        content.Children.Add(new TextBlock { Text = "Native XAML host — no translation or capture", Style = Style("Phase1TranslationTextStyle") });
        content.Children.Add(new Button { Content = "Native button fixture", Style = Style("Phase1ActionButtonStyle") });
        content.Children.Add(new BitmapIcon { Style = Style("Phase1ScanIconStyle") });
        var root = new Border { Style = Style("Phase1SurfaceStyle"), Child = content, RequestedTheme = ElementTheme.Light };
        window.Content = root;
        if (compact)
        {
            var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }
        var loaded = new TaskCompletionSource();
        RoutedEventHandler handler = (_, _) => loaded.TrySetResult();
        root.Loaded += handler;
        try
        {
            window.Activate();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { root.Loaded -= handler; }
        return window;
    }

    private async Task<object> Measure(Window window, int dipHeight, string screenshot)
    {
        var root = (Border)window.Content;
        var appWindow = window.AppWindow;
        Require(root.XamlRoot.ContentIslandEnvironment.AppWindowId.Value == appWindow.Id.Value, "XAML root not associated with Window.AppWindow.");
        var scale = root.XamlRoot.RasterizationScale;
        var target = new SizeInt32((int)Math.Round(400 * scale), (int)Math.Round(dipHeight * scale));
        appWindow.ResizeClient(target);
        var display = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
        var bounds = display.OuterBounds;
        var localWork = display.WorkArea;
        var desktopWork = new RectInt32(bounds.X + localWork.X, bounds.Y + localWork.Y, localWork.Width, localWork.Height);
        Require(appWindow.Size.Width <= desktopWork.Width && appWindow.Size.Height <= desktopWork.Height, "Fixture window cannot fit current work area.");
        appWindow.Move(new PointInt32(desktopWork.X + (desktopWork.Width - appWindow.Size.Width) / 2,
            desktopWork.Y + (desktopWork.Height - appWindow.Size.Height) / 2));
        await Task.Delay(200);
        root.UpdateLayout();
        Require(appWindow.ClientSize.Width == target.Width && appWindow.ClientSize.Height == target.Height, "ResizeClient did not apply requested physical size.");
        Require(Math.Abs(root.ActualWidth - 400) <= 1 && Math.Abs(root.ActualHeight - dipHeight) <= 1, "Native effective XAML dimensions differ from DIP target.");
        Require(appWindow.Position.X >= desktopWork.X && appWindow.Position.Y >= desktopWork.Y
            && appWindow.Position.X + appWindow.Size.Width <= desktopWork.X + desktopWork.Width
            && appWindow.Position.Y + appWindow.Size.Height <= desktopWork.Y + desktopWork.Height, "Native window outside converted desktop work area.");
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        Require(pixels.Length > 0, "Native root rendering returned no pixels.");
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var file = File.Create(Path.Combine(output, screenshot));
        await stream.AsStreamForRead().CopyToAsync(file);
        var presenter = (OverlappedPresenter)appWindow.Presenter;
        return new
        {
            title = window.Title, appWindowId = appWindow.Id.Value,
            scale, requestedWidthDip = 400, requestedHeightDip = dipHeight,
            actualWidthDip = root.ActualWidth, actualHeightDip = root.ActualHeight,
            positionPhysical = new { appWindow.Position.X, appWindow.Position.Y },
            sizePhysical = new { appWindow.Size.Width, appWindow.Size.Height },
            clientPhysical = new { appWindow.ClientSize.Width, appWindow.ClientSize.Height },
            displayBounds = Rect(bounds), displayRelativeWorkArea = Rect(localWork), desktopWorkArea = Rect(desktopWork),
            presenter.HasBorder, presenter.HasTitleBar, presenter.IsResizable,
            actualTheme = root.ActualTheme.ToString(), surface = ((Microsoft.UI.Xaml.Media.SolidColorBrush)root.Background).Color.ToString(),
            screenshot, renderedWidthPhysical = bitmap.PixelWidth, renderedHeightPhysical = bitmap.PixelHeight,
            xamlRootAssociated = true, withinWorkArea = true
        };
    }

    private Style Style(string key) => Resources[key] as Style ?? throw new InvalidOperationException($"Missing native style: {key}");
    private static object Rect(RectInt32 rect) => new { rect.X, rect.Y, rect.Width, rect.Height };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
