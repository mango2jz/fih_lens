using System.Text.Json;
using FihLens.Application.Translation;
using FihLens.Core.Models;

namespace FihLens.Infrastructure.Translation;

public sealed class GeminiTranslationProvider : ITranslationProvider
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent";
    private readonly HttpClient _client;
    private readonly string _apiKey;
    private readonly PromptBuilder _promptBuilder;
    private readonly TranslationParser _parser;

    public GeminiTranslationProvider(HttpClient client, string apiKey, PromptBuilder promptBuilder,
        TranslationParser parser)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? throw new ArgumentException("An API key is required.", nameof(apiKey)) : apiKey;
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instructions = _promptBuilder.Build(request);
        cancellationToken.ThrowIfCancellationRequested();

        var body = JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = instructions.SystemText } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = instructions.UserText } } } },
            generationConfig = new
            {
                maxOutputTokens = 2048,
                responseFormat = new
                {
                    text = new
                    {
                        mimeType = "APPLICATION_JSON",
                        schema = AnalysisSchema
                    }
                }
            }
        });

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        httpRequest.Headers.Add("x-goog-api-key", _apiKey);
        ProviderHttp.SetJsonContent(httpRequest, body);
        var responseJson = await ProviderHttp.PostAsync(_client, httpRequest, cancellationToken).ConfigureAwait(false);
        try
        {
            var analysisJson = ExtractAnalysis(responseJson);
            return _parser.Parse(analysisJson, request);
        }
        catch (TranslationProviderException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new TranslationProviderException(TranslationProviderFailureKind.InvalidResponse, exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new TranslationProviderException(TranslationProviderFailureKind.InvalidResponse, exception);
        }
    }

    private static readonly JsonElement AnalysisSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type":"object",
          "properties":{
            "naturalTranslation":{"type":"string"},
            "explanation":{"type":["string","null"]},
            "context":{"type":["string","null"]},
            "tone":{"type":["string","null"]},
            "vocabulary":{"type":"array","items":{"type":"object","properties":{
              "word":{"type":"string"},"phonetic":{"type":["string","null"]},
              "partOfSpeech":{"type":"string"},"meaning":{"type":"string"},
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

    private static string ExtractAnalysis(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Gemini response must be an object.");
        }

        if (root.TryGetProperty("promptFeedback", out var feedback))
        {
            if (feedback.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Gemini prompt feedback is invalid.");
            }
            if (feedback.TryGetProperty("blockReason", out var blockReason))
            {
                if (blockReason.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException("Gemini block reason is invalid.");
                }
                if (!string.IsNullOrWhiteSpace(blockReason.GetString()))
                {
                    throw new TranslationProviderException(TranslationProviderFailureKind.Refused);
                }
            }
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() != 1)
        {
            throw new JsonException("Gemini response must contain exactly one candidate.");
        }

        var candidate = candidates[0];
        if (candidate.ValueKind != JsonValueKind.Object || !candidate.TryGetProperty("finishReason", out var finishReason) || finishReason.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Gemini response has no completion reason.");
        }

        var reason = finishReason.GetString();
        if (reason is "SAFETY" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "RECITATION")
        {
            throw new TranslationProviderException(TranslationProviderFailureKind.Refused);
        }
        if (reason != "STOP")
        {
            throw new JsonException("Gemini response did not complete normally.");
        }

        if (!candidate.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object ||
            !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Gemini response has no content parts.");
        }

        var text = new System.Text.StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Gemini content part is invalid.");
            }
            if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
            {
                continue;
            }
            if (part.TryGetProperty("thought", out thought) && thought.ValueKind != JsonValueKind.False)
            {
                throw new JsonException("Gemini thought marker is invalid.");
            }
            if (part.TryGetProperty("functionCall", out _) || part.TryGetProperty("functionResponse", out _) ||
                part.TryGetProperty("inlineData", out _) || part.TryGetProperty("fileData", out _))
            {
                throw new JsonException("Gemini returned unsupported nontext content.");
            }
            if (!part.TryGetProperty("text", out var textPart) || textPart.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Gemini returned an unsupported content part.");
            }
            text.Append(textPart.GetString());
        }

        if (string.IsNullOrWhiteSpace(text.ToString()))
        {
            throw new JsonException("Gemini returned no usable text.");
        }

        return text.ToString();
    }
}
