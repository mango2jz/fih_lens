using System.Net.Http;
using FihLens.Application.Translation;
using FihLens.Infrastructure.Translation;

namespace FihLens.WinUI;

internal static class TranslationStartup
{
    internal static (ITranslationProvider? Provider, string? Label, HttpClient? Client, string? UnavailableReason) Create(
        Func<string, string?> readEnvironment)
    {
        var mode = readEnvironment("FIHLENS_PROVIDER_MODE");
        if (mode is null or "demo")
            return (null, null, null, "Ask AI is unavailable. Set FIHLENS_PROVIDER_MODE to gemini or deepseek, then restart FIH Lens.");

        var credentialName = mode switch
        {
            "gemini" => "GEMINI_API_KEY",
            "deepseek" => "DEEPSEEK_API_KEY",
            _ => null
        };
        if (credentialName is null)
            return (null, null, null, "Ask AI is unavailable. Set FIHLENS_PROVIDER_MODE to gemini or deepseek, then restart FIH Lens.");
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
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 1_048_576
        };
        var builder = new PromptBuilder();
        var parser = new TranslationParser();
        return mode == "gemini"
            ? (new GeminiTranslationProvider(client, credential, builder, parser), "Gemini", client, null)
            : (new DeepSeekTranslationProvider(client, credential, builder, parser), "DeepSeek", client, null);
    }
}
