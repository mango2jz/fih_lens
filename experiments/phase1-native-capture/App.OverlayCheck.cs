using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Graphics.Imaging;

namespace Phase1NativeCapture;

public partial class App
{
    private void CheckOverlayRetirement(object sender, RoutedEventArgs args) => CheckOverlayRetirementCore(false);
    private void CheckTimestampRetirement(object sender, RoutedEventArgs args) => CheckOverlayRetirementCore(true);

    private async void CheckOverlayRetirementCore(bool timestampGate)
    {
        if (operation is not null || window is null || (!timestampGate && currentCrop is null)) return;
        var owner = window;
        var evidenceOutput = timestampGate ? Path.Combine(Path.GetDirectoryName(output)!, "runtime-r7") : output;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        operation = cancellation; currentOcr = null; recognized.Text = ""; UpdateActions();
        Window? overlay = null;
        var overlayClosed = false;
        var subscribed = false;
        var attempts = new List<object>();
        var temporalAttempts = new List<object>();
        long? retirementQpc = null;
        void OverlayClosed(object closedSender, WindowEventArgs closedArgs) => overlayClosed = true;
        void Failure(string stage, Exception error)
        {
            if (!timestampGate) { RecordFailure(stage, error); return; }
            Directory.CreateDirectory(evidenceOutput);
            File.WriteAllText(Path.Combine(evidenceOutput, stage + "-failure.json"), JsonSerializer.Serialize(new
            { type = error.GetType().FullName, hresult = error.HResult, observedUtc = DateTimeOffset.UtcNow }));
        }
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            var monitor = NativeCapture.MonitorFromWindow(hwnd, 0);
            var origin = new NativeCapture.Point();
            Require(NativeCapture.ClientToScreen(hwnd, ref origin), "Fixture origin unavailable.");
            var top = fixture.TransformToVisual((UIElement)owner.Content).TransformPoint(new Windows.Foundation.Point());
            var scale = fixture.XamlRoot.RasterizationScale;
            var crop = new NativeCapture.Rect(origin.X + checked((int)Math.Round(top.X * scale)), origin.Y + checked((int)Math.Round(top.Y * scale)),
                origin.X + checked((int)Math.Round((top.X + fixture.ActualWidth) * scale)), origin.Y + checked((int)Math.Round((top.Y + fixture.ActualHeight) * scale)));
            var topology = NativeCapture.Displays();
            NativeCapture.ValidateCrop(topology.Single(x => x.Handle == monitor).Bounds, crop);
            overlay = new Window { Title = "FIH Lens — owned magenta overlay marker", Content = new Border { Background = Brush(255, 0, 255) } };
            overlay.Closed += OverlayClosed; subscribed = true;
            var presenter = (OverlappedPresenter)overlay.AppWindow.Presenter;
            presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false;
            overlay.AppWindow.ResizeClient(new SizeInt32(80, 80));
            overlay.AppWindow.Move(new PointInt32(crop.Left, crop.Top));
            overlay.Activate();
            status.Text = "Qualifying real pixels with owned overlay present…";
            await Qualify(true);
            Require(window == owner && ReferenceEquals(operation, cancellation) && !cancellation.IsCancellationRequested, "Acquisition owner invalid before retirement.");
            overlay.Close();
            Require(overlayClosed, "Overlay Closed was not observed.");
            if (timestampGate) retirementQpc = System.Diagnostics.Stopwatch.GetTimestamp();
            Require(window == owner && ReferenceEquals(operation, cancellation) && !cancellation.IsCancellationRequested, "Retiring overlay canceled the retained acquisition owner.");
            status.Text = "Overlay retired; qualifying actual overlay-free fixture pixels…";
            await Qualify(false);
            cancellation.Token.ThrowIfCancellationRequested();
            if (window != owner) return;
            overlay.Closed -= OverlayClosed; subscribed = false;
            Directory.CreateDirectory(evidenceOutput);
            File.WriteAllText(Path.Combine(evidenceOutput, timestampGate ? "timestamp-retirement-result.json" : "overlay-retirement-result.json"), JsonSerializer.Serialize(new
            {
                success = true, observedUtc = DateTimeOffset.UtcNow, identity = Windows.ApplicationModel.Package.Current.Id.FullName,
                scale, width = crop.Width, height = crop.Height, overlayClosed, retainedAcquisitionOwner = ReferenceEquals(operation, cancellation),
                tokenCanceledByOverlayRetirement = false, overlayPresentPixelPredicatePassed = true, overlayAbsentAndBaseMarkersPredicatePassed = true,
                attempts, maximumAttemptsPerPhase = 8, cancellationBudgetSeconds = 30, fixedSleepUsed = false, firstFrameAssumed = false,
                providerCalls = 0, pixelPersistence = false, recognizedTextPersisted = false,
                limitations = new[] { "Experiment owned marker window, not production selection overlay", "One retained operation/token; each qualifying attempt owns a fresh one-shot native session",
                    "No monitor edges/real topology change/mixed-DPI/negative-origin hardware", "Native copy drains after cancellation; budget is not an enforceable native-copy deadline", "No native timeout/itemClosed/device-loss/post-start cancel/clipboard failure proof" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (timestampGate)
                File.WriteAllText(Path.Combine(evidenceOutput, "timestamp-admission-result.json"), JsonSerializer.Serialize(new
                {
                    retirementQpc, qpcFrequency = System.Diagnostics.Stopwatch.Frequency, temporalAttempts,
                    timestampAloneProvesOverlayAbsence = false, maximumRejectedFramesPerAttempt = 8,
                    timestampQuantumMarginTicks = 1, qpcMarginTicks = 1
                }, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = "PASS: real overlay-present then overlay-free pixels; acquisition owner/token retained.";

            async Task Qualify(bool present)
            {
                for (var attempt = 1; attempt <= 8; attempt++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    Require(window == owner && ReferenceEquals(operation, cancellation) && topology.SequenceEqual(NativeCapture.Displays()), "Frozen acquisition owner/source changed.");
                    var trace = new NativeCapture.ProbeTrace();
                    using var bitmap = await NativeCapture.CropAsync(monitor, crop, cancellation.Token, trace, renderedAfterQpc: present ? null : retirementQpc);
                    Require(topology.SequenceEqual(NativeCapture.Displays()), "Frozen topology changed during qualification.");
                    Require(bitmap.PixelWidth == crop.Width && bitmap.PixelHeight == crop.Height, "Qualification crop dimensions differ.");
                    Require(trace.OwnerScopesExited && trace.FramesAcquired == trace.FramesDisposed && trace.SubscriptionsAdded == trace.SubscriptionsRemoved && trace.CopiesStarted == trace.CopiesDrained, "Qualifying capture cleanup unbalanced.");
                    var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)]; bitmap.CopyToBuffer(pixels.AsBuffer());
                    var magentaPixels = 0;
                    for (var offset = 0; offset < pixels.Length; offset += 4)
                        if (pixels[offset] == 255 && pixels[offset + 1] == 0 && pixels[offset + 2] == 255) magentaPixels++;
                    var qualifies = Pixel(464, 184, 0, 0, 255) && Pixel(240, 20, 255, 255, 255)
                        && (present ? magentaPixels > 0 && Pixel(16, 16, 255, 0, 255) : magentaPixels == 0 && Pixel(16, 16, 255, 0, 0));
                    attempts.Add(new { phase = present ? "overlay present" : "overlay retired", attempt, qualifies, magentaPixels,
                        trace.SessionStarts, trace.FramesAcquired, trace.FramesDisposed, trace.SubscriptionsAdded, trace.SubscriptionsRemoved, trace.CopiesStarted, trace.CopiesDrained });
                    if (timestampGate) temporalAttempts.Add(new { phase = present ? "overlay present" : "overlay retired", attempt,
                        trace.TimestampFramesRejected, trace.AcceptedFrameTimeSpanTicks, timestampGateApplied = !present,
                        markerQpc = present ? null : retirementQpc, independentPixelsQualified = qualifies });
                    if (qualifies) { if (!present) CheckMarkers(bitmap, scale); return; }

                    bool Pixel(int x, int y, byte red, byte green, byte blue)
                    {
                        var offset = checked(((int)Math.Round(y * scale) * bitmap.PixelWidth + (int)Math.Round(x * scale)) * 4);
                        return pixels[offset] == blue && pixels[offset + 1] == green && pixels[offset + 2] == red;
                    }
                }
                throw new InvalidOperationException("No qualifying owned-overlay frame within bounded attempts.");
            }
        }
        catch (OperationCanceledException) { if (window == owner) status.Text = "Overlay check canceled/timed out; no success published."; }
        catch (Exception e) { Failure(timestampGate ? "timestamp-retirement" : "overlay-retirement", e); if (window == owner) status.Text = $"Overlay check failed: {e.GetType().Name} (0x{e.HResult:X8})."; }
        finally
        {
            try
            {
                try { if (overlay is not null && !overlayClosed) overlay.Close(); }
                finally { if (overlay is not null && subscribed) overlay.Closed -= OverlayClosed; }
            }
            catch (Exception e) { Failure("overlay-cleanup", e); if (window == owner) status.Text = $"Overlay cleanup failed: {e.GetType().Name} (0x{e.HResult:X8})."; }
            finally { operation = null; if (window == owner) UpdateActions(); }
        }
    }
}
