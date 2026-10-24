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
        Assert.Equal("current selection", dictionary.Input);
        Assert.Equal(1, dictionary.Calls);
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
        Assert.Equal("new input", dictionary.Input);
        Assert.Equal(1, dictionary.Calls);
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
        public Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
        {
            Calls++; Input = input;
            return Task.FromResult(new DictionaryLookupResult(input, Source, Array.Empty<DictionaryEntry>()));
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
