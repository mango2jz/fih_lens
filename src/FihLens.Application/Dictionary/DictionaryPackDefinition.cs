using FihLens.Core.Enums;

namespace FihLens.Application.Dictionary;

public sealed record DictionaryPackDefinition(
    string PackId,
    string Version,
    string Name,
    Language SourceLanguage,
    Language TargetLanguage,
    string License,
    string SourceUrl,
    string SourceRevision,
    long DownloadBytes,
    long InstalledBytes,
    string ArchiveSha256,
    string ManifestSha256,
    Uri? DownloadUri = null);
