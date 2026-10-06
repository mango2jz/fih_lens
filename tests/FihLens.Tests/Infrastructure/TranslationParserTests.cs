using System.Text.Json;
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
        Assert.Equal("Xin chào", result.NaturalTranslation);
        Assert.Null(result.Explanation);
        Assert.Equal("greeting", result.Context);
        Assert.Equal("hello", Assert.Single(result.Vocabulary).Word);
        Assert.Equal("greeting", Assert.Single(result.Grammar).Pattern);
        Assert.Equal("Chào bạn", Assert.Single(result.Alternatives).Text);
        Assert.IsAssignableFrom<IReadOnlyList<VocabularyItem>>(result.Vocabulary);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"naturalTranslation\":\" \",\"vocabulary\":[],\"grammar\":[],\"alternatives\":[]}")]
    [InlineData("{\"naturalTranslation\":\"ok\",\"vocabulary\":null,\"grammar\":[],\"alternatives\":[]}")]
    [InlineData("{\"naturalTranslation\":\"ok\",\"vocabulary\":[null],\"grammar\":[],\"alternatives\":[]}")]
    [InlineData("{\"naturalTranslation\":\"ok\",\"vocabulary\":[],\"grammar\":[{}],\"alternatives\":[]}")]
    [InlineData("{\"naturalTranslation\":\"ok\",\"vocabulary\":[],\"grammar\":[],\"alternatives\":[{\"text\":\"x\",\"explanation\":4}]}")]
    public void Parse_RejectsInvalidJsonOrShape(string json) =>
        Assert.Throws<JsonException>(() => _parser.Parse(json, Request()));
}
