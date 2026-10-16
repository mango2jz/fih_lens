namespace FihLens.Core.Models;

public sealed record GrammarPoint
{
    public required string Pattern { get; init; }
    public string? Explanation { get; init; }
    public string? ExampleSentence { get; init; }
}
