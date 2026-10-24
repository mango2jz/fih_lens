using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FihLens.Application.Capture;

namespace FihLens.Windows.Capture;

internal sealed class SelectedTextCaptureWorker
{
    private const long DeadlineTicks = 1500;
    private readonly BlockingCollection<Request> _queue = new(1);
    private readonly SelectedTextCaptureWorkerPolicy _policy = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _stopped;
    internal bool IsBusy => _policy.IsOccupied;

    internal SelectedTextCaptureWorker()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "FIH Lens UI Automation" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    internal Task DrainCompletion => _drained.Task;

    internal void Stop()
    {
        lock (_queue)
        {
            if (Volatile.Read(ref _stopped)) return;
            _stopped = true;
            _queue.CompleteAdding();
        }
    }

    internal Task<SelectedTextCaptureResult> CaptureAsync(WindowsSelectedTextCapture owner, SelectedTextSource source,
        CancellationToken token, Action<Task> setDrainCompletion)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<SelectedTextCaptureResult>(token);
        if (Volatile.Read(ref _stopped) || !source.SnapshotAvailable || !owner.IsAvailable)
            return Task.FromResult(SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Unavailable));
        if (!_policy.TryAcquire()) return Task.FromResult(SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Unavailable));

        var request = new Request(owner, source, source.MonotonicTimestamp + (long)(DeadlineTicks * Stopwatch.Frequency / 1000d));
        request.NativeCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_queue)
        {
            if (Volatile.Read(ref _stopped))
            {
                _policy.ReleaseAfterNativeDrain();
                request.NativeCancellation.Dispose();
                request.Drained.TrySetResult();
                return Task.FromResult(SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Unavailable));
            }
            _queue.Add(request);
            setDrainCompletion(request.Drained.Task);
        }
        var remaining = TimeSpan.FromSeconds((double)(request.Deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency);
        return SelectedTextCaptureWorkerPolicy.ObserveNativeAsync(request.Native.Task, remaining, token,
            logicalEnd: request.NativeCancellation.Cancel);
    }

    private void Run()
    {
        int initialized = CoInitializeEx(0, 0x0); // COINIT_MULTITHREADED
        try
        {
            foreach (var request in _queue.GetConsumingEnumerable())
            {
                try
                {
                    SelectedTextCaptureResult result = initialized < 0
                        ? SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Unavailable)
                        : UiAutomationInterop.Capture(request.Source, request.Deadline, request.NativeCancellation.Token,
                            () => request.Owner.IsSourceCurrent(request.Source));
                    request.Native.TrySetResult(result);
                }
                catch (Exception)
                {
                    request.Native.TrySetResult(SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Failed));
                }
                finally
                {
                    request.NativeCancellation.Dispose();
                    _policy.ReleaseAfterNativeDrain();
                    request.Drained.TrySetResult();
                }
            }
        }
        finally
        {
            _queue.Dispose();
            if (initialized >= 0) CoUninitialize();
            _drained.TrySetResult();
        }
    }

    private sealed record Request(WindowsSelectedTextCapture Owner, SelectedTextSource Source, long Deadline)
    {
        internal TaskCompletionSource<SelectedTextCaptureResult> Native { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenSource NativeCancellation { get; set; } = null!;
    }

    [DllImport("ole32.dll", PreserveSig = true)] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
