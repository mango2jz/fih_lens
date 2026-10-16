using System.Net.Http;
using FihLens.Application.Dictionary;
using FihLens.Application.Translation;
using FihLens.Core.Models;
using FihLens.Infrastructure.Dictionary;
using FihLens.WinUI.ViewModels;
using FihLens.Windows.Pronunciation;
using Microsoft.UI.Xaml;

namespace FihLens.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? _window;
    private HttpClient? _translationClient;
    private TranslationService? _translationService;
    private string? _providerLabel;
    private string? _providerUnavailableReason;
    private readonly HashSet<MainWindowViewModel> _backends = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task _dictionaryInitialization = Task.CompletedTask;
    private DictionaryLookupService? _dictionary;
    private DictionarySource? _dictionarySource;
    private string? _dictionaryUnavailableReason;
    private bool _closing;
    private readonly WindowsPronunciationService _pronunciation = new();

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startup = TranslationStartup.Create(Environment.GetEnvironmentVariable);
        _translationClient = startup.Client;
        _translationService = startup.Provider is null ? null : new TranslationService(startup.Provider);
        _providerLabel = startup.Label;
        _providerUnavailableReason = startup.UnavailableReason;
        _window = new MainWindow(CreateBackend(), CreateBackend, ReleaseBackend, CancelSharedWork, FinalizeShutdownAsync);
        _window.Activate();
        _dictionaryInitialization = InitializeDictionaryAsync(_window.DispatcherQueue);
    }

    private MainWindowViewModel CreateBackend()
    {
        var backend = new MainWindowViewModel(_translationService, _providerLabel, _providerUnavailableReason, _pronunciation);
        if (_dictionary is not null || _dictionaryUnavailableReason is not null)
            backend.SetDictionaryAvailability(_dictionary, _dictionarySource, _dictionaryUnavailableReason);
        _backends.Add(backend);
        return backend;
    }

    private void ReleaseBackend(MainWindowViewModel backend)
    {
        backend.Close();
        _backends.Remove(backend);
    }

    private async Task InitializeDictionaryAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("FIHLENS_LOCAL_ASSET_ROOT") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FihLens", "local-assets");
            var dictionary = await JsonlDictionaryLookup.LoadAsync(root, _shutdown.Token);
            dispatcher.TryEnqueue(() =>
            {
                if (_closing) return;
                _dictionary = new DictionaryLookupService(dictionary);
                _dictionarySource = dictionary.Source;
                PublishDictionaryAvailability();
            });
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception exception)
        {
            var message = exception is DictionaryDataException failure ? failure.Message : "Local dictionary could not load.";
            dispatcher.TryEnqueue(() =>
            {
                if (_closing) return;
                _dictionaryUnavailableReason = $"{message} Install the approved dictionary data, then restart FIH Lens.";
                PublishDictionaryAvailability();
            });
        }
    }

    private void PublishDictionaryAvailability()
    {
        foreach (var backend in _backends.ToArray())
            backend.SetDictionaryAvailability(_dictionary, _dictionarySource, _dictionaryUnavailableReason);
    }

    private void CancelSharedWork()
    {
        if (_closing) return;
        _closing = true;
        var failures = new List<Exception>();
        try { _shutdown.Cancel(); }
        catch (Exception exception) { failures.Add(exception); }
        foreach (var backend in _backends.ToArray())
        {
            try { backend.Close(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private async Task FinalizeShutdownAsync()
    {
        try
        {
            await _dictionaryInitialization;
            await _pronunciation.DisposeAsync();
        }
        finally
        {
            _translationClient?.Dispose();
            _translationClient = null;
            _shutdown.Dispose();
            _backends.Clear();
        }
    }
}
