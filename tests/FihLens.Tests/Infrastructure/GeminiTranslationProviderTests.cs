using System.Net;
using System.Text.Json;
using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Infrastructure.Translation;

namespace FihLens.Tests.Infrastructure;

public sealed class GeminiTranslationProviderTests
{
    private const string Analysis = "{\"naturalTranslation\":\"Xin chào\",\"vocabulary\":[],\"grammar\":[],\"alternatives\":[]}";
    private const string Envelope = "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"thought\":true,\"text\":\"ignore\"},{\"text\":\"{\\\"naturalTranslation\\\":\\\"Xin chào\\\",\"},{\"text\":\"\\\"vocabulary\\\":[],\\\"grammar\\\":[],\\\"alternatives\\\":[]}\"}]}}]}";
    private static TranslationRequest Request(string text = "Hello world") => new(text, Language.English, Language.Vietnamese);
    private static GeminiTranslationProvider Create(HttpClient client, string key = "secret") =>
        new(client, key, new PromptBuilder(), new TranslationParser());

    [Fact]
    public async Task TranslateAsync_SendsGeminiWireShapeAndMapsResult()
    {
        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope);
        using var client = new HttpClient(handler);
        var provider = Create(client);

        var result = await provider.TranslateAsync(Request("  Hello world  "));

        Assert.Equal("  Hello world  ", result.OriginalText);
        Assert.Equal("Xin chào", result.NaturalTranslation);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("POST", handler.Request!.Method.Method);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent", handler.Request.RequestUri!.ToString());
        Assert.Equal("secret", Assert.Single(handler.Request.Headers.GetValues("x-goog-api-key")));
        Assert.False(client.DefaultRequestHeaders.Contains("x-goog-api-key"));
        using var wire = JsonDocument.Parse(handler.RequestBody!);
        var root = wire.RootElement;
        Assert.Equal("Hello world", root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(2048, root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
        var jsonSchema = root.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text");
        Assert.Equal("APPLICATION_JSON", jsonSchema.GetProperty("mimeType").GetString());
        Assert.Contains("alternatives", jsonSchema.GetProperty("schema").GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.True(handler.ResponseContent!.WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request.Content!.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("https://unit.test/")).StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TranslationProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.PaymentRequired, TranslationProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, TranslationProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, TranslationProviderFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, TranslationProviderFailureKind.RequestRejected)]
    [InlineData(HttpStatusCode.NotFound, TranslationProviderFailureKind.RequestRejected)]
    [InlineData(HttpStatusCode.UnprocessableEntity, TranslationProviderFailureKind.RequestRejected)]
    [InlineData(HttpStatusCode.GatewayTimeout, TranslationProviderFailureKind.Timeout)]
    [InlineData(HttpStatusCode.InternalServerError, TranslationProviderFailureKind.Unavailable)]
    public async Task TranslateAsync_MapsHttpStatus(HttpStatusCode status, TranslationProviderFailureKind expected)
    {
        using var client = new HttpClient(ProviderTestHandler.Respond(status, "sensitive provider body"));
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(expected, exception.FailureKind);
        Assert.DoesNotContain("sensitive", exception.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{\"parts\":[]}}]}")]
    [InlineData("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"},\"candidates":[]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"{}\"}]}}]}")]
    public async Task TranslateAsync_MapsInvalidAndBlockedResponses(string envelope)
    {
        var expected = envelope.Contains("SAFETY", StringComparison.Ordinal)
            ? TranslationProviderFailureKind.Refused
            : TranslationProviderFailureKind.InvalidResponse;
        using var client = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, envelope));
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(expected, exception.FailureKind);
    }

    [Fact]
    public async Task TranslateAsync_RejectsUnsupportedRequestBeforeHttp()
    {
        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope);
        using var client = new HttpClient(handler);
        var request = new TranslationRequest("こんにちは", Language.Japanese, Language.English);
        await Assert.ThrowsAsync<NotSupportedException>(() => Create(client).TranslateAsync(request));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task TranslateAsync_PropagatesCallerCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ProviderTestHandler(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var operation = Create(client).TranslateAsync(Request(), cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task TranslateAsync_MapsTimeoutAndResponseLimit()
    {
        var timeoutHandler = new ProviderTestHandler((_, _) => Task.FromException<HttpResponseMessage>(new OperationCanceledException()));
        using (var timeoutClient = new HttpClient(timeoutHandler))
        {
            var timeout = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(timeoutClient).TranslateAsync(Request()));
            Assert.Equal(TranslationProviderFailureKind.Timeout, timeout.FailureKind);
        }

        using var limitedClient = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, new string('x', 100)))
        {
            MaxResponseContentBufferSize = 16
        };
        var limit = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(limitedClient).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, limit.FailureKind);
    }
}
