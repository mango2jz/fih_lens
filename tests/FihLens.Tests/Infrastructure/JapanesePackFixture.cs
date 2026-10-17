using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FihLens.Application.Dictionary;
using FihLens.Core.Enums;
using FihLens.Core.Models;

namespace FihLens.Tests.Infrastructure;

internal static class JapanesePackFixture
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const string SourceHash = "B9768F4F5F9C36F1A5EEB4A92B6790F4BB2BB0A12D6C8883F7A2B07E5CCEB3D1";
    private const string ArchiveHash = "68784C2A4691CBF1074BDCD04BE2215A687F57B9810DA21A66C22DA4024558EF";

    public static (string Directory, DictionaryPackDefinition Definition) Create(string root,
        int homophones = 2, bool addWrittenAliasAfterReadings = false)
    {
        Directory.CreateDirectory(root);
        var entries = new List<Entry>();
        for (int id = 1; id <= homophones; id++)
            entries.Add(new Entry(id, id == 1 ? [new WrittenForm("生", ["often kana"])] : [], [new Reading("せい", [], id != 1, []), new Reading("いきる", id == 1 ? ["生"] : [], false, [])],
                [new Sense([new Gloss($"meaning-{id}", "fig")], ["noun"], [], [], ["note"], ["biology"], [], [], ["生きる"])
                {
                    LoanwordSources = id == 1 ? [new("vie", "fre", "part", "y"), new("", "eng", "full", null)] : [],
                    Antonyms = id == 1 ? ["反対語・1"] : []
                }]));
        if (addWrittenAliasAfterReadings)
            entries.Add(new Entry(homophones + 1, [new WrittenForm("せい", [])], [new Reading("せい", [], false, [])], [new Sense([new Gloss("written alias", null)], [], [], [], [], [], [], [], [])]));
        return CreatePack(root, entries);
    }

    public static (string Directory, DictionaryPackDefinition Definition) CreatePack(string root, IReadOnlyList<Entry> entries)
    {
        Directory.CreateDirectory(root);
        var payload = new MemoryStream();
        var aliases = new List<(byte[] Key, ulong Id, long Offset, uint Length)>();
        foreach (var entry in entries.OrderBy(e => e.Id))
        {
            byte[] bytes = Utf8.GetBytes(JsonSerializer.Serialize(entry, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n");
            long offset = payload.Position;
            payload.Write(bytes);
            foreach (byte[] key in entry.WrittenForms.Select(f => f.Text).Concat(entry.Readings.Select(r => r.Text)).Distinct(StringComparer.Ordinal)
                         .Select(k => Utf8.GetBytes(k.Normalize(NormalizationForm.FormC))))
                aliases.Add((key, (ulong)entry.Id, offset, (uint)bytes.Length));
        }
        aliases = aliases.OrderBy(a => a.Key, ByteArrayComparer.Instance).ThenBy(a => a.Id).ToList();
        using var keys = new MemoryStream();
        using var index = new MemoryStream();
        index.Write("FIHJIDX1"u8);
        WriteU32(index, 1); WriteU32(index, 1); WriteU64(index, (ulong)aliases.Count); WriteU64(index, 0);
        ulong keyOffset = 0;
        foreach (var alias in aliases)
        {
            keys.Write(alias.Key);
            WriteU64(index, keyOffset); WriteU32(index, (uint)alias.Key.Length);
            WriteU64(index, (ulong)alias.Offset); WriteU32(index, alias.Length); WriteU64(index, alias.Id);
            keyOffset += (uint)alias.Key.Length;
        }
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["entries.jsonl"] = payload.ToArray(), ["keys.utf8"] = keys.ToArray(), ["lookup.idx"] = index.ToArray(),
            ["LICENSE-DATA.txt"] = Utf8.GetBytes("CC BY-SA 4.0\n"), ["ATTRIBUTION.txt"] = Utf8.GetBytes("EDRDG JMdict\n"),
            ["CHANGES.txt"] = Utf8.GetBytes("Converted fixture\n")
        };
        foreach (var pair in contents) File.WriteAllBytes(Path.Combine(root, pair.Key), pair.Value);
        var manifest = new
        {
            schemaVersion = 3, converterSha256 = ConverterHash, artifactSha256 = ArtifactHash, packId = "jmdict-ja-en", version = "2026-01-01-s3-abcdef0123456789abcdef01", sourceId = "EDRDG-JMdict-e-legacy",
            sourceRevision = SourceHash, sourceLanguage = "ja", targetLanguage = "en", name = "JMdict fixture",
            license = "CC-BY-SA-4.0", sourceUrl = "https://example.test/jmdict", attribution = "EDRDG",
            qualityStatus = "synthetic", humanReviewScope = "synthetic fixture", sourceArchiveSha256 = SourceHash,
            sourceDate = "2026-01-01", entryCount = entries.Count, indexRows = aliases.Count,
            files = contents.Select(p => new { path = p.Key, bytes = p.Value.Length, sha256 = Hash(p.Value) }).ToArray()
        };
        byte[] manifestBytes = Utf8.GetBytes(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n");
        File.WriteAllBytes(Path.Combine(root, "manifest.json"), manifestBytes);
        string manifestHash = Hash(manifestBytes);
        return (root, new DictionaryPackDefinition("jmdict-ja-en", "2026-01-01-s3-abcdef0123456789abcdef01", "JMdict fixture", Language.Japanese,
            Language.English, "CC-BY-SA-4.0", "https://example.test/jmdict", SourceHash,
            1, contents.Values.Sum(v => (long)v.Length) + manifestBytes.LongLength, ArchiveHash, manifestHash));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private const string ConverterHash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string ArtifactHash = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
    private static void WriteU32(Stream stream, uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, value); stream.Write(b); }
    private static void WriteU64(Stream stream, ulong value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, value); stream.Write(b); }
    internal sealed record Entry(long Id, WrittenForm[] WrittenForms, Reading[] Readings, Sense[] Senses);
    internal sealed record WrittenForm(string Text, string[] Information);
    internal sealed record Reading(string Text, string[] Restrictions, bool NoKanji, string[] Information);
    internal sealed record Gloss(string Text, string? Type);
    internal sealed record Sense(Gloss[] Glosses, string[] PartsOfSpeech, string[] WrittenRestrictions,
        string[] ReadingRestrictions, string[] Notes, string[] Fields, string[] Misc, string[] Dialects, string[] CrossReferences)
    {
        public JapaneseLoanwordSource[] LoanwordSources { get; init; } = [];
        public string[] Antonyms { get; init; } = [];
    }

    internal sealed record JapaneseLoanwordSource(string Text, string Language, string Type, string? Wasei);

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y) => (x ?? []).AsSpan().SequenceCompareTo(y ?? []);
    }
}
