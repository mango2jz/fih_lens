using System.Text.Json;

namespace FihLens.Infrastructure.Translation;

// JSON schema of the analysis object TranslationParser accepts; shared by providers that support structured output.
internal static class TranslationAnalysisSchema
{
    public static readonly JsonElement Value = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type":"object",
          "properties":{
            "naturalTranslation":{"type":"string"},
            "explanation":{"type":["string","null"]},
            "context":{"type":["string","null"]},
            "tone":{"type":["string","null"]},
            "vocabulary":{"type":"array","items":{"type":"object","properties":{
              "word":{"type":"string"},"phonetic":{"type":["string","null"]},
              "reading":{"type":["string","null"]},"baseForm":{"type":["string","null"]},
              "partOfSpeech":{"type":"string"},"meaning":{"type":"string"},
              "synonyms":{"type":"array","items":{"type":"string"}},
              "exampleSentence":{"type":["string","null"]}},
              "required":["word","partOfSpeech","meaning"]}},
            "grammar":{"type":"array","items":{"type":"object","properties":{
              "pattern":{"type":"string"},"explanation":{"type":["string","null"]},
              "exampleSentence":{"type":["string","null"]}},"required":["pattern"]}},
            "alternatives":{"type":"array","items":{"type":"object","properties":{
              "text":{"type":"string"},"explanation":{"type":["string","null"]}},
              "required":["text"]}}
          },
          "required":["naturalTranslation","vocabulary","grammar","alternatives"]
        }
        """);
}
