using FihLens.Core.Models;

namespace FihLens.Application.Dictionary;

public interface IDictionaryLookup
{
    Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default);
}
