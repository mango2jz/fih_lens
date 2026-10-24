namespace FihLens.Windows.Capture;

using FihLens.Application.Capture;

/// <summary>Production lane state: logical completion never releases a draining native call.</summary>
public sealed class SelectedTextCaptureWorkerPolicy
{
    private int _occupied;

    public bool TryAcquire() => Interlocked.CompareExchange(ref _occupied, 1, 0) == 0;
    public bool IsOccupied => Volatile.Read(ref _occupied) != 0;
    public void ReleaseAfterNativeDrain() => Volatile.Write(ref _occupied, 0);

    public static async Task<SelectedTextCaptureResult> ObserveNativeAsync(
        Task<SelectedTextCaptureResult> nativeTask, TimeSpan remaining, CancellationToken cancellationToken,
        TimeProvider? timeProvider = null, Action? logicalEnd = null)
    {
        ArgumentNullException.ThrowIfNull(nativeTask);
        _ = nativeTask.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            return await nativeTask.WaitAsync(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining,
                timeProvider ?? TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            try { logicalEnd?.Invoke(); } catch (ObjectDisposedException) { }
            return SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Timeout);
        }
        catch (OperationCanceledException)
        {
            try { logicalEnd?.Invoke(); } catch (ObjectDisposedException) { }
            throw;
        }
    }
}
