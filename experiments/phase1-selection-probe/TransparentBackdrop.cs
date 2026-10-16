using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Compositor = Windows.UI.Composition.Compositor;
using CompositionColorBrush = Windows.UI.Composition.CompositionColorBrush;
using System.Runtime.ExceptionServices;

namespace Phase1SelectionProbe;

internal sealed class TransparentBackdrop : SystemBackdrop
{
    private Compositor? compositor;
    private CompositionColorBrush? brush;
    public int Connected { get; private set; }
    public int Disconnected { get; private set; }
    private bool disposalFailed;
    public bool ResourcesDisposed => compositor is null && brush is null && !disposalFailed;
    public string Stage { get; private set; } = "not connected";
    public string? FailureStage { get; private set; }
    public bool? SystemQueuePresentBefore { get; private set; }
    public bool? SystemQueuePresentAfter { get; private set; }
    public List<ProbeFailure> CleanupFailures { get; } = [];

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, Microsoft.UI.Xaml.XamlRoot root)
    {
        if (compositor is not null) throw new InvalidOperationException("Backdrop already has an owner.");
        try
        {
            Stage = "backdrop ensure system dispatcher queue";
            SystemQueuePresentBefore = Windows.System.DispatcherQueue.GetForCurrentThread() is not null;
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("Backdrop requires its owning UI dispatcher.");
            // The App SDK dispatcher owns the system queue's lifetime; do not shut it down here.
            dispatcher.EnsureSystemDispatcherQueue();
            SystemQueuePresentAfter = Windows.System.DispatcherQueue.GetForCurrentThread() is not null;
            Stage = "backdrop compositor constructor";
            compositor = new Compositor();
            Stage = "backdrop create color brush";
            brush = compositor.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
            Stage = "backdrop target assignment";
            target.SystemBackdrop = brush;
            Connected++;
            Stage = "backdrop connected";
        }
        catch (Exception error)
        {
            FailureStage = Stage;
            DisposeResources(error);
            throw;
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        Stage = "backdrop target clear";
        var first = ProbeFailureLog.Release(Stage, () => target.SystemBackdrop = null, null, CleanupFailures);
        try { DisposeResources(first); }
        finally { Disconnected++; Stage = "backdrop disconnected"; }
    }

    private void DisposeResources(Exception? first = null)
    {
        var ownedBrush = brush; brush = null;
        var ownedCompositor = compositor; compositor = null;
        first = ProbeFailureLog.Release("backdrop brush dispose", () =>
        {
            try { ownedBrush?.Dispose(); }
            catch { disposalFailed = true; throw; }
        }, first, CleanupFailures);
        first = ProbeFailureLog.Release("backdrop compositor dispose", () =>
        {
            try { ownedCompositor?.Dispose(); }
            catch { disposalFailed = true; throw; }
        }, first, CleanupFailures);
        if (first is not null) ExceptionDispatchInfo.Capture(first).Throw();
    }
}
