namespace FihLens.Core.Models;

public sealed record DictionaryEntry(string Headword, IReadOnlyList<string> Senses,
    string? PartOfSpeech, string? Example, string SourceId, string SourceArticleId, string SourceOrigin);

public sealed record DictionarySource(string SourceId, string Name, string Revision,
    string SourceLanguage, string TargetLanguage, string License, string SourceUrl,
    string Attribution, string QualityStatus, string HumanReviewScope,
    IReadOnlyDictionary<string, string> Notices);

public sealed record DictionaryLookupResult(string OriginalText, DictionarySource Source,
    IReadOnlyList<DictionaryEntry> Entries)
{
    public bool IsMatch => Entries.Count != 0;
}
