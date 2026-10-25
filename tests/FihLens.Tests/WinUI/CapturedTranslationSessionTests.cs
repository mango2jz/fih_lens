using FihLens.Application.Capture;
using FihLens.Application.Clipboard;
using FihLens.Application.Dictionary;
using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.WinUI.Presentation;
using FihLens.WinUI.ViewModels;

namespace FihLens.Tests.WinUI;

public sealed class CapturedTranslationSessionTests
{
    [Fact]
    public async Task SelectedInputWaitsForShownPopupBeforeStartingOneLocalLookup()
    {
        var provider = new Provider();
        var dictionary = new Dictionary();
        var backend = new MainWindowViewModel(new TranslationService(provider), "Fake");
        backend.SetDictionaryAvailability(new DictionaryLookupService(dictionary), Source);
        using var session = new CapturedTranslationSession(backend, new Clipboard());

        Assert.True(session.AdoptInput("raw selected text"));
        Assert.False(session.AdoptInput("replacement"));
        Assert.Equal("raw selected text", backend.InputText);
        Assert.Equal(0, dictionary.Calls);
        Assert.Equal(0, provider.Calls);
        Assert.True(session.Popup.AskAiCommand.CanExecute(null));

        session.PopupShown();
        await session.LocalLookupCompletion;
        Assert.Equal("raw selected text", dictionary.Input);
        Assert.Equal(1, dictionary.Calls);
        Assert.Equal(0, provider.Calls);
        session.Popup.AskAiCommand.Execute(null);
        await backend.AskAiCommand.ExecutionTask!;
        Assert.Equal("raw selected text", provider.Input);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task CaptureLooksUpCurrentInputAndOnlyExplicitAskAiCallsProvider()
    {
        var provider = new Provider();
        var dictionary = new Dictionary();
        var clipboard = new Clipboard();
        var backend = new MainWindowViewModel(new TranslationService(provider), "Fake");
        backend.SetDictionaryAvailability(new DictionaryLookupService(dictionary), Source);
        var capture = new Capture(RegionTextCaptureResult.Success("current selection"));
        using var session = NewSession(capture, backend, clipboard);
        Assert.False(session.Popup.AskAiCommand.CanExecute(null));
        Assert.True(session.Confirm(Request));
        Assert.Equal(0, dictionary.Calls);
        Assert.Equal(0, provider.Calls);
        await session.OverlayRetiredAsync();
        await session.OverlayRetiredAsync();
        session.PopupShown();
        await session.LocalLookupCompletion;
        Assert.Equal("current selection", backend.InputText);
        // A region capture looks up the whole text, then each word for the word picker.
        Assert.Equal(["current selection", "current", "selection"], dictionary.Inputs);
        Assert.Equal(1, capture.Calls);
        Assert.Equal(0, provider.Calls);
        var samePopup = session.Popup;
        Assert.Contains("Retain full notice", session.Popup.SourceDetails);
        session.Popup.ToggleExpansionCommand.Execute(null);
        Assert.Equal(0, provider.Calls);
        session.Popup.AskAiCommand.Execute(null);
        await backend.AskAiCommand.ExecutionTask!;
        Assert.Equal(1, provider.Calls);
        Assert.Equal("current selection", provider.Input);
        Assert.Same(samePopup, session.Popup);
        Assert.True(session.Popup.IsExpanded);
        Assert.Equal("actual output", session.Popup.OutputText);
        await session.Popup.CopyCommand.ExecuteAsync(null);
        Assert.Equal("actual output", clipboard.Text);
        Assert.Equal(1, provider.Calls);
        Assert.False(session.Popup.SaveCommand.CanExecute(null));
        Assert.False(session.Popup.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task DictionaryBecomingAvailableAfterCaptureRunsOneLocalLookup()
    {
        var provider = new Provider();
        var backend = new MainWindowViewModel(new TranslationService(provider), "Fake");
        using var session = NewSession(new Capture(RegionTextCaptureResult.Success("new input")), backend);
        session.Confirm(Request);
        await session.OverlayRetiredAsync();
        session.PopupShown();
        await session.LocalLookupCompletion;
        Assert.True(session.Popup.AskAiCommand.CanExecute(null));
        var dictionary = new Dictionary();
        backend.SetDictionaryAvailability(new DictionaryLookupService(dictionary), Source);
        await session.LocalLookupCompletion;
        Assert.Equal(["new input", "new", "input"], dictionary.Inputs);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task JapaneseCaptureUsesLateJapanesePackAndRetainsLocalCardsAfterExplicitAi()
    {
        string root = Path.Combine(Path.GetTempPath(), "FihLens-ja-capture-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pack = FihLens.Tests.Infrastructure.JapanesePackFixture.Create(root);
            await using var lookup = await FihLens.Infrastructure.Dictionary.IndexedJapaneseDictionaryLookup.LoadAsync(pack.Directory, pack.Definition);
            var provider = new Provider();
            var backend = new MainWindowViewModel(new TranslationService(provider), "Fake")
                { SourceLanguage = Language.Japanese, TargetLanguage = Language.English };
            var clipboard = new Clipboard();
            using var session = NewSession(new Capture(RegionTextCaptureResult.Success("せい")), backend, clipboard);
            session.Confirm(new RegionTextCaptureRequest(Request.Source, Request.Crop, Language.Japanese));
            await session.OverlayRetiredAsync();
            session.PopupShown();
            backend.SetDictionaryAvailability(Language.Japanese, Language.English, new DictionaryLookupService(lookup), lookup.Source);
            await session.LocalLookupCompletion;
            Assert.Equal(2, session.Popup.DictionaryCards.Count);
            Assert.Equal(Language.Japanese, session.Popup.SourceLanguage);
            Assert.Equal(0, provider.Calls);
            session.Popup.AskAiCommand.Execute(null);
            await backend.AskAiCommand.ExecutionTask!;
            Assert.Equal(1, provider.Calls);
            Assert.Equal(2, session.Popup.DictionaryCards.Count);
            Assert.Contains("meaning-1", session.Popup.DictionaryCards[0].SensesText);
            Assert.Contains("figurative", session.Popup.DictionaryCards[0].SensesText);
            Assert.Contains("often kana", session.Popup.DictionaryCards[0].WrittenFormsText);
            Assert.Contains("figurative", session.Popup.DictionaryCards[0].CopyText);
            Assert.Contains("language: fre, type: part, wasei: y]: vie", session.Popup.DictionaryCards[0].SensesText);
            Assert.Contains("(source term omitted)", session.Popup.DictionaryCards[0].SensesText);
            Assert.Contains("Antonym references: 反対語・1", session.Popup.DictionaryCards[0].CopyText);
            Assert.DoesNotContain("sample review only", backend.DictionarySourceLabel);
            await session.Popup.CopyCommand.ExecuteAsync(null);
            Assert.Contains("actual output", clipboard.Text);
            Assert.Contains("meaning-1", clipboard.Text);
            Assert.Contains($"License: {lookup.Source.License}", clipboard.Text);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task JapaneseCaptureKeepsSourceAndSkipsEnglishVietnameseDictionary()
    {
        var provider = new Provider();
        var dictionary = new Dictionary();
        var backend = new MainWindowViewModel(new TranslationService(provider), "Fake")
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };
        backend.SetDictionaryAvailability(new DictionaryLookupService(dictionary), Source);
        var capture = new Capture(RegionTextCaptureResult.Success("食べました"));
        using var session = NewSession(capture, backend);
        var request = new RegionTextCaptureRequest(Request.Source, Request.Crop, Language.Japanese);
        Assert.True(session.Confirm(request));
        await session.OverlayRetiredAsync();
        session.PopupShown();

        Assert.Equal(Language.Japanese, capture.Request!.SourceLanguage);
        Assert.Equal(0, dictionary.Calls);
        Assert.Equal("食べました", backend.InputText);
        Assert.True(session.Popup.AskAiCommand.CanExecute(null));
        session.Popup.AskAiCommand.Execute(null);
        await backend.AskAiCommand.ExecutionTask!;
        Assert.Equal(Language.Japanese, provider.Request!.SourceLanguage);
        Assert.Equal(Language.English, provider.Request.TargetLanguage);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task CloseSuppressesLateCaptureWhileRetainingBusyUntilDrain()
    {
        var capture = new DelayedCapture();
        var provider = new Provider();
        var backend = new MainWindowViewModel(new TranslationService(provider), "Fake");
        using var session = NewSession(capture, backend);
        session.Confirm(Request);
        var work = session.OverlayRetiredAsync();
        session.Dispose();
        Assert.True(session.Capture!.IsBusy);
        Assert.True(capture.Token.IsCancellationRequested);
        capture.Result.SetResult(RegionTextCaptureResult.Success("late captured text"));
        await work;
        Assert.False(session.Capture!.IsBusy);
        Assert.Null(session.Capture.Result);
        Assert.Empty(backend.InputText);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData(RegionTextCaptureOutcome.NoText)]
    [InlineData(RegionTextCaptureOutcome.Denied)]
    [InlineData(RegionTextCaptureOutcome.RecognizerUnavailable)]
    public async Task CaptureFailuresAreHonestAndCannotSendPlaceholderToAi(RegionTextCaptureOutcome outcome)
    {
        var provider = new Provider();
        using var session = NewSession(new Capture(RegionTextCaptureResult.Failure(outcome)),
            new MainWindowViewModel(new TranslationService(provider), "Fake"));
        session.Confirm(Request);
        await session.OverlayRetiredAsync();
        Assert.False(session.Popup.IsPreview);
        Assert.True(session.Popup.IsErrorStatus);
        Assert.False(session.Popup.AskAiCommand.CanExecute(null));
        Assert.False(session.Popup.HasOutput);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task WordPickerSelectsFirstFoundWordAndSelectionSwapsCards()
    {
        var clipboard = new Clipboard();
        using var session = await CaptureWords("Tom can run fast,\nToday", clipboard, "can", "run", "fast");
        var popup = session.Popup;

        Assert.True(popup.ShowWordPicker);
        Assert.False(popup.IsWholePhrase);
        Assert.Equal(["Tom", "can", "run", "fast", "Today"], popup.WordChoices.Select(choice => choice.Word.Text));
        Assert.Equal([false, true, true, true, false], popup.WordChoices.Select(choice => choice.HasEntry));
        Assert.Equal("Tom, no dictionary entry", popup.WordChoices[0].AutomationName);
        Assert.Equal((",", true), (popup.WordChoices[3].Word.Trailing, popup.WordChoices[3].Word.LineBreakAfter));
        Assert.Equal("3 of 5 words found", popup.WordsFoundText);
        Assert.Equal(1, popup.SelectedWordIndex);
        Assert.Equal("can", Assert.Single(popup.DictionaryCards).Headword);
        Assert.False(popup.HasDictionaryStatus);

        Assert.False(popup.SelectWord(0));
        Assert.True(popup.SelectWord(2));
        Assert.Equal("run", Assert.Single(popup.DictionaryCards).Headword);
        Assert.True(popup.MoveWordSelection(1));
        Assert.Equal("fast", popup.SelectedWord!.Word.Text);
        Assert.False(popup.MoveWordSelection(1));
        Assert.True(popup.MoveWordSelection(-1));
        Assert.True(popup.MoveWordSelection(-1));
        Assert.Equal("can", popup.SelectedWord!.Word.Text);
        Assert.Equal("Tom can run fast,\nToday", popup.OriginalText);

        await popup.CopyCommand.ExecuteAsync(null);
        Assert.Equal("can", clipboard.Text);
        await popup.CopyOriginalCommand.ExecuteAsync(null);
        Assert.Equal("Tom can run fast,\nToday", clipboard.Text);
    }

    [Fact]
    public async Task WordPickerShowsWholePhraseMatchUntilSplit()
    {
        using var session = await CaptureWords("a piece of cake!", null, "a piece of cake", "cake");
        var popup = session.Popup;

        Assert.True(popup.IsWholePhrase);
        var chip = Assert.Single(popup.WordChoices);
        Assert.Equal(("a piece of cake", "!"), (chip.Word.Text, chip.Word.Trailing));
        Assert.Equal("Whole phrase", popup.WordsFoundText);
        Assert.Equal("a piece of cake", Assert.Single(popup.DictionaryCards).Headword);
        Assert.False(popup.ShowWordMoveKeys);

        popup.SplitIntoWordsCommand.Execute(null);
        Assert.False(popup.IsWholePhrase);
        Assert.Equal(4, popup.WordChoices.Count);
        Assert.Equal("cake", popup.SelectedWord!.Word.Text);
        Assert.Equal("cake", Assert.Single(popup.DictionaryCards).Headword);
    }

    [Fact]
    public async Task WordPickerWithNoEntriesSelectsNothingAndOffersAskAi()
    {
        using var session = await CaptureWords("Kubernetes autoscaler v2", null, "run");
        var popup = session.Popup;

        Assert.True(popup.ShowWordPicker);
        Assert.Null(popup.SelectedWord);
        Assert.Equal(-1, popup.SelectedWordIndex);
        Assert.True(popup.HasNoWordEntries);
        Assert.False(popup.ShowLocalBadge);
        Assert.Equal("0 of 3 words found", popup.WordsFoundText);
        Assert.Empty(popup.DictionaryCards);
        Assert.False(popup.IsErrorStatus);
        Assert.True(popup.AskAiCommand.CanExecute(null));
        Assert.False(popup.MoveWordSelection(1));
    }

    private static async Task<CapturedTranslationSession> CaptureWords(string text, IClipboardWriter? clipboard, params string[] headwords)
    {
        var backend = new MainWindowViewModel(new TranslationService(new Provider()), "Fake");
        backend.SetDictionaryAvailability(new DictionaryLookupService(new HeadwordDictionary(headwords)), Source);
        var session = NewSession(new Capture(RegionTextCaptureResult.Success(text)), backend, clipboard);
        session.Confirm(Request);
        await session.OverlayRetiredAsync();
        return session;
    }

    private static CapturedTranslationSession NewSession(IRegionTextCapture capture, MainWindowViewModel backend,
        IClipboardWriter? clipboard = null) => new(capture, action => { action(); return Task.FromResult(true); }, backend, clipboard ?? new Clipboard());
    private static readonly RegionTextCaptureRequest Request = new(new CaptureSourceSnapshot("display", new(0, 0, 100, 100), "topology"), new(1, 1, 20, 20));
    private static readonly DictionarySource Source = new("source", "Local", "v1", "en", "vi", "license", "https://example.test", "attribution", "sample", "scope",
        new Dictionary<string, string> { ["notice"] = "Retain full notice" });
    private sealed class Capture(RegionTextCaptureResult result) : IRegionTextCapture
    {
        public int Calls;
        public RegionTextCaptureRequest? Request;
        public Task<RegionTextCaptureResult> CaptureAsync(RegionTextCaptureRequest request, CancellationToken cancellationToken = default) { Calls++; Request = request; return Task.FromResult(result); }
    }
    private sealed class DelayedCapture : IRegionTextCapture
    {
        public TaskCompletionSource<RegionTextCaptureResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public Task<RegionTextCaptureResult> CaptureAsync(RegionTextCaptureRequest request, CancellationToken cancellationToken = default) { Token = cancellationToken; return Result.Task; }
    }
    private sealed class Dictionary : IDictionaryLookup
    {
        public int Calls;
        public string? Input;
        public List<string> Inputs = [];
        public Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
        {
            Calls++; Input = input; Inputs.Add(input);
            return Task.FromResult(new DictionaryLookupResult(input, Source, Array.Empty<DictionaryEntry>()));
        }
    }
    private sealed class HeadwordDictionary(string[] headwords) : IDictionaryLookup
    {
        public Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
        {
            string key = DictionaryLookupService.NormalizeInput(input);
            DictionaryEntry[] entries = headwords.Contains(key, StringComparer.OrdinalIgnoreCase)
                ? [new DictionaryEntry(key, ["nghĩa"], null, null, "source", "article", "origin")] : [];
            return Task.FromResult(new DictionaryLookupResult(input, Source, entries));
        }
    }
    private sealed class Provider : ITranslationProvider
    {
        public int Calls;
        public string? Input;
        public TranslationRequest? Request;
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++; Input = request.Text; Request = request;
            return Task.FromResult(new TranslationResult { OriginalText = request.Text, SourceLanguage = request.SourceLanguage, TargetLanguage = request.TargetLanguage, NaturalTranslation = "actual output", Explanation = "actual explanation" });
        }
    }
    private sealed class Clipboard : IClipboardWriter
    {
        public string? Text;
        public Task WriteTextAsync(string text, CancellationToken cancellationToken = default) { Text = text; return Task.CompletedTask; }
    }
}
