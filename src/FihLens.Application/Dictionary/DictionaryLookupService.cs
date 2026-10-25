using System.Text;
using FihLens.Core.Models;

namespace FihLens.Application.Dictionary;

public sealed class DictionaryLookupService(IDictionaryLookup lookup)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IDictionaryLookup _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));

    public async Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeInput(input);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _lookup.LookupAsync(input, cancellationToken).ConfigureAwait(false);
        // Captured text often wraps an exact headword in punctuation or line breaks ("a piece of cake!").
        if (result.IsMatch || DictionaryWords.Phrase(normalized) is not { } phrase || phrase.Text == normalized) return result;
        cancellationToken.ThrowIfCancellationRequested();
        var trimmed = await _lookup.LookupAsync(phrase.Text, cancellationToken).ConfigureAwait(false);
        return trimmed.IsMatch ? trimmed with { OriginalText = input } : result;
    }

    /// <summary>Looks up the whole text, then each word when <paramref name="splitWords"/> is set; otherwise the whole text is the only word.</summary>
    public async Task<DictionaryTextLookupResult> LookupWordsAsync(string input, bool splitWords,
        CancellationToken cancellationToken = default)
    {
        var whole = await LookupAsync(input, cancellationToken).ConfigureAwait(false);
        string normalized = NormalizeInput(input);
        var phrase = DictionaryWords.Phrase(normalized);
        var phraseResult = new DictionaryWordResult(phrase ?? new DictionaryWord(normalized, "", "", false), whole);
        if (!splitWords) return new(phraseResult, phrase is null ? [] : [phraseResult]);
        var cache = new Dictionary<string, DictionaryLookupResult>(StringComparer.Ordinal);
        var words = new List<DictionaryWordResult>();
        foreach (var word in DictionaryWords.Split(normalized))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!cache.TryGetValue(word.Text, out var result))
                cache[word.Text] = result = await _lookup.LookupAsync(word.Text, cancellationToken).ConfigureAwait(false);
            words.Add(new(word, result));
        }
        return new(phraseResult, words);
    }

    public static string NormalizeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 2_000)
            throw new ArgumentException("Enter between 1 and 2,000 characters for dictionary lookup.", nameof(input));
        try
        {
            _ = StrictUtf8.GetByteCount(input);
            return input.Trim().Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException) { throw new ArgumentException("Dictionary input contains invalid Unicode.", nameof(input)); }
    }
}
