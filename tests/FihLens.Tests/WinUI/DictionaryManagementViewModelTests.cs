using FihLens.Application.Dictionary;
using FihLens.Core.Enums;
using FihLens.WinUI.ViewModels;

namespace FihLens.Tests.WinUI;

public sealed class DictionaryManagementViewModelTests
{
    [Fact]
    public async Task SetupAndSkipNeverInstallAndDismissalPersists()
    {
        var manager = new Manager(downloadable: false);
        var model = new DictionaryManagementViewModel(manager);
        Assert.True(await model.ShouldOfferSetupAsync());
        await model.LoadAsync();
        Assert.False(model.InstallCommand.CanExecute(null));
        Assert.Contains("Japanese → English", Assert.Single(model.Packs).Title);
        await model.CloseAsync();
        Assert.Equal(0, manager.InstallCalls);
        Assert.False(await new DictionaryManagementViewModel(manager).ShouldOfferSetupAsync());
    }

    [Fact]
    public async Task ClosingDuringExplicitInstallCancelsAndDrainsBeforeDismissal()
    {
        var manager = new Manager(downloadable: true);
        var model = new DictionaryManagementViewModel(manager);
        await model.LoadAsync();
        var work = model.InstallCommand.ExecuteAsync(null);
        await manager.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(model.IsBusy);
        Assert.False(model.InstallCommand.CanExecute(null));
        await model.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await work;
        Assert.True(manager.Token.IsCancellationRequested);
        Assert.True(manager.Dismissed);
        Assert.Equal(1, manager.InstallCalls);
        Assert.False(model.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreferenceSaveFailureDoesNotPreventIdempotentClosure()
    {
        var manager = new Manager(downloadable: false, failDismissal: true);
        var model = new DictionaryManagementViewModel(manager);
        await model.LoadAsync();

        await model.CloseAsync();
        await model.CloseAsync();

        Assert.False(model.InstallCommand.CanExecute(null));
        Assert.False(manager.Dismissed);
        Assert.Contains("preference", model.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-path", model.StatusText);
    }

    [Fact]
    public async Task ConcurrentCloseWaitsForPreferenceSaveOnce()
    {
        var manager = new Manager(downloadable: false, delayDismissal: true);
        var model = new DictionaryManagementViewModel(manager);
        await model.LoadAsync();

        var firstClose = model.CloseAsync();
        await manager.DismissalStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondClose = model.CloseAsync();

        Assert.False(firstClose.IsCompleted);
        Assert.False(secondClose.IsCompleted);
        Assert.Equal(1, manager.DismissalCalls);
        Assert.False(model.IsEditingEnabled);
        Assert.False(model.InstallCommand.CanExecute(null));

        manager.ReleaseDismissal.SetResult();
        await Task.WhenAll(firstClose, secondClose);
        await model.CloseAsync();
        Assert.Equal(1, manager.DismissalCalls);
    }

    [Fact]
    public async Task ConcurrentCloseDuringOperationWaitsForDrainAndSave()
    {
        var manager = new Manager(downloadable: true, delayDismissal: true, delayInstall: true);
        var model = new DictionaryManagementViewModel(manager);
        await model.LoadAsync();
        var work = model.InstallCommand.ExecuteAsync(null);
        await manager.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var firstClose = model.CloseAsync();
        var secondClose = model.CloseAsync();
        Assert.True(manager.Token.IsCancellationRequested);
        Assert.False(firstClose.IsCompleted);
        Assert.False(secondClose.IsCompleted);
        Assert.Equal(0, manager.DismissalCalls);

        manager.ReleaseInstall.SetResult();
        await manager.DismissalStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(firstClose.IsCompleted);
        Assert.False(secondClose.IsCompleted);
        Assert.Equal(1, manager.DismissalCalls);
        Assert.False(model.IsEditingEnabled);
        Assert.False(model.InstallCommand.CanExecute(null));

        manager.ReleaseDismissal.SetResult();
        await Task.WhenAll(firstClose, secondClose, work);
        Assert.Equal(1, manager.InstallCalls);
        Assert.Equal(1, manager.DismissalCalls);
    }

    [Fact]
    public async Task ConcurrentClosePreferenceFailureIsSafe()
    {
        var manager = new Manager(downloadable: false, delayDismissal: true, failDismissal: true);
        var model = new DictionaryManagementViewModel(manager);
        await model.LoadAsync();

        var firstClose = model.CloseAsync();
        await manager.DismissalStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondClose = model.CloseAsync();
        manager.ReleaseDismissal.SetResult();
        await Task.WhenAll(firstClose, secondClose);

        Assert.Equal(1, manager.DismissalCalls);
        Assert.False(model.IsEditingEnabled);
        Assert.Contains("preference", model.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-path", model.StatusText);
    }

    private sealed class Manager(bool downloadable, bool failDismissal = false, bool delayDismissal = false, bool delayInstall = false) : IDictionaryPackManager
    {
        public bool Dismissed { get; private set; }
        public int InstallCalls { get; private set; }
        public int DismissalCalls { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DismissalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDismissal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseInstall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<DictionaryPackStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DictionaryPackStatus>>([new(new("jmdict-ja-en", "1", "JMdict", Language.Japanese,
                Language.English, "CC-BY-SA-4.0", "https://example.test/source", "1", 100, 500,
                new string('0', 64), new string('1', 64), downloadable ? new Uri("https://example.test/pack.zip") : null), DictionaryPackState.NotInstalled)]);
        public async Task InstallAsync(string packId, IProgress<DictionaryPackProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            Token = cancellationToken;
            Started.SetResult();
            if (delayInstall) await ReleaseInstall.Task;
            else await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public Task ScheduleRemovalAsync(string packId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> GetSetupDismissedAsync(CancellationToken cancellationToken = default) => Task.FromResult(Dismissed);
        public async Task SetSetupDismissedAsync(bool dismissed, CancellationToken cancellationToken = default)
        {
            DismissalCalls++;
            DismissalStarted.SetResult();
            if (delayDismissal) await ReleaseDismissal.Task;
            if (failDismissal) throw new IOException("private-path");
            Dismissed = dismissed;
        }
    }
}
