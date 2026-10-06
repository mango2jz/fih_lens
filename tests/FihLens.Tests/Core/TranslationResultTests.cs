using FihLens.Core.Models;

namespace FihLens.Tests.Core;

public class TranslationResultTests
{
    [Fact]
    public void Result_UsesNullOptionalTextAndEmptyCollectionsByDefault()
    {
        var result = new TranslationResult { OriginalText = "hello" };

        Assert.Null(result.NaturalTranslation);
        Assert.Null(result.Explanation);
        Assert.Null(result.Context);
        Assert.Null(result.Tone);
        Assert.Empty(result.Vocabulary);
        Assert.Empty(result.Grammar);
        Assert.Empty(result.Alternatives);
    }

    [Fact]
    public void Result_RetainsDescriptionsAndCollectionItems()
    {
        var vocabulary = new VocabularyItem { Word = "hello", PartOfSpeech = "interjection", Meaning = "greeting", Phonetic = "həˈloʊ", ExampleSentence = "Hello, there." };
        var grammar = new GrammarPoint { Pattern = "です", Explanation = "polite copula", ExampleSentence = "学生です。" };
        var alternative = new TranslationAlternative { Text = "Hi", Explanation = "informal" };
        var result = new TranslationResult
        {
            OriginalText = "hello", NaturalTranslation = "こんにちは", Explanation = "A greeting", Context = "casual", Tone = "friendly",
            Vocabulary = [vocabulary], Grammar = [grammar], Alternatives = [alternative]
        };

        Assert.Equal("hello", result.OriginalText);
        Assert.Equal("こんにちは", result.NaturalTranslation);
        var actualVocabulary = Assert.Single(result.Vocabulary);
        Assert.Equal("hello", actualVocabulary.Word);
        Assert.Equal("interjection", actualVocabulary.PartOfSpeech);
        Assert.Equal("greeting", actualVocabulary.Meaning);
        Assert.Equal("həˈloʊ", actualVocabulary.Phonetic);
        Assert.Equal("Hello, there.", actualVocabulary.ExampleSentence);

        var actualGrammar = Assert.Single(result.Grammar);
        Assert.Equal("です", actualGrammar.Pattern);
        Assert.Equal("polite copula", actualGrammar.Explanation);
        Assert.Equal("学生です。", actualGrammar.ExampleSentence);

        var actualAlternative = Assert.Single(result.Alternatives);
        Assert.Equal("Hi", actualAlternative.Text);
        Assert.Equal("informal", actualAlternative.Explanation);
    }

    [Fact]
    public void Result_RejectsNullCollectionsOnInitializationAndWithExpressions()
    {
        var result = new TranslationResult { OriginalText = "hello" };

        Assert.Throws<ArgumentNullException>(() => new TranslationResult { OriginalText = "hello", Vocabulary = null! });
        Assert.Throws<ArgumentNullException>(() => new TranslationResult { OriginalText = "hello", Grammar = null! });
        Assert.Throws<ArgumentNullException>(() => new TranslationResult { OriginalText = "hello", Alternatives = null! });
        Assert.Throws<ArgumentNullException>(() => result with { Vocabulary = null! });
        Assert.Throws<ArgumentNullException>(() => result with { Grammar = null! });
        Assert.Throws<ArgumentNullException>(() => result with { Alternatives = null! });
    }
}
