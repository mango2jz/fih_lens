using FihLens.Core.Models;
using FihLens.Core.Enums;

namespace FihLens.Tests.Core;

public class TranslationResultTests
{
    [Fact]
    public void Result_UsesNullOptionalTextAndEmptyCollectionsByDefault()
    {
        var result = new TranslationResult { OriginalText = "hello", SourceLanguage = Language.English, TargetLanguage = Language.Vietnamese };

        Assert.Null(result.NaturalTranslation);
        Assert.Null(result.Explanation);
        Assert.Null(result.Context);
        Assert.Null(result.Tone);
        Assert.Empty(result.Vocabulary);
        Assert.Empty(result.Grammar);
        Assert.Empty(result.Alternatives);
        var vocabulary = new VocabularyItem { Word = "word", PartOfSpeech = "noun", Meaning = "meaning" };
        Assert.Empty(vocabulary.Synonyms);
        Assert.Throws<ArgumentNullException>(() => new VocabularyItem { Word = "word", PartOfSpeech = "noun", Meaning = "meaning", Synonyms = null! });
    }

    [Fact]
    public void Result_RetainsDescriptionsAndCollectionItems()
    {
        var vocabulary = new VocabularyItem { Word = "hello", PartOfSpeech = "interjection", Meaning = "greeting", Phonetic = "həˈloʊ", Reading = "hello", BaseForm = "hello", Synonyms = ["hi"], ExampleSentence = "Hello, there." };
        var grammar = new GrammarPoint { Pattern = "です", Explanation = "polite copula", ExampleSentence = "学生です。" };
        var alternative = new TranslationAlternative { Text = "Hi", Explanation = "informal" };
        var result = new TranslationResult
        {
            OriginalText = "hello", SourceLanguage = Language.English, TargetLanguage = Language.Japanese, NaturalTranslation = "こんにちは", Explanation = "A greeting", Context = "casual", Tone = "friendly",
            Vocabulary = [vocabulary], Grammar = [grammar], Alternatives = [alternative]
        };

        Assert.Equal("hello", result.OriginalText);
        Assert.Equal("こんにちは", result.NaturalTranslation);
        var actualVocabulary = Assert.Single(result.Vocabulary);
        Assert.Equal("hello", actualVocabulary.Word);
        Assert.Equal("interjection", actualVocabulary.PartOfSpeech);
        Assert.Equal("greeting", actualVocabulary.Meaning);
        Assert.Equal("həˈloʊ", actualVocabulary.Phonetic);
        Assert.Equal("hello", actualVocabulary.Reading);
        Assert.Equal("hello", actualVocabulary.BaseForm);
        Assert.Equal(new[] { "hi" }, actualVocabulary.Synonyms);
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
        var result = new TranslationResult { OriginalText = "hello", SourceLanguage = Language.English, TargetLanguage = Language.Vietnamese };

        Assert.Throws<ArgumentNullException>(() => new TranslationResult { OriginalText = "hello", SourceLanguage = Language.English, TargetLanguage = Language.Vietnamese, Vocabulary = null! });
        Assert.Throws<ArgumentNullException>(() => new TranslationResult { OriginalText = "hello", SourceLanguage = Language.English, TargetLanguage = Language.Vietnamese, Grammar = null! });
        Assert.Throws<ArgumentNullException>(() => new TranslationResult { OriginalText = "hello", SourceLanguage = Language.English, TargetLanguage = Language.Vietnamese, Alternatives = null! });
        Assert.Throws<ArgumentNullException>(() => result with { Vocabulary = null! });
        Assert.Throws<ArgumentNullException>(() => result with { Grammar = null! });
        Assert.Throws<ArgumentNullException>(() => result with { Alternatives = null! });
    }
}
