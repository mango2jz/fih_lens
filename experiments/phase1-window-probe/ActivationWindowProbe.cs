using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Phase1WindowProbe;

public partial class App
{
    private async Task RunActivation()
    {
        var prior = GetForegroundWindow();
        priorForegroundToRestore = prior;
        Require(prior != 0, "No separate foreground fixture available.");
        Require(GetWindowRect(prior, out var reader), "No separate foreground fixture rectangle available.");
        var launcher = await Open("E1 activation launcher — click Open E1 transient", false);
        var second = await Open("E1 activation independent session", true);
        var clicked = new TaskCompletionSource<nint>();
        var button = new Button { Content = "Open E1 transient", Style = Style("Phase1PrimaryButtonStyle") };
        RoutedEventHandler click = (_, _) => clicked.TrySetResult(GetForegroundWindow());
        button.Click += click;
        ((StackPanel)((Border)launcher.Content).Child).Children.Add(button);
        File.WriteAllText(Path.Combine(output, "ready-for-launcher-click.json"), JsonSerializer.Serialize(new
        {
            launcher = WinRT.Interop.WindowNative.GetWindowHandle(launcher).ToInt64(), prior = prior.ToInt64(),
            button = "Open E1 transient", timeoutSeconds = 120
        }));
        nint clickForeground;
        try { clickForeground = await clicked.Task.WaitAsync(TimeSpan.FromSeconds(120)); }
        finally { button.Click -= click; }
        Require(clickForeground == WinRT.Interop.WindowNative.GetWindowHandle(launcher), "Launch click did not originate from genuinely foreground own launcher.");
        // Window.Activate runs synchronously from the real launcher click continuation.
        var transient = await Open("E1 activation compact transient", true);
        var firstId = transient.AppWindow.Id.Value;
        var results = new List<object>();
        var activation = new List<object>();
        results.Add(await Measure(transient, 325, "activated-compact.png"));
        activation.Add(AssertActivation(transient, prior, reader));
        results.Add(await Measure(transient, 569, "activated-expanded.png"));
        activation.Add(AssertActivation(transient, prior, reader));
        transient.Close();
        await Task.Delay(100);
        Require(closed.Contains(transient) && !closed.Contains(second) && !closed.Contains(launcher), "Activated transient close damaged surviving windows.");
        var reopened = await Open("E1 activation reopened transient", true);
        Require(firstId != reopened.AppWindow.Id.Value, "Activated reopen reused closed native identity.");
        results.Add(await Measure(reopened, 325, "activated-reopened.png"));
        activation.Add(AssertActivation(reopened, prior, reader));
        reopened.Close();
        await Task.Delay(100);
        Require(!closed.Contains(second) && second.AppWindow.IsVisible && !closed.Contains(launcher) && launcher.AppWindow.IsVisible, "Activated reopen close did not preserve independent windows.");
        results.Add(await Measure(second, 325, "independent-after-activation-close.png"));
        second.Close();
        await Task.Delay(100);
        Require(!closed.Contains(launcher) && launcher.AppWindow.IsVisible, "Activated independent close stopped launcher.");
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            success = true, observedUtc = DateTimeOffset.UtcNow, operatingSystem = Environment.OSVersion.VersionString,
            dotnet = Environment.Version.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            windowsAppSdkPackage = "2.5.1", highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast,
            createdWindows = owned.Count, closedTransientsBeforeLauncher = closed.Count, launcherAliveUntilFinalCleanup = true,
            priorForeground = prior.ToInt64(), priorBounds = NativeRect(reader), realLauncherClickForeground = clickForeground.ToInt64(),
            windows = results, activation,
            restorationEvidence = "foreground-restoration.json: one best-effort attempt after all owned windows close",
            unproved = new[] { "background process may not force foreground", "desktop screenshot/native shadow fidelity", "negative-origin physical monitor", "keyboard/Narrator" }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private object AssertActivation(Window window, nint prior, NativeBounds reader)
    {
        var own = WinRT.Interop.WindowNative.GetWindowHandle(window);
        Require(GetWindowRect(own, out var rect), "Activated own window bounds unavailable.");
        var foreground = GetForegroundWindow();
        var overlap = rect.Left < reader.Right && rect.Right > reader.Left && rect.Top < reader.Bottom && rect.Bottom > reader.Top;
        Require(foreground == own, "Native Window.Activate did not acquire foreground after genuine launcher input.");
        Require(overlap, "Activated own window did not overlap prior separate-app fixture bounds.");
        return new { own = own.ToInt64(), foreground = foreground.ToInt64(), prior = prior.ToInt64(), nativeBounds = NativeRect(rect), foregroundOwn = true, overlapsPriorForegroundBounds = true };
    }
}
