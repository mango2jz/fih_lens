using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Core.ValueObjects;
using FihLens.Infrastructure.Translation;

namespace FihLens.Tests.Infrastructure;

public sealed class FakeTranslationProviderTests
{
    [Fact]
    public async Task ReturnsExactFixtureAndFreshReadOnlyCollections()
    {
        var provider = new FakeTranslationProvider();
        var first = await provider.TranslateAsync(Request());
        var second = await provider.TranslateAsync(Request());

        Assert.Equal("Hello world", first.OriginalText);
        Assert.Equal("Xin chào thế giới", first.NaturalTranslation);
        Assert.Equal("A simple greeting to the world.", first.Explanation);
        Assert.Equal("Greeting", first.Context);
        Assert.Equal("Friendly", first.Tone);
        Assert.Equal(new VocabularyItem { Word = "hello", Phonetic = "/həˈləʊ/", PartOfSpeech = "interjection", Meaning = "xin chào", ExampleSentence = "Hello, everyone!" }, first.Vocabulary.Single());
        Assert.Equal(new GrammarPoint { Pattern = "Greeting + addressee", Explanation = "A greeting can be followed by the person or group addressed.", ExampleSentence = "Hello, world!" }, first.Grammar.Single());
        Assert.Equal(new TranslationAlternative { Text = "Chào thế giới", Explanation = "A shorter Vietnamese greeting." }, first.Alternatives.Single());
        Assert.NotSame(first, second);
        Assert.NotSame(first.Vocabulary, second.Vocabulary);
        Assert.NotSame(first.Grammar, second.Grammar);
        Assert.NotSame(first.Alternatives, second.Alternatives);
        Assert.Throws<NotSupportedException>(() => ((IList<VocabularyItem>)first.Vocabulary).Add(first.Vocabulary[0]));
        Assert.Throws<NotSupportedException>(() => ((IList<GrammarPoint>)first.Grammar)[0] = new GrammarPoint { Pattern = "changed" });
        Assert.Throws<NotSupportedException>(() => ((IList<TranslationAlternative>)first.Alternatives).Clear());
    }

    [Fact]
    public async Task AcceptsNullOrCefrAndRejectsUnsupportedFixtures()
    {
        var provider = new FakeTranslationProvider();
        foreach (var level in Enum.GetValues<CefrLevel>().Where(level => level != CefrLevel.Unknown))
        {
            Assert.NotNull(await provider.TranslateAsync(Request(level: new CefrLearningLevel(level))));
        }

        Assert.Throws<NotSupportedException>(() => { _ = provider.TranslateAsync(Request(text: "hello world")); });
        Assert.Throws<NotSupportedException>(() => { _ = provider.TranslateAsync(Request(text: " Hello world ")); });
        Assert.Throws<NotSupportedException>(() => { _ = provider.TranslateAsync(Request(mode: TranslationMode.Explain)); });
        Assert.Throws<NotSupportedException>(() => { _ = provider.TranslateAsync(new TranslationRequest("Hello world", Language.English, Language.Japanese)); });
        Assert.Throws<NotSupportedException>(() => { _ = provider.TranslateAsync(new TranslationRequest("Hello world", Language.Auto, Language.Vietnamese)); });
        Assert.Throws<NotSupportedException>(() => { _ = provider.TranslateAsync(new TranslationRequest("Hello world", Language.Japanese, Language.Vietnamese, new JlptLearningLevel(JlptLevel.N5))); });
    }

    [Fact]
    public async Task ChecksNullThenCancellationThenSupportAndValidatesLatency()
    {
        var provider = new FakeTranslationProvider();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        Assert.Throws<ArgumentNullException>(() => { _ = provider.TranslateAsync(null!, canceled.Token); });
        var canceledTask = provider.TranslateAsync(Request("unsupported"), canceled.Token);
        Assert.True(canceledTask.IsCanceled);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledTask);
        Assert.Equal(canceled.Token, exception.CancellationToken);
        Assert.Throws<NotSupportedException>(() => { _ = provider.TranslateAsync(Request("unsupported")); });
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeTranslationProvider(TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeTranslationProvider(TimeSpan.FromDays(100)));
    }

    [Fact]
    public async Task DefaultLatencyCompletesImmediatelyAndLaterCancellationKeepsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var task = new FakeTranslationProvider().TranslateAsync(Request(), cancellation.Token);
        Assert.True(task.IsCompletedSuccessfully);
        var result = await task;
        cancellation.Cancel();
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(result, await task);
    }

    [Fact]
    public async Task DelayedCallUsesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var task = new FakeTranslationProvider(TimeSpan.FromSeconds(30)).TranslateAsync(Request(), cancellation.Token);
        Assert.False(task.IsCompleted);
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private static TranslationRequest Request(string text = "Hello world", TranslationMode mode = TranslationMode.Translate,
        LearningLevel? level = null, Language target = Language.Vietnamese) => new(text, Language.English, target, level, mode);
}
