using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Core.ValueObjects;
using FihLens.Infrastructure.Translation;

namespace FihLens.Tests.Infrastructure;

public sealed class FakeTranslationProviderTests
{
    [Fact]
    public void ReturnsExactFixtureAndFreshReadOnlyCollections()
    {
        var provider = new FakeTranslationProvider();
        var first = provider.TranslateAsync(Request()).Result;
        var second = provider.TranslateAsync(Request()).Result;

        Assert.Equal("Hello world", first.OriginalText);
        Assert.Equal("Xin chào thế giới", first.NaturalTranslation);
        Assert.Equal("A simple greeting to the world.", first.Explanation);
        Assert.Equal("Greeting", first.Context);
        Assert.Equal("Friendly", first.Tone);
        Assert.Equal(new VocabularyItem { Word = "hello", Phonetic = "/həˈləʊ/", PartOfSpeech = "interjection", Meaning = "xin chào", ExampleSentence = "Hello, everyone!" }, first.Vocabulary.Single());
        Assert.Equal(new GrammarPoint { Pattern = "Greeting + addressee", Explanation = "A greeting can be followed by the person or group addressed.", ExampleSentence = "Hello, world!" }, first.Grammar.Single());
        Assert.Equal(new TranslationAlternative { Text = "Chào thế giới", Explanation = "A shorter Vietnamese greeting." }, first.Alternatives.Single());
        Assert.NotSame(first.Vocabulary, second.Vocabulary);
        Assert.Throws<NotSupportedException>(() => ((IList<VocabularyItem>)first.Vocabulary).Add(first.Vocabulary[0]));
    }

    [Fact]
    public void AcceptsNullOrCefrAndRejectsUnsupportedFixtures()
    {
        var provider = new FakeTranslationProvider();
        foreach (var level in Enum.GetValues<CefrLevel>().Where(level => level != CefrLevel.Unknown))
        {
            Assert.NotNull(provider.TranslateAsync(Request(level: new CefrLearningLevel(level))).Result);
        }

        Assert.Throws<NotSupportedException>(() => provider.TranslateAsync(Request(text: "hello world")));
        Assert.Throws<NotSupportedException>(() => provider.TranslateAsync(Request(mode: TranslationMode.Explain)));
        Assert.Throws<NotSupportedException>(() => provider.TranslateAsync(new TranslationRequest("Hello world", Language.English, Language.Japanese)));
    }

    [Fact]
    public void ChecksNullThenCancellationThenSupportAndValidatesLatency()
    {
        var provider = new FakeTranslationProvider();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        Assert.Throws<ArgumentNullException>(() => provider.TranslateAsync(null!, canceled.Token));
        Assert.True(provider.TranslateAsync(Request("unsupported"), canceled.Token).IsCanceled);
        Assert.Throws<NotSupportedException>(() => provider.TranslateAsync(Request("unsupported")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeTranslationProvider(TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeTranslationProvider(TimeSpan.FromDays(100)));
    }

    [Fact]
    public async Task DelayedCallUsesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var task = new FakeTranslationProvider(TimeSpan.FromSeconds(30)).TranslateAsync(Request(), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static TranslationRequest Request(string text = "Hello world", TranslationMode mode = TranslationMode.Translate,
        LearningLevel? level = null, Language target = Language.Vietnamese) => new(text, Language.English, target, level, mode);
}
