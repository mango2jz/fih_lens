using FihLens.Application.Capture;
using FihLens.WinUI.Presentation;

namespace FihLens.Tests.WinUI;

public sealed class SelectedTextCaptureSessionTests
{
    [Fact]
    public async Task SuccessCommitsOnlyAfterUiDispatchAndCurrentSourceValidation()
    {
        var queued = new QueuedPublication();
        var capture = new Capture(SelectedTextCaptureResult.Success("exact selected text"));
        var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int validations = 0, commits = 0, offers = 0;
        string? committed = null;
        using var session = NewSession(capture, () => drain.Task, queued.Publish,
            () => { validations++; return null; }, () => null, text => { commits++; committed = text; }, _ => offers++, _ => { });

        Assert.True(session.Start());
        Assert.False(session.Start());
        await queued.Ready.Task;
        Assert.Equal(0, commits);
        Assert.Equal(0, validations);
        queued.Run();
        await session.Completion;

        Assert.True(session.IsCommitted);
        Assert.Equal(1, validations);
        Assert.Equal(1, commits);
        Assert.Equal("exact selected text", committed);
        Assert.Equal(0, offers);
        Assert.False(session.DrainCompletion.IsCompleted);
    }

    [Theory]
    [InlineData(SelectedTextCaptureOutcome.NoText)]
    [InlineData(SelectedTextCaptureOutcome.Unavailable)]
    [InlineData(SelectedTextCaptureOutcome.Unsupported)]
    [InlineData(SelectedTextCaptureOutcome.Denied)]
    [InlineData(SelectedTextCaptureOutcome.MultipleRanges)]
    [InlineData(SelectedTextCaptureOutcome.TooLarge)]
    [InlineData(SelectedTextCaptureOutcome.Timeout)]
    [InlineData(SelectedTextCaptureOutcome.Failed)]
    public async Task EligibleFailureOffersRegionWithoutCapturingIt(SelectedTextCaptureOutcome outcome)
    {
        int commits = 0, offers = 0;
        SelectedTextCaptureOutcome? offered = null;
        using var session = NewSession(new Capture(SelectedTextCaptureResult.Failure(outcome)), () => Task.CompletedTask,
            PublishImmediately, () => null, () => null, _ => commits++, value => { offers++; offered = value; }, _ => { });

        Assert.True(session.Start());
        await session.Completion;

        Assert.Equal(0, commits);
        Assert.Equal(1, offers);
        Assert.Equal(outcome, offered);
        Assert.True(session.IsOffered);
    }

    [Theory]
    [InlineData(SelectedTextCaptureOutcome.Sensitive)]
    [InlineData(SelectedTextCaptureOutcome.SourceChanged)]
    public async Task ProtectedOrStaleFailureDoesNotOfferRegion(SelectedTextCaptureOutcome outcome)
    {
        int offers = 0, reports = 0;
        using var session = NewSession(new Capture(SelectedTextCaptureResult.Failure(outcome)), () => Task.CompletedTask,
            PublishImmediately, () => null, () => null, _ => { }, _ => offers++, _ => reports++);

        session.Start();
        await session.Completion;

        Assert.Equal(0, offers);
        Assert.Equal(1, reports);
    }

    [Fact]
    public async Task SourceChangeAtFinalUiGuardSuppressesSuccess()
    {
        int commits = 0, offers = 0;
        using var session = NewSession(new Capture(SelectedTextCaptureResult.Success("stale")), () => Task.CompletedTask,
            PublishImmediately, () => SelectedTextCaptureOutcome.SourceChanged, () => null, _ => commits++, _ => offers++, _ => { });

        session.Start();
        await session.Completion;

        Assert.Equal(0, commits);
        Assert.Equal(0, offers);
        Assert.Equal(SelectedTextCaptureOutcome.SourceChanged, session.Outcome);
    }

    [Fact]
    public async Task ExpiredSuccessOffersRegionOnlyAfterOfferGuardAndSensitiveResultStaysSensitive()
    {
        int offers = 0, reports = 0;
        SelectedTextCaptureOutcome? offered = null, reported = null;
        using (var expired = NewSession(new Capture(SelectedTextCaptureResult.Success("late")), () => Task.CompletedTask,
            PublishImmediately, () => SelectedTextCaptureOutcome.Timeout, () => null, _ => { },
            outcome => { offers++; offered = outcome; }, _ => { }))
        {
            expired.Start();
            await expired.Completion;
            Assert.Equal(SelectedTextCaptureOutcome.Timeout, offered);
        }

        offers = 0;
        using var sensitive = NewSession(new Capture(SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Sensitive)),
            () => Task.CompletedTask, PublishImmediately, () => null, () => SelectedTextCaptureOutcome.Timeout,
            _ => { }, _ => offers++, outcome => { reports++; reported = outcome; });
        sensitive.Start();
        await sensitive.Completion;

        Assert.Equal(0, offers);
        Assert.Equal(1, reports);
        Assert.Equal(SelectedTextCaptureOutcome.Sensitive, reported);
    }

    [Fact]
    public async Task CancelWhileUiPublicationIsQueuedInvalidatesTheQueuedCommit()
    {
        var queued = new QueuedPublication();
        int commits = 0, offers = 0;
        using var session = NewSession(new Capture(SelectedTextCaptureResult.Success("late")), () => Task.CompletedTask,
            queued.Publish, () => null, () => null, _ => commits++, _ => offers++, _ => { });

        session.Start();
        await queued.Ready.Task;
        session.Cancel();
        queued.Run();
        await session.Completion;

        Assert.Equal(0, commits);
        Assert.Equal(0, offers);
    }

    [Fact]
    public async Task CaptureExceptionBecomesSafeFailureOffer()
    {
        int offers = 0;
        using var session = NewSession(new Capture(new InvalidOperationException()), () => Task.CompletedTask,
            PublishImmediately, () => null, () => null, _ => { }, _ => offers++, _ => { });

        session.Start();
        await session.Completion;

        Assert.Equal(SelectedTextCaptureOutcome.Failed, session.Outcome);
        Assert.Equal(1, offers);
    }

    [Fact]
    public async Task CancelSuppressesLateNativeSuccessAndRetainsCancellationUntilDrain()
    {
        var nativeDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new PendingCapture();
        int commits = 0, offers = 0;
        using var session = NewSession(capture, () => nativeDrain.Task, PublishImmediately,
            () => null, () => null, _ => commits++, _ => offers++, _ => { });

        session.Start();
        session.Cancel();
        Assert.True(capture.Token.IsCancellationRequested);
        capture.Result.SetResult(SelectedTextCaptureResult.Success("late native result"));
        await session.Completion;

        Assert.Equal(0, commits);
        Assert.Equal(0, offers);
        Assert.False(session.IsCancellationRetired);
        Assert.False(session.CancellationRetirement.IsCompleted);

        nativeDrain.SetResult();
        await session.CancellationRetirement;
        Assert.True(session.IsCancellationRetired);
    }

    [Fact]
    public async Task DispatcherRejectionAndFaultCloseAttemptWithoutPublication()
    {
        foreach (bool fault in new[] { false, true })
        {
            var nativeDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int commits = 0, offers = 0;
            Func<Action, Task<bool>> dispatch = fault
                ? _ => Task.FromException<bool>(new InvalidOperationException("dispatcher fault"))
                : _ => Task.FromResult(false);
            using var session = NewSession(new Capture(SelectedTextCaptureResult.Success("unpublished")),
                () => nativeDrain.Task, dispatch, () => null, () => null, _ => commits++, _ => offers++, _ => { });

            session.Start();
            if (fault) await Assert.ThrowsAsync<InvalidOperationException>(() => session.Completion);
            else await session.Completion;

            Assert.True(session.IsClosed);
            Assert.Equal(0, commits);
            Assert.Equal(0, offers);
            Assert.False(session.CancellationRetirement.IsCompleted);
            nativeDrain.SetResult();
            await session.CancellationRetirement;
            Assert.True(session.IsCancellationRetired);
        }
    }

    private static SelectedTextCaptureSession NewSession(ISelectedTextCaptureSource capture, Func<Task> drain,
        Func<Action, Task<bool>> publish, Func<SelectedTextCaptureOutcome?> validate,
        Func<SelectedTextCaptureOutcome?> validateOffer, Action<string> commit,
        Action<SelectedTextCaptureOutcome> offer, Action<SelectedTextCaptureOutcome> report) =>
        new(capture, drain, publish, validate, validateOffer, commit, offer, report);

    private static Task<bool> PublishImmediately(Action action)
    {
        action();
        return Task.FromResult(true);
    }

    private sealed class Capture(object result) : ISelectedTextCaptureSource
    {
        public Task<SelectedTextCaptureResult> CaptureAsync(CancellationToken cancellationToken) => result switch
        {
            SelectedTextCaptureResult value => Task.FromResult(value),
            Exception => Task.FromException<SelectedTextCaptureResult>((Exception)result),
            _ => throw new InvalidOperationException()
        };
    }

    private sealed class QueuedPublication
    {
        private Action? _action;
        private readonly TaskCompletionSource<bool> _published = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> Publish(Action action)
        {
            _action = action;
            Ready.TrySetResult();
            return _published.Task;
        }

        public void Run()
        {
            _action!();
            _published.TrySetResult(true);
        }
    }

    private sealed class PendingCapture : ISelectedTextCaptureSource
    {
        public TaskCompletionSource<SelectedTextCaptureResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<SelectedTextCaptureResult> CaptureAsync(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Result.Task;
        }
    }
}
