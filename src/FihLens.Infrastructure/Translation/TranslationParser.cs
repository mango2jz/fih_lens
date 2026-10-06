using System.Text.Json;
using FihLens.Core.Models;

namespace FihLens.Infrastructure.Translation;

public sealed class TranslationParser
{
    public TranslationResult Parse(string json, TranslationRequest request)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(request);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireKind(root, JsonValueKind.Object);

        var naturalTranslation = RequiredText(root, "naturalTranslation");
        var vocabulary = RequiredArray(root, "vocabulary").EnumerateArray()
            .Select(ParseVocabulary).ToArray();
        var grammar = RequiredArray(root, "grammar").EnumerateArray()
            .Select(ParseGrammar).ToArray();
        var alternatives = RequiredArray(root, "alternatives").EnumerateArray()
            .Select(ParseAlternative).ToArray();

        return new TranslationResult
        {
            OriginalText = request.Text,
            NaturalTranslation = naturalTranslation,
            Explanation = OptionalText(root, "explanation"),
            Context = OptionalText(root, "context"),
            Tone = OptionalText(root, "tone"),
            Vocabulary = Array.AsReadOnly(vocabulary),
            Grammar = Array.AsReadOnly(grammar),
            Alternatives = Array.AsReadOnly(alternatives)
        };
    }

    private static VocabularyItem ParseVocabulary(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object);
        return new VocabularyItem
        {
            Word = RequiredText(element, "word"),
            Phonetic = OptionalText(element, "phonetic"),
            PartOfSpeech = RequiredText(element, "partOfSpeech"),
            Meaning = RequiredText(element, "meaning"),
            ExampleSentence = OptionalText(element, "exampleSentence")
        };
    }

    private static GrammarPoint ParseGrammar(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object);
        return new GrammarPoint
        {
            Pattern = RequiredText(element, "pattern"),
            Explanation = OptionalText(element, "explanation"),
            ExampleSentence = OptionalText(element, "exampleSentence")
        };
    }

    private static TranslationAlternative ParseAlternative(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object);
        return new TranslationAlternative
        {
            Text = RequiredText(element, "text"),
            Explanation = OptionalText(element, "explanation")
        };
    }

    private static JsonElement RequiredArray(JsonElement element, string name)
    {
        var value = RequiredProperty(element, name);
        RequireKind(value, JsonValueKind.Array);
        return value;
    }

    private static string RequiredText(JsonElement element, string name)
    {
        var value = RequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new JsonException($"'{name}' must be a nonblank string.");
        }

        return value.GetString()!;
    }

    private static string? OptionalText(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"'{name}' must be a string or null.");
        }

        return value.GetString();
    }

    private static JsonElement RequiredProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value
            : throw new JsonException($"Missing required property '{name}'.");

    private static void RequireKind(JsonElement element, JsonValueKind kind)
    {
        if (element.ValueKind != kind)
        {
            throw new JsonException($"Expected {kind}.");
        }
    }
}
