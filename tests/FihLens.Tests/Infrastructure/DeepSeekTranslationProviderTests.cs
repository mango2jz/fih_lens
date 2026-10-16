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
        Assert.Equal("  Hello world  ", root.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Contains("JSON", root.GetProperty("messages")[0].GetProperty("content").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(2048, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.True(handler.ResponseContent!.WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request.Content!.ReadAsStringAsync());
        using var nextResponse = await client.GetAsync("https://unit.test/");
        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
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
    [InlineData(HttpStatusCode.ServiceUnavailable, TranslationProviderFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.RequestTimeout, TranslationProviderFailureKind.Timeout)]
    [InlineData((HttpStatusCode)418, TranslationProviderFailureKind.RequestRejected)]
    public async Task TranslateAsync_MapsHttpStatus(HttpStatusCode status, TranslationProviderFailureKind expected)
    {
        var handler = ProviderTestHandler.Respond(status, "sensitive provider body");
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(expected, exception.FailureKind);
        Assert.DoesNotContain("sensitive", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.Equal(1, handler.Calls);
        Assert.True(handler.ResponseContent!.WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
        using var nextResponse = await client.GetAsync("https://unit.test/");
        Assert.Equal(status, nextResponse.StatusCode);
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
    public async Task TranslateAsync_RejectsMalformedAnalysisAndSupportsJapanesePair()
    {
        using var malformedClient = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope(content: "{}")));
        var malformed = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(malformedClient).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, malformed.FailureKind);

        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope());
        using var client = new HttpClient(handler);
        var japanese = await Create(client).TranslateAsync(new TranslationRequest("こんにちは", Language.Japanese, Language.English));
        Assert.Equal(Language.Japanese, japanese.SourceLanguage);
        Assert.Equal(Language.English, japanese.TargetLanguage);
        Assert.Equal(1, handler.Calls);
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
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    [Fact]
    public async Task TranslateAsync_DistinguishesTimeoutFromUnexpectedCancellationAndTransportFailure()
    {
        var plainCancellation = new OperationCanceledException("unrelated cancellation");
        using (var client = new HttpClient(new ProviderTestHandler((_, _) => Task.FromException<HttpResponseMessage>(plainCancellation))))
        {
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(client).TranslateAsync(Request()));
            Assert.Same(plainCancellation, exception);
        }

        var failures = new (Exception Cause, TranslationProviderFailureKind Kind)[]
        {
            (new OperationCanceledException("timeout", new TimeoutException()), TranslationProviderFailureKind.Timeout),
            (new TimeoutException(), TranslationProviderFailureKind.Timeout),
            (new HttpRequestException(HttpRequestError.ConnectionError, "secret transport diagnostic"), TranslationProviderFailureKind.Unavailable),
            (new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded, "no message matching"), TranslationProviderFailureKind.InvalidResponse)
        };
        foreach (var (cause, kind) in failures)
        {
            var handler = new ProviderTestHandler((_, _) => Task.FromException<HttpResponseMessage>(cause));
            using var client = new HttpClient(handler);
            var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
            Assert.Equal(kind, exception.FailureKind);
            Assert.Same(cause, exception.InnerException);
            Assert.DoesNotContain("secret", exception.Message);
            Assert.Equal(1, handler.Calls);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TranslateAsync_RejectsKnownAndUnknownLengthBodiesAboveOneMiB(bool knownLength)
    {
        var content = new BoundedTestContent(knownLength);
        var handler = new ProviderTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var client = new HttpClient(handler) { MaxResponseContentBufferSize = 1_048_576 };
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.FailureKind);
        Assert.True(content.WasDisposed);
        Assert.Equal(1, handler.Calls);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{},{}]}")]
    [InlineData("{\"choices\":[{}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\"}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":null}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\" \"}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":4}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"{}\",\"tool_calls\":[]}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"{}\",\"function_call\":{}}}]}")]
    public async Task TranslateAsync_RejectsIncompleteOrUnsupportedEnvelope(string envelope)
    {
        using var client = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, envelope));
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.FailureKind);
    }
    [Fact]
    public async Task TranslateAsync_CallerCancellationWinsOverTimeoutShapedFailureAndPreCanceledCallsDoNotSend()
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException("timeout shaped", new TimeoutException(), cancellation.Token);
        var handler = new ProviderTestHandler((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromException<HttpResponseMessage>(failure);
        });
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(client).TranslateAsync(Request(), cancellation.Token));
        Assert.Same(failure, exception);
        Assert.Equal(1, handler.Calls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(client).TranslateAsync(Request(), cancellation.Token));
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task TranslateAsync_RejectsEveryUnsupportedDimensionBeforeSending()
    {
        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(handler);
        var requests = new[]
        {
            new TranslationRequest("Hello", Language.Auto, Language.Vietnamese),
            new TranslationRequest("Hello", Language.English, Language.English),
            new TranslationRequest("Hello", Language.English, Language.Vietnamese, mode: TranslationMode.Explain),
            new TranslationRequest("Hello", Language.English, Language.Vietnamese, new FihLens.Core.ValueObjects.CefrLearningLevel(CefrLevel.A1)),
            new TranslationRequest("こんにちは", Language.Japanese, Language.English, new FihLens.Core.ValueObjects.JlptLearningLevel(FihLens.Core.Enums.JlptLevel.N5))
        };
        foreach (var request in requests)
            await Assert.ThrowsAsync<NotSupportedException>(() => Create(client).TranslateAsync(request));
        Assert.Equal(0, handler.Calls);
    }
}
