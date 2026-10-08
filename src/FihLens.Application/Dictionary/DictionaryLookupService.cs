using System.Text;
using FihLens.Core.Models;

namespace FihLens.Application.Dictionary;

public sealed class DictionaryLookupService(IDictionaryLookup lookup)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IDictionaryLookup _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));

    public Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
    {
        NormalizeInput(input);
        cancellationToken.ThrowIfCancellationRequested();
        return _lookup.LookupAsync(input, cancellationToken);
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
