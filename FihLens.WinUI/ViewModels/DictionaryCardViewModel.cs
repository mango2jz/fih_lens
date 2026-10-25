using CommunityToolkit.Mvvm.ComponentModel;
using FihLens.Core.Models;

namespace FihLens.WinUI.ViewModels;

public sealed partial class DictionaryCardViewModel : ObservableObject
{
    public DictionaryCardViewModel(DictionaryEntry entry, PronunciationViewModel pronunciation)
    {
        Pronunciation = pronunciation;
        var match = entry.Japanese?.Match;
        Headword = match is null ? entry.Headword : match.ApplicableWrittenForms.FirstOrDefault()?.Text ?? match.Alias;
        WrittenFormsText = match is null ? "" : string.Join(" / ", match.ApplicableWrittenForms
            .Select(form => form.Text + (form.Information.Count == 0 ? "" : $" [{string.Join("; ", form.Information)}]")));
        ReadingsText = match is null ? "" : string.Join(" · ", match.ApplicableReadings.Select(reading =>
            reading.Text + (reading.Restrictions.Count == 0 ? "" : $" ({string.Join(" / ", reading.Restrictions)})") +
            (reading.Information.Count == 0 ? "" : $" [{string.Join("; ", reading.Information)}]")));
        Senses = match is null
            ? entry.Senses.Select((sense, i) => new DictionarySense(i + 1, sense, "")).ToArray()
            : match.ApplicableSenses.Select((sense, i) => new DictionarySense(i + 1,
                string.Join("; ", sense.Glosses.Select(gloss => gloss.Text + (gloss.Type is null ? "" : $" ({GlossLabel(gloss.Type)})"))),
                string.Join("\n", SenseNotes(sense)))).ToArray();
        SensesText = match is null ? string.Join("\n", entry.Senses) : string.Join("\n\n", Senses.Select(sense =>
            string.Join("\n", new[] { $"{sense.Number}. {sense.Text}", sense.Note }.Where(text => text.Length > 0))));
        PartOfSpeech = match is null ? entry.PartOfSpeech ?? "" : "";
        Reading = match?.ApplicableReadings.FirstOrDefault()?.Text ?? "";
        Summary = match is null ? string.Join("; ", entry.Senses)
            : string.Join("; ", match.ApplicableSenses.FirstOrDefault()?.Glosses.Select(gloss => gloss.Text) ?? []);
        Example = entry.Example ?? "";
        SourceId = entry.SourceId;
        IsJapanese = match is not null;
        Provenance = $"Source: {entry.SourceId}\nArticle: {entry.SourceArticleId}\nOrigin: {entry.SourceOrigin}";
        SpeechText = match?.ApplicableReadings.FirstOrDefault()?.Text ?? Headword;
        CopyText = string.Join("\n", new[] { Headword, WrittenFormsText, ReadingsText, SensesText, PartOfSpeech, Example, Provenance }
            .Where(text => text.Length > 0));
    }

    public PronunciationViewModel Pronunciation { get; }
    public string Headword { get; }
    public string WrittenFormsText { get; }
    public string ReadingsText { get; }
    public string SensesText { get; }
    /// <summary>Numbered senses for the dictionary card; Note holds part of speech, restrictions and references.</summary>
    public IReadOnlyList<DictionarySense> Senses { get; }
    public string SourceId { get; }
    public bool IsJapanese { get; }
    public bool IsLatin => !IsJapanese;
    public bool HasReading => Reading.Length > 0;
    public bool HasReadings => ReadingsText.Length > 0;
    public bool HasWrittenForms => WrittenFormsText.Length > 0;
    public bool HasPartOfSpeech => PartOfSpeech.Length > 0;
    public bool HasExample => Example.Length > 0;
    public bool HasSpeechUnavailableReason => SpeechUnavailableReason.Length > 0;
    public string SpeechHint => HasSpeechUnavailableReason ? SpeechUnavailableReason : SpeechAutomationName;
    public string PartOfSpeech { get; }
    /// <summary>First reading, shown beside the headword in a result row.</summary>
    public string Reading { get; }
    /// <summary>One-line meaning for a result row: every sense, or the first Japanese sense's glosses.</summary>
    public string Summary { get; }
    public string Example { get; }
    public string Provenance { get; }
    public string CopyText { get; }
    public string SpeechText { get; }

    /// <summary>Set by the launcher's result list; drives the selected row's koi tile.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
    public string SpeechAutomationName => $"Pronounce {Headword} in {Pronunciation.SourceLanguage}";
    public string SpeechUnavailableReason => Pronunciation.GetUnavailableReason(SpeechText) ?? "";

    private static IEnumerable<string> SenseNotes(JapaneseSense sense) => new[]
    {
        string.Join(", ", sense.PartsOfSpeech),
        sense.WrittenRestrictions.Count == 0 ? "" : $"Written forms: {string.Join(" / ", sense.WrittenRestrictions)}",
        sense.ReadingRestrictions.Count == 0 ? "" : $"Readings: {string.Join(" / ", sense.ReadingRestrictions)}",
        sense.LoanwordSources.Count == 0 ? "" : $"Loanword source: {string.Join("; ", sense.LoanwordSources.Select(SourceLabel))}",
        sense.Antonyms.Count == 0 ? "" : $"Antonym references: {string.Join("; ", sense.Antonyms)}",
        string.Join(" · ", sense.Notes.Concat(sense.Fields).Concat(sense.Misc).Concat(sense.Dialects)),
        sense.CrossReferences.Count == 0 ? "" : $"References: {string.Join("; ", sense.CrossReferences)}"
    }.Where(text => text.Length > 0);

    private static string GlossLabel(string type) => type switch
    {
        "expl" => "explanation", "fig" => "figurative", "lit" => "literal", "tm" => "trademark", _ => type
    };

    private static string SourceLabel(JapaneseLoanwordSource source) =>
        $"[language: {source.Language}, type: {source.Type}{(source.Wasei is null ? "" : $", wasei: {source.Wasei}")}]: " +
        (source.Text.Length == 0 ? "(source term omitted)" : source.Text);
}

public sealed record DictionarySense(int Number, string Text, string Note)
{
    public bool HasNote => Note.Length > 0;
}
