namespace FihLens.Application.Translation;

public sealed class TranslationProviderException : Exception
{
    public TranslationProviderFailureKind FailureKind { get; }

    public TranslationProviderException(TranslationProviderFailureKind failureKind, Exception? innerException = null)
        : base(GetSafeMessage(failureKind), innerException)
    {
        FailureKind = failureKind;
    }

    private static string GetSafeMessage(TranslationProviderFailureKind kind) => kind switch
    {
        TranslationProviderFailureKind.Authentication => "The translation provider rejected its credentials.",
        TranslationProviderFailureKind.RateLimited => "The translation provider is rate limiting requests.",
        TranslationProviderFailureKind.Unavailable => "The translation provider is unavailable.",
        TranslationProviderFailureKind.Timeout => "The translation request timed out.",
        TranslationProviderFailureKind.InvalidResponse => "The translation provider returned an invalid response.",
        TranslationProviderFailureKind.Refused => "The translation provider refused this request.",
        TranslationProviderFailureKind.RequestRejected => "The translation provider rejected this request.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
