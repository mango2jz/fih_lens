using FihLens.WinUI.Presentation;

namespace FihLens.Tests.WinUI;

public sealed class ShutdownCompletionTests
{
    [Fact]
    public async Task FinalizationAttemptsShellEvenWhenSharedCleanupFails()
    {
        var shellCalls = 0;
        bool complete = await ShutdownCompletion.FinalizeWithShellAsync(
            () => Task.FromResult(false),
            () => { shellCalls++; return true; },
            _ => { });

        Assert.False(complete);
        Assert.Equal(1, shellCalls);
    }

    [Fact]
    public async Task ShellFailureBlocksFinalCloseAndCanSucceedOnRetry()
    {
        var sharedCalls = 0;
        var shellCalls = 0;
        bool first = await ShutdownCompletion.FinalizeWithShellAsync(
            () => { sharedCalls++; return Task.FromResult(true); },
            () => ++shellCalls > 1,
            _ => { });
        bool retry = await ShutdownCompletion.FinalizeWithShellAsync(
            () => { sharedCalls++; return Task.FromResult(true); },
            () => ++shellCalls > 1,
            _ => { });

        Assert.False(first);
        Assert.True(retry);
        Assert.Equal(2, sharedCalls);
        Assert.Equal(2, shellCalls);
    }

    [Fact]
    public async Task SharedFinalizationExceptionStillAttemptsShellRetirement()
    {
        var failure = new IOException("shared cleanup");
        var reported = new List<Exception>();
        var shellCalls = 0;

        bool complete = await ShutdownCompletion.FinalizeWithShellAsync(
            () => Task.FromException<bool>(failure),
            () => { shellCalls++; return true; },
            reported.Add);

        Assert.False(complete);
        Assert.Equal(1, shellCalls);
        Assert.Contains(failure, reported);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("fault")]
    [InlineData("cancel")]
    public async Task TerminalFaultAndCancellationStillDrainPendingWork(string delayedOutcome)
    {
        var fault = new InvalidOperationException("first failure");
        var canceled = Task.FromCanceled(new CancellationToken(true));
        var delayed = new TaskCompletionSource();
        var work = new List<Task> { Task.FromException(fault), canceled, delayed.Task };
        var reported = new List<Exception>();

        var drain = ShutdownCompletion.DrainAsync(work, reported.Add);
        Assert.False(drain.IsCompleted);

        CompleteLate(delayed, delayedOutcome);

        var exception = await Record.ExceptionAsync(() => drain);
        Assert.Null(exception);
        Assert.Empty(work);
        Assert.Contains(fault, reported);
        Assert.Equal(delayedOutcome == "fault" ? 2 : 1, reported.Count);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("fault")]
    [InlineData("cancel")]
    public void WorkRegisteredDuringDrainIsAlsoObservedAndDrained(string lateOutcome)
    {
        var context = new QueuedSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        TaskCompletionSource? first = null;
        TaskCompletionSource? late = null;
        Task? drain = null;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            first = new TaskCompletionSource();
            late = new TaskCompletionSource();
            var work = new List<Task> { first.Task };
            var reported = new List<Exception>();
            drain = ShutdownCompletion.DrainAsync(work, reported.Add);
            work.Add(late.Task);

            CompleteWithoutInlining(first);
            context.RunOne();

            Assert.False(drain.IsCompleted);
            Assert.Single(work);
            Assert.Same(context, context.LastContinuationContext);

            CompleteLateWithoutInlining(late, lateOutcome);
            context.RunOne();

            Assert.True(drain.IsCompletedSuccessfully);
            Assert.Empty(work);
            Assert.Equal(lateOutcome == "fault" ? 1 : 0, reported.Count);

            var next = new TaskCompletionSource();
            work.Add(next.Task);
            var nextDrain = ShutdownCompletion.DrainAsync(work, reported.Add);
            CompleteWithoutInlining(next);
            context.RunOne();
            Assert.True(nextDrain.IsCompletedSuccessfully);
            Assert.Empty(work);
        }
        finally
        {
            try
            {
                if (first is { Task.IsCompleted: false }) CompleteLateWithoutInlining(first, "success");
                if (late is { Task.IsCompleted: false }) CompleteLateWithoutInlining(late, "success");
                while (drain is { IsCompleted: false }) context.RunOne();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
                context.Dispose();
            }
        }
    }

    [Fact]
    public async Task SharedFinalizationWaitsForInitialization()
    {
        var initialization = new TaskCompletionSource();
        var order = new List<string>();
        var finalization = ShutdownCompletion.FinalizeSharedAsync(
            initialization.Task,
            () => RecordAsync(order, "reader"),
            () => RecordAsync(order, "speech"),
            () => order.Add("client"),
            () => order.Add("manager"),
            () => order.Add("cancellation"),
            _ => { });

        Assert.False(finalization.IsCompleted);
        Assert.Empty(order);

        initialization.SetResult();

        Assert.True(await finalization);
        Assert.Equal(new[] { "reader", "speech", "client", "manager", "cancellation" }, order);
    }

    [Fact]
    public async Task NativeCaptureDrainWaitsAfterIndependentCleanup()
    {
        var nativeDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var finalization = ShutdownCompletion.FinalizeSharedAsync(
            Task.CompletedTask,
            () => RecordAsync(order, "reader"),
            () => RecordAsync(order, "speech"),
            () => order.Add("client"),
            () => order.Add("manager"),
            () => order.Add("cancellation"),
            _ => { },
            () => new ValueTask(nativeDrain.Task));

        await Task.Yield();
        Assert.False(finalization.IsCompleted);
        Assert.Equal(new[] { "reader", "speech", "client", "manager", "cancellation" }, order);

        nativeDrain.SetResult();
        Assert.True(await finalization);
    }

    [Theory]
    [InlineData("fault")]
    [InlineData("cancel")]
    public async Task SharedFinalizationContinuesAfterTerminalInitializationFailure(string outcome)
    {
        var initialization = new TaskCompletionSource();
        var order = new List<string>();
        var reported = new List<Exception>();
        var finalization = ShutdownCompletion.FinalizeSharedAsync(
            initialization.Task,
            () => RecordAsync(order, "reader"),
            () => RecordAsync(order, "speech"),
            () => order.Add("client"),
            () => order.Add("manager"),
            () => order.Add("cancellation"),
            reported.Add);

        Assert.False(finalization.IsCompleted);
        Assert.Empty(order);
        if (outcome == "fault") initialization.SetException(new IOException("initialization failure"));
        else initialization.SetCanceled();

        var exception = await Record.ExceptionAsync(async () => await finalization);
        Assert.Null(exception);
        Assert.True(await finalization);
        Assert.Equal(new[] { "reader", "speech", "client", "manager", "cancellation" }, order);
        Assert.Single(reported);
        if (outcome == "fault") Assert.IsType<IOException>(reported[0]);
        else Assert.IsAssignableFrom<OperationCanceledException>(reported[0]);
    }

    [Theory]
    [InlineData("reader", false)]
    [InlineData("reader", true)]
    [InlineData("speech", false)]
    [InlineData("speech", true)]
    [InlineData("client", false)]
    [InlineData("manager", false)]
    [InlineData("cancellation", false)]
    public async Task SharedFinalizationAttemptsIndependentCleanupAfterFailure(string failedStage, bool returnsFaultedTask)
    {
        var order = new List<string>();
        var reported = new List<Exception>();
        var finalization = ShutdownCompletion.FinalizeSharedAsync(
            Task.CompletedTask,
            () => RecordMaybeFailAsync(order, "reader", failedStage, returnsFaultedTask),
            () => RecordMaybeFailAsync(order, "speech", failedStage, returnsFaultedTask),
            () => RecordMaybeFail(order, "client", failedStage),
            () => RecordMaybeFail(order, "manager", failedStage),
            () => RecordMaybeFail(order, "cancellation", failedStage),
            reported.Add);

        var exception = await Record.ExceptionAsync(async () => await finalization);
        Assert.Null(exception);
        Assert.False(await finalization);
        Assert.Equal(new[] { "reader", "speech", "client", "manager", "cancellation" }, order);
        Assert.Single(reported);
        Assert.IsType<InvalidOperationException>(reported[0]);
    }

    [Fact]
    public async Task FailedCleanupCanRetryWithoutLosingItsOwner()
    {
        var reader = new FakeAsyncOwner(failNext: true);
        var speech = new FakeAsyncOwner();
        var client = new FakeSyncOwner();
        var manager = new FakeSyncOwner();
        var cancellation = new FakeSyncOwner();
        var backendsOwned = true;
        var reported = new List<Exception>();

        async Task<bool> Finalize()
        {
            var complete = await ShutdownCompletion.FinalizeSharedAsync(
                Task.CompletedTask,
                reader.DisposeAsync,
                speech.DisposeAsync,
                client.Dispose,
                manager.Dispose,
                cancellation.Dispose,
                reported.Add);
            if (complete) backendsOwned = false;
            return complete;
        }

        bool? firstResult = null;
        var exception = await Record.ExceptionAsync(async () => firstResult = await Finalize());
        Assert.Null(exception);
        Assert.False(firstResult);
        Assert.True(reader.IsOwned);
        Assert.False(speech.IsOwned);
        Assert.False(client.IsOwned);
        Assert.False(manager.IsOwned);
        Assert.False(cancellation.IsOwned);
        Assert.True(backendsOwned);

        Assert.True(await Finalize());
        Assert.False(reader.IsOwned);
        Assert.False(backendsOwned);
        Assert.Equal(2, reader.DisposeCalls);
        Assert.Equal(1, speech.DisposeCalls);
        Assert.Equal(1, client.DisposeCalls);
        Assert.Equal(1, manager.DisposeCalls);
        Assert.Equal(1, cancellation.DisposeCalls);
    }

    private static ValueTask RecordAsync(List<string> order, string stage)
    {
        order.Add(stage);
        return ValueTask.CompletedTask;
    }

    private static ValueTask RecordMaybeFailAsync(List<string> order, string stage, string failedStage, bool returnsFaultedTask)
    {
        order.Add(stage);
        if (stage != failedStage) return ValueTask.CompletedTask;
        if (returnsFaultedTask) return ValueTask.FromException(new InvalidOperationException(stage));
        throw new InvalidOperationException(stage);
    }

    private static void RecordMaybeFail(List<string> order, string stage, string failedStage)
    {
        order.Add(stage);
        if (stage == failedStage) throw new InvalidOperationException(stage);
    }

    private sealed class FakeAsyncOwner(bool failNext = false)
    {
        public bool IsOwned { get; private set; } = true;
        public int DisposeCalls { get; private set; }
        private bool _failNext = failNext;

        public ValueTask DisposeAsync()
        {
            if (!IsOwned) return ValueTask.CompletedTask;
            DisposeCalls++;
            if (_failNext)
            {
                _failNext = false;
                return ValueTask.FromException(new IOException("fake disposal failure"));
            }
            IsOwned = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSyncOwner
    {
        public bool IsOwned { get; private set; } = true;
        public int DisposeCalls { get; private set; }

        public void Dispose()
        {
            if (!IsOwned) return;
            DisposeCalls++;
            IsOwned = false;
        }
    }

    private static void CompleteWithoutInlining(TaskCompletionSource completion)
    {
        var current = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            completion.SetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(current); }
    }

    private static void CompleteLateWithoutInlining(TaskCompletionSource completion, string outcome)
    {
        var current = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            CompleteLate(completion, outcome);
        }
        finally { SynchronizationContext.SetSynchronizationContext(current); }
    }

    private static void CompleteLate(TaskCompletionSource completion, string outcome)
    {
        switch (outcome)
        {
            case "success": completion.SetResult(); break;
            case "fault": completion.SetException(new IOException("late failure")); break;
            case "cancel": completion.SetCanceled(); break;
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        private readonly AutoResetEvent _posted = new(false);

        public SynchronizationContext? LastContinuationContext { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_callbacks) _callbacks.Enqueue((d, state));
            _posted.Set();
        }

        public void RunOne()
        {
            Assert.True(_posted.WaitOne(TimeSpan.FromSeconds(5)), "The drain continuation was not posted to the test UI context.");
            (SendOrPostCallback Callback, object? State) work;
            lock (_callbacks) work = _callbacks.Dequeue();
            LastContinuationContext = Current;
            work.Callback(work.State);
        }

        public void Dispose() => _posted.Dispose();
    }
}
