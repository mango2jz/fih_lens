using FihLens.WinUI.Presentation;

namespace FihLens.Tests.WinUI;

public sealed class BackgroundShellControllerTests
{
    [Fact]
    public void CommandsShareCaptureCallbackAndStopRejectsCaptureButAllowsOpen()
    {
        var opened = 0;
        var captures = 0;
        var exits = 0;
        var controller = new BackgroundShellController(() => opened++, () => { captures++; return RegionCaptureRequestOutcome.Started; }, () => exits++);

        Assert.Equal(RegionCaptureRequestOutcome.Started, controller.Execute(ShellCommand.Capture));
        Assert.Equal(RegionCaptureRequestOutcome.Started, controller.Execute(ShellCommand.Capture));
        controller.RequestExit();
        Assert.Equal(RegionCaptureRequestOutcome.Stopping, controller.Execute(ShellCommand.Capture));
        controller.Execute(ShellCommand.Open);

        Assert.Equal(2, captures);
        Assert.Equal(1, exits);
        Assert.Equal(1, opened);
        Assert.True(controller.IsStopping);
    }

    [Theory]
    [InlineData(RegionCaptureRequestOutcome.Busy)]
    [InlineData(RegionCaptureRequestOutcome.ModalBlocked)]
    [InlineData(RegionCaptureRequestOutcome.Unavailable)]
    public void CaptureCommandReturnsFeedbackOutcomeToItsCompositionCaller(RegionCaptureRequestOutcome expected)
    {
        var controller = new BackgroundShellController(() => { }, () => expected, () => { });

        Assert.Equal(expected, controller.Execute(ShellCommand.Capture));
    }

    [Theory]
    [InlineData(true, false, LauncherCloseAction.Hide)]
    [InlineData(true, true, LauncherCloseAction.KeepVisible)]
    [InlineData(false, true, LauncherCloseAction.BeginExit)]
    public void ClosePolicyUsesTrayAndModalState(bool tray, bool modal, LauncherCloseAction expected)
    {
        var controller = Create();
        Assert.Equal(expected, controller.OnClose(tray, modal));
    }

    [Fact]
    public void PendingAndFailedExitTakePrecedenceOverTrayAndModal()
    {
        var controller = Create();
        controller.RequestExit();
        Assert.Equal(LauncherCloseAction.ObserveExit, controller.OnClose(false, true));
        controller.CompleteExit(false);
        Assert.Equal(LauncherCloseAction.RetryExit, controller.OnClose(false, true));
    }

    [Fact]
    public void ExitIsCoalescedWhilePendingAndSynchronousFailureCanRetry()
    {
        var calls = 0;
        bool fail = true;
        var controller = new BackgroundShellController(() => { }, () => RegionCaptureRequestOutcome.Started, () =>
        {
            calls++;
            if (fail) throw new InvalidOperationException("exit setup failed");
        });

        Assert.Throws<InvalidOperationException>(controller.RequestExit);
        Assert.False(controller.ExitPending);
        Assert.True(controller.ExitFailed);
        fail = false;
        controller.RequestExit();
        controller.RequestExit();
        Assert.True(controller.ExitPending);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ShellRetryOnlyRetriesUnavailableResourcesAndStopsAfterExit()
    {
        var hotkeyRetries = 0;
        var trayRetries = 0;
        var controller = Create();
        controller.SetAvailability(true, null, false, 5);

        Assert.True(controller.RetryShell(() => hotkeyRetries++, () => trayRetries++));
        Assert.Equal(0, hotkeyRetries);
        Assert.Equal(1, trayRetries);

        controller.RequestExit();
        Assert.False(controller.RetryShell(() => hotkeyRetries++, () => trayRetries++));
        Assert.Equal(0, hotkeyRetries);
        Assert.Equal(1, trayRetries);
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, true, false)]
    public void LauncherHidesOnlyWhenTrayOrHotkeyCanReopenIt(bool tray, bool hotkey, bool modal, bool expected)
    {
        Assert.Equal(expected, Create().CanHide(tray, hotkey, modal));
    }

    [Fact]
    public void LauncherStaysVisibleWhileExiting()
    {
        var controller = Create();
        controller.RequestExit();

        Assert.False(controller.CanHide(true, true, false));
    }

    private static BackgroundShellController Create() => new(() => { }, () => RegionCaptureRequestOutcome.Started, () => { });
}
