using FihLens.Core.Enums;
using FihLens.Core.Models;

namespace FihLens.Application.Dictionary;

public sealed record DictionaryAvailability(DictionaryLookupService? Service, DictionarySource? Source, string Reason);

public sealed class DictionaryLookupRouter
{
    private readonly Dictionary<(Language Source, Language Target), DictionaryAvailability> _providers = new();

    public void SetAvailability(Language source, Language target, DictionaryLookupService? service,
        DictionarySource? metadata, string? reason = null)
    {
        if (!SupportsPair(source, target)) throw new ArgumentException("Unsupported local dictionary direction.");
        if (service is not null && (metadata is null || metadata.SourceLanguage != Code(source) || metadata.TargetLanguage != Code(target)))
            throw new ArgumentException("Dictionary source does not match its registered language pair.", nameof(metadata));
        _providers[(source, target)] = new(service, metadata,
            reason ?? "Local dictionary unavailable. Open Dictionaries to view setup options.");
    }

    public DictionaryAvailability GetAvailability(Language source, Language target) =>
        !SupportsPair(source, target)
            ? new(null, null, "No local dictionary for this direction. Ask AI remains an explicit action.")
            : _providers.GetValueOrDefault((source, target)) ?? new(null, null,
                "Local dictionary unavailable. Open Dictionaries to view setup options.");

    public Task<DictionaryLookupResult> LookupAsync(string input, Language source, Language target,
        CancellationToken cancellationToken = default) =>
        (GetAvailability(source, target).Service ?? throw new InvalidOperationException("Local dictionary unavailable for this direction."))
            .LookupAsync(input, cancellationToken);

    public static bool SupportsPair(Language source, Language target) =>
        (source, target) is (Language.English, Language.Vietnamese) or (Language.Japanese, Language.English);

    public static Language ParseLanguage(string code) => code switch
    {
        "en" => Language.English, "vi" => Language.Vietnamese, "ja" => Language.Japanese,
        _ => throw new ArgumentException("Unsupported dictionary language.", nameof(code))
    };

    private static string Code(Language language) => language switch
    {
        Language.English => "en", Language.Vietnamese => "vi", Language.Japanese => "ja",
        _ => throw new ArgumentException("Unsupported dictionary language.", nameof(language))
    };
}
