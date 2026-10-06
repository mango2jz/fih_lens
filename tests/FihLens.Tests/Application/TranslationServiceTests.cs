using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;

namespace FihLens.Tests.Application;

public sealed class TranslationServiceTests
{
    [Fact]
    public void ConstructorAndRequestNullGuardsAreSynchronous()
    {
        Assert.Throws<ArgumentNullException>(() => new TranslationService(null!));
        var provider = new RecordingProvider(Task.FromResult(Result()));
        var service = new TranslationService(provider);

        Assert.Throws<ArgumentNullException>(() => service.TranslateAsync(null!));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ForwardsExactRequestTokenTaskAndResult()
    {
        var request = Request();
        using var source = new CancellationTokenSource();
        var expected = Result();
        var providerTask = Task.FromResult(expected);
        var provider = new RecordingProvider(providerTask);

        var actualTask = new TranslationService(provider).TranslateAsync(request, source.Token);

        Assert.Same(providerTask, actualTask);
        Assert.Same(request, provider.Request);
        Assert.Equal(source.Token, provider.Token);
        Assert.Same(expected, await actualTask);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ComposesWithFakeProvider()
    {
        var result = await new TranslationService(new FihLens.Infrastructure.Translation.FakeTranslationProvider())
            .TranslateAsync(Request());

        Assert.Equal("Xin chào thế giới", result.NaturalTranslation);
    }

    private static TranslationRequest Request() => new("Hello world", Language.English, Language.Vietnamese);
    private static TranslationResult Result() => new() { OriginalText = "Hello world" };

    private sealed class RecordingProvider(Task<TranslationResult> task) : ITranslationProvider
    {
        public int Calls { get; private set; }
        public TranslationRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Request = request;
            Token = cancellationToken;
            return task;
        }
    }
}
