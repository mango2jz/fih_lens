using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FihLens.Application.Dictionary;
using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;

namespace FihLens.WinUI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly TranslationService? _translationService;
    private readonly string? _providerLabel;
    private readonly string _aiUnavailableReason;
    private DictionaryLookupService? _dictionaryService;
    private CancellationTokenSource? _operationCancellation;
    private long _generation;
    private bool _closed;
    private bool _dictionaryOperation;

    public MainWindowViewModel(TranslationService? translationService = null, string? providerLabel = null,
        string? aiUnavailableReason = null)
    {
        _translationService = translationService;
        _providerLabel = providerLabel;
        _aiUnavailableReason = aiUnavailableReason ?? "Ask AI is unavailable. Configure Gemini or DeepSeek, then restart FIH Lens.";
        AiStatusText = translationService is null ? _aiUnavailableReason : "Ready";
    }

    public string AiSendNotice => _translationService is null
        ? _aiUnavailableReason : $"Sends this text to {_providerLabel}.";
    public string AiSourceLabel => _translationService is null ? "AI unavailable" : $"Generated AI output · {_providerLabel}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookupDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskAiCommand))]
    public partial string InputText { get; set; } = "Hello world";

    [ObservableProperty]
    public partial TranslationResult? Result { get; set; }
    [ObservableProperty]
    public partial DictionaryLookupResult? DictionaryResult { get; set; }
    [ObservableProperty]
    public partial string DictionaryText { get; set; } = "";
    [ObservableProperty]
    public partial string DictionaryInputSnapshot { get; set; } = "";
    [ObservableProperty]
    public partial string AiInputSnapshot { get; set; } = "";
    [ObservableProperty]
    public partial string DictionarySourceLabel { get; set; } = "Local dictionary";
    [ObservableProperty]
    public partial string DictionarySourceDetails { get; set; } = "";
    [ObservableProperty]
    public partial string DictionaryStatusText { get; set; } = "Loading local dictionary…";
    [ObservableProperty]
    public partial string? DictionaryErrorMessage { get; set; }
    [ObservableProperty]
    public partial string AiStatusText { get; set; } = "Ready";
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingEnabled))]
    [NotifyCanExecuteChangedFor(nameof(LookupDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskAiCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    public bool IsEditingEnabled => !_closed && !IsBusy;
    private bool CanStart() => !_closed && !IsBusy && !string.IsNullOrWhiteSpace(InputText);
    private bool CanLookupDictionary() => CanStart() && _dictionaryService is not null;
    private bool CanAskAi() => CanStart() && _translationService is not null;
    private bool CanCancel() => !_closed && IsBusy && _operationCancellation is { IsCancellationRequested: false };

    public void SetDictionaryAvailability(DictionaryLookupService? service, DictionarySource? source,
        string? unavailableReason = null)
    {
        if (_closed) return;
        if (service is not null && source is null) throw new ArgumentNullException(nameof(source));
        _dictionaryService = service;
        DictionaryStatusText = service is null
            ? unavailableReason ?? "Local dictionary unavailable. Install the approved dictionary data, then restart FIH Lens."
            : "Ready · exact whole word or phrase lookup";
        DictionarySourceLabel = source is null ? "Local dictionary unavailable"
            : $"{source.Name} · {source.QualityStatus} · sample review only";
        DictionarySourceDetails = source is null ? "" :
            $"Source: {source.Name} ({source.SourceId})\nRevision: {source.Revision}\nLanguages: {source.SourceLanguage} → {source.TargetLanguage}\nQuality: {source.QualityStatus}\nHuman review scope: {source.HumanReviewScope}\nLicense: {source.License}\nSource URL: {source.SourceUrl}\nAttribution: {source.Attribution}\n\n" +
            string.Join("\n\n", source.Notices.Select(notice => $"{notice.Key}\n{notice.Value}"));
        LookupDictionaryCommand.NotifyCanExecuteChanged();
    }

    partial void OnInputTextChanged(string value)
    {
        InvalidateOperation();
        Result = null;
        DictionaryResult = null;
        DictionaryText = "";
        DictionaryInputSnapshot = "";
        AiInputSnapshot = "";
        ErrorMessage = null;
        DictionaryErrorMessage = null;
        if (_dictionaryService is not null) DictionaryStatusText = "Ready · exact whole word or phrase lookup";
        AiStatusText = _translationService is null ? _aiUnavailableReason : "Ready";
    }

    [RelayCommand(CanExecute = nameof(CanLookupDictionary))]
    private Task LookupDictionaryAsync() => RunAsync(dictionary: true);

    [RelayCommand(CanExecute = nameof(CanAskAi))]
    private Task AskAiAsync() => RunAsync(dictionary: false);

    private async Task RunAsync(bool dictionary)
    {
        if (dictionary ? !CanLookupDictionary() : !CanAskAi()) return;
        var text = InputText;
        var generation = ++_generation;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        _dictionaryOperation = dictionary;
        if (dictionary)
        {
            DictionaryResult = null;
            DictionaryText = "";
            DictionaryErrorMessage = null;
            DictionaryInputSnapshot = text;
            DictionaryStatusText = "Looking up locally…";
        }
        else
        {
            Result = null;
            ErrorMessage = null;
            AiInputSnapshot = text;
            AiStatusText = $"Asking {_providerLabel}…";
        }
        IsBusy = true;
        try
        {
            if (dictionary)
            {
                var result = await _dictionaryService!.LookupAsync(text, cancellation.Token);
                if (!IsCurrent(generation, cancellation.Token)) return;
                DictionaryResult = result;
                DictionaryText = string.Join("\n\n", result.Entries.Select(entry =>
                    $"{entry.Headword}\n{entry.PartOfSpeech}\n{string.Join("\n", entry.Senses)}\n{entry.Example}\nSource: {entry.SourceId}\nArticle: {entry.SourceArticleId}\nOrigin: {entry.SourceOrigin}"));
                DictionaryStatusText = result.IsMatch ? "Dictionary match" : "No exact dictionary entry for this text.";
            }
            else
            {
                var request = new TranslationRequest(text, Language.English, Language.Vietnamese);
                var result = await _translationService!.TranslateAsync(request, cancellation.Token);
                if (!IsCurrent(generation, cancellation.Token)) return;
                Result = result;
                AiStatusText = "AI translation complete";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancel/edit/close already invalidated the generation and updated its visible state.
        }
        catch (Exception exception)
        {
            if (!IsCurrent(generation, cancellation.Token)) return;
            if (dictionary)
            {
                DictionaryErrorMessage = exception is ArgumentException
                    ? "Enter valid text between 1 and 2,000 characters for dictionary lookup."
                    : "Dictionary lookup failed. Please try again.";
                DictionaryStatusText = "Dictionary lookup failed";
            }
            else
            {
                ErrorMessage = exception is TranslationProviderException failure
                    ? failure.Message : "Translation failed. Please try again.";
                AiStatusText = "AI translation failed";
            }
        }
        finally
        {
            _operationCancellation = null;
            if (!_closed) IsBusy = false;
        }
    }

    private bool IsCurrent(long generation, CancellationToken token) =>
        !_closed && generation == _generation && !token.IsCancellationRequested;

    private void InvalidateOperation()
    {
        ++_generation;
        _operationCancellation?.Cancel();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!CanCancel()) return;
        InvalidateOperation();
        if (_dictionaryOperation) DictionaryStatusText = "Dictionary lookup canceled";
        else AiStatusText = "AI translation canceled";
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        InvalidateOperation();
        OnPropertyChanged(nameof(IsEditingEnabled));
        LookupDictionaryCommand.NotifyCanExecuteChanged();
        AskAiCommand.NotifyCanExecuteChanged();
    }
}
