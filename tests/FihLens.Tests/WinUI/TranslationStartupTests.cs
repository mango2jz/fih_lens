using FihLens.Infrastructure.Translation;
using FihLens.WinUI;

namespace FihLens.Tests.WinUI;

public sealed class TranslationStartupTests
{
    [Fact]
    public async Task SelectedClientDoesNotFollowRedirects()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var startup = TranslationStartup.Create(name => name == "FIHLENS_PROVIDER_MODE" ? "gemini" : "synthetic-key");
        using var client = startup.Client!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var server = RespondWithRedirectAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/translation")
        {
            Content = new StringContent("synthetic input")
        };
        request.Headers.Add("x-goog-api-key", "synthetic-key");
        using var response = await client.SendAsync(request, timeout.Token);
        await server;
        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.False(listener.Pending());

        async Task RespondWithRedirectAsync()
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
            var reply = System.Text.Encoding.ASCII.GetBytes(
                $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{port}/redirected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(reply, timeout.Token);
        }
    }
    [Theory]
    [InlineData(null)]
    [InlineData("demo")]
    public void UnsetOrDemoDisablesAiAndReadsNoCredentials(string? mode)
    {
        var reads = new List<string>();
        var startup = TranslationStartup.Create(name => { reads.Add(name); return mode; });
        Assert.Null(startup.Provider);
        Assert.Null(startup.Label);
        Assert.Contains("Ask AI is unavailable", startup.UnavailableReason);
        Assert.Null(startup.Client);
        Assert.Equal(new[] { "FIHLENS_PROVIDER_MODE" }, reads);
    }

    [Theory]
    [InlineData("gemini", "GEMINI_API_KEY", "Gemini")]
    [InlineData("deepseek", "DEEPSEEK_API_KEY", "DeepSeek")]
    public void LiveReadsOnlySelectedCredentialAndConfiguresClient(string mode, string keyName, string label)
    {
        var reads = new List<string>();
        var startup = TranslationStartup.Create(name =>
        {
            reads.Add(name);
            return name == "FIHLENS_PROVIDER_MODE" ? mode : name == keyName ? "test-key" : null;
        });
        using var client = startup.Client;
        Assert.Equal(label, startup.Label);
        Assert.Equal(mode == "gemini" ? typeof(GeminiTranslationProvider) : typeof(DeepSeekTranslationProvider), startup.Provider!.GetType());
        Assert.NotNull(client);
        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
        Assert.Equal(1_048_576, client.MaxResponseContentBufferSize);
        Assert.Equal(new[] { "FIHLENS_PROVIDER_MODE", keyName }, reads);
    }

    [Theory]
    [InlineData("gemini", null, "GEMINI_API_KEY")]
    [InlineData("deepseek", "  ", "DEEPSEEK_API_KEY")]
    [InlineData("secret-invalid-mode", null, "FIHLENS_PROVIDER_MODE")]
    [InlineData("", null, "FIHLENS_PROVIDER_MODE")]
    public void InvalidConfigurationDisablesOnlyAiWithoutFallbackOrReflectingValues(string mode, string? key, string expectedSetting)
    {
        var startup = TranslationStartup.Create(name => name == "FIHLENS_PROVIDER_MODE" ? mode : key);
        Assert.Null(startup.Provider);
        Assert.Null(startup.Client);
        var error = startup.UnavailableReason!;
        Assert.Contains(expectedSetting, error);
        Assert.DoesNotContain("secret-invalid-mode", error);
    }
}
