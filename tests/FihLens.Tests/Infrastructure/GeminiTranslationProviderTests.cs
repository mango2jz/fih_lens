using System.Net;
using System.Text.Json;
using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Infrastructure.Translation;

namespace FihLens.Tests.Infrastructure;

public sealed class GeminiTranslationProviderTests
{
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
        Assert.Equal("  Hello world  ", root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(2048, root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
        var jsonSchema = root.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text");
        Assert.Equal("APPLICATION_JSON", jsonSchema.GetProperty("mimeType").GetString());
        var schema = jsonSchema.GetProperty("schema");
        Assert.Equal(new[] { "naturalTranslation", "vocabulary", "grammar", "alternatives" }, schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        var properties = schema.GetProperty("properties");
        Assert.Equal(new[] { "word", "partOfSpeech", "meaning" }, properties.GetProperty("vocabulary").GetProperty("items").GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        var vocabularyProperties = properties.GetProperty("vocabulary").GetProperty("items").GetProperty("properties");
        Assert.Equal("array", vocabularyProperties.GetProperty("synonyms").GetProperty("type").GetString());
        Assert.Equal("string", vocabularyProperties.GetProperty("reading").GetProperty("type")[0].GetString());
        Assert.Equal("string", vocabularyProperties.GetProperty("baseForm").GetProperty("type")[0].GetString());
        Assert.Equal("pattern", Assert.Single(properties.GetProperty("grammar").GetProperty("items").GetProperty("required").EnumerateArray()).GetString());
        Assert.Equal("text", Assert.Single(properties.GetProperty("alternatives").GetProperty("items").GetProperty("required").EnumerateArray()).GetString());
        Assert.Equal(new[] { "string", "null" }, properties.GetProperty("tone").GetProperty("type").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new PromptBuilder().Build(Request()).SystemText, root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("user", root.GetProperty("contents")[0].GetProperty("role").GetString());
        Assert.False(root.GetProperty("generationConfig").TryGetProperty("responseMimeType", out _));
        Assert.False(root.GetProperty("generationConfig").TryGetProperty("responseSchema", out _));
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
    [InlineData("not json")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{\"parts\":[]}}]}")]
    [InlineData("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"},\"candidates\":[]}")]
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
    public async Task TranslateAsync_SupportsJapanesePairAndUsesRequestMetadata()
    {
        var handler = ProviderTestHandler.Respond(HttpStatusCode.OK, Envelope);
        using var client = new HttpClient(handler);
        var request = new TranslationRequest("こんにちは", Language.Japanese, Language.English);
        var result = await Create(client).TranslateAsync(request);
        Assert.Equal(Language.Japanese, result.SourceLanguage);
        Assert.Equal(Language.English, result.TargetLanguage);
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
    public async Task TranslateAsync_MapsTimeoutAndResponseLimit()
    {
        var timeoutHandler = new ProviderTestHandler((_, _) => Task.FromException<HttpResponseMessage>(new OperationCanceledException("timeout", new TimeoutException())));
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
    [InlineData("SAFETY")]
    [InlineData("BLOCKLIST")]
    [InlineData("PROHIBITED_CONTENT")]
    [InlineData("RECITATION")]
    [InlineData("SPII")]
    public async Task TranslateAsync_MapsSafetyCompletionToRefused(string reason)
    {
        var envelope = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = reason } } });
        using var client = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, envelope));
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.Refused, exception.FailureKind);
    }

    [Theory]
    [InlineData("functionCall")]
    [InlineData("functionResponse")]
    [InlineData("inlineData")]
    [InlineData("fileData")]
    [InlineData("executableCode")]
    [InlineData("codeExecutionResult")]
    public async Task TranslateAsync_RejectsNontextFieldsEvenWithValidText(string field)
    {
        var part = new Dictionary<string, object>
        {
            ["text"] = """{"naturalTranslation":"ok","vocabulary":[],"grammar":[],"alternatives":[]}""",
            [field] = new { }
        };
        var envelope = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { part } } } } });
        using var client = new HttpClient(ProviderTestHandler.Respond(HttpStatusCode.OK, envelope));
        var exception = await Assert.ThrowsAsync<TranslationProviderException>(() => Create(client).TranslateAsync(Request()));
        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.FailureKind);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{},{}]}")]
    [InlineData("{\"candidates\":[{}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"UNKNOWN\"}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"thought\":true,\"text\":\"{}\"}]}}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[]}}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"inlineData\":{}}]}}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"thought\":\"true\",\"text\":\"{}\"}]}}]}")]
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
            new TranslationRequest("Hello", Language.English, Language.Vietnamese, mode: TranslationMode.Explain),
            new TranslationRequest("Hello", Language.English, Language.Vietnamese, new FihLens.Core.ValueObjects.CefrLearningLevel(CefrLevel.A1)),
            new TranslationRequest("こんにちは", Language.Japanese, Language.English, new FihLens.Core.ValueObjects.JlptLearningLevel(FihLens.Core.Enums.JlptLevel.N5))
        };
        foreach (var request in requests)
            await Assert.ThrowsAsync<NotSupportedException>(() => Create(client).TranslateAsync(request));
        Assert.Equal(0, handler.Calls);
    }
}
