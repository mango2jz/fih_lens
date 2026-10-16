using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FihLens.Application.Dictionary;
using FihLens.Application.Translation;
using FihLens.Application.Pronunciation;
using FihLens.Core.Enums;
using FihLens.Core.Models;

namespace FihLens.WinUI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly TranslationService? _translationService;
    private readonly string? _providerLabel;
    private readonly string _aiUnavailableReason;
    private DictionaryLookupService? _dictionaryService;
    private string _dictionaryUnavailableReason = "Local dictionary unavailable. Install the approved dictionary data, then restart FIH Lens.";
    private CancellationTokenSource? _operationCancellation;
    private long _generation;
    private bool _closed;
    private bool _dictionaryOperation;

    public IReadOnlyList<Language> AvailableLanguages { get; } = [Language.English, Language.Vietnamese, Language.Japanese];

    public MainWindowViewModel(TranslationService? translationService = null, string? providerLabel = null,
        string? aiUnavailableReason = null, IPronunciationService? pronunciationService = null)
    {
        Pronunciation = new PronunciationViewModel(pronunciationService);
        Pronunciation.SetSource(InputText, SourceLanguage);
        _translationService = translationService;
        _providerLabel = providerLabel;
        _aiUnavailableReason = aiUnavailableReason ?? "Ask AI is unavailable. Configure Gemini or DeepSeek, then restart FIH Lens.";
        AiStatusText = translationService is null ? _aiUnavailableReason : "Ready";
    }

    public string AiSendNotice => _translationService is null
        ? _aiUnavailableReason : $"Sends this text to {_providerLabel}.";
    public PronunciationViewModel Pronunciation { get; }

    [ObservableProperty]
    public partial IReadOnlyList<VocabularyCardViewModel> WordCards { get; set; } = [];

    partial void OnResultChanged(TranslationResult? value)
    {
        Pronunciation.Stop();
        Pronunciation.SetSource(value?.OriginalText ?? InputText, value?.SourceLanguage ?? SourceLanguage);
        WordCards = value?.Vocabulary.Select(item => new VocabularyCardViewModel(item, Pronunciation)).ToArray() ?? [];
    }
    public string AiSourceLabel => _translationService is null ? "AI unavailable" : $"Generated AI output · {_providerLabel}";
    public string SourceLanguageLabel => $"From: {LanguageName(SourceLanguage)}";
    public string TargetLanguageLabel => $"To: {LanguageName(TargetLanguage)}";
    public string LanguagePairLabel => $"{LanguageName(SourceLanguage)} → {LanguageName(TargetLanguage)}";
    public string? LanguagePairUnavailableReason =>
        !AvailableLanguages.Contains(SourceLanguage) || !AvailableLanguages.Contains(TargetLanguage)
            ? "Choose English, Vietnamese, or Japanese for both languages."
            : SourceLanguage == TargetLanguage ? "Choose two different languages to translate." : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookupDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskAiCommand))]
    public partial string InputText { get; set; } = "Hello world";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookupDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskAiCommand))]
    public partial Language SourceLanguage { get; set; } = Language.English;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookupDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskAiCommand))]
    public partial Language TargetLanguage { get; set; } = Language.Vietnamese;

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
    private bool CanStart() => !_closed && !IsBusy && !string.IsNullOrWhiteSpace(InputText) && LanguagePairUnavailableReason is null;
    private bool CanLookupDictionary() => CanStart() && SourceLanguage == Language.English && TargetLanguage == Language.Vietnamese && _dictionaryService is not null;
    private bool CanAskAi() => CanStart() && _translationService is not null;
    private bool CanCancel() => !_closed && IsBusy && _operationCancellation is { IsCancellationRequested: false };

    public void SetDictionaryAvailability(DictionaryLookupService? service, DictionarySource? source,
        string? unavailableReason = null)
    {
        if (_closed) return;
        if (service is not null && source is null) throw new ArgumentNullException(nameof(source));
        _dictionaryService = service;
        _dictionaryUnavailableReason = unavailableReason ?? "Local dictionary unavailable. Install the approved dictionary data, then restart FIH Lens.";
        UpdateDictionaryStatus();
        DictionarySourceLabel = source is null ? "Local dictionary unavailable"
            : $"{source.Name} · {source.QualityStatus} · sample review only";
        DictionarySourceDetails = source is null ? "" :
            $"Source: {source.Name} ({source.SourceId})\nRevision: {source.Revision}\nLanguages: {source.SourceLanguage} → {source.TargetLanguage}\nQuality: {source.QualityStatus}\nHuman review scope: {source.HumanReviewScope}\nLicense: {source.License}\nSource URL: {source.SourceUrl}\nAttribution: {source.Attribution}\n\n" +
            string.Join("\n\n", source.Notices.Select(notice => $"{notice.Key}\n{notice.Value}"));
        LookupDictionaryCommand.NotifyCanExecuteChanged();
    }

    private bool IsDictionaryPair => SourceLanguage == Language.English && TargetLanguage == Language.Vietnamese;

    private void UpdateDictionaryStatus() => DictionaryStatusText = !IsDictionaryPair
        ? "Local dictionary is available for English→Vietnamese only; use Ask AI for this pair."
        : _dictionaryService is not null ? "Ready · exact whole word or phrase lookup" : _dictionaryUnavailableReason;

    partial void OnSourceLanguageChanged(Language value) => OnLanguagePairChanged();
    partial void OnTargetLanguageChanged(Language value) => OnLanguagePairChanged();

    private void OnLanguagePairChanged()
    {
        OnPropertyChanged(nameof(SourceLanguageLabel));
        OnPropertyChanged(nameof(TargetLanguageLabel));
        OnPropertyChanged(nameof(LanguagePairLabel));
        OnPropertyChanged(nameof(LanguagePairUnavailableReason));
        InvalidateOperation();
        ClearResultsForEdit();
        LookupDictionaryCommand.NotifyCanExecuteChanged();
        AskAiCommand.NotifyCanExecuteChanged();
    }

    partial void OnInputTextChanged(string value)
    {
        InvalidateOperation();
        ClearResultsForEdit();
    }

    private void ClearResultsForEdit()
    {
        Result = null;
        DictionaryResult = null;
        DictionaryText = "";
        DictionaryInputSnapshot = "";
        AiInputSnapshot = "";
        ErrorMessage = null;
        DictionaryErrorMessage = null;
        UpdateDictionaryStatus();
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
                var sourceLanguage = SourceLanguage;
                var targetLanguage = TargetLanguage;
                var request = new TranslationRequest(text, sourceLanguage, targetLanguage);
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

    private static string LanguageName(Language language) => language switch
    {
        Language.English => "English",
        Language.Vietnamese => "Vietnamese",
        Language.Japanese => "Japanese",
        Language.Auto => "Auto",
        _ => language.ToString()
    };

    private bool IsCurrent(long generation, CancellationToken token) =>
        !_closed && generation == _generation && !token.IsCancellationRequested;

    private void InvalidateOperation()
    {
        Pronunciation.Stop();
        Pronunciation.SetSource(InputText, SourceLanguage);
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
        Pronunciation.Close();
        _closed = true;
        InvalidateOperation();
        OnPropertyChanged(nameof(IsEditingEnabled));
        LookupDictionaryCommand.NotifyCanExecuteChanged();
        AskAiCommand.NotifyCanExecuteChanged();
    }
}
