namespace FihLens.Core.Models;

public sealed record VocabularyItem
{
    private IReadOnlyList<string> _synonyms = Array.Empty<string>();

    public required string Word { get; init; }
    public string? Phonetic { get; init; }
    public string? Reading { get; init; }
    public string? BaseForm { get; init; }
    public required string PartOfSpeech { get; init; }
    public required string Meaning { get; init; }
    public IReadOnlyList<string> Synonyms
    {
        get => _synonyms;
        init => _synonyms = value ?? throw new ArgumentNullException(nameof(value));
    }
    public string? ExampleSentence { get; init; }
}
