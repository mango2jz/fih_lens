using FihLens.Core.Models;

namespace FihLens.WinUI.ViewModels;

public sealed class DictionaryCardViewModel
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
        SensesText = match is null ? string.Join("\n", entry.Senses) : string.Join("\n\n", match.ApplicableSenses.Select((sense, i) =>
            string.Join("\n", new[]
            {
                $"{i + 1}. {string.Join("; ", sense.Glosses.Select(gloss => gloss.Text + (gloss.Type is null ? "" : $" ({GlossLabel(gloss.Type)})")))}",
                string.Join(", ", sense.PartsOfSpeech),
                sense.WrittenRestrictions.Count == 0 ? "" : $"Written forms: {string.Join(" / ", sense.WrittenRestrictions)}",
                sense.ReadingRestrictions.Count == 0 ? "" : $"Readings: {string.Join(" / ", sense.ReadingRestrictions)}",
                sense.LoanwordSources.Count == 0 ? "" : $"Loanword source: {string.Join("; ", sense.LoanwordSources.Select(SourceLabel))}",
                sense.Antonyms.Count == 0 ? "" : $"Antonym references: {string.Join("; ", sense.Antonyms)}",
                string.Join(" · ", sense.Notes.Concat(sense.Fields).Concat(sense.Misc).Concat(sense.Dialects)),
                sense.CrossReferences.Count == 0 ? "" : $"References: {string.Join("; ", sense.CrossReferences)}"
            }.Where(text => text.Length > 0))));
        PartOfSpeech = match is null ? entry.PartOfSpeech ?? "" : "";
        Example = entry.Example ?? "";
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
    public string PartOfSpeech { get; }
    public string Example { get; }
    public string Provenance { get; }
    public string CopyText { get; }
    public string SpeechText { get; }
    public string SpeechAutomationName => $"Pronounce {Headword} in {Pronunciation.SourceLanguage}";
    public string SpeechUnavailableReason => Pronunciation.GetUnavailableReason(SpeechText) ?? "";

    private static string GlossLabel(string type) => type switch
    {
        "expl" => "explanation", "fig" => "figurative", "lit" => "literal", "tm" => "trademark", _ => type
    };

    private static string SourceLabel(JapaneseLoanwordSource source) =>
        $"[language: {source.Language}, type: {source.Type}{(source.Wasei is null ? "" : $", wasei: {source.Wasei}")}]: " +
        (source.Text.Length == 0 ? "(source term omitted)" : source.Text);
}
