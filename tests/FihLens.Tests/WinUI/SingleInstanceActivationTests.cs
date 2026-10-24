using FihLens.WinUI.Presentation;

namespace FihLens.Tests.WinUI;

public sealed class SingleInstanceActivationTests
{
    [Fact]
    public void EarlyActivationsCoalesceUntilReady()
    {
        var policy = new SingleInstanceActivationPolicy();

        Assert.Equal(ActivationRequest.Pending, policy.RequestOpen());
        Assert.Equal(ActivationRequest.Pending, policy.RequestOpen());

        Assert.True(policy.MarkReady());
        Assert.False(policy.MarkReady());
        Assert.Equal(ActivationRequest.Pending, policy.RequestOpen());
        Assert.True(policy.RunOpen(() => { }));
        Assert.Equal(ActivationRequest.Open, policy.RequestOpen());
    }

    [Fact]
    public void ShutdownDropsPendingAndFutureActivations()
    {
        var policy = new SingleInstanceActivationPolicy();
        Assert.Equal(ActivationRequest.Pending, policy.RequestOpen());

        policy.BeginShutdown();

        Assert.False(policy.MarkReady());
        Assert.Equal(ActivationRequest.Ignored, policy.RequestOpen());
    }

    [Fact]
    public void ShutdownDropsAlreadyDispatchedOpen()
    {
        var policy = new SingleInstanceActivationPolicy();
        policy.RequestOpen();
        policy.MarkReady();
        int opened = 0;
        Action queuedOpen = () => policy.RunOpen(() => opened++);

        policy.BeginShutdown();
        queuedOpen();

        Assert.Equal(0, opened);
    }

    [Fact]
    public void FailedOpenCallbackReleasesScheduledGateForRetry()
    {
        var policy = new SingleInstanceActivationPolicy();
        policy.MarkReady();
        Assert.Equal(ActivationRequest.Open, policy.RequestOpen());

        Assert.Throws<InvalidOperationException>(() => policy.RunOpen(() => throw new InvalidOperationException()));

        Assert.Equal(ActivationRequest.Open, policy.RequestOpen());
        Assert.True(policy.RunOpen(() => { }));
    }

    [Fact]
    public void RejectedDispatchReleasesScheduledGateForRetry()
    {
        var policy = new SingleInstanceActivationPolicy();
        policy.MarkReady();
        Assert.Equal(ActivationRequest.Open, policy.RequestOpen());

        policy.CancelScheduledOpen();

        Assert.Equal(ActivationRequest.Open, policy.RequestOpen());
    }

    [Fact]
    public void ActivationWhileStoppingRequestsClosingStatus()
    {
        var policy = new SingleInstanceActivationPolicy();
        policy.MarkReady();
        policy.BeginShutdown();
        int reported = 0;

        Assert.Equal(ActivationRequest.Closing, policy.RequestOpen());
        Assert.True(policy.RunClosing(() => reported++));
        Assert.Equal(1, reported);

        policy.Retire();
        Assert.Equal(ActivationRequest.Ignored, policy.RequestOpen());
        Assert.False(policy.RunClosing(() => reported++));
    }

    [Fact]
    public void PrimaryDoesNotAttemptRedirect()
    {
        bool attempted = false;

        var decision = SingleInstanceActivationPolicy.Decide(true, () =>
        {
            attempted = true;
            return SecondaryActivationResult.Failed;
        });

        Assert.Equal(ActivationLaunchDecision.Primary, decision);
        Assert.False(SingleInstanceActivationPolicy.ShouldShowStartupFailure(decision));
        Assert.False(attempted);
    }

    [Theory]
    [InlineData(SecondaryActivationResult.Completed, ActivationLaunchDecision.Redirected)]
    [InlineData(SecondaryActivationResult.TimedOut, ActivationLaunchDecision.TimedOut)]
    [InlineData(SecondaryActivationResult.Failed, ActivationLaunchDecision.Failed)]
    public void SecondaryDecisionReflectsBoundedRedirect(SecondaryActivationResult result, ActivationLaunchDecision expected)
    {
        var decision = SingleInstanceActivationPolicy.Decide(false, () => result);
        Assert.Equal(expected, decision);
        Assert.Equal(expected is ActivationLaunchDecision.TimedOut or ActivationLaunchDecision.Failed,
            SingleInstanceActivationPolicy.ShouldShowStartupFailure(decision));
    }
}
