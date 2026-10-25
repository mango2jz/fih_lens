using System.Net;
using System.Text.Json;
using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Infrastructure.Translation;

namespace FihLens.Tests.Infrastructure;

public sealed class OpenRouterTranslationProviderTests
{
    private const string Model = "nvidia/nemotron-3.5-super-vl:free";
    private const string Analysis = "{\"naturalTranslation\":\"Xin chào\",\"vocabulary\":[],\"grammar\":[],\"alternatives\":[]}";
    private static string Envelope(string finish = "stop", string? content = null) =>
        JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finish, message = new { role = "assistant", reasoning = "ignore this", content = content ?? Analysis } } } });
    private static TranslationRequest Request(string text = "Hello world") => new(text, Language.English, Language.Vietnamese);
    private static OpenRouterTranslationProvider Create(HttpClient client, string key = "secret", string model = Model, bool useJsonSchema = false) =>
        new(client, key, model, useJsonSchema, new PromptBuilder(), new TranslationParser());

    [Theory]
    [InlineData("nvidia/nemotron-3.5-super-vl:free", true)]
    [InlineData("google/gemma-4-31b-it:free", false)]
    public async Task TranslateAsync_SendsOpenRouterWireShapeAndMapsResult(string model, bool useJsonSchema)
    {
        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope());
        using var client = new HttpClient(handler);

        var result = await Create(client, model: model, useJsonSchema: useJsonSchema).TranslateAsync(Request("  Hello world  "));

        Assert.Equal("  Hello world  ", result.OriginalText);
        Assert.Equal("Xin chào", result.NaturalTranslation);
        Assert.Equal("POST", handler.Request!.Method.Method);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer secret", handler.Request.Headers.Authorization!.ToString());
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.False(handler.Request.Headers.Contains("HTTP-Referer"));
        Assert.False(handler.Request.Headers.Contains("X-Title"));
        using var wire = JsonDocument.Parse(handler.RequestBody!);
        var root = wire.RootElement;
        Assert.Equal(model, root.GetProperty("model").GetString());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("user", root.GetProperty("messages")[1].GetProperty("role").GetString());
        Assert.Equal("  Hello world  ", root.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Contains("JSON", root.GetProperty("messages")[0].GetProperty("content").GetString(), StringComparison.OrdinalIgnoreCase);
        var format = root.GetProperty("response_format");
        if (useJsonSchema)
        {
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            var schema = format.GetProperty("json_schema");
            Assert.Equal("translation_analysis", schema.GetProperty("name").GetString());
            Assert.False(schema.GetProperty("strict").GetBoolean());
            Assert.Contains(schema.GetProperty("schema").GetProperty("required").EnumerateArray(), item => item.GetString() == "naturalTranslation");
            Assert.True(root.GetProperty("provider").GetProperty("require_parameters").GetBoolean());
        }
        else
        {
            Assert.Equal("json_object", format.GetProperty("type").GetString());
            Assert.False(format.TryGetProperty("json_schema", out _));
            Assert.False(root.TryGetProperty("provider", out _));
        }
        Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.True(root.GetProperty("reasoning").GetProperty("exclude").GetBoolean());
        Assert.Equal(4096, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.True(handler.ResponseContent!.WasDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request.Content!.ReadAsStringAsync());
    }

    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        using var client = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope()));
        Assert.Throws<ArgumentNullException>(() => new OpenRouterTranslationProvider(null!, "key", Model, false, new PromptBuilder(), new TranslationParser()));
        Assert.Throws<ArgumentException>(() => new OpenRouterTranslationProvider(client, " ", Model, false, new PromptBuilder(), new TranslationParser()));
        Assert.Throws<ArgumentException>(() => new OpenRouterTranslationProvider(client, "key", " ", false, new PromptBuilder(), new TranslationParser()));
        Assert.Throws<ArgumentNullException>(() => new OpenRouterTranslationProvider(client, "key", Model, false, null!, new TranslationParser()));
        Assert.Throws<ArgumentNullException>(() => new OpenRouterTranslationProvider(client, "key", Model, false, new PromptBuilder(), null!));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TranslationProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.PaymentRequired, TranslationProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, TranslationProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, TranslationProviderFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, TranslationProviderFailureKind.RequestRejected)]
    [InlineData(HttpStatusCode.NotFound, TranslationProviderFailureKind.RequestRejected)]
    [InlineData(HttpStatusCode.GatewayTimeout, TranslationProviderFailureKind.Timeout)]
    [InlineData(HttpStatusCode.RequestTimeout, TranslationProviderFailureKind.Timeout)]
    [InlineData(HttpStatusCode.BadGateway, TranslationProviderFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TranslationProviderFailureKind.Unavailable)]
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
    }

    [Theory]
    [InlineData("401", TranslationProviderFailureKind.Authentication)]
    [InlineData("402", TranslationProviderFailureKind.Authentication)]
    [InlineData("429", TranslationProviderFailureKind.RateLimited)]
    [InlineData("408", TranslationProviderFailureKind.Timeout)]
    [InlineData("502", TranslationProviderFailureKind.Unavailable)]
    [InlineData("400", TranslationProviderFailureKind.RequestRejected)]
    [InlineData("\"rate_limited\"", TranslationProviderFailureKind.Unavailable)]
    [InlineData("null", TranslationProviderFailureKind.Unavailable)]
    public async Task TranslateAsync_MapsErrorInSuccessfulBodyWithoutLeakingMessage(string code, TranslationProviderFailureKind expected)
    {
        var error = $"{{\"code\":{code},\"message\":\"echo SENTINEL-INPUT\",\"metadata\":{{\"raw\":\"SENTINEL-INPUT\"}}}}";
        var bodies = new[]
        {
            $"{{\"error\":{error}}}",
            Envelope().Insert(1, $"\"error\":{error},"),
            $"{{\"choices\":[{{\"finish_reason\":\"error\",\"error\":{error},\"message\":{{\"content\":\"\"}}}}]}}"
        };
        foreach (var body in bodies)
        {
            using var client = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, body));
            var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
            Assert.Equal(expected, exception.FailureKind);
            Assert.DoesNotContain("SENTINEL", exception.ToString());
        }
    }

    [Fact]
    public async Task TranslateAsync_ErrorWithoutCodeIsUnavailable()
    {
        using var client = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, "{\"error\":{\"message\":\"SENTINEL\"}}"));
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.Unavailable, exception.FailureKind);
        Assert.DoesNotContain("SENTINEL", exception.ToString());
    }

    [Theory]
    [InlineData("not json", "InvalidResponse")]
    [InlineData("length", "InvalidResponse")]
    [InlineData("tool_calls", "InvalidResponse")]
    [InlineData("unknown_reason", "InvalidResponse")]
    [InlineData("content_filter", "Refused")]
    [InlineData("error", "Unavailable")]
    public async Task TranslateAsync_MapsProviderCompletionReasons(string finish, string expectedKind)
    {
        var body = finish == "not json" ? finish : Envelope(finish);
        using var client = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, body));
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(expectedKind, exception.FailureKind.ToString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{},{}]}")]
    [InlineData("{\"choices\":[{}]}")]
    [InlineData("{\"choices\":[3]}")]
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
    public async Task TranslateAsync_AcceptsLeadingWhitespaceAndRejectsMalformedAnalysis()
    {
        using var whitespaceClient = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, "\n\n   " + Envelope()));
        var result = await Create(whitespaceClient).TranslateAsync(Request());
        Assert.Equal("Xin chào", result.NaturalTranslation);

        var nullFields = JsonSerializer.Serialize(new { error = (object?)null, choices = new[] { new { finish_reason = "stop", error = (object?)null, message = new { content = Analysis, tool_calls = (object?)null, function_call = (object?)null } } } });
        using var nullFieldsClient = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, nullFields));
        Assert.Equal("Xin chào", (await Create(nullFieldsClient).TranslateAsync(Request())).NaturalTranslation);

        using var malformedClient = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope(content: "{}")));
        var malformed = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(malformedClient).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, malformed.FailureKind);

        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope());
        using var client = new HttpClient(handler);
        var japanese = await Create(client).TranslateAsync(new TranslationRequest("こんにちは", Language.Japanese, Language.English));
        Assert.Equal(Language.Japanese, japanese.SourceLanguage);
        Assert.Equal(Language.English, japanese.TargetLanguage);
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
    public async Task TranslateAsync_MapsTimeoutAndTransportFailuresAndPreCanceledCallsDoNotSend()
    {
        var failures = new (Exception Cause, TranslationProviderFailureKind Kind)[]
        {
            (new OperationCanceledException("timeout", new TimeoutException()), TranslationProviderFailureKind.Timeout),
            (new HttpRequestException(HttpRequestError.ConnectionError, "secret transport diagnostic"), TranslationProviderFailureKind.Unavailable)
        };
        foreach (var (cause, kind) in failures)
        {
            var handler = new ProviderTestHandler((_, _) => Task.FromException<HttpResponseMessage>(cause));
            using var client = new HttpClient(handler);
            var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
            Assert.Equal(kind, exception.FailureKind);
            Assert.DoesNotContain("secret", exception.Message);
        }

        var unsent = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope());
        using var unsentClient = new HttpClient(unsent);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(unsentClient).TranslateAsync(Request(), canceled.Token));
        Assert.Equal(0, unsent.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TranslateAsync_RejectsBodiesAboveOneMiB(bool knownLength)
    {
        var content = new BoundedTestContent(knownLength);
        var handler = new ProviderTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var client = new HttpClient(handler) { MaxResponseContentBufferSize = 1_048_576 };
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.FailureKind);
        Assert.True(content.WasDisposed);
    }

    [Fact]
    public async Task TranslateAsync_RejectsUnsupportedRequestsBeforeSending()
    {
        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope());
        using var client = new HttpClient(handler);
        var requests = new[]
        {
            new TranslationRequest("Hello", Language.Auto, Language.Vietnamese),
            new TranslationRequest("Hello", Language.English, Language.English),
            new TranslationRequest("Hello", Language.English, Language.Vietnamese, mode: TranslationMode.Explain)
        };
        foreach (var request in requests)
            await Assert.ThrowsAsync<NotSupportedException>(() => Create(client).TranslateAsync(request));
        Assert.Equal(0, handler.Calls);
    }
}
