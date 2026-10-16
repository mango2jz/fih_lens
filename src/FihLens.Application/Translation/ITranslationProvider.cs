using FihLens.Core.Models;

namespace FihLens.Application.Translation;

public interface ITranslationProvider
{
    /// <summary>Translates a supported request; faults, unsupported requests, and cancellation remain observable by the caller.</summary>
    /// <remarks>The returned task does not promise a particular execution thread.</remarks>
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
