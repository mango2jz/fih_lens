using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FihLens.Application.Dictionary;
using FihLens.Core.Models;

namespace FihLens.WinUI.ViewModels;

public sealed record DictionaryPackRowViewModel(DictionaryPackStatus Status, bool HasInstallation)
{
    public string PackId => Status.Definition.PackId;
    public string Title => $"{Status.Definition.Name} · {Status.Definition.SourceLanguage} → {Status.Definition.TargetLanguage}";
    public string Details => $"Word dictionary · {Status.Definition.DownloadBytes:N0} download bytes · {Status.Definition.InstalledBytes:N0} installed bytes\n" +
        $"Source: {Status.Definition.SourceUrl}\nRevision: {Status.Definition.SourceRevision}\nLicense: {Status.Definition.License}";
    public string StatusText => Status.Message ?? (Status.Definition.DownloadUri is null
        ? $"{Status.State} · Pack publication is pending; download unavailable."
        : Status.State switch
        {
            DictionaryPackState.InstallPendingRestart => "Installed — restart to enable",
            DictionaryPackState.UpdatePendingRestart => "Update ready — restart to enable",
            DictionaryPackState.RemovalPendingRestart => "Removal scheduled — restart",
            _ => Status.State.ToString()
        });
    public bool CanInstall => Status.Definition.DownloadUri is not null && Status.State is
        DictionaryPackState.NotInstalled or DictionaryPackState.Failed or DictionaryPackState.Unavailable;
    public bool CanRemove => HasInstallation && Status.State != DictionaryPackState.RemovalPendingRestart;

    // One switch per direction: on while installed or waiting for a restart to enable.
    public string Flag => $"{TranslationPopupViewModel.LanguageCode(Status.Definition.SourceLanguage)} → {TranslationPopupViewModel.LanguageCode(Status.Definition.TargetLanguage)}";
    public string DirectionName => $"{Status.Definition.SourceLanguage} → {Status.Definition.TargetLanguage}";
    public bool IsOn => HasInstallation && Status.State != DictionaryPackState.RemovalPendingRestart;
    public bool IsAvailable => Status.Definition.DownloadUri is not null || HasInstallation;
    public bool CanToggle => IsOn ? CanRemove : CanInstall;
    public bool NeedsRestart => Status.State is DictionaryPackState.InstallPendingRestart or DictionaryPackState.UpdatePendingRestart
        or DictionaryPackState.RemovalPendingRestart;
    public double RowOpacity => IsAvailable ? 1 : 0.5;
    public string Summary => !IsAvailable ? "Not available offline yet · Ask AI still works" : Status.Message ?? Status.State switch
    {
        DictionaryPackState.Installed => $"{Status.Definition.Name} · installed",
        DictionaryPackState.InstallPendingRestart => $"{Status.Definition.Name} · installed",
        DictionaryPackState.UpdatePendingRestart => $"{Status.Definition.Name} · update ready",
        DictionaryPackState.RemovalPendingRestart => $"{Status.Definition.Name} · removal scheduled",
        DictionaryPackState.Failed => $"{Status.Definition.Name} · last install failed",
        _ => $"{Status.Definition.Name} · {Status.Definition.DownloadBytes / 1_048_576.0:0.#} MB download"
    };
}

public partial class DictionaryManagementViewModel : ObservableObject
{
    private readonly IDictionaryPackManager? _manager;
    private CancellationTokenSource? _operation;
    private Task _work = Task.CompletedTask;
    private Task? _closeTask;
    private bool _closed;

    public DictionaryManagementViewModel(IDictionaryPackManager? manager)
    {
        _manager = manager;
        InstallCommand = new AsyncRelayCommand(() => StartAsync(InstallCoreAsync), () => !_closed && !IsBusy && SelectedPack?.CanInstall == true);
        RemoveCommand = new AsyncRelayCommand(() => StartAsync(RemoveCoreAsync), () => !_closed && !IsBusy && SelectedPack?.CanRemove == true);
        CancelCommand = new RelayCommand(() => _operation?.Cancel(), () => IsBusy);
    }

    public IAsyncRelayCommand InstallCommand { get; }
    public IAsyncRelayCommand RemoveCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public bool IsEditingEnabled => !_closed && !IsBusy;

    [ObservableProperty]
    public partial IReadOnlyList<DictionaryPackRowViewModel> Packs { get; set; } = [];
    [ObservableProperty]
    public partial DictionaryPackRowViewModel? SelectedPack { get; set; }
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Switching a direction on downloads it. Closing keeps the app usable.";
    [ObservableProperty]
    public partial string LegacyStatusText { get; set; } = "English → Vietnamese · Checking existing approved local data.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingEnabled))]
    public partial bool IsBusy { get; set; }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnSelectedPackChanged(DictionaryPackRowViewModel? value) => NotifyCommands();
    private void NotifyCommands()
    {
        InstallCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    public partial bool LegacyInstalled { get; set; }
    public string LegacySummary => LegacyInstalled ? "Installed · managed by the existing setup" : "Not installed";

    partial void OnLegacyInstalledChanged(bool value) => OnPropertyChanged(nameof(LegacySummary));

    /// <summary>The switch on a pack row: on installs, off schedules removal. False when the pack did not reach that state.</summary>
    public async Task<bool> SetPackEnabledAsync(DictionaryPackRowViewModel pack, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(pack);
        if (_closed || IsBusy || enabled == pack.IsOn || !pack.CanToggle) return false;
        SelectedPack = pack;
        await (enabled ? InstallCommand : RemoveCommand).ExecuteAsync(null);
        // RunAsync swallows failures and cancellation, so check the reloaded row rather than trusting the command.
        return Packs.Any(row => row.PackId == pack.PackId && row.IsOn == enabled);
    }

    public void SetLegacySource(DictionarySource? source, string? reason)
    {
        LegacyInstalled = source is not null;
        LegacyStatusText = source is null
        ? $"English → Vietnamese · {reason ?? "Existing approved data is not installed; use the original dictionary setup."}"
        : $"English → Vietnamese · {source.Name} · {source.QualityStatus}\nLicense: {source.License}\nSource: {source.SourceUrl}\nManaged by the existing setup; this dialog does not remove it.";
    }

    public Task<bool> ShouldOfferSetupAsync(CancellationToken token = default) => _manager is null
        ? Task.FromResult(false) : ShouldOfferCoreAsync(token);
    private async Task<bool> ShouldOfferCoreAsync(CancellationToken token) => !await _manager!.GetSetupDismissedAsync(token);

    public Task LoadAsync() => StartAsync(RefreshAsync);
    private Task StartAsync(Func<CancellationToken, Task> action)
    {
        if (_closed || IsBusy) return Task.CompletedTask;
        return _work = RunAsync(action);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        IsBusy = true;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { if (!_closed) StatusText = "Operation canceled or timed out; the last working installation is preserved."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException)
        { if (!_closed) StatusText = "Dictionary operation could not complete. The last working installation is preserved; retry when ready."; }
        finally
        {
            _operation = null;
            IsBusy = false;
        }
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        if (_manager is null) { StatusText = "Dictionary management is unavailable. Check the local asset folder configuration."; return; }
        var statuses = await _manager.GetStatusAsync(token);
        token.ThrowIfCancellationRequested();
        if (_closed) return;
        string? selected = SelectedPack?.PackId;
        Packs = statuses.GroupBy(status => status.Definition.PackId).Select(group =>
            new DictionaryPackRowViewModel(group.OrderByDescending(status => status.Definition.Version, StringComparer.Ordinal).First(),
                group.Any(status => status.State is DictionaryPackState.Installed or DictionaryPackState.Failed or DictionaryPackState.InstallPendingRestart or
                    DictionaryPackState.UpdatePendingRestart or DictionaryPackState.RemovalPendingRestart))).ToArray();
        SelectedPack = Packs.FirstOrDefault(pack => pack.PackId == selected) ?? Packs.FirstOrDefault();
        if (Packs.Count == 0) StatusText = "Japanese → English · No published pack is available yet. Dictionary downloads are optional; Ask AI remains explicit.";
    }

    private async Task InstallCoreAsync(CancellationToken token)
    {
        string id = SelectedPack!.PackId;
        var operation = _operation;
        var progress = new Progress<DictionaryPackProgress>(value =>
        {
            if (!_closed && ReferenceEquals(_operation, operation) && operation is { IsCancellationRequested: false })
                StatusText = value.TotalBytes is long total ? $"{value.Stage}: {value.CompletedBytes:N0} / {total:N0} bytes" : value.Stage;
        });
        await _manager!.InstallAsync(id, progress, token);
        await RefreshAsync(token);
        if (!_closed) StatusText = "Installed — restart FIH Lens to enable this version.";
    }

    private async Task RemoveCoreAsync(CancellationToken token)
    {
        await _manager!.ScheduleRemovalAsync(SelectedPack!.PackId, token);
        await RefreshAsync(token);
        if (!_closed) StatusText = "Removal scheduled — restart FIH Lens. Other dictionaries and saved data are preserved.";
    }

    public Task CloseAsync() => _closeTask ??= CloseCoreAsync();

    private async Task CloseCoreAsync()
    {
        _closed = true;
        _operation?.Cancel();
        await _work;
        if (_manager is not null)
        {
            try { await _manager.SetSetupDismissedAsync(true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine($"Dictionary stage=setup-dismissed exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
                StatusText = "Setup preference could not be saved. You can skip again next time.";
            }
        }
        NotifyCommands();
    }
}
