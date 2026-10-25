using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FihLens.Application.Translation;
using FihLens.Core.Models;

namespace FihLens.Infrastructure.Translation;

public sealed class OpenRouterTranslationProvider : ITranslationProvider
{
    private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";
    private readonly HttpClient _client;
    private readonly string _apiKey;
    private static readonly JsonSerializerOptions BodyOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly string _model;
    private readonly bool _useJsonSchema;
    private readonly PromptBuilder _promptBuilder;
    private readonly TranslationParser _parser;

    public OpenRouterTranslationProvider(HttpClient client, string apiKey, string model, bool useJsonSchema,
        PromptBuilder promptBuilder, TranslationParser parser)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? throw new ArgumentException("An API key is required.", nameof(apiKey)) : apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? throw new ArgumentException("A model id is required.", nameof(model)) : model;
        _useJsonSchema = useJsonSchema;
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
            model = _model,
            messages = new[]
            {
                new { role = "system", content = instructions.SystemText },
                new { role = "user", content = instructions.UserText }
            },
            // Constrained decoding where the endpoint supports it; require_parameters stops OpenRouter silently dropping the schema.
            response_format = _useJsonSchema
                ? (object)new { type = "json_schema", json_schema = new { name = "translation_analysis", strict = false, schema = TranslationAnalysisSchema.Value } }
                : new { type = "json_object" },
            provider = _useJsonSchema ? new { require_parameters = true } : null,
            // Free models are reasoning models: low effort bounds latency and token use; exclude keeps reasoning out of the reply.
            reasoning = new { effort = "low", exclude = true },
            max_tokens = 4096,
            stream = false
        }, BodyOptions);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
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

    private static string ExtractAnalysis(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("OpenRouter response must be an object.");
        }

        // OpenRouter can report upstream failures inside a 200 body; the error wins over any choices.
        ThrowIfError(root);

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() != 1)
        {
            throw new JsonException("OpenRouter response must contain exactly one choice.");
        }

        var choice = choices[0];
        if (choice.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("OpenRouter choice is invalid.");
        }

        ThrowIfError(choice);

        if (!choice.TryGetProperty("finish_reason", out var finishReason) || finishReason.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("OpenRouter response has no completion reason.");
        }

        switch (finishReason.GetString())
        {
            case "content_filter":
                throw new TranslationProviderException(TranslationProviderFailureKind.Refused);
            case "error":
                throw new TranslationProviderException(TranslationProviderFailureKind.Unavailable);
            case "stop":
                break;
            default:
                throw new JsonException("OpenRouter response did not complete normally.");
        }

        if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            HasValue(message, "tool_calls") || HasValue(message, "function_call") ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(content.GetString()))
        {
            throw new JsonException("OpenRouter response has no usable message content.");
        }

        return content.GetString()!;
    }

    // Some upstreams send explicit nulls for unused fields; only a real value counts.
    private static bool HasValue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

    // Reads only the numeric code; error.message and metadata can echo provider or input text.
    private static void ThrowIfError(JsonElement element)
    {
        if (!element.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var kind = TranslationProviderFailureKind.Unavailable;
        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code) &&
            code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var status))
        {
            kind = status switch
            {
                401 or 402 or 403 => TranslationProviderFailureKind.Authentication,
                429 => TranslationProviderFailureKind.RateLimited,
                408 or 504 => TranslationProviderFailureKind.Timeout,
                >= 500 => TranslationProviderFailureKind.Unavailable,
                _ => TranslationProviderFailureKind.RequestRejected
            };
        }

        throw new TranslationProviderException(kind);
    }
}
