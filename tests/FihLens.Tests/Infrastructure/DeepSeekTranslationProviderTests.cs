using System.Net;
using System.Text.Json;
using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Infrastructure.Translation;

namespace FihLens.Tests.Infrastructure;

public sealed class DeepSeekTranslationProviderTests
{
    private const string Analysis = "{\"naturalTranslation\":\"Xin chào\",\"vocabulary\":[],\"grammar\":[],\"alternatives\":[]}";
    private static string Envelope(string finish = "stop", string? content = null) =>
        JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finish, message = new { role = "assistant", reasoning_content = "ignore this", content = content ?? Analysis } } } });
    private static TranslationRequest Request(string text = "Hello world") => new(text, Language.English, Language.Vietnamese);
    private static DeepSeekTranslationProvider Create(HttpClient client, string key = "secret") =>
        new(client, key, new PromptBuilder(), new TranslationParser());

    [Fact]
    public async Task TranslateAsync_SendsDeepSeekWireShapeAndMapsResult()
    {
        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope());
        using var client = new HttpClient(handler);

        var result = await Create(client).TranslateAsync(Request("  Hello world  "));

        Assert.Equal("  Hello world  ", result.OriginalText);
        Assert.Equal("Xin chào", result.NaturalTranslation);
        Assert.Equal("POST", handler.Request!.Method.Method);
        Assert.Equal("https://api.deepseek.com/chat/completions", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer secret", handler.Request.Headers.Authorization!.ToString());
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        using var wire = JsonDocument.Parse(handler.RequestBody!);
        var root = wire.RootElement;
        Assert.Equal("deepseek-flash", root.GetProperty("model").GetString());
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Hello world", root.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Contains("JSON", root.GetProperty("messages")[0].GetProperty("content").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(2048, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
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
    [InlineData("not json", "InvalidResponse")]
    [InlineData("length", "InvalidResponse")]
    [InlineData("tool_calls", "InvalidResponse")]
    [InlineData("content_filter", "Refused")]
    [InlineData("insufficient_system_resource", "Unavailable")]
    [InlineData("aborted", "Unavailable")]
    public async Task TranslateAsync_MapsProviderCompletionReasons(string finish, string expectedKind)
    {
        var body = finish == "not json" ? finish : Envelope(finish);
        using var client = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, body));
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(expectedKind, exception.FailureKind.ToString());
    }

    [Fact]
    public async Task TranslateAsync_RejectsMalformedAnalysisAndUnsupportedRequestBeforeHttp()
    {
        using var malformedClient = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope(content: "{}")));
        var malformed = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(malformedClient).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, malformed.FailureKind);

        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope());
        using var client = new HttpClient(handler);
        var unsupported = new TranslationRequest("こんにちは", Language.Japanese, Language.English);
        await Assert.ThrowsAsync<NotSupportedException>(() => Create(client).TranslateAsync(unsupported));
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
}
