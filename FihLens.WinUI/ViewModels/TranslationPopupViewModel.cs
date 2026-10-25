using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FihLens.Core.Models;
using FihLens.Application.Clipboard;
using FihLens.Application.Dictionary;
using FihLens.Core.Enums;
using System.Windows.Input;

namespace FihLens.WinUI.ViewModels;

/// <summary>A display snapshot; acquiring text and requesting translations belong to the session owner.</summary>
public sealed class TranslationPopupViewModel : ObservableObject
{
    private bool _isClosed;
    private bool _isExpanded;
    private IClipboardWriter? _clipboard;
    private ICommand? _askAi;
    private ICommand? _cancel;
    private ICommand? _previewSave;
    private Action? _openDetail;
    private ICommand? _previewUndo;
    private ICommand? _selectAgain;
    private CancellationTokenSource? _copyCancellation;
    private long _copyGeneration;
    private string _copyFeedback = "";
    private bool _copyFailed;
    private string _dictionaryStatus = "";
    private string _aiStatus = "";
    private bool _isBusy;
    private bool _hasBackendError;
    private bool _captureStage;
    private string _backendErrorMessage = "";
    private string _aiSendNotice = "AI unavailable: no configured provider command.";
    private PopupPreviewState _previewState;
    private IReadOnlyList<VocabularyItem> _vocabulary = [];
    private DictionaryLookupResult? _dictionaryResult;
    private bool _dictionaryOnly;
    // Word picker: the backend list it was built from, the whole-text candidate and the current selection.
    private IReadOnlyList<DictionaryWordResult>? _wordSource;
    private IReadOnlyList<DictionaryWordResult> _words = [];
    private DictionaryWordResult? _phrase;
    private bool _phraseMode;
    private int _selectedWord = -1;

    public Language SourceLanguage { get; private set; } = Language.English;
    public Language TargetLanguage { get; private set; } = Language.Vietnamese;
    public string InputLabel => SourceLanguage.ToString();
    public string LanguagePairLabel => $"{SourceLanguage} → {TargetLanguage}";
    public string LanguagePairCode => $"{LanguageCode(SourceLanguage)} → {LanguageCode(TargetLanguage)}";

    /// <summary>Two-letter uppercase code for pills and headers (EN, VI, JA).</summary>
    public static string LanguageCode(Language language) => language switch
    {
        Language.English => "EN", Language.Vietnamese => "VI", Language.Japanese => "JA", _ => "AUTO"
    };
    public PronunciationViewModel Pronunciation { get; private set; } = new();
    public IReadOnlyList<VocabularyCardViewModel> WordCards { get; private set; } = [];
    public IReadOnlyList<DictionaryCardViewModel> DictionaryCards { get; private set; } = [];
    public bool HasDictionaryCards => DictionaryCards.Count > 0;
    public bool ShowOutputText => HasOutput && !_dictionaryOnly;

    /// <summary>Captured text as selectable candidates: one whole-phrase chip, or one per word.</summary>
    public IReadOnlyList<WordChoice> WordChoices { get; private set; } = [];
    public int SelectedWordIndex => _phraseMode ? 0 : _selectedWord;
    public DictionaryWordResult? SelectedWord => _phraseMode ? _phrase : _selectedWord >= 0 ? _words[_selectedWord] : null;
    public bool ShowWordPicker => _dictionaryOnly && ShowAnswer && WordChoices.Count > 0;
    public bool IsWholePhrase => ShowWordPicker && _phraseMode;
    private int FoundWordCount => _words.Count(word => word.Result.IsMatch);
    public string WordsFoundText => _phraseMode ? "Whole phrase" : $"{FoundWordCount} of {_words.Count} words found";
    public bool ShowLocalBadge => ShowWordPicker && (_phraseMode || FoundWordCount > 0);
    public bool HasNoWordEntries => ShowWordPicker && SelectedWord is null;
    public bool ShowWordKeys => ShowWordPicker && SelectedWord is not null;
    public bool ShowWordMoveKeys => ShowWordKeys && !_phraseMode && FoundWordCount > 1;
    public bool ShowSourceLine => !ShowWordPicker;
    public bool ShowOutputUnavailable => HasNoOutput && !ShowWordPicker;
    public bool ShowLocalDictionarySource => HasDictionaryCards && !ShowWordPicker;
    public bool ShowResultIcons => ShowResultActions && !ShowWordPicker;
    public bool ShowExplainButton => ShowResultActions && !ShowWordPicker;
    public string LocalDictionarySource => _dictionaryResult is { } result ? $"Local dictionary · {result.Source.Name} · {result.Source.QualityStatus}" : "";

    public void BindPronunciation(PronunciationViewModel pronunciation, Language source, Language target)
    {
        if (IsClosed) return;
        Pronunciation = pronunciation;
        SourceLanguage = source;
        TargetLanguage = target;
        Pronunciation.SetSource(OriginalText, source);
        WordCards = _vocabulary.Select(item => new VocabularyCardViewModel(item, Pronunciation)).ToArray();
        DictionaryCards = _dictionaryResult?.Entries.Select(entry => new DictionaryCardViewModel(entry, Pronunciation)).ToArray() ?? [];
        foreach (string property in new[] { nameof(Pronunciation), nameof(WordCards), nameof(InputLabel), nameof(LanguagePairLabel), nameof(DictionaryCards), nameof(HasDictionaryCards), nameof(ShowOutputText), nameof(LocalDictionarySource) })
            OnPropertyChanged(property);
    }

    private TranslationPopupViewModel(string originalText, string outputText, string outputLabel,
        string sourceLabel, string sourceDetails, string vocabularyText, string outputUnavailableReason,
        string expandedTitle, string expandedText, bool isPreview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalText);
        OriginalText = originalText;
        OutputText = outputText;
        OutputLabel = outputLabel;
        SourceLabel = sourceLabel;
        SourceDetails = sourceDetails;
        VocabularyText = vocabularyText;
        OutputUnavailableReason = outputUnavailableReason;
        IsPreview = isPreview;
        ExpandedTitle = expandedTitle;
        ExpandedText = expandedText;
        CloseCommand = new RelayCommand(Close, () => !IsClosed);
        ToggleExpansionCommand = new RelayCommand(ToggleExpansion, () => ShowResultActions && HasExpandedContent && !IsClosed);
        ExplainCommand = new RelayCommand(Explain, CanExplain);
        CopyCommand = new AsyncRelayCommand(() => CopyAsync(ShowWordPicker ? SelectedWord?.Word.Text : null),
            () => !IsClosed && HasOutput && _clipboard is not null && _copyCancellation is null);
        CopyOriginalCommand = new AsyncRelayCommand(() => CopyAsync(OriginalText),
            () => !IsClosed && ShowWordPicker && _clipboard is not null && _copyCancellation is null);
        SplitIntoWordsCommand = new RelayCommand(SplitIntoWords, () => !IsClosed && IsWholePhrase);
        AskAiCommand = new RelayCommand(() => { if (CanAskAi()) _askAi!.Execute(null); }, CanAskAi);
        CancelCommand = new RelayCommand(Cancel, () => !IsClosed &&
            (_copyCancellation is { IsCancellationRequested: false } || IsBusy && _cancel?.CanExecute(null) == true));
        SaveCommand = new RelayCommand(() => { if (!IsClosed && _previewSave?.CanExecute(null) == true) _previewSave.Execute(null); }, () => !IsClosed && _previewSave?.CanExecute(null) == true);
        UndoCommand = new RelayCommand(() => { if (!IsClosed && _previewUndo?.CanExecute(null) == true) _previewUndo.Execute(null); }, () => !IsClosed && _previewUndo?.CanExecute(null) == true);
        SelectAgainCommand = new RelayCommand(() => { if (!IsClosed && _selectAgain?.CanExecute(null) == true) _selectAgain.Execute(null); }, () => !IsClosed && _selectAgain?.CanExecute(null) == true);
    }

    public string OriginalText { get; private set; }
    public string OutputText { get; private set; }
    public string OutputLabel { get; private set; }
    public string SourceLabel { get; private set; }
    public string SourceDetails { get; private set; }
    public string VocabularyText { get; private set; }
    public string ExpandedTitle { get; private set; }
    public string ExpandedText { get; private set; }
    public bool HasExpandedContent => !string.IsNullOrWhiteSpace(ExpandedText);
    public bool IsExpanded => _isExpanded;
    public bool IsCollapsed => !IsExpanded;
    public string ExplainButtonText => IsExpanded ? "Less" : "Explain";
    public string ExplainAutomationName => _openDetail is not null ? "Explain in the FIH Lens bar" : !HasExpandedContent ? "Explanation unavailable" :
        IsExpanded ? "Collapse details" : "Expand details";
    public bool IsPreview { get; }
    public bool HasOutput => ShowAnswer && !string.IsNullOrWhiteSpace(OutputText);
    public bool HasNoOutput => !HasOutput;
    public bool HasVocabulary => ShowAnswer && !string.IsNullOrWhiteSpace(VocabularyText);
    public string PreviewNotice => IsPreview ? "Design preview · sample data · no lookup or AI request" : "";
    public string OutputUnavailableReason { get; private set; }
    public string UnavailableActionsReason =>
        (IsPreview ? "Save/Undo are simulated; nothing is stored. " : "Phrase saving and Undo are not supported yet. ") +
        (_clipboard is null ? "Copy unavailable. " : "") +
        (HasExpandedContent ? "" : " Explanation unavailable.");
    public string CopyFeedback => _copyFeedback;
    public string DictionaryStatus => _dictionaryStatus;
    public string AiStatus => _aiStatus;
    public string BackendErrorMessage => _backendErrorMessage;
    public string AiSendNotice => _aiSendNotice;
    public bool IsBusy => _isBusy;
    public bool HasStatus => IsBusy || DictionaryStatus.Length > 0 || AiStatus.Length > 0 || CopyFeedback.Length > 0 || IsPreview;
    // A capture popup shows its stage (reading, canceled, failed) until recognized text arrives.
    public bool IsCaptureError => _captureStage && _hasBackendError;
    public bool ShowStageAlert => IsOcrErrorPreview || IsAiErrorPreview || IsCaptureError;
    public bool ShowSelectAgain => IsOcrErrorPreview || IsCaptureError;
    public bool ShowAnswer => !_captureStage && (!IsPreview || _previewState is not (PopupPreviewState.OcrLoading or PopupPreviewState.OcrError or PopupPreviewState.EmptyOcr or PopupPreviewState.Translating or PopupPreviewState.TranslationError));
    public bool ShowResultActions => ShowAnswer;
    public bool ShowStageActions => !ShowAnswer || !HasOutput;
    public bool HasStageStatus => DictionaryStatus.Length > 0 || AiStatus.Length > 0;
    public bool HasFeedback => CopyFeedback.Length > 0 || SaveFeedback.Length > 0;
    public bool HasFeedbackStrip => HasFeedback && !IsPreviewSaving;
    public bool IsNotPreviewSaved => !IsPreviewSaved;
    public bool IsErrorStatus => _copyFailed || _hasBackendError || _previewState is PopupPreviewState.OcrError or PopupPreviewState.EmptyOcr or PopupPreviewState.TranslationError or PopupPreviewState.SaveError or PopupPreviewState.CopyError or PopupPreviewState.DictionaryError;
    public bool IsSuccessFeedback => _previewState is PopupPreviewState.Copied or PopupPreviewState.Saved or PopupPreviewState.ExpandedSaved || CopyFeedback == "Copied.";
    // The word picker's meta line replaces the lookup status.
    public bool HasDictionaryStatus => DictionaryStatus.Length > 0 && !ShowWordPicker;
    public bool HasAiStatus => AiStatus.Length > 0;
    public bool IsOcrLoadingPreview => IsPreview && _previewState == PopupPreviewState.OcrLoading;
    public bool IsTranslatingPreview => IsPreview && _previewState == PopupPreviewState.Translating;
    public bool IsProcessingPreview => IsOcrLoadingPreview || IsTranslatingPreview;
    public bool IsOcrErrorPreview => IsPreview && _previewState is PopupPreviewState.OcrError or PopupPreviewState.EmptyOcr;
    public bool IsAiErrorPreview => IsPreview && _previewState == PopupPreviewState.TranslationError;
    public bool IsSaveErrorPreview => IsPreview && _previewState == PopupPreviewState.SaveError;
    public bool IsPreviewStage => IsProcessingPreview || IsOcrErrorPreview || IsAiErrorPreview;
    public bool ShowBackendStatuses => !IsPreviewStage && !IsCaptureError;
    public string StageHeading => IsOcrLoadingPreview ? "Reading selected text…" : IsTranslatingPreview ? $"Translating to {TargetLanguage}…" : IsOcrErrorPreview ? "No readable text here" : IsAiErrorPreview ? "Translation unavailable" : IsCaptureError ? "Nothing to translate yet" : "";
    public string StageDescription => IsOcrLoadingPreview ? "Keep working. This popup will update when the text is ready." : IsTranslatingPreview ? "Text recognized. Finding the meaning in this context." : IsOcrErrorPreview ? "Try a bigger region with sharper text." : IsAiErrorPreview ? "The translation provider is unavailable. Your selected text is still here." : IsCaptureError ? DictionaryStatus : "";
    public string FirstProcessingStep => IsOcrLoadingPreview ? "Reading text" : "Text read";
    public string SecondProcessingStep => IsOcrLoadingPreview ? "Dictionary" : "Asking AI";
    public string RetainedTextLabel => IsOcrErrorPreview ? "Selection" : IsAiErrorPreview ? $"{SourceLanguage} · recognized text" : "Selected text";
    public string RetainedText => IsOcrErrorPreview ? "The selected region contained no clear text." : OriginalText;
    public bool ShowRetainedText => IsPreviewStage && !IsOcrLoadingPreview;
    public string FooterNotice => IsPreview ? "Design preview only · Esc close" : "Save unavailable · Esc close";
    public bool ShowFooter => !IsProcessingPreview;
    public string PreviewStateName => IsPreview ? $"Preview only · {_previewState}" : "";
    public bool IsPreviewSaving => IsPreview && _previewState is PopupPreviewState.Saving or PopupPreviewState.ExpandedSaving;
    public bool IsPreviewSaved => IsPreview && _previewState is PopupPreviewState.Saved or PopupPreviewState.ExpandedSaved;
    public string SaveButtonText => IsPreviewSaving ? "Saving…" : IsPreviewSaved ? "Saved" : "Save phrase";
    public string SaveFeedback => !IsPreview ? "" : _previewState switch
    {
        PopupPreviewState.Saving or PopupPreviewState.ExpandedSaving => "Preview: saving phrase… no storage is used.",
        PopupPreviewState.Saved or PopupPreviewState.ExpandedSaved => "Preview: saved · nothing was stored.",
        PopupPreviewState.SaveError => "Preview: couldn't save. Your phrase is still here.",
        _ => ""
    };
    public int PreferredHeight
    {
        get
        {
            if (IsPreviewStage) return _previewState switch
            {
                PopupPreviewState.OcrLoading => 308, PopupPreviewState.Translating => 309,
                PopupPreviewState.TranslationError => 297, PopupPreviewState.OcrError => 276,
                _ => 325
            };
            int feedbackHeight = IsPreviewSaving ? 30 : HasFeedbackStrip ? (SaveFeedback.Length > 0 ? 48 : 46) : 0;
            // Both messages share a strip; the additional caption uses Phase1CaptionLineHeight.
            if (HasFeedbackStrip && SaveFeedback.Length > 0 && CopyFeedback.Length > 0) feedbackHeight += 18;
            return (IsExpanded ? 569 : 325) + feedbackHeight;
        }
    }
    public bool IsClosed => _isClosed;
    public IRelayCommand CloseCommand { get; }
    public IRelayCommand ToggleExpansionCommand { get; }
    /// <summary>Opens the launcher's Result detail when one is attached; otherwise expands in place (design preview).</summary>
    public IRelayCommand ExplainCommand { get; }

    public void SetOpenDetail(Action openDetail)
    {
        _openDetail = openDetail ?? throw new ArgumentNullException(nameof(openDetail));
        ExplainCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ExplainAutomationName));
    }

    /// <summary>Select again after a failed capture closes this popup and starts a new region capture.</summary>
    public void SetSelectAgain(Action selectAgain)
    {
        ArgumentNullException.ThrowIfNull(selectAgain);
        _selectAgain = new RelayCommand(selectAgain);
        SelectAgainCommand.NotifyCanExecuteChanged();
    }

    private bool CanExplain() => !IsClosed && (_openDetail is not null
        ? ShowAnswer && (!ShowWordPicker || SelectedWord is not null) : ToggleExpansionCommand.CanExecute(null));

    private void Explain()
    {
        if (!CanExplain()) return;
        if (_openDetail is { } open) open();
        else ToggleExpansionCommand.Execute(null);
    }
    /// <summary>Copies the selected word in the word picker, otherwise the current output.</summary>
    public IAsyncRelayCommand CopyCommand { get; }
    /// <summary>Copies the whole captured text (word picker only).</summary>
    public IAsyncRelayCommand CopyOriginalCommand { get; }
    public IRelayCommand SplitIntoWordsCommand { get; }
    public IRelayCommand AskAiCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand SelectAgainCommand { get; }
    public event EventHandler? CloseRequested;

    internal void BindPreviewSaveActions(ICommand save, ICommand undo, ICommand selectAgain)
    {
        _previewSave = save;
        _previewUndo = undo;
        _selectAgain = selectAgain;
        NotifyState();
    }

    public static TranslationPopupViewModel ForInput(string originalText,
        Language source = Language.English, Language target = Language.Vietnamese)
    {
        var popup = new TranslationPopupViewModel(originalText, "", "Output",
            "No result yet", "Results retain their own source and notices.", "", "Awaiting local lookup or explicit Ask AI.", "Details", "", false);
        popup.BindPronunciation(popup.Pronunciation, source, target);
        return popup;
    }

    // The session owns backend work. This view forwards explicit actions and never calls a provider.
    public void BindActions(ICommand? askAi, ICommand? cancel, string providerNotice, IClipboardWriter? clipboard = null)
    {
        if (IsClosed) return;
        if (askAi is not null) ArgumentException.ThrowIfNullOrWhiteSpace(providerNotice);
        _copyGeneration++;
        _copyCancellation?.Cancel();
        _copyFeedback = "";
        _copyFailed = false;
        if (_askAi is not null) _askAi.CanExecuteChanged -= BackendCanExecuteChanged;
        _askAi = askAi;
        _cancel = cancel;
        _clipboard = clipboard;
        _aiSendNotice = askAi is null ? "AI unavailable: no configured provider command." : providerNotice;
        if (_askAi is not null) _askAi.CanExecuteChanged += BackendCanExecuteChanged;
        NotifyState();
    }

    // T07's session publishes existing backend state; its commands retain sole lifecycle ownership.
    public void PresentBackendState(MainWindowViewModel backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (IsClosed || IsPreview || !string.Equals(OriginalText, backend.InputText, StringComparison.Ordinal)) return;
        _isBusy = backend.IsBusy;
        _dictionaryStatus = backend.DictionaryStatusText;
        _aiStatus = backend.AiStatusText;
        _backendErrorMessage = string.Join("\n", new[] { backend.DictionaryErrorMessage, backend.ErrorMessage }.Where(value => !string.IsNullOrWhiteSpace(value)));
        OnPropertyChanged(nameof(BackendErrorMessage));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SourceDetails));
        _hasBackendError = backend.DictionaryErrorMessage is not null || backend.ErrorMessage is not null;
        NotifyState();
    }

    internal void RefreshBackendResult(MainWindowViewModel backend)
    {
        if (IsClosed || IsPreview || !string.Equals(OriginalText, backend.InputText, StringComparison.Ordinal)) return;
        var snapshot = backend.Result is { } translation
            ? FromTranslation(translation, backend.AiSourceLabel)
            : backend.DictionaryResult is { } dictionary ? FromDictionary(SyncWords(backend.DictionaryWords, dictionary), backend.InputText) :
                ForInput(backend.InputText, backend.SourceLanguage, backend.TargetLanguage);
        ApplySnapshot(snapshot);
        // A dictionary-only snapshot already holds the selected word's result.
        if (backend.Result is not null) _dictionaryResult = backend.DictionaryResult;
        BindPronunciation(Pronunciation, snapshot.SourceLanguage, snapshot.TargetLanguage);
        if (backend.Result is not null)
        {
            SourceLabel = backend.AiSourceLabel;
            if (!string.IsNullOrWhiteSpace(backend.DictionarySourceDetails))
                SourceDetails += "\n\nLocal dictionary source and notices\n" + backend.DictionarySourceDetails;
        }
        if (backend.Result is null && backend.DictionaryResult is null)
        {
            SourceLabel = backend.DictionarySourceLabel;
            SourceDetails = backend.DictionarySourceDetails;
        }
        PresentBackendState(backend);
    }

    // Rebuilds the picker only for a new lookup, so backend refreshes keep the user's selection.
    private DictionaryLookupResult SyncWords(IReadOnlyList<DictionaryWordResult> words, DictionaryLookupResult phrase)
    {
        if (!ReferenceEquals(_wordSource, words))
        {
            _wordSource = words;
            _words = words;
            _phrase = DictionaryWords.Phrase(phrase.OriginalText) is { } whole ? new(whole, phrase) : null;
            // A one-word text is a word, not a phrase.
            _phraseMode = _phrase is not null && phrase.IsMatch && words.Count > 1;
            _selectedWord = FirstFoundWord();
            BuildWordChoices();
        }
        return SelectedWord?.Result ?? phrase;
    }

    private int FirstFoundWord()
    {
        for (int i = 0; i < _words.Count; i++)
            if (_words[i].Result.IsMatch) return i;
        return -1;
    }

    private void BuildWordChoices()
    {
        WordChoices = _phraseMode
            ? [new WordChoice(0, _phrase!.Word, true, $"{_phrase.Word.Text}, whole phrase, has dictionary entry")]
            : _words.Select((word, i) => new WordChoice(i, word.Word, word.Result.IsMatch,
                $"{word.Word.Text}, {(word.Result.IsMatch ? "has dictionary entry" : "no dictionary entry")}")).ToArray();
        OnPropertyChanged(nameof(WordChoices));
    }

    /// <summary>Selects a found word and shows its cards; not-found words cannot be selected.</summary>
    public bool SelectWord(int index)
    {
        if (IsClosed || !ShowWordPicker || _phraseMode || index == _selectedWord ||
            (uint)index >= (uint)_words.Count || !_words[index].Result.IsMatch) return false;
        _selectedWord = index;
        ApplyWordSelection();
        return true;
    }

    /// <summary>Moves to the previous or next found word.</summary>
    public bool MoveWordSelection(int direction)
    {
        if (_selectedWord < 0) return false;
        for (int i = _selectedWord + Math.Sign(direction); i >= 0 && i < _words.Count; i += Math.Sign(direction))
            if (_words[i].Result.IsMatch) return SelectWord(i);
        return false;
    }

    private void SplitIntoWords()
    {
        if (!IsWholePhrase) return;
        _phraseMode = false;
        _selectedWord = FirstFoundWord();
        BuildWordChoices();
        ApplyWordSelection();
    }

    private void ApplyWordSelection()
    {
        ApplySnapshot(FromDictionary(SelectedWord?.Result ?? _phrase!.Result, OriginalText));
        NotifyState();
    }

    // Refresh the existing popup without retiring commands or canceling its live backend.
    private void ApplySnapshot(TranslationPopupViewModel snapshot)
    {
        if (OutputText != snapshot.OutputText || OriginalText != snapshot.OriginalText ||
            SourceLanguage != snapshot.SourceLanguage || TargetLanguage != snapshot.TargetLanguage ||
            !_vocabulary.SequenceEqual(snapshot._vocabulary))
        {
            Pronunciation.Stop();
            ++_copyGeneration;
            _copyCancellation?.Cancel();
            _copyFeedback = "";
            _copyFailed = false;
        }
        OriginalText = snapshot.OriginalText;
        OutputText = snapshot.OutputText;
        OutputLabel = snapshot.OutputLabel;
        SourceLabel = snapshot.SourceLabel;
        SourceDetails = snapshot.SourceDetails;
        VocabularyText = snapshot.VocabularyText;
        _vocabulary = snapshot._vocabulary;
        _dictionaryResult = snapshot._dictionaryResult;
        _dictionaryOnly = snapshot._dictionaryOnly;
        BindPronunciation(Pronunciation, snapshot.SourceLanguage, snapshot.TargetLanguage);
        ExpandedTitle = snapshot.ExpandedTitle;
        ExpandedText = snapshot.ExpandedText;
        OutputUnavailableReason = snapshot.OutputUnavailableReason;
        foreach (string property in new[] { nameof(OriginalText), nameof(OutputText), nameof(OutputLabel),
            nameof(SourceLabel), nameof(SourceDetails), nameof(VocabularyText), nameof(ExpandedTitle),
            nameof(ExpandedText), nameof(OutputUnavailableReason), nameof(HasExpandedContent) }) OnPropertyChanged(property);
    }

    internal void PresentCaptureStatus(string message, bool busy, bool error)
    {
        if (IsClosed || IsPreview) return;
        _captureStage = true;
        _dictionaryStatus = message;
        _isBusy = busy;
        _hasBackendError = error;
        OutputUnavailableReason = message;
        OnPropertyChanged(nameof(OutputUnavailableReason));
        NotifyState();
    }

    internal void SetCapturedInput(string text)
    {
        if (IsClosed || IsPreview) return;
        _captureStage = false;
        ApplySnapshot(ForInput(text, SourceLanguage, TargetLanguage));
        NotifyState();
    }

    private bool CanAskAi() => !IsClosed && !IsBusy && _askAi?.CanExecute(null) == true;
    private void BackendCanExecuteChanged(object? sender, EventArgs args) => AskAiCommand.NotifyCanExecuteChanged();

    // Copies plainText as is, or the current output with its dictionary attribution when null.
    private async Task CopyAsync(string? plainText)
    {
        if (IsClosed || (plainText is null && !HasOutput) || _clipboard is null || _copyCancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        _copyCancellation = cancellation;
        long generation = ++_copyGeneration;
        _copyFeedback = "";
        _copyFailed = false;
        NotifyState();
        try
        {
            string copyText = plainText ?? (_dictionaryOnly ? string.Join("\n\n", DictionaryCards.Select(card => card.CopyText))
                : OutputText + (HasDictionaryCards ? "\n\nLocal dictionary\n" + string.Join("\n\n", DictionaryCards.Select(card => card.CopyText)) : ""));
            if (plainText is null && HasDictionaryCards && _dictionaryResult is { Source: var source })
                copyText += $"\n\nDictionary: {source.Name}\nSource URL: {source.SourceUrl}\nLicense: {source.License}\nRevision: {source.Revision}";
            await _clipboard.WriteTextAsync(copyText, cancellation.Token);
            if (!IsClosed && generation == _copyGeneration && !cancellation.IsCancellationRequested)
                _copyFeedback = IsPreview ? "Preview: copied (simulated)." : "Copied.";
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!IsClosed && generation == _copyGeneration && !cancellation.IsCancellationRequested)
            {
                _copyFeedback = "Copy failed. Try again.";
                _copyFailed = true;
            }
        }
        finally
        {
            if (ReferenceEquals(_copyCancellation, cancellation)) _copyCancellation = null;
            cancellation.Dispose();
            if (!IsClosed) NotifyState();
        }
    }

    private void Cancel()
    {
        if (IsClosed) return;
        _copyGeneration++;
        _copyCancellation?.Cancel();
        _copyFeedback = "";
        _copyFailed = false;
        if (_cancel?.CanExecute(null) == true) _cancel.Execute(null);
        NotifyState();
    }

    internal void SetPreviewState(PopupPreviewState state)
    {
        if (!IsPreview || IsClosed) return;
        _copyGeneration++;
        _copyCancellation?.Cancel();
        _previewState = state;
        _isExpanded = state is PopupPreviewState.Expanded or PopupPreviewState.ExpandedSaving or PopupPreviewState.ExpandedSaved;
        _dictionaryStatus = state switch
        {
            PopupPreviewState.OcrLoading => $"Preview: recognizing {SourceLanguage} text locally…",
            PopupPreviewState.OcrError => "Preview: OCR failed. Reselect the text.",
            PopupPreviewState.EmptyOcr => "Preview: no text recognized. Reselect the text.",
            PopupPreviewState.DictionaryLoading => "Preview: looking up the local dictionary…",
            PopupPreviewState.DictionaryUnavailable => "Preview: local dictionary unavailable. Configured AI remains independent.",
            PopupPreviewState.DictionaryMiss => "Preview: no exact local dictionary entry. Awaiting explicit Ask AI.",
            PopupPreviewState.DictionaryInvalidInput => "Preview: this text exceeds the local dictionary input limit.",
            PopupPreviewState.DictionaryError => "Preview: local dictionary lookup failed.",
            PopupPreviewState.Canceled => "Preview: canceled; no late result will be shown.",
            PopupPreviewState.NoSupportedPhrase => "Preview: no supported phrase classification was supplied.",
            _ => ""
        };
        _aiStatus = state switch
        {
            PopupPreviewState.Translating => "Preview: translating after explicit Ask AI.",
            PopupPreviewState.TranslationError => "Preview: AI request failed. Ask AI retries explicitly.",
            PopupPreviewState.AiUnavailable => "Preview: AI unconfigured; local dictionary remains available.",
            _ => ""
        };
        _isBusy = state is PopupPreviewState.OcrLoading or PopupPreviewState.DictionaryLoading or PopupPreviewState.Translating;
        _copyFeedback = state switch { PopupPreviewState.Copied => "Preview: copied (simulated).", PopupPreviewState.CopyError => "Preview: copy failed. Try again.", _ => "" };
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsCollapsed));
        OnPropertyChanged(nameof(ExplainButtonText));
        OnPropertyChanged(nameof(ExplainAutomationName));
        NotifyState();
    }

    private void NotifyState()
    {
        foreach (string property in new[] { nameof(DictionaryStatus), nameof(AiStatus), nameof(CopyFeedback), nameof(AiSendNotice), nameof(IsBusy), nameof(HasStatus), nameof(PreviewStateName), nameof(IsPreviewSaving), nameof(IsPreviewSaved), nameof(SaveButtonText), nameof(SaveFeedback), nameof(PreferredHeight), nameof(UnavailableActionsReason), nameof(ShowAnswer), nameof(ShowResultActions), nameof(ShowStageActions), nameof(HasStageStatus), nameof(HasFeedback), nameof(HasFeedbackStrip), nameof(IsNotPreviewSaved), nameof(IsErrorStatus), nameof(IsSuccessFeedback), nameof(HasOutput), nameof(HasNoOutput), nameof(HasVocabulary), nameof(HasDictionaryStatus), nameof(HasAiStatus), nameof(IsOcrLoadingPreview), nameof(IsTranslatingPreview), nameof(IsProcessingPreview), nameof(IsOcrErrorPreview), nameof(IsAiErrorPreview), nameof(IsSaveErrorPreview), nameof(IsPreviewStage), nameof(ShowBackendStatuses), nameof(StageHeading), nameof(StageDescription), nameof(FirstProcessingStep), nameof(SecondProcessingStep), nameof(RetainedTextLabel), nameof(RetainedText), nameof(ShowRetainedText), nameof(ShowFooter), nameof(IsCaptureError), nameof(ShowStageAlert), nameof(ShowSelectAgain),
            nameof(SelectedWordIndex), nameof(SelectedWord), nameof(ShowWordPicker), nameof(IsWholePhrase), nameof(WordsFoundText), nameof(ShowLocalBadge),
            nameof(HasNoWordEntries), nameof(ShowWordKeys), nameof(ShowWordMoveKeys), nameof(ShowSourceLine), nameof(ShowOutputUnavailable),
            nameof(ShowLocalDictionarySource), nameof(ShowResultIcons), nameof(ShowExplainButton) }) OnPropertyChanged(property);
        CopyCommand.NotifyCanExecuteChanged();
        CopyOriginalCommand.NotifyCanExecuteChanged();
        SplitIntoWordsCommand.NotifyCanExecuteChanged();
        AskAiCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        SelectAgainCommand.NotifyCanExecuteChanged();
        ToggleExpansionCommand.NotifyCanExecuteChanged();
        ExplainCommand.NotifyCanExecuteChanged();
    }

    public static TranslationPopupViewModel FromTranslation(TranslationResult result, string providerLabel,
        string? sourceDetails = null) => BuildTranslation(result, providerLabel, sourceDetails, false);

    internal static TranslationPopupViewModel FromPreviewTranslation(TranslationResult result, string providerLabel,
        string? sourceDetails = null) => BuildTranslation(result, providerLabel, sourceDetails, true);

    private static TranslationPopupViewModel BuildTranslation(TranslationResult result, string providerLabel,
        string? sourceDetails, bool isPreview)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerLabel);
        string vocabulary = string.Join("\n\n", result.Vocabulary
            .Where(item => !string.IsNullOrWhiteSpace(item.Word))
            .Select(item => string.Join("\n", new[]
            {
                string.Join(" · ", new[] { item.Word, item.Phonetic, item.PartOfSpeech, item.Meaning }
                    .Where(value => !string.IsNullOrWhiteSpace(value))), item.ExampleSentence
            }.Where(value => !string.IsNullOrWhiteSpace(value)))));
        string grammar = string.Join("\n\n", result.Grammar.Select(item =>
            string.Join("\n", new[] { item.Pattern, item.Explanation, item.ExampleSentence }
                .Where(value => !string.IsNullOrWhiteSpace(value)))));
        string alternatives = string.Join("\n\n", result.Alternatives.Select(item =>
            string.Join("\n", new[] { item.Text, item.Explanation }
                .Where(value => !string.IsNullOrWhiteSpace(value)))));
        string expanded = string.Join("\n\n", new[]
        {
            result.Explanation, Section("Context", result.Context), Section("Tone", result.Tone),
            Section("Vocabulary", vocabulary), Section("Grammar", grammar), Section("Alternatives", alternatives)
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var popup = new TranslationPopupViewModel(result.OriginalText,
            string.IsNullOrWhiteSpace(result.NaturalTranslation) ? "" : result.NaturalTranslation,
            result.TargetLanguage.ToString(), $"Generated AI output · {providerLabel}",
            string.IsNullOrWhiteSpace(sourceDetails)
                ? "AI-generated output; check important wording against the original." : sourceDetails,
            vocabulary, "No translation was returned.", "Provided learning notes", expanded, isPreview);
        popup._vocabulary = result.Vocabulary;
        popup.BindPronunciation(popup.Pronunciation, result.SourceLanguage, result.TargetLanguage);
        return popup;
    }

    /// <param name="originalText">The captured text when <paramref name="result"/> is for one of its words.</param>
    public static TranslationPopupViewModel FromDictionary(DictionaryLookupResult result, string? originalText = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        DictionarySource source = result.Source;
        string output = string.Join("\n\n", result.Entries.Select(entry =>
            string.Join("\n", new[] { entry.Headword, entry.PartOfSpeech,
                string.Join("\n", entry.Senses), entry.Example,
                $"Source: {entry.SourceId}", $"Article: {entry.SourceArticleId}", $"Origin: {entry.SourceOrigin}" }
                .Where(value => !string.IsNullOrWhiteSpace(value)))));
        string details =
            $"Source: {source.Name} ({source.SourceId})\nRevision: {source.Revision}\nLanguages: {source.SourceLanguage} → {source.TargetLanguage}\nQuality: {source.QualityStatus}\nHuman review scope: {source.HumanReviewScope}\nLicense: {source.License}\nSource URL: {source.SourceUrl}\nAttribution: {source.Attribution}\n\n" +
            string.Join("\n\n", source.Notices.Select(notice => $"{notice.Key}\n{notice.Value}"));
        var popup = new TranslationPopupViewModel(originalText ?? result.OriginalText, output, "Dictionary definitions",
            $"{source.Name} · {source.QualityStatus}", details, "",
            "No exact dictionary entry for this text.", "Dictionary senses and source",
            string.Join("\n\n", new[] { output, details }.Where(value => !string.IsNullOrWhiteSpace(value))), false);
        popup._dictionaryResult = result;
        popup._dictionaryOnly = true;
        popup.BindPronunciation(popup.Pronunciation, DictionaryLookupRouter.ParseLanguage(source.SourceLanguage),
            DictionaryLookupRouter.ParseLanguage(source.TargetLanguage));
        return popup;
    }

    private static string Section(string title, string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : $"{title}\n{text}";

    private void ToggleExpansion()
    {
        if (IsClosed || !ShowResultActions || !HasExpandedContent) return;
        SetProperty(ref _isExpanded, !IsExpanded, nameof(IsExpanded));
        OnPropertyChanged(nameof(IsCollapsed));
        OnPropertyChanged(nameof(ExplainButtonText));
        OnPropertyChanged(nameof(ExplainAutomationName));
        OnPropertyChanged(nameof(PreferredHeight));
    }

    internal bool TryHandleShortcut(char key, bool hasModifiers, bool isEditing)
    {
        if (hasModifiers || isEditing) return false;
        System.Windows.Input.ICommand? command = key switch
        {
            'C' => CopyCommand,
            'E' => ToggleExpansionCommand,
            _ => null
        };
        if (command?.CanExecute(null) != true) return false;
        command.Execute(null);
        return true;
    }

    private void Close()
    {
        if (IsClosed) return;
        Pronunciation.Close();
        _copyGeneration++;
        _copyCancellation?.Cancel();
        if (_askAi is not null) _askAi.CanExecuteChanged -= BackendCanExecuteChanged;
        _openDetail = null;
        SetProperty(ref _isClosed, true, nameof(IsClosed));
        if (_cancel?.CanExecute(null) == true) _cancel.Execute(null);
        CloseCommand.NotifyCanExecuteChanged();
        ToggleExpansionCommand.NotifyCanExecuteChanged();
        NotifyState();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>One chip in the word picker; <see cref="Index"/> is its position in the picker.</summary>
public sealed record WordChoice(int Index, DictionaryWord Word, bool HasEntry, string AutomationName);

public enum PopupPreviewState
{
    Compact, Expanded, OcrLoading, Translating, Copied, TranslationError, OcrError,
    Saving, Saved, SaveError, ExpandedSaving, ExpandedSaved, DictionaryLoading,
    DictionaryUnavailable, DictionaryMiss, DictionaryInvalidInput, DictionaryError,
    AiUnavailable, EmptyOcr, Canceled, CopyError, NoSupportedPhrase
}
