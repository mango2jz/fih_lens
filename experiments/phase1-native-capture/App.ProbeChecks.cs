using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Windows.Graphics.Imaging;

namespace Phase1NativeCapture;

public partial class App
{
    private async void RunChecks(object sender, RoutedEventArgs args)
    {
        if (operation is not null || window is null || currentCrop is null) return;
        var owner = window;
        using var originalCrop = SoftwareBitmap.Copy(currentCrop);
        using var cancellation = new CancellationTokenSource();
        operation = cancellation; currentOcr = null; recognized.Text = ""; UpdateActions();
        var cycles = new List<object>();
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            var monitor = NativeCapture.MonitorFromWindow(hwnd, 0);
            var point = new NativeCapture.Point();
            Require(NativeCapture.ClientToScreen(hwnd, ref point), "Fixture origin unavailable.");
            var top = fixture.TransformToVisual((UIElement)owner.Content).TransformPoint(new Windows.Foundation.Point());
            var scale = fixture.XamlRoot.RasterizationScale;
            var crop = new NativeCapture.Rect(point.X + checked((int)Math.Round(top.X * scale)), point.Y + checked((int)Math.Round(top.Y * scale)),
                point.X + checked((int)Math.Round((top.X + fixture.ActualWidth) * scale)), point.Y + checked((int)Math.Round((top.Y + fixture.ActualHeight) * scale)));
            for (var index = 0; index < 20; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var cancelBeforeStart = index % 2 == 0;
                using var cycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                var trace = new NativeCapture.ProbeTrace();
                var canceled = false;
                status.Text = $"Bounded native cycle {index + 1}/20: {(cancelBeforeStart ? "cancel before start" : "owned crop")}.";
                try
                {
                    using var bitmap = await NativeCapture.CropAsync(monitor, crop, cycleCancellation.Token, trace, cancelBeforeStart ? cycleCancellation.Cancel : null);
                    Require(!cancelBeforeStart, "Before-start cancellation published a bitmap.");
                    Require(bitmap.PixelWidth == crop.Width && bitmap.PixelHeight == crop.Height, "Cycle crop dimensions mismatch.");
                    CheckMarkers(bitmap, scale);
                }
                catch (OperationCanceledException) when (cancelBeforeStart && !cancellation.IsCancellationRequested) { canceled = true; }
                Require(trace.OwnerScopesExited && trace.DevicesCreated == 1 && trace.PoolsCreated == 1 && trace.SessionsCreated == 1, "Native owner scopes incomplete.");
                Require(trace.SubscriptionsAdded == trace.SubscriptionsRemoved && trace.FramesAcquired == trace.FramesDisposed && trace.CopiesStarted == trace.CopiesDrained, "Owned native work did not balance.");
                Require(cancelBeforeStart ? canceled && trace.SessionStarts == 0 && trace.FramesAcquired == 0 : trace.SessionStarts == 1 && trace.CopiesStarted == 1, "Cycle path mismatch.");
                using var process = Process.GetCurrentProcess(); process.Refresh();
                cycles.Add(new { cycle = index + 1, warmupSample = index < 4, mode = cancelBeforeStart ? "native resources created; canceled before StartCapture" : "real owned fixture capture",
                    canceled, trace.OwnerScopesExited, trace.DevicesCreated, trace.PoolsCreated, trace.SessionsCreated, trace.SessionStarts,
                    trace.FramesAcquired, trace.FramesDisposed, trace.CallbacksEntered, trace.CallbacksExited, trace.SubscriptionsAdded, trace.SubscriptionsRemoved,
                    trace.CopiesStarted, trace.CopiesDrained, process.HandleCount, process.PrivateMemorySize64 });
            }
            var lateCancel = await CheckLateOcr(originalCrop, false, cancellation.Token);
            var lateClose = await CheckLateOcr(originalCrop, true, cancellation.Token);
            var activationFailure = await CheckLateOcr(originalCrop, true, cancellation.Token, failActivation: true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (window != owner) return;
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "bounded-checks-result.json"), JsonSerializer.Serialize(new
            {
                success = true, observedUtc = DateTimeOffset.UtcNow, identity = Windows.ApplicationModel.Package.Current.Id.FullName,
                nativeCycles = cycles, actualCaptureCycles = 10, nativeBeforeStartCancelCycles = 10, lateCancel, lateClose, activationFailure,
                providerCalls = 0, pixelPersistence = false, recognizedTextPersisted = false,
                limitations = new[] { "Owner counters prove managed using-scope exit, not every OS/GPU allocation release", "Memory/handle samples are observations without a universal leak threshold",
                    "Callbacks may already be queued when event unsubscribe occurs; retired gate prevents them touching disposed frame/pool", "OCR native in-flight status is reported, never assumed",
                    "No real topology change, production overlay, monitor edges, native itemClosed/device-loss/timeout or clipboard failure proof", "No actual negative-origin/mixed-DPI hardware" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = "PASS: bounded cycles, late OCR cancel/close and injected activation-failure cleanup. See trace limits.";
        }
        catch (OperationCanceledException) { if (window == owner) status.Text = "Bounded checks canceled; no success published."; }
        catch (Exception e) { RecordFailure("bounded-checks", e); if (window == owner) status.Text = $"Bounded checks failed: {e.GetType().Name} (0x{e.HResult:X8})."; }
        finally { operation = null; if (window == owner) UpdateActions(); }
    }

    private sealed class InjectedActivationFailure : Exception { }

    private static async Task<object> CheckLateOcr(SoftwareBitmap source, bool closeWindow, CancellationToken outerCancellation, bool failActivation = false)
    {
        using var bitmap = SoftwareBitmap.Copy(source);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        Window? child = null;
        var childClosed = false;
        var nativeStarted = false;
        var nativeDrained = false;
        var statusAtCancellation = "not started";
        var outputPublished = false;
        var handlerSubscribed = false;
        var handlerDetached = false;
        var handlerRetired = false;
        var activationFailureObserved = false;
        var cleanupCloseRequested = false;
        void ChildClosed(object sender, WindowEventArgs args)
        {
            childClosed = true;
            if (!handlerRetired) cancellation.Cancel();
        }
        try
        {
            if (closeWindow)
            {
                child = new Window { Title = "FIH Lens — owned OCR close check", Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Harmless OCR close-race fixture" } };
                child.Closed += ChildClosed;
                handlerSubscribed = true;
                if (failActivation) throw new InjectedActivationFailure();
                child.Activate();
            }
            try
            {
                _ = await FixtureOcr.RecognizeAsync(bitmap, cancellation.Token, nativeStatus =>
                {
                    nativeStarted = true; statusAtCancellation = nativeStatus.ToString();
                    if (child is not null) child.Close(); else cancellation.Cancel();
                }, () => nativeDrained = true);
                outputPublished = true;
            }
            catch (OperationCanceledException) when (!outerCancellation.IsCancellationRequested) { }
            outerCancellation.ThrowIfCancellationRequested();
            Require(nativeStarted && nativeDrained && !outputPublished && cancellation.IsCancellationRequested, "Late native OCR output escaped cancellation.");
            Require(!closeWindow || childClosed, "Owned OCR check window did not close.");
        }
        catch (InjectedActivationFailure) when (failActivation) { activationFailureObserved = true; }
        finally
        {
            try
            {
                if (child is not null && !childClosed) { cleanupCloseRequested = true; child.Close(); }
            }
            finally
            {
                // Retire before detaching so an already-queued event cannot touch the disposed CTS.
                handlerRetired = true;
                if (child is not null && handlerSubscribed) { child.Closed -= ChildClosed; handlerDetached = true; }
            }
        }
        if (failActivation)
            Require(activationFailureObserved && cleanupCloseRequested && childClosed && handlerDetached && !nativeStarted && !outputPublished, "Injected activation failure left an owned window or subscription alive.");
        return new { mode = failActivation ? "injected failure after real child creation/subscription before Activate" : closeWindow ? "real owned child Window.Close after RecognizeAsync dispatch" : "cancel after RecognizeAsync dispatch",
            nativeStarted, nativeDrained, statusAtCancellation, childClosed, outputPublished, actualInFlightAtCancellation = statusAtCancellation == "Started",
            activationFailureObserved, cleanupCloseRequested, handlerSubscribed, handlerDetached, handlerRetired };
    }

    private static void CheckMarkers(SoftwareBitmap bitmap, double scale)
    {
        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)]; bitmap.CopyToBuffer(pixels.AsBuffer());
        foreach (var marker in new[] { (X: 16, Y: 16, R: (byte)255, G: (byte)0, B: (byte)0), (X: 464, Y: 184, R: (byte)0, G: (byte)0, B: (byte)255), (X: 240, Y: 20, R: (byte)255, G: (byte)255, B: (byte)255) })
        {
            var offset = checked(((int)Math.Round(marker.Y * scale) * bitmap.PixelWidth + (int)Math.Round(marker.X * scale)) * 4);
            Require(pixels[offset] == marker.B && pixels[offset + 1] == marker.G && pixels[offset + 2] == marker.R, "Cycle owned marker mismatch.");
        }
    }
}
