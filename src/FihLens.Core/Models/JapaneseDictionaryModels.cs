namespace FihLens.Core.Models;

public sealed record JapaneseDictionaryData(
    IReadOnlyList<JapaneseWrittenForm> WrittenForms,
    IReadOnlyList<JapaneseReading> Readings,
    IReadOnlyList<JapaneseSense> Senses,
    JapaneseDictionaryMatch Match);

public sealed record JapaneseWrittenForm(string Text, IReadOnlyList<string> Information);

public sealed record JapaneseReading(string Text, IReadOnlyList<string> Restrictions, bool NoKanji, IReadOnlyList<string> Information);

public sealed record JapaneseGloss(string Text, string? Type);

public sealed record JapaneseLoanwordSource(string Text, string Language, string Type, string? Wasei);

public sealed record JapaneseSense(
    IReadOnlyList<JapaneseGloss> Glosses,
    IReadOnlyList<string> PartsOfSpeech,
    IReadOnlyList<string> WrittenRestrictions,
    IReadOnlyList<string> ReadingRestrictions,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> Misc,
    IReadOnlyList<string> Dialects,
    IReadOnlyList<string> CrossReferences)
{
    public IReadOnlyList<JapaneseLoanwordSource> LoanwordSources { get; init; } = [];
    public IReadOnlyList<string> Antonyms { get; init; } = [];
}

public sealed record JapaneseDictionaryMatch(
    string Alias,
    JapaneseMatchKind Kind,
    IReadOnlyList<JapaneseWrittenForm> ApplicableWrittenForms,
    IReadOnlyList<JapaneseReading> ApplicableReadings,
    IReadOnlyList<JapaneseSense> ApplicableSenses);

public enum JapaneseMatchKind
{
    WrittenForm,
    Reading
}
