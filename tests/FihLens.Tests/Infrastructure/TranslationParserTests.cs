using System.Text.Json;
using System.Text.Json.Nodes;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Infrastructure.Translation;

namespace FihLens.Tests.Infrastructure;

public sealed class TranslationParserTests
{
    private readonly TranslationParser _parser = new();
    private static TranslationRequest Request() => new("  Hello world  ", Language.English, Language.Vietnamese);

    [Fact]
    public void Parse_MapsAnalysisAndUsesRequestText()
    {
        var source = """{"naturalTranslation":"Xin chào","explanation":null,"context":"greeting","tone":"friendly","vocabulary":[{"word":"hello","partOfSpeech":"interjection","meaning":"xin chào","phonetic":null,"exampleSentence":"Hello!"}],"grammar":[{"pattern":"greeting","explanation":null}],"alternatives":[{"text":"Chào bạn","explanation":"informal"}],"extra":true}""";

        var result = _parser.Parse(source, Request());

        Assert.Equal("  Hello world  ", result.OriginalText);
        Assert.Equal(Language.English, result.SourceLanguage);
        Assert.Equal(Language.Vietnamese, result.TargetLanguage);
        Assert.Equal("Xin chào", result.NaturalTranslation);
        Assert.Null(result.Explanation);
        Assert.Equal("greeting", result.Context);
        Assert.Equal("friendly", result.Tone);
        var vocabulary = Assert.Single(result.Vocabulary);
        Assert.Equal("hello", vocabulary.Word);
        Assert.Equal("interjection", vocabulary.PartOfSpeech);
        Assert.Equal("xin chào", vocabulary.Meaning);
        Assert.Null(vocabulary.Phonetic);
        Assert.Equal("Hello!", vocabulary.ExampleSentence);
        Assert.Null(vocabulary.Reading);
        Assert.Null(vocabulary.BaseForm);
        Assert.Empty(vocabulary.Synonyms);
        var grammar = Assert.Single(result.Grammar);
        Assert.Equal("greeting", grammar.Pattern);
        Assert.Null(grammar.Explanation);
        Assert.Null(grammar.ExampleSentence);
        var alternative = Assert.Single(result.Alternatives);
        Assert.Equal("Chào bạn", alternative.Text);
        Assert.Equal("informal", alternative.Explanation);
        Assert.IsAssignableFrom<IReadOnlyList<VocabularyItem>>(result.Vocabulary);
    }

    [Fact]
    public void Parse_MapsJapaneseWordDetailsAndUsesTrustedRequestPair()
    {
        var request = new TranslationRequest("食べました", Language.Japanese, Language.English);
        const string source = """{"naturalTranslation":"ate","sourceLanguage":"Vietnamese","targetLanguage":"Japanese","vocabulary":[{"word":"食べました","reading":"たべました","baseForm":"食べる","partOfSpeech":"verb","meaning":"ate","synonyms":["食った","召し上がった"]}],"grammar":[],"alternatives":[]}""";

        var result = _parser.Parse(source, request);
        Assert.Equal(Language.Japanese, result.SourceLanguage);
        Assert.Equal(Language.English, result.TargetLanguage);
        var word = Assert.Single(result.Vocabulary);
        Assert.Equal("たべました", word.Reading);
        Assert.Equal("食べる", word.BaseForm);
        Assert.Equal(new[] { "食った", "召し上がった" }, word.Synonyms);
    }

    [Theory]
    [InlineData(Language.English, Language.Vietnamese)]
    [InlineData(Language.Vietnamese, Language.English)]
    [InlineData(Language.English, Language.Japanese)]
    [InlineData(Language.Japanese, Language.English)]
    [InlineData(Language.Vietnamese, Language.Japanese)]
    [InlineData(Language.Japanese, Language.Vietnamese)]
    public void Parse_UsesRequestPairForEverySupportedDirection(Language source, Language target)
    {
        var result = _parser.Parse(MinimalAnalysis, new TranslationRequest("word", source, target));
        Assert.Equal(source, result.SourceLanguage);
        Assert.Equal(target, result.TargetLanguage);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("4")]
    [InlineData("[null]")]
    [InlineData("[\" \"]")]
    [InlineData("[\"x\",\"x\",\"x\",\"x\",\"x\",\"x\"]")]
    public void Parse_RejectsInvalidSynonyms(string synonyms)
    {
        var json = "{\"naturalTranslation\":\"ok\",\"vocabulary\":[{\"word\":\"x\",\"partOfSpeech\":\"noun\",\"meaning\":\"y\",\"synonyms\":" + synonyms + "}],\"grammar\":[],\"alternatives\":[]}";
        Assert.Throws<JsonException>(() => _parser.Parse(json, Request()));
    }

    [Fact]
    public void Parse_RejectsOversizeSynonym()
    {
        var synonyms = System.Text.Json.JsonSerializer.Serialize(new[] { new string('x', 201) });
        var json = "{\"naturalTranslation\":\"ok\",\"vocabulary\":[{\"word\":\"x\",\"partOfSpeech\":\"noun\",\"meaning\":\"y\",\"synonyms\":" + synonyms + "}],\"grammar\":[],\"alternatives\":[]}";
        Assert.Throws<JsonException>(() => _parser.Parse(json, Request()));
    }

    [Theory]
    [InlineData("reading", "\" \"")]
    [InlineData("baseForm", "4")]
    public void Parse_RejectsInvalidNewOptionalTerms(string field, string value)
    {
        var json = "{\"naturalTranslation\":\"ok\",\"vocabulary\":[{\"word\":\"x\",\"partOfSpeech\":\"noun\",\"meaning\":\"y\",\"" + field + "\":" + value + "}],\"grammar\":[],\"alternatives\":[]}";
        Assert.Throws<JsonException>(() => _parser.Parse(json, Request()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"naturalTranslation\":\" \",\"vocabulary\":[],\"grammar\":[],\"alternatives\":[]}")]
    [InlineData("{\"naturalTranslation\":4,\"vocabulary\":[],\"grammar\":[],\"alternatives\":[]}")]
    [InlineData("{\"naturalTranslation\":\"ok\",\"vocabulary\":null,\"grammar\":[],\"alternatives\":[]}")]
    [InlineData("{\"naturalTranslation\":\"ok\",\"vocabulary\":[null],\"grammar\":[],\"alternatives\":[]}")]
    [InlineData("{\"naturalTranslation\":\"ok\",\"vocabulary\":[],\"grammar\":[{}],\"alternatives\":[]}")]
    [InlineData("{\"naturalTranslation\":\"ok\",\"vocabulary\":[],\"grammar\":[],\"alternatives\":[{\"text\":\"x\",\"explanation\":4}]}")]
    public void Parse_RejectsInvalidJsonOrShape(string json) =>
        Assert.ThrowsAny<JsonException>(() => _parser.Parse(json, Request()));

    private const string MinimalAnalysis = """{"naturalTranslation":"ok","vocabulary":[],"grammar":[],"alternatives":[]}""";

    [Theory]
    [InlineData("vocabulary")]
    [InlineData("grammar")]
    [InlineData("alternatives")]
    public void Parse_RejectsMissingNullWrongTypeAndNullElementForEachArray(string name)
    {
        var root = JsonNode.Parse(MinimalAnalysis)!.AsObject();
        root.Remove(name);
        Assert.Throws<JsonException>(() => _parser.Parse(root.ToJsonString(), Request()));
        foreach (var invalid in new[] { "null", "{}", "[null]" })
        {
            root[name] = JsonNode.Parse(invalid);
            Assert.Throws<JsonException>(() => _parser.Parse(root.ToJsonString(), Request()));
        }
    }

    [Theory]
    [InlineData("explanation")]
    [InlineData("context")]
    [InlineData("tone")]
    public void Parse_OptionalRootTextAcceptsAbsentOrNullButRejectsWrongType(string name)
    {
        var root = JsonNode.Parse(MinimalAnalysis)!.AsObject();
        var absent = _parser.Parse(root.ToJsonString(), Request());
        Assert.Null(absent.Explanation);
        Assert.Null(absent.Context);
        Assert.Null(absent.Tone);
        root[name] = null;
        _parser.Parse(root.ToJsonString(), Request());
        root[name] = 4;
        Assert.Throws<JsonException>(() => _parser.Parse(root.ToJsonString(), Request()));
    }

    [Theory]
    [InlineData("vocabulary", "word")]
    [InlineData("vocabulary", "partOfSpeech")]
    [InlineData("vocabulary", "meaning")]
    [InlineData("grammar", "pattern")]
    [InlineData("alternatives", "text")]
    public void Parse_RejectsMissingBlankAndWrongTypeRequiredItemText(string array, string field)
    {
        var root = JsonNode.Parse("""{"naturalTranslation":"ok","vocabulary":[{"word":"hello","partOfSpeech":"interjection","meaning":"xin chào"}],"grammar":[{"pattern":"greeting"}],"alternatives":[{"text":"chào"}]}""")!;
        var item = root[array]![0]!.AsObject();
        item.Remove(field);
        Assert.Throws<JsonException>(() => _parser.Parse(root.ToJsonString(), Request()));
        foreach (var invalid in new[] { "null", "4", "\" \"" })
        {
            item[field] = JsonNode.Parse(invalid);
            Assert.Throws<JsonException>(() => _parser.Parse(root.ToJsonString(), Request()));
        }
    }

    [Theory]
    [InlineData("vocabulary", "phonetic")]
    [InlineData("vocabulary", "exampleSentence")]
    [InlineData("grammar", "explanation")]
    [InlineData("grammar", "exampleSentence")]
    [InlineData("alternatives", "explanation")]
    public void Parse_OptionalItemTextAcceptsAbsentNullOrTextButRejectsWrongType(string array, string field)
    {
        var root = JsonNode.Parse("""{"naturalTranslation":"ok","vocabulary":[{"word":"hello","partOfSpeech":"interjection","meaning":"xin chào"}],"grammar":[{"pattern":"greeting"}],"alternatives":[{"text":"chào"}]}""")!;
        _parser.Parse(root.ToJsonString(), Request());
        var item = root[array]![0]!.AsObject();
        item[field] = null;
        _parser.Parse(root.ToJsonString(), Request());
        item[field] = "example";
        _parser.Parse(root.ToJsonString(), Request());
        item[field] = true;
        Assert.Throws<JsonException>(() => _parser.Parse(root.ToJsonString(), Request()));
    }
}
