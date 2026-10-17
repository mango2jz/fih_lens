using System.Collections.ObjectModel;
using FihLens.Application.Dictionary;
using FihLens.Core.Enums;

namespace FihLens.Infrastructure.Dictionary;

internal sealed class DictionaryPackCatalog
{
    private readonly IReadOnlyDictionary<(string PackId, string Version), DictionaryPackDefinition> _versions;

    // Source review is recorded separately; downloads remain disabled by user decision.
    public static DictionaryPackCatalog BuiltIn { get; } = new([
        new DictionaryPackDefinition("jmdict-ja-en", "2026-10-16-s3-992be02f3a404e9cd761bdbb", "JMdict Japanese–English",
            Language.Japanese, Language.English, "CC-BY-SA-4.0", "https://www.edrdg.org/pub/Nihongo/JMdict_e.gz",
            "3846524394e7b149f88f1cf09d9d6b10aa0b0e05267cd180bfb5eea31873952b", 19182109, 146083524,
            "d0bb8b6b589b32a48710d6854d8fb494683d669b24cb14bb596b943e5cda0ee0",
            "c8dcb6726d636fccb015215f25e0be09ea4c8c2860c42a38300808ccf059d156")
    ]);

    internal DictionaryPackCatalog(IEnumerable<DictionaryPackDefinition> versions)
    {
        var entries = new Dictionary<(string PackId, string Version), DictionaryPackDefinition>();
        foreach (var definition in versions)
        {
            if (definition.PackId != DictionaryPackPaths.PackId || !DictionaryPackPaths.IsSafeId(definition.Version) ||
                definition.SourceLanguage != FihLens.Core.Enums.Language.Japanese ||
                definition.TargetLanguage != FihLens.Core.Enums.Language.English ||
                definition.DownloadBytes is <= 0 or > MaximumArchiveBytes || definition.InstalledBytes is <= 0 or > MaximumExtractedBytes ||
                !IsSha256(definition.ArchiveSha256) || !IsSha256(definition.ManifestSha256) ||
                (definition.DownloadUri is { } uri && (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
                    !string.IsNullOrEmpty(uri.UserInfo))) || !entries.TryAdd((definition.PackId, definition.Version), definition))
                throw new ArgumentException("Invalid trusted dictionary pack catalog.", nameof(versions));
        }

        _versions = new ReadOnlyDictionary<(string PackId, string Version), DictionaryPackDefinition>(entries);
    }

    public IReadOnlyList<DictionaryPackDefinition> Definitions => _versions.Values.ToArray();

    public bool TryGet(string packId, string version, out DictionaryPackDefinition definition) =>
        _versions.TryGetValue((packId, version), out definition!);

    public bool TryGetLatest(string packId, out DictionaryPackDefinition definition)
    {
        definition = _versions.Values.Where(x => x.PackId == packId && x.DownloadUri is not null)
            .OrderByDescending(x => x.Version, StringComparer.Ordinal).FirstOrDefault()!;
        return definition is not null;
    }

    internal const long MaximumArchiveBytes = 128L * 1024 * 1024;
    internal const long MaximumExtractedBytes = 512L * 1024 * 1024;
    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
