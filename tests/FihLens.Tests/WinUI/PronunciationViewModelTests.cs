using FihLens.Application.Pronunciation;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.WinUI.ViewModels;

namespace FihLens.Tests.WinUI;

public sealed class PronunciationViewModelTests
{
    [Fact]
    public void MissingVoiceDisablesSpeakerAndKeepsReason()
    {
        var service = new RecordingSpeech { UnavailableReason = "No Japanese voice installed." };
        var model = new PronunciationViewModel(service);
        model.SetSource("食べました", Language.Japanese);
        Assert.False(model.SpeakCommand.CanExecute(model.SourceText));
        Assert.Equal(service.UnavailableReason, model.SourceUnavailableReason);
        Assert.Empty(service.Calls);
    }

    [Fact]
    public async Task JapaneseCardUsesSurfaceReadingAndPreservesMetadata()
    {
        var service = new RecordingSpeech();
        var model = new PronunciationViewModel(service);
        model.SetSource("食べました", Language.Japanese);
        var card = new VocabularyCardViewModel(new VocabularyItem
        {
            Word = "食べました", Reading = "たべました", BaseForm = "食べる",
            PartOfSpeech = "verb", Meaning = "ate", Synonyms = new[] { "食する" }
        }, model);
        Task speaking = model.SpeakCommand.ExecuteAsync(card.SpeechText);
        Assert.Equal("たべました", service.Calls.Single().Text);
        Assert.Equal(Language.Japanese, service.Calls.Single().Language);
        Assert.Contains("食する", card.SynonymsText);
        Assert.Equal("Base form: 食べる", card.BaseFormText);
        model.Stop();
        service.Calls[0].Completion.SetCanceled();
        await speaking;
    }

    [Fact]
    public async Task OldCompletionCannotClearNewPlayingState()
    {
        var service = new RecordingSpeech();
        var model = new PronunciationViewModel(service);
        model.SetSource("first", Language.English);
        Task first = model.SpeakCommand.ExecuteAsync("first");
        Task second = model.SpeakCommand.ExecuteAsync("second");
        Assert.True(service.Calls[0].Token.IsCancellationRequested);
        service.Calls[0].Completion.SetException(new InvalidOperationException("private captured text"));
        await first;
        Assert.True(model.IsPlaying);
        Assert.DoesNotContain("private", model.Status);
        service.Calls[1].Completion.SetResult();
        await second;
        Assert.False(model.IsPlaying);
    }

    [Fact]
    public async Task SupersededWindowClearsStateAndCannotCancelNewOwner()
    {
        var service = new RecordingSpeech();
        var first = new PronunciationViewModel(service);
        var second = new PronunciationViewModel(service);
        first.SetSource("one", Language.English);
        second.SetSource("two", Language.English);
        Task oldWork = first.SpeakCommand.ExecuteAsync("one");
        Task newWork = second.SpeakCommand.ExecuteAsync("two");
        service.Calls[0].Completion.SetCanceled();
        await oldWork;
        Assert.False(first.IsPlaying);
        Assert.Equal("", first.Status);
        first.Close();
        Assert.False(service.Calls[1].Token.IsCancellationRequested);
        Assert.True(second.IsPlaying);
        service.Calls[1].Completion.SetResult();
        await newWork;
    }

    [Fact]
    public async Task PairEditCancelsOwnedAudioWithoutCallingTranslation()
    {
        var service = new RecordingSpeech();
        var backend = new MainWindowViewModel(pronunciationService: service);
        Task work = backend.Pronunciation.SpeakCommand.ExecuteAsync(backend.InputText);
        backend.TargetLanguage = Language.Japanese;
        Assert.True(service.Calls.Single().Token.IsCancellationRequested);
        Assert.Null(backend.Result);
        service.Calls[0].Completion.SetCanceled();
        await work;
        backend.Close();
        Assert.False(backend.Pronunciation.SpeakCommand.CanExecute("again"));
    }

    [Fact]
    public void JapanesePopupUsesResultPairAndTypedCards()
    {
        var popup = TranslationPopupViewModel.FromTranslation(new TranslationResult
        {
            OriginalText = "食べました", NaturalTranslation = "ate",
            SourceLanguage = Language.Japanese, TargetLanguage = Language.English,
            Vocabulary = new[] { new VocabularyItem { Word = "食べました", Reading = "たべました", PartOfSpeech = "verb", Meaning = "ate" } }
        }, "test provider");
        Assert.Equal("Japanese → English", popup.LanguagePairLabel);
        Assert.Equal("English", popup.OutputLabel);
        Assert.Equal("Japanese", popup.InputLabel);
        Assert.Equal("たべました", popup.WordCards.Single().SpeechText);
        Assert.Equal("No synonyms supplied", popup.WordCards.Single().SynonymsText);
    }

    private sealed class RecordingSpeech : IPronunciationService
    {
        public string? UnavailableReason { get; init; }
        public List<SpeechCall> Calls { get; } = [];
        public string? GetUnavailableReason(string text, Language language) =>
            PronunciationInput.GetUnavailableReason(text, language) ?? UnavailableReason;
        public Task SpeakAsync(string text, Language language, CancellationToken cancellationToken = default)
        {
            var call = new SpeechCall(text, language, cancellationToken);
            Calls.Add(call);
            return call.Completion.Task;
        }
    }

    private sealed record SpeechCall(string Text, Language Language, CancellationToken Token)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
