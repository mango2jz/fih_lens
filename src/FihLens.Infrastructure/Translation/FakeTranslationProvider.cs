using System.Collections.ObjectModel;
using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Core.ValueObjects;

namespace FihLens.Infrastructure.Translation;

public sealed class FakeTranslationProvider : ITranslationProvider
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    private readonly TimeSpan _latency;

    public FakeTranslationProvider(TimeSpan latency = default)
    {
        if (latency < TimeSpan.Zero || latency > MaximumDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(latency));
        }

        _latency = latency;
    }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TranslationResult>(cancellationToken);
        }

        if (!IsSupported(request))
        {
            throw new NotSupportedException("The demo provider supports only the exact 'Hello world' English to Vietnamese translation fixture.");
        }

        return _latency == TimeSpan.Zero
            ? Task.FromResult(CreateResult())
            : TranslateAfterDelayAsync(cancellationToken);
    }

    private async Task<TranslationResult> TranslateAfterDelayAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(_latency, cancellationToken).ConfigureAwait(false);
        return CreateResult();
    }

    private static bool IsSupported(TranslationRequest request) =>
        string.Equals(request.Text, "Hello world", StringComparison.Ordinal) &&
        request.SourceLanguage == Language.English &&
        request.TargetLanguage == Language.Vietnamese &&
        request.Mode == TranslationMode.Translate &&
        (request.UserLevel is null || request.UserLevel is CefrLearningLevel);

    private static TranslationResult CreateResult() => new()
    {
        OriginalText = "Hello world",
        NaturalTranslation = "Xin chào thế giới",
        Explanation = "A simple greeting to the world.",
        Context = "Greeting",
        Tone = "Friendly",
        Vocabulary = ReadOnly(new VocabularyItem
        {
            Word = "hello",
            Phonetic = "/həˈləʊ/",
            PartOfSpeech = "interjection",
            Meaning = "xin chào",
            ExampleSentence = "Hello, everyone!"
        }),
        Grammar = ReadOnly(new GrammarPoint
        {
            Pattern = "Greeting + addressee",
            Explanation = "A greeting can be followed by the person or group addressed.",
            ExampleSentence = "Hello, world!"
        }),
        Alternatives = ReadOnly(new TranslationAlternative
        {
            Text = "Chào thế giới",
            Explanation = "A shorter Vietnamese greeting."
        })
    };

    private static ReadOnlyCollection<T> ReadOnly<T>(T item) => Array.AsReadOnly(new[] { item });
}
