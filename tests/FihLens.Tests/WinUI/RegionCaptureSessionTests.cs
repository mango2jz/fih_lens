using FihLens.Application.Capture;
using FihLens.WinUI.Presentation;

namespace FihLens.Tests.WinUI;

public sealed class RegionCaptureSessionTests
{
    private static RegionTextCaptureRequest Request() => new(new("display", new(-100, 0, 100, 100), "stamp"), new(-90, 10, 40, 40));
    private static Task<bool> Publish(Action action) { action(); return Task.FromResult(true); }

    [Fact]
    public async Task ConfirmWaitsForActualOverlayRetirementAndDispatchesExactlyOneFrozenRequest()
    {
        var capture = new RecordingCapture();
        var owner = new RegionCaptureSession(capture, Publish);
        var request = Request();
        Assert.True(owner.Confirm(request));
        Assert.False(owner.Confirm(Request()));
        Assert.Empty(capture.Requests);
        // A failed Close attempt has no Closed acknowledgement, so no dispatch; a retry may acknowledge later.
        var operation = owner.OverlayRetiredAsync();
        Assert.Same(request, Assert.Single(capture.Requests));
        Assert.True(owner.IsBusy);
        Assert.False(capture.Token.IsCancellationRequested);
        await owner.OverlayRetiredAsync();
        Assert.Single(capture.Requests);
        Assert.NotNull(capture.Token.WaitHandle);
        capture.Completion.SetResult(RegionTextCaptureResult.Success("current"));
        await operation;
        Assert.Equal("current", owner.Result!.Text);
        Assert.False(owner.IsBusy);
        Assert.Throws<ObjectDisposedException>(() => capture.Token.WaitHandle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DismissBeforeRetirementIncludingUnexpectedCloseMakesZeroCalls(bool confirmed)
    {
        var capture = new RecordingCapture();
        var owner = new RegionCaptureSession(capture, Publish);
        if (confirmed) owner.Confirm(Request());
        owner.Dismiss();
        await owner.OverlayRetiredAsync();
        Assert.Empty(capture.Requests);
        Assert.Null(owner.Result);
        Assert.False(owner.IsBusy);
        Assert.False(owner.Confirm(Request()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenuineCancellationRetainsBusyGateUntilLateSuccessOrFailureDrains(bool fail)
    {
        var capture = new RecordingCapture();
        var owner = new RegionCaptureSession(capture, Publish);
        owner.Confirm(Request());
        var operation = owner.OverlayRetiredAsync();
        owner.Dismiss();
        Assert.True(capture.Token.IsCancellationRequested);
        Assert.True(owner.IsBusy);
        Assert.False(owner.Confirm(Request()));
        if (fail) capture.Completion.SetException(new InvalidOperationException("sensitive captured text"));
        else capture.Completion.SetResult(RegionTextCaptureResult.Success("late"));
        await operation;
        Assert.Null(owner.Result);
        Assert.False(owner.IsBusy);
    }

    [Fact]
    public async Task CancellationDuringQueuedUiPublicationSuppressesResult()
    {
        var capture = new RecordingCapture();
        Action? queued = null;
        var queuedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new RegionCaptureSession(capture, action =>
        {
            queued = action;
            queuedSignal.SetResult();
            return published.Task;
        });
        owner.Confirm(Request());
        var operation = owner.OverlayRetiredAsync();
        capture.Completion.SetResult(RegionTextCaptureResult.Success("late queued"));
        await queuedSignal.Task;
        Assert.True(owner.IsBusy);
        owner.Dismiss();
        queued!();
        published.SetResult(true);
        await operation;
        Assert.Null(owner.Result);
        Assert.False(owner.IsBusy);
    }

    [Fact]
    public async Task RejectedDispatcherRetiresDrainedOwnerWithoutNotificationOrEscapingException()
    {
        var capture = new RecordingCapture();
        Action? rejectedCallback = null;
        var owner = new RegionCaptureSession(capture,
            action =>
            {
                rejectedCallback = action;
                return Task.FromResult(false);
            });
        owner.Confirm(Request());
        var notifications = new List<string?>();
        owner.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        var operation = owner.OverlayRetiredAsync();
        Assert.True(owner.IsBusy);
        capture.Completion.SetResult(RegionTextCaptureResult.Success("unpublishable"));
        await operation;
        Assert.Empty(notifications);
        Assert.False(owner.IsBusy);
        Assert.True(owner.IsCanceled);
        Assert.Null(owner.Result);
        rejectedCallback!();
        owner.Dismiss();
        Assert.Empty(notifications);
        Assert.False(owner.Confirm(Request()));
        await owner.OverlayRetiredAsync();
        Assert.Single(capture.Requests);
        Assert.Throws<ObjectDisposedException>(() => capture.Token.WaitHandle);
    }

    [Fact]
    public async Task UnexpectedPublisherFaultRemainsObservableAndRetiresDrainedOwner()
    {
        var capture = new RecordingCapture();
        Action? callback = null;
        var failure = new InvalidOperationException("Unexpected publisher bug");
        var owner = new RegionCaptureSession(capture, action =>
        {
            callback = action;
            return Task.FromException<bool>(failure);
        });
        owner.Confirm(Request());
        var notifications = new List<string?>();
        owner.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        var operation = owner.OverlayRetiredAsync();
        Assert.True(owner.IsBusy);
        capture.Completion.SetResult(RegionTextCaptureResult.Success("unpublished"));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => operation));
        Assert.False(owner.IsBusy);
        Assert.True(owner.IsCanceled);
        Assert.Null(owner.Result);
        callback!();
        owner.Dismiss();
        Assert.Empty(notifications);
        Assert.Single(capture.Requests);
        Assert.Throws<ObjectDisposedException>(() => capture.Token.WaitHandle);
    }

    [Fact]
    public async Task PropertyChangedSubscriberFaultRemainsObservableAndRetiresDrainedOwner()
    {
        var capture = new RecordingCapture();
        var owner = new RegionCaptureSession(capture, Publish);
        owner.Confirm(Request());
        var failure = new InvalidOperationException("Unexpected subscriber bug");
        int notifications = 0;
        owner.PropertyChanged += (_, _) => { notifications++; throw failure; };
        var operation = owner.OverlayRetiredAsync();
        Assert.True(owner.IsBusy);
        capture.Completion.SetResult(RegionTextCaptureResult.Success("failed publication"));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => operation));
        Assert.Equal(1, notifications);
        Assert.False(owner.IsBusy);
        Assert.True(owner.IsCanceled);
        Assert.Null(owner.Result);
        owner.Dismiss();
        Assert.Equal(1, notifications);
        Assert.Single(capture.Requests);
        Assert.Throws<ObjectDisposedException>(() => capture.Token.WaitHandle);
    }

    [Fact]
    public async Task CurrentFailureIsTypedAndCancellationIsNotAnError()
    {
        var capture = new RecordingCapture();
        var owner = new RegionCaptureSession(capture, Publish);
        owner.Confirm(Request());
        var operation = owner.OverlayRetiredAsync();
        capture.Completion.SetException(new InvalidOperationException("sensitive"));
        await operation;
        Assert.Equal(RegionTextCaptureOutcome.Failed, owner.Result!.Outcome);
        Assert.Null(owner.Result.Text);

        capture = new RecordingCapture();
        owner = new RegionCaptureSession(capture, Publish);
        owner.Confirm(Request());
        operation = owner.OverlayRetiredAsync();
        owner.Dismiss();
        capture.Completion.SetCanceled(capture.Token);
        await operation;
        Assert.Null(owner.Result);
        Assert.False(owner.IsBusy);
    }

    private sealed class RecordingCapture : IRegionTextCapture
    {
        public List<RegionTextCaptureRequest> Requests { get; } = [];
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource<RegionTextCaptureResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RegionTextCaptureResult> CaptureAsync(RegionTextCaptureRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Token = cancellationToken;
            return Completion.Task;
        }
    }
}
