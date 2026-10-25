using System.Net.Http;
using FihLens.Application.Translation;
using FihLens.Infrastructure.Translation;

namespace FihLens.WinUI;

internal static class TranslationStartup
{
    private const string ModeHelp = "Set FIHLENS_PROVIDER_MODE to gemini, deepseek, or openrouter-nemotron, then restart FIH Lens.";

    internal static (ITranslationProvider? Provider, string? Label, HttpClient? Client, string? UnavailableReason) Create(
        Func<string, string?> readEnvironment)
    {
        var mode = readEnvironment("FIHLENS_PROVIDER_MODE");
        if (mode is null or "demo")
            return (null, null, null, $"Ask AI is unavailable. {ModeHelp}");

        var credentialName = mode switch
        {
            "gemini" => "GEMINI_API_KEY",
            "deepseek" => "DEEPSEEK_API_KEY",
            "openrouter-nemotron" => "OPENROUTER_API_KEY",
            _ => null
        };
        if (credentialName is null)
            return (null, null, null, $"Ask AI is unavailable. {ModeHelp}");
        var credential = readEnvironment(credentialName);
        if (string.IsNullOrWhiteSpace(credential))
            return (null, null, null, $"Ask AI is unavailable. Set {credentialName} for the selected provider, then restart FIH Lens.");

        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            // Refresh pooled connections periodically so DNS changes can take effect.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            // Free OpenRouter models are queued reasoning models and need more time than paid endpoints.
            Timeout = TimeSpan.FromSeconds(mode.StartsWith("openrouter-", StringComparison.Ordinal) ? 60 : 30),
            MaxResponseContentBufferSize = 1_048_576
        };
        var builder = new PromptBuilder();
        var parser = new TranslationParser();
        return mode switch
        {
            "gemini" => (new GeminiTranslationProvider(client, credential, builder, parser), "Gemini", client, null),
            "deepseek" => (new DeepSeekTranslationProvider(client, credential, builder, parser), "DeepSeek", client, null),
            "openrouter-nemotron" => (new OpenRouterTranslationProvider(client, credential,
                "nvidia/nemotron-3.5-super-vl:free", useJsonSchema: true, builder, parser), "OpenRouter Nemotron (free, may log text)", client, null),
            _ => throw new System.Diagnostics.UnreachableException()
        };
    }
}
