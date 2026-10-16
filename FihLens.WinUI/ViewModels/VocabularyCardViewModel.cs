using FihLens.Core.Enums;
using FihLens.Core.Models;

namespace FihLens.WinUI.ViewModels;

public sealed class VocabularyCardViewModel(VocabularyItem item, PronunciationViewModel pronunciation)
{
    public VocabularyItem Item { get; } = item;
    public PronunciationViewModel Pronunciation { get; } = pronunciation;
    public bool HasReading => !string.IsNullOrWhiteSpace(Item.Reading);
    public bool HasBaseForm => !string.IsNullOrWhiteSpace(Item.BaseForm);
    public string BaseFormText => HasBaseForm ? $"Base form: {Item.BaseForm}" : "";
    public string SynonymsText => Item.Synonyms.Count == 0 ? "No synonyms supplied" :
        $"Synonyms: {string.Join(" · ", Item.Synonyms)}";
    public string SpeechText => Pronunciation.SourceLanguage == Language.Japanese && HasReading
        ? Item.Reading! : Item.Word;
    public string SpeechAutomationName => $"Pronounce {Item.Word} in {Pronunciation.SourceLanguage}";
    public string SpeechUnavailableReason => Pronunciation.GetUnavailableReason(SpeechText) ?? "";
    public bool HasSpeechUnavailableReason => SpeechUnavailableReason.Length > 0;
    public string SpeechHint => HasSpeechUnavailableReason ? SpeechUnavailableReason : SpeechAutomationName;
}
