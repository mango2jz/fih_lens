namespace FihLens.Core.Models;

public class VocabularyItem(string word, string phonetic, string partOfSpeech, string meaning, string exampleSentence)
{
    public string Word { get; set; } = word;
    public string Phonetic { get; set; } = phonetic;
    public string PartOfSpeech { get; set; } = partOfSpeech;
    public string Meaning { get; set; } = meaning;
    public string ExampleSentence { get; set; } = exampleSentence;
}
