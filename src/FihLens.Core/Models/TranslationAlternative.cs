namespace FihLens.Core.Models;

public sealed record TranslationAlternative
{
    public required string Text { get; init; }
    public string? Explanation { get; init; }
}
