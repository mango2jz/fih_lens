using FihLens.Core.Models;

namespace FihLens.Application.Translation;

public sealed class TranslationService(ITranslationProvider provider)
{
    private readonly ITranslationProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _provider.TranslateAsync(request, cancellationToken);
    }
}
