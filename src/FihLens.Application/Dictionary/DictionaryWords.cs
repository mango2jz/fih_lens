using System.Text;
using FihLens.Core.Models;

namespace FihLens.Application.Dictionary;

/// <summary>One lookup candidate from captured text, with the punctuation trimmed off it kept for display.</summary>
public sealed record DictionaryWord(string Text, string Leading, string Trailing, bool LineBreakAfter);

public sealed record DictionaryWordResult(DictionaryWord Word, DictionaryLookupResult Result);

/// <summary>The whole text as one candidate, then each word; <see cref="Words"/> holds the whole text alone when words are not split.</summary>
public sealed record DictionaryTextLookupResult(DictionaryWordResult Phrase, IReadOnlyList<DictionaryWordResult> Words);

public static class DictionaryWords
{
    /// <summary>Whole text with whitespace collapsed and leading/trailing punctuation removed; null when nothing remains.</summary>
    public static DictionaryWord? Phrase(string text)
    {
        string collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        int start = 0, end = collapsed.Length;
        while (start < end && (char.IsPunctuation(collapsed[start]) || collapsed[start] == ' ')) start++;
        while (end > start && (char.IsPunctuation(collapsed[end - 1]) || collapsed[end - 1] == ' ')) end--;
        return start == end ? null : new DictionaryWord(collapsed[start..end], collapsed[..start].Trim(), collapsed[end..].Trim(), false);
    }

    /// <summary>Splits on whitespace and punctuation; apostrophes and hyphens between letters stay inside a word.</summary>
    public static IReadOnlyList<DictionaryWord> Split(string text)
    {
        var words = new List<DictionaryWord>();
        var pending = new StringBuilder(); // punctuation waiting for the next word
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (c is '\n' or '\r' && words.Count > 0) words[^1] = words[^1] with { LineBreakAfter = true };
                i++;
            }
            else if (char.IsPunctuation(c) && !IsJoiner(text, i))
            {
                // Punctuation touching the end of a word trails it; otherwise it leads the next word.
                if (words.Count > 0 && pending.Length == 0 && i > 0 && !char.IsWhiteSpace(text[i - 1]))
                    words[^1] = words[^1] with { Trailing = words[^1].Trailing + c };
                else pending.Append(c);
                i++;
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && (!char.IsPunctuation(text[i]) || IsJoiner(text, i))) i++;
                words.Add(new DictionaryWord(text[start..i], pending.ToString(), "", false));
                pending.Clear();
            }
        }
        // Punctuation after the last word with a space before it still belongs to the text.
        if (pending.Length > 0 && words.Count > 0) words[^1] = words[^1] with { Trailing = words[^1].Trailing + pending };
        return words;
    }

    private static bool IsJoiner(string text, int i) =>
        text[i] is '\'' or '’' or '-' or '‐' or '‑' &&
        i > 0 && i + 1 < text.Length && IsWordChar(text[i - 1]) && IsWordChar(text[i + 1]);

    private static bool IsWordChar(char c) => !char.IsWhiteSpace(c) && !char.IsPunctuation(c);
}
