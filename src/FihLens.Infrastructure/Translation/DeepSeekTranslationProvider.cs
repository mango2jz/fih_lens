using System.Net.Http.Headers;
using System.Text.Json;
using FihLens.Application.Translation;
using FihLens.Core.Models;

namespace FihLens.Infrastructure.Translation;

public sealed class DeepSeekTranslationProvider : ITranslationProvider
{
    private const string Endpoint = "https://api.deepseek.com/chat/completions";
    private const string Model = "deepseek-flash";
    private readonly HttpClient _client;
    private readonly string _apiKey;
    private readonly PromptBuilder _promptBuilder;
    private readonly TranslationParser _parser;

    public DeepSeekTranslationProvider(HttpClient client, string apiKey, PromptBuilder promptBuilder,
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
            model = Model,
            thinking = new { type = "disabled" },
            messages = new[]
            {
                new { role = "system", content = instructions.SystemText },
                new { role = "user", content = instructions.UserText }
            },
            response_format = new { type = "json_object" },
            max_tokens = 2048,
            stream = false
        });

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
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
        {
            throw new JsonException("DeepSeek response must contain exactly one choice.");
        }

        var choice = choices[0];
        if (choice.ValueKind != JsonValueKind.Object || !choice.TryGetProperty("finish_reason", out var finishReason) || finishReason.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("DeepSeek response has no completion reason.");
        }

        switch (finishReason.GetString())
        {
            case "content_filter":
                throw new TranslationProviderException(TranslationProviderFailureKind.Refused);
            case "insufficient_system_resource":
            case "aborted":
                throw new TranslationProviderException(TranslationProviderFailureKind.Unavailable);
            case "stop":
                break;
            default:
                throw new JsonException("DeepSeek response did not complete normally.");
        }

        if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            message.TryGetProperty("tool_calls", out _) || message.TryGetProperty("function_call", out _) ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(content.GetString()))
        {
            throw new JsonException("DeepSeek response has no usable message content.");
        }

        return content.GetString()!;
    }
}
