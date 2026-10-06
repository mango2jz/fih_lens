namespace FihLens.Core.Models;

public sealed record TranslationResult
{
    private IReadOnlyList<VocabularyItem> _vocabulary = Array.Empty<VocabularyItem>();
    private IReadOnlyList<GrammarPoint> _grammar = Array.Empty<GrammarPoint>();
    private IReadOnlyList<TranslationAlternative> _alternatives = Array.Empty<TranslationAlternative>();

    public required string OriginalText { get; init; }
    public string? NaturalTranslation { get; init; }
    public string? Explanation { get; init; }
    public string? Context { get; init; }
    public string? Tone { get; init; }
    public IReadOnlyList<VocabularyItem> Vocabulary
    {
        get => _vocabulary;
        init => _vocabulary = value ?? throw new ArgumentNullException(nameof(value));
    }
    public IReadOnlyList<GrammarPoint> Grammar
    {
        get => _grammar;
        init => _grammar = value ?? throw new ArgumentNullException(nameof(value));
    }
    public IReadOnlyList<TranslationAlternative> Alternatives
    {
        get => _alternatives;
        init => _alternatives = value ?? throw new ArgumentNullException(nameof(value));
    }
}
