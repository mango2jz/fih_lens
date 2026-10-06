namespace FihLens.Core.Models;

public sealed record VocabularyItem
{
    public required string Word { get; init; }
    public string? Phonetic { get; init; }
    public required string PartOfSpeech { get; init; }
    public required string Meaning { get; init; }
    public string? ExampleSentence { get; init; }
}
