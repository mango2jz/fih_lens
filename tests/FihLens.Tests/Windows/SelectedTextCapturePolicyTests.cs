using FihLens.Windows.Capture;
using FihLens.Application.Capture;

namespace FihLens.Tests.Windows;

public sealed class SelectedTextCapturePolicyTests
{
    [Fact]
    public async Task ConcurrentAttemptsAdmitOneOwnerUntilNativeDrainRetiresIt()
    {
        var policy = new SelectedTextCaptureWorkerPolicy();
        using var start = new ManualResetEventSlim();
        var attempts = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return policy.TryAcquire();
            }))
            .ToArray();

        start.Set();
        var acquired = await Task.WhenAll(attempts);

        Assert.Single(acquired, wasAcquired => wasAcquired);
        Assert.True(policy.IsOccupied);
        Assert.False(policy.TryAcquire());

        policy.ReleaseAfterNativeDrain();

        Assert.False(policy.IsOccupied);
        Assert.True(policy.TryAcquire());
        Assert.True(policy.IsOccupied);
    }

    [Fact]
    public async Task TimeoutCompletesLogicallyButRetainsLaneUntilNativeTaskDrains()
    {
        var policy = new SelectedTextCaptureWorkerPolicy();
        var native = new TaskCompletionSource<SelectedTextCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualTimeProvider();
        var logicalEndCount = 0;
        Assert.True(policy.TryAcquire());

        var logical = SelectedTextCaptureWorkerPolicy.ObserveNativeAsync(
            native.Task, TimeSpan.FromSeconds(5), CancellationToken.None, clock, () => logicalEndCount++);
        Assert.False(logical.IsCompleted);
        Assert.True(policy.IsOccupied);
        Assert.False(policy.TryAcquire());

        clock.Advance(TimeSpan.FromSeconds(5));
        var result = await logical;

        Assert.Equal(SelectedTextCaptureOutcome.Timeout, result.Outcome);
        Assert.Equal(1, logicalEndCount);
        Assert.True(policy.IsOccupied);
        Assert.False(policy.TryAcquire());

        native.SetException(new InvalidOperationException("late native failure"));
        Assert.Equal(SelectedTextCaptureOutcome.Timeout, result.Outcome);
        policy.ReleaseAfterNativeDrain();
        Assert.False(policy.IsOccupied);
        Assert.True(policy.TryAcquire());
    }

    [Fact]
    public async Task CancellationCompletesLogicallyButDoesNotReleaseNativeLane()
    {
        var policy = new SelectedTextCaptureWorkerPolicy();
        var native = new TaskCompletionSource<SelectedTextCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var logicalEndCount = 0;
        Assert.True(policy.TryAcquire());

        var logical = SelectedTextCaptureWorkerPolicy.ObserveNativeAsync(
            native.Task, TimeSpan.FromSeconds(5), cancellation.Token, logicalEnd: () => logicalEndCount++);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => logical);
        Assert.Equal(1, logicalEndCount);
        Assert.True(policy.IsOccupied);
        Assert.False(policy.TryAcquire());

        native.SetResult(SelectedTextCaptureResult.Success("late"));
        policy.ReleaseAfterNativeDrain();
        Assert.True(policy.TryAcquire());
    }

    [Fact]
    public async Task ExpiredDeadlineReturnsTimeoutWithoutReleasingNativeLane()
    {
        var policy = new SelectedTextCaptureWorkerPolicy();
        var native = new TaskCompletionSource<SelectedTextCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(policy.TryAcquire());

        var result = await SelectedTextCaptureWorkerPolicy.ObserveNativeAsync(
            native.Task, TimeSpan.FromMilliseconds(-1), CancellationToken.None);

        Assert.Equal(SelectedTextCaptureOutcome.Timeout, result.Outcome);
        Assert.True(policy.IsOccupied);
        Assert.False(policy.TryAcquire());
        native.SetResult(SelectedTextCaptureResult.Success("late"));
        policy.ReleaseAfterNativeDrain();
    }

    [Fact]
    public async Task NativeCompletionBeforeDeadlineWins()
    {
        var native = new TaskCompletionSource<SelectedTextCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualTimeProvider();
        var logical = SelectedTextCaptureWorkerPolicy.ObserveNativeAsync(
            native.Task, TimeSpan.FromSeconds(5), CancellationToken.None, clock);

        native.SetResult(SelectedTextCaptureResult.Success("captured"));

        var result = await logical;
        Assert.Equal(SelectedTextCaptureOutcome.Success, result.Outcome);
        Assert.Equal("captured", result.Text);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private TimeSpan _elapsed;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_timers) _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            ManualTimer[] due;
            lock (_timers)
            {
                _elapsed += elapsed;
                due = _timers.Where(timer => timer.IsDue(_elapsed)).ToArray();
            }
            foreach (var timer in due) timer.Fire();
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan? _dueAt;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._timers)
                    _dueAt = _disposed || dueTime == Timeout.InfiniteTimeSpan ? null : owner._elapsed + dueTime;
                return !_disposed;
            }

            public void Dispose()
            {
                lock (owner._timers)
                {
                    _disposed = true;
                    _dueAt = null;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal bool IsDue(TimeSpan elapsed)
            {
                lock (owner._timers) return !_disposed && _dueAt is { } dueAt && elapsed >= dueAt;
            }

            internal void Fire()
            {
                lock (owner._timers)
                {
                    if (_disposed || _dueAt is null) return;
                    _dueAt = null;
                }
                callback(state);
            }
        }
    }
}
