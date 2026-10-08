using System.Net.Http;
using FihLens.Application.Dictionary;
using FihLens.Application.Translation;
using FihLens.Infrastructure.Dictionary;
using FihLens.WinUI.ViewModels;
using Microsoft.UI.Xaml;

namespace FihLens.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private HttpClient? _translationClient;
    private readonly object _loadLock = new();
    private CancellationTokenSource? _dictionaryLoadCancellation;
    private Task? _dictionaryInitialization;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startup = TranslationStartup.Create(Environment.GetEnvironmentVariable);
        // The final window ends the process; process teardown owns this HTTP client.
        _translationClient = startup.Client;
        var viewModel = new MainWindowViewModel(startup.Provider is null ? null : new TranslationService(startup.Provider),
            startup.Label, startup.UnavailableReason);
        _window = new MainWindow(viewModel);
        var cancellation = new CancellationTokenSource();
        _dictionaryLoadCancellation = cancellation;
        _window.Closed += (_, _) =>
        {
            lock (_loadLock) _dictionaryLoadCancellation?.Cancel();
        };
        _window.Activate();
        _dictionaryInitialization = InitializeDictionaryAsync(viewModel, _window.DispatcherQueue, cancellation);
    }

    private async Task InitializeDictionaryAsync(MainWindowViewModel viewModel,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher, CancellationTokenSource cancellation)
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("FIHLENS_LOCAL_ASSET_ROOT") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FihLens", "local-assets");
            var dictionary = await JsonlDictionaryLookup.LoadAsync(root, cancellation.Token);
            dispatcher.TryEnqueue(() =>
            {
                if (!cancellation.IsCancellationRequested)
                    viewModel.SetDictionaryAvailability(new DictionaryLookupService(dictionary), dictionary.Source);
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            var message = exception is DictionaryDataException failure ? failure.Message : "Local dictionary could not load.";
            dispatcher.TryEnqueue(() => viewModel.SetDictionaryAvailability(null, null,
                $"{message} Install the approved dictionary data, then restart FIH Lens."));
        }
        finally
        {
            lock (_loadLock)
            {
                _dictionaryLoadCancellation = null;
                cancellation.Dispose();
            }
        }
    }
}
