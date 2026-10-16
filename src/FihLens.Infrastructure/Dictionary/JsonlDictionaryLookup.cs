using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FihLens.Application.Dictionary;
using FihLens.Core.Models;

namespace FihLens.Infrastructure.Dictionary;

public sealed class JsonlDictionaryLookup : IDictionaryLookup
{
    public const long MaximumFileBytes = 500L * 1024 * 1024;
    public const int MaximumRowBytes = 1024 * 1024;
    public const int MaximumRows = 1_000_000;
    private const string SourceId = "thichhoc-en-vi-v0.9.0";
    private const string Revision = "ec3fa199aebdc4da4ebcaa7a11ac4b9c44046f75";
    private const string CorpusHash = "f2726c2e435e262eb87b051d0c1cf159a309d3f4a25f77a9ca762136117a6b4c";
    private const string QueryHash = "63319ee978a572e12f16b498b09781e84237134b712df0761984b1214c7460ee";
    private const string ManifestHash = "c35cc43ea36f535ca918aef4cb7a43cb6ae70793c33f103f9653534cc1ba6185";
    private const string ReviewHash = "edf6fba01dc062ce3e20fe2b2595311d02623540f42d3ebe34b333bc7c4e6047";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly FrozenDictionary<string, IReadOnlyList<DictionaryEntry>> _entries;

    private JsonlDictionaryLookup(DictionarySource source,
        System.Collections.Generic.Dictionary<string, List<DictionaryEntry>> entries, int entryCount)
    {
        Source = source;
        EntryCount = entryCount;
        _entries = entries.ToFrozenDictionary(pair => pair.Key,
            pair => (IReadOnlyList<DictionaryEntry>)pair.Value.AsReadOnly(), StringComparer.OrdinalIgnoreCase);
    }

    public DictionarySource Source { get; }
    public int EntryCount { get; }
    public int DistinctHeadwordCount => _entries.Count;

    public Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
    {
        string key = DictionaryLookupService.NormalizeInput(input);
        cancellationToken.ThrowIfCancellationRequested();
        _entries.TryGetValue(key, out var entries);
        return Task.FromResult(new DictionaryLookupResult(input, Source, entries ?? Array.Empty<DictionaryEntry>()));
    }

    // CPU parsing/indexing runs on a worker even when the caller is the UI thread.
    public static Task<JsonlDictionaryLookup> LoadAsync(string root, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            try { return await LoadCoreAsync(root, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (DictionaryDataException) { throw; }
            catch (JsonException) { throw new DictionaryDataException("invalid JSON"); }
            catch (DecoderFallbackException) { throw new DictionaryDataException("invalid UTF-8"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or NotSupportedException or KeyNotFoundException or FormatException or System.Security.SecurityException)
            { throw new DictionaryDataException("local setup or invalid data"); }
        }, cancellationToken);

    private static async Task<JsonlDictionaryLookup> LoadCoreAsync(string root, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        root = ValidateRoot(root);
        byte[] manifestBytes = await ReadSmallAsync(Member(root, "dictionary-manifest.json"), token);
        using var manifest = Parse(manifestBytes);
        var m = manifest.RootElement;
        Object(m);
        if (Number(m, "schemaVersion") != 1) Fail("unsupported schema version");
        if (Text(m, "sourceId") != SourceId || Text(m, "revision") != Revision) Fail("unsupported source");
        if ((m.TryGetProperty("sourceLanguage", out var sl) && sl.GetString() != "en") ||
            (m.TryGetProperty("targetLanguage", out var tl) && tl.GetString() != "vi")) Fail("unsupported language direction");
        if (Text(m, "license") != "CC-BY-SA-4.0" ||
            Text(m, "qualityStatus") != "sample-reviewed-user-report; generated Vietnamese senses") Fail("unapproved source status");
        string dictionaryFile = Text(m, "dictionaryFile");
        string dictionaryPath = Member(root, dictionaryFile);
        string reviewPath = Member(root, Text(m, "humanReviewFile"));
        if (Text(m, "querySampleSha256") != QueryHash) Fail("unapproved review queries");
        long expectedBytes = Number(m, "dictionaryBytes");
        CheckFileSize(expectedBytes);
        string expectedHash = Text(m, "dictionarySha256");

        var files = m.GetProperty("files");
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() != 5) Fail("missing notices");
        var members = new System.Collections.Generic.Dictionary<string, (string Path, string Hash, long Bytes)>(StringComparer.Ordinal);
        // Validate every member before opening any manifest-selected file.
        foreach (var file in files.EnumerateArray())
        {
            Object(file);
            string name = Text(file, "path");
            string path = Member(root, name);
            if (!members.TryAdd(name, (path, Text(file, "sha256"), Number(file, "bytes")))) Fail("duplicate manifest member");
        }
        string[] noticeNames = ["LICENSE-DATA", "UPSTREAM-CODE-LICENSE", "UPSTREAM-README.md", "ATTRIBUTION.txt"];
        if (!members.TryGetValue(dictionaryFile, out var corpus) || corpus.Hash != expectedHash || corpus.Bytes != expectedBytes ||
            noticeNames.Any(name => !members.ContainsKey(name))) Fail("invalid file manifest");
        if (expectedHash != CorpusHash) Fail("unapproved corpus");
        VerifyHash(manifestBytes, ManifestHash);

        byte[] reviewBytes = await ReadSmallAsync(reviewPath, token);
        using var review = Parse(reviewBytes);
        var r = review.RootElement;
        Object(r);
        if (Text(r, "sourceId") != SourceId || Text(r, "revision") != Revision ||
            Text(r, "corpusSha256") != expectedHash || Text(r, "querySampleSha256") != QueryHash ||
            Number(r, "reviewedQueries") != 100 || Number(r, "acceptableResults") < 90 ||
            Number(r, "acceptableResults") + Number(r, "missingResults") != 100 ||
            Number(r, "criticalMisleadingMeaningsReported") != 0) Fail("unapproved human review");
        VerifyHash(reviewBytes, ReviewHash);

        var notices = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in noticeNames)
        {
            var file = members[name];
            byte[] bytes = await ReadSmallAsync(file.Path, token);
            if (bytes.LongLength != file.Bytes) Fail("notice size mismatch");
            VerifyHash(bytes, file.Hash);
            notices.Add(name, StrictUtf8.GetString(bytes));
        }

        var (entries, rows) = await ReadEntriesAsync(dictionaryPath, expectedBytes, expectedHash, token).ConfigureAwait(false);
        if (rows != Number(m, "entryCount") || entries.Count < 10_000) Fail("dictionary count mismatch");
        token.ThrowIfCancellationRequested();
        var loaded = new JsonlDictionaryLookup(new DictionarySource(SourceId, Text(m, "name"), Revision, "en", "vi",
            Text(m, "license"), Text(m, "sourceUrl"), Text(m, "attribution"), Text(m, "qualityStatus"),
            Text(m, "humanReviewScope"), new ReadOnlyDictionary<string, string>(notices)), entries, rows);
        token.ThrowIfCancellationRequested();
        return loaded;
    }

    internal static async Task<(System.Collections.Generic.Dictionary<string, List<DictionaryEntry>> Entries, int Rows)>
        ReadEntriesAsync(string dictionaryPath, long expectedBytes, string expectedHash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CheckFileSize(expectedBytes);
        var entries = new System.Collections.Generic.Dictionary<string, List<DictionaryEntry>>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int rows = 0;
        using var stream = Open(dictionaryPath);
        CheckFileSize(stream.Length);
        if (stream.Length != expectedBytes) Fail("dictionary size mismatch");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] block = new byte[64 * 1024];
        byte[] row = new byte[MaximumRowBytes];
        int rowLength = 0;
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(block, token).ConfigureAwait(false)) != 0)
        {
            total += read;
            CheckFileSize(total);
            hash.AppendData(block, 0, read);
            for (int i = 0; i < read; i++)
            {
                if (block[i] == (byte)'\n')
                {
                    AddRow();
                    rowLength = 0;
                }
                else
                {
                    if (rowLength == MaximumRowBytes) throw new DictionaryDataException("row size limit", rows + 1);
                    row[rowLength++] = block[i];
                }
            }
        }
        if (rowLength != 0) AddRow();
        token.ThrowIfCancellationRequested();
        if (total != expectedBytes || !HashEquals(Convert.ToHexString(hash.GetHashAndReset()), expectedHash)) Fail("dictionary hash mismatch");
        return (entries, rows);

        void AddRow()
        {
            token.ThrowIfCancellationRequested();
            CheckRowCount(++rows);
            try
            {
                string json = StrictUtf8.GetString(row, 0, rowLength);
                using var document = JsonDocument.Parse(json);
                var e = document.RootElement;
                Object(e);
                if (e.TryGetProperty("schemaVersion", out var version) && version.GetInt32() != 1) Fail("unsupported row schema");
                if ((e.TryGetProperty("sourceLanguage", out var source) && source.GetString() != "en") ||
                    (e.TryGetProperty("targetLanguage", out var target) && target.GetString() != "vi")) Fail("unsupported row language");
                string headword = Text(e, "headword");
                string key = headword.Trim().Normalize(NormalizationForm.FormC);
                if (Text(e, "sourceId") != SourceId) Fail("unsupported row source");
                string id = Text(e, "sourceArticleId");
                if (!ids.Add(id)) Fail("duplicate article ID");
                var senses = e.GetProperty("senses");
                if (senses.ValueKind != JsonValueKind.Array || senses.GetArrayLength() == 0) Fail("empty senses");
                var meanings = senses.EnumerateArray().Select(ValueText).ToArray();
                var entry = new DictionaryEntry(headword, Array.AsReadOnly(meanings), Optional(e, "partOfSpeech"),
                    Optional(e, "example"), SourceId, id, Text(e, "sourceOrigin"));
                if (!entries.TryGetValue(key, out var group)) entries.Add(key, group = []);
                group.Add(entry);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is JsonException or DecoderFallbackException or ArgumentException
                or InvalidOperationException or KeyNotFoundException or FormatException or DictionaryDataException)
            { throw new DictionaryDataException("invalid record", rows); }
        }
    }

    internal static string ValidateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || root.Length < 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' ||
            (root[2] != '\\' && root[2] != '/') || root.IndexOf(':', 2) >= 0 ||
            root.Split(['\\', '/']).Skip(1).Any(p => p is "." or ".." ||
                p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDeviceName(p)))
            Fail("invalid local root");
        root = Path.GetFullPath(root);
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        CheckDriveType(drive.DriveType);
        RejectReparse(root);
        return root;
    }

    internal static string Member(string root, string member)
    {
        if (Path.IsPathRooted(member) || member.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || member.Contains(':') ||
            member.Split(['\\', '/']).Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDeviceName(p))) Fail("invalid manifest path");
        string path = Path.GetFullPath(Path.Combine(root, member));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Fail("invalid manifest path");
        RejectReparse(path);
        return path;
    }

    private static void RejectReparse(string path)
    {
        var ancestors = new Stack<string>();
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
        // Check parents first, so attribute checks never traverse an unchecked junction.
        foreach (string current in ancestors)
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) Fail("reparse point");
    }

    private static FileStream Open(string path)
    {
        RejectReparse(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static async Task<byte[]> ReadSmallAsync(string path, CancellationToken token)
    {
        using var stream = Open(path);
        if (stream.Length > MaximumRowBytes) Fail("metadata size limit");
        byte[] bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return bytes;
    }

    private static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(StrictUtf8.GetString(bytes));
    private static string Text(JsonElement e, string name) => ValueText(e.GetProperty(name));
    private static string ValueText(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(e.GetString())) Fail("invalid text field");
        string value = e.GetString()!;
        _ = StrictUtf8.GetByteCount(value);
        _ = value.Normalize(NormalizationForm.FormC);
        return value;
    }
    private static string? Optional(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null : ValueText(value);
    private static long Number(JsonElement e, string name)
    {
        if (!e.GetProperty(name).TryGetInt64(out long value) || value < 0) Fail("invalid number field");
        return value;
    }
    private static void Object(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) Fail("invalid object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in e.EnumerateObject())
            if (!names.Add(property.Name)) Fail("duplicate JSON field");
    }
    private static void CheckFileSize(long bytes)
    {
        if (bytes < 0 || bytes > MaximumFileBytes) Fail("file size limit");
    }
    internal static void CheckRowCount(int rows)
    {
        if (rows < 0 || rows > MaximumRows) throw new DictionaryDataException("row count limit");
    }
    internal static void CheckDriveType(DriveType driveType)
    {
        if (driveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram)) Fail("nonlocal drive");
    }
    private static bool IsDeviceName(string member)
    {
        string name = member.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '1' and <= '9');
    }
    private static void VerifyHash(byte[] bytes, string expected)
    {
        if (!HashEquals(Convert.ToHexString(SHA256.HashData(bytes)), expected)) Fail("hash mismatch");
    }
    private static bool HashEquals(string actual, string expected) => StringComparer.OrdinalIgnoreCase.Equals(actual, expected);
    private static void Fail(string category) => throw new DictionaryDataException(category);
}
