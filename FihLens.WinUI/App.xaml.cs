using System.Net.Http;
using FihLens.Application.Dictionary;
using FihLens.Application.Translation;
using FihLens.Core.Models;
using FihLens.Core.Enums;
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
    private DictionaryPackManager? _packManager;
    private IndexedJapaneseDictionaryLookup? _japaneseLookup;
    private DictionaryLookupService? _japaneseDictionary;
    private DictionarySource? _japaneseSource;
    private string? _japaneseUnavailableReason;
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
        try { _packManager = new DictionaryPackManager(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Dictionary stage=asset-root exceptionType={ex.GetType().FullName} hresult=0x{ex.HResult:X8}");
            _japaneseUnavailableReason = "Dictionary management could not open the configured local asset folder.";
        }
        _window = new MainWindow(CreateBackend(), CreateBackend, ReleaseBackend, CancelSharedWork, FinalizeShutdownAsync, _packManager);
        _window.Activate();
        _dictionaryInitialization = Task.WhenAll(InitializeDictionaryAsync(_window.DispatcherQueue),
            InitializeJapaneseDictionaryAsync(_window.DispatcherQueue));
    }

    private MainWindowViewModel CreateBackend()
    {
        var backend = new MainWindowViewModel(_translationService, _providerLabel, _providerUnavailableReason, _pronunciation);
        if (_dictionary is not null || _dictionaryUnavailableReason is not null)
            backend.SetDictionaryAvailability(_dictionary, _dictionarySource, _dictionaryUnavailableReason);
        if (_japaneseDictionary is not null || _japaneseUnavailableReason is not null)
            backend.SetDictionaryAvailability(Language.Japanese, Language.English, _japaneseDictionary, _japaneseSource, _japaneseUnavailableReason);
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
        {
            backend.SetDictionaryAvailability(_dictionary, _dictionarySource, _dictionaryUnavailableReason);
            backend.SetDictionaryAvailability(Language.Japanese, Language.English, _japaneseDictionary, _japaneseSource, _japaneseUnavailableReason);
        }
        _window?.SetLegacyDictionarySource(_dictionarySource, _dictionaryUnavailableReason);
    }

    private async Task InitializeJapaneseDictionaryAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        try
        {
            if (_packManager is null) return;
            _japaneseLookup = await _packManager.OpenInstalledAsync(_shutdown.Token);
            dispatcher.TryEnqueue(() =>
            {
                if (_closing) return;
                _japaneseSource = _japaneseLookup?.Source;
                _japaneseDictionary = _japaneseLookup is null ? null : new DictionaryLookupService(_japaneseLookup);
                _japaneseUnavailableReason = _japaneseLookup is null
                    ? "Japanese→English dictionary is not installed. Open Dictionaries for availability and setup." : null;
                PublishDictionaryAvailability();
            });
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Dictionary stage=japanese-startup exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
            dispatcher.TryEnqueue(() =>
            {
                if (_closing) return;
                _japaneseUnavailableReason = "Japanese dictionary could not load. The English→Vietnamese dictionary remains independent.";
                PublishDictionaryAvailability();
            });
        }
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
            if (_japaneseLookup is not null) await _japaneseLookup.DisposeAsync();
            await _pronunciation.DisposeAsync();
        }
        finally
        {
            _translationClient?.Dispose();
            _translationClient = null;
            _packManager?.Dispose();
            _shutdown.Dispose();
            _backends.Clear();
        }
    }
}
