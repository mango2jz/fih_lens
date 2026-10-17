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

    private sealed class Manager(bool downloadable, bool failDismissal = false) : IDictionaryPackManager
    {
        public bool Dismissed { get; private set; }
        public int InstallCalls { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<DictionaryPackStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DictionaryPackStatus>>([new(new("jmdict-ja-en", "1", "JMdict", Language.Japanese,
                Language.English, "CC-BY-SA-4.0", "https://example.test/source", "1", 100, 500,
                new string('0', 64), new string('1', 64), downloadable ? new Uri("https://example.test/pack.zip") : null), DictionaryPackState.NotInstalled)]);
        public async Task InstallAsync(string packId, IProgress<DictionaryPackProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            Token = cancellationToken;
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public Task ScheduleRemovalAsync(string packId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> GetSetupDismissedAsync(CancellationToken cancellationToken = default) => Task.FromResult(Dismissed);
        public Task SetSetupDismissedAsync(bool dismissed, CancellationToken cancellationToken = default)
        {
            if (failDismissal) return Task.FromException(new IOException("private-path"));
            Dismissed = dismissed;
            return Task.CompletedTask;
        }
    }
}
