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

        Assert.Throws<ArgumentNullException>(() => { _ = service.TranslateAsync(null!); });
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
    public void OmittedTokenRemainsDefaultAndSynchronousFailureIsPreserved()
    {
        var provider = new RecordingProvider(Task.FromResult(Result()));
        new TranslationService(provider).TranslateAsync(Request());
        Assert.Equal(default, provider.Token);
        Assert.Equal(1, provider.Calls);

        var failure = new InvalidOperationException("provider failure");
        var throwingProvider = new DelegateProvider((_, _) => throw failure);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            { _ = new TranslationService(throwingProvider).TranslateAsync(Request()); }));
    }

    [Fact]
    public async Task FaultedAndCanceledTasksAreForwardedUnchanged()
    {
        var failure = new InvalidOperationException("provider failure");
        var faulted = Task.FromException<TranslationResult>(failure);
        var faultTask = new TranslationService(new RecordingProvider(faulted)).TranslateAsync(Request());
        Assert.Same(faulted, faultTask);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => faultTask));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = Task.FromCanceled<TranslationResult>(cancellation.Token);
        var provider = new RecordingProvider(canceled);
        var cancelTask = new TranslationService(provider).TranslateAsync(Request(), cancellation.Token);
        Assert.Same(canceled, cancelTask);
        Assert.Equal(cancellation.Token, provider.Token);
        Assert.Equal(1, provider.Calls);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelTask);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task PendingOverlappingCallsCanCompleteInReverseOrder()
    {
        var firstRequest = Request();
        var secondRequest = new TranslationRequest("Second request", Language.English, Language.Vietnamese);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var firstCompletion = new TaskCompletionSource<TranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource<TranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<(TranslationRequest Request, CancellationToken Token)>();
        var service = new TranslationService(new DelegateProvider((request, token) =>
        {
            calls.Add((request, token));
            return calls.Count == 1 ? firstCompletion.Task : secondCompletion.Task;
        }));

        var firstTask = service.TranslateAsync(firstRequest, firstCancellation.Token);
        var secondTask = service.TranslateAsync(secondRequest, secondCancellation.Token);
        Assert.Same(firstCompletion.Task, firstTask);
        Assert.Same(secondCompletion.Task, secondTask);
        Assert.False(firstTask.IsCompleted);
        Assert.False(secondTask.IsCompleted);
        Assert.Equal(2, calls.Count);
        Assert.Same(firstRequest, calls[0].Request);
        Assert.Equal(firstCancellation.Token, calls[0].Token);
        Assert.Same(secondRequest, calls[1].Request);
        Assert.Equal(secondCancellation.Token, calls[1].Token);

        var secondResult = new TranslationResult { OriginalText = "Second request", SourceLanguage = Language.English, TargetLanguage = Language.Vietnamese };
        secondCompletion.SetResult(secondResult);
        Assert.Same(secondResult, await secondTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(firstTask.IsCompleted);
        var firstResult = Result();
        firstCompletion.SetResult(firstResult);
        Assert.Same(firstResult, await firstTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ComposesWithFakeProvider()
    {
        var result = await new TranslationService(new FihLens.Infrastructure.Translation.FakeTranslationProvider())
            .TranslateAsync(Request());

        Assert.Equal("Xin chào thế giới", result.NaturalTranslation);
    }

    private static TranslationRequest Request() => new("Hello world", Language.English, Language.Vietnamese);
    private static TranslationResult Result() => new() { OriginalText = "Hello world", SourceLanguage = Language.English, TargetLanguage = Language.Vietnamese };

    private sealed class DelegateProvider(Func<TranslationRequest, CancellationToken, Task<TranslationResult>> translate)
        : ITranslationProvider
    {
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
            => translate(request, cancellationToken);
    }

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
