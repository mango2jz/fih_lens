namespace FihLens.Application.Translation;

public enum TranslationProviderFailureKind
{
    Authentication,
    RateLimited,
    Unavailable,
    Timeout,
    InvalidResponse,
    Refused,
    RequestRejected
}
