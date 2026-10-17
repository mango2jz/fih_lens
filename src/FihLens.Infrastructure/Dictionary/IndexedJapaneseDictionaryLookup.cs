using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using FihLens.Application.Dictionary;
using FihLens.Core.Enums;
using FihLens.Core.Models;

namespace FihLens.Infrastructure.Dictionary;

/// <summary>Reads one already-installed, immutable Japanese-to-English pack.</summary>
public sealed class IndexedJapaneseDictionaryLookup : IDictionaryLookup, IAsyncDisposable
{
    public const int MaximumEntries = 128;
    public const int MaximumIndexRows = 2_000_000;
    public const int MaximumEntryBytes = 1024 * 1024;
    public const long MaximumInstalledBytes = 512L * 1024 * 1024;
    private const int HeaderBytes = 32;
    private const int IndexRowBytes = 32;
    private const int MaximumMetadataBytes = 1024 * 1024;
    private static readonly byte[] Magic = "FIHJIDX1"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SafeFileHandle _entries;
    private readonly SafeFileHandle _keys;
    private readonly SafeFileHandle _index;
    private readonly object _leaseLock = new();
    private TaskCompletionSource? _drained;
    private int _leases;
    private bool _disposing;
    private bool _disposed;

    private IndexedJapaneseDictionaryLookup(DictionarySource source, SafeFileHandle entries,
        SafeFileHandle keys, SafeFileHandle index, ulong rows)
    {
        Source = source;
        _entries = entries;
        _keys = keys;
        _index = index;
        IndexRows = rows;
    }

    public DictionarySource Source { get; }
    public ulong IndexRows { get; }

    public static Task<IndexedJapaneseDictionaryLookup> LoadAsync(string packDirectory,
        DictionaryPackDefinition definition, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadCoreAsync(packDirectory, definition, cancellationToken, null), cancellationToken);

    internal static Task<ValidationCandidate> OpenForValidationAsync(string packDirectory,
        DictionaryPackDefinition definition, CancellationToken cancellationToken = default) =>
        Task.Run(() => OpenCandidateCore(packDirectory, definition, cancellationToken), cancellationToken);

    private static ValidationCandidate OpenCandidateCore(string packDirectory, DictionaryPackDefinition definition, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string root = JsonlDictionaryLookup.ValidateRoot(packDirectory);
        var handles = new List<SafeFileHandle>();
        try
        {
            foreach (string name in new[] { "entries.jsonl", "keys.utf8", "lookup.idx" })
                handles.Add(Open(Member(root, name)));
            return new ValidationCandidate(root, definition, handles.ToArray());
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
    }

    private static IndexedJapaneseDictionaryLookup LoadCoreAsync(string packDirectory,
        DictionaryPackDefinition definition, CancellationToken token, SafeFileHandle[]? retainedHandles)
    {
        var handles = retainedHandles is null ? new List<SafeFileHandle>() : [.. retainedHandles];
        try
        {
            ArgumentNullException.ThrowIfNull(definition);
            token.ThrowIfCancellationRequested();
            string root = JsonlDictionaryLookup.ValidateRoot(packDirectory);
            var manifestPath = Member(root, "manifest.json");
            using var manifestHandle = Open(manifestPath);
            byte[] manifestBytes = ReadAllSmall(manifestHandle, MaximumMetadataBytes, token);
            VerifyHash(manifestBytes, definition.ManifestSha256);
            using var manifest = Parse(manifestBytes);
            var m = manifest.RootElement;
            ValidateManifest(m, definition);
            var files = ReadFiles(m);
            long installedBytes = checked(files.Values.Aggregate(0L, (sum, f) => checked(sum + f.Bytes)) + manifestBytes.LongLength);
            if (installedBytes != definition.InstalledBytes || installedBytes > MaximumInstalledBytes)
                Fail("installed size mismatch");

            SafeFileHandle entries = retainedHandles is null ? Add("entries.jsonl") : retainedHandles[0];
            SafeFileHandle keys = retainedHandles is null ? Add("keys.utf8") : retainedHandles[1];
            SafeFileHandle index = retainedHandles is null ? Add("lookup.idx") : retainedHandles[2];
            VerifyMember(entries, files["entries.jsonl"], MaximumInstalledBytes, token);
            VerifyMember(keys, files["keys.utf8"], MaximumInstalledBytes, token);
            VerifyMember(index, files["lookup.idx"], HeaderBytes + (long)MaximumIndexRows * IndexRowBytes, token);
            var notices = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in NoticeNames)
            {
                using var handle = Open(Member(root, name));
                byte[] bytes = ReadAllSmall(handle, MaximumEntryBytes, token);
                var expected = files[name];
                if (bytes.LongLength != expected.Bytes) Fail("notice size mismatch");
                VerifyHash(bytes, expected.Hash);
                notices.Add(name, StrictUtf8.GetString(bytes));
            }

            ulong rows = ValidateIndexAndPayloads(entries, keys, index, files["entries.jsonl"].Bytes,
                files["keys.utf8"].Bytes, files["lookup.idx"].Bytes, Number(m, "entryCount"),
                Number(m, "indexRows"), token);
            token.ThrowIfCancellationRequested();
            var source = new DictionarySource(Text(m, "sourceId"), Text(m, "name"), Text(m, "sourceRevision"),
                "ja", "en", Text(m, "license"), Text(m, "sourceUrl"), Text(m, "attribution"),
                Text(m, "qualityStatus"), Text(m, "humanReviewScope"),
                new ReadOnlyDictionary<string, string>(notices));
            var reader = new IndexedJapaneseDictionaryLookup(source, entries, keys, index, rows);
            handles.Clear();
            return reader;

            SafeFileHandle Add(string name)
            {
                var handle = Open(Member(root, name));
                handles.Add(handle);
                return handle;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (DictionaryDataException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException or KeyNotFoundException or FormatException
            or OverflowException or DecoderFallbackException or JsonException or System.Security.SecurityException)
        { throw new DictionaryDataException("invalid pack or local setup"); }
        finally
        {
            foreach (var handle in handles) handle.Dispose();
        }
    }

    internal sealed class ValidationCandidate : IDisposable
    {
        private readonly string _root;
        private readonly DictionaryPackDefinition _definition;
        private SafeFileHandle[]? _handles;

        internal ValidationCandidate(string root, DictionaryPackDefinition definition, SafeFileHandle[] handles)
        { _root = root; _definition = definition; _handles = handles; }

        public Task<IndexedJapaneseDictionaryLookup> ValidateAsync(CancellationToken token = default)
        {
            var handles = Interlocked.Exchange(ref _handles, null) ?? throw new ObjectDisposedException(nameof(ValidationCandidate));
            return Task.Run(() => LoadCoreAsync(_root, _definition, token, handles));
        }

        public void Dispose()
        {
            var handles = Interlocked.Exchange(ref _handles, null);
            if (handles is not null) foreach (var handle in handles) handle.Dispose();
        }
    }

    public async Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
    {
        string normalized = DictionaryLookupService.NormalizeInput(input);
        byte[] key = StrictUtf8.GetBytes(normalized);
        using var lease = AcquireLease();
        cancellationToken.ThrowIfCancellationRequested();
        ulong rowCount = IndexRows;
        ulong lo = 0, hi = rowCount;
        while (lo < hi)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ulong mid = lo + (hi - lo) / 2;
            var row = await ReadIndexRowAsync(_index, mid, cancellationToken).ConfigureAwait(false);
            int comparison = await CompareKeyAsync(_keys, row, key, cancellationToken).ConfigureAwait(false);
            if (comparison < 0) lo = mid + 1;
            else hi = mid;
        }

        var entries = new List<DictionaryEntry>(MaximumEntries + 1);
        ulong priorId = 0;
        for (ulong i = lo; i < rowCount && entries.Count <= MaximumEntries; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await ReadIndexRowAsync(_index, i, cancellationToken).ConfigureAwait(false);
            if (await CompareKeyAsync(_keys, row, key, cancellationToken).ConfigureAwait(false) != 0) break;
            if (entries.Count != 0 && row.Id <= priorId)
                Fail("unordered lookup rows");
            priorId = row.Id;
            byte[] payload = new byte[checked((int)row.PayloadLength)];
            await ReadExactlyAsync(_entries, payload, checked((long)row.PayloadOffset), cancellationToken).ConfigureAwait(false);
            entries.Add(ParseEntry(payload, row.Id, normalized));
        }
        cancellationToken.ThrowIfCancellationRequested();
        bool truncated = entries.Count > MaximumEntries;
        if (truncated) entries.RemoveAt(entries.Count - 1);
        return new DictionaryLookupResult(input, Source, entries.AsReadOnly()) { IsTruncated = truncated };
    }

    private static readonly string[] NoticeNames = ["LICENSE-DATA.txt", "ATTRIBUTION.txt", "CHANGES.txt"];
    private static readonly HashSet<string> AllowedFiles = new(
        ["entries.jsonl", "keys.utf8", "lookup.idx", .. NoticeNames], StringComparer.Ordinal);

    private sealed record FileInfo(long Bytes, string Hash);
    private readonly record struct IndexRow(ulong KeyOffset, uint KeyLength, ulong PayloadOffset,
        uint PayloadLength, ulong Id);

    private static Dictionary<string, FileInfo> ReadFiles(JsonElement manifest)
    {
        var files = manifest.GetProperty("files");
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() != AllowedFiles.Count)
            Fail("invalid file list");
        var result = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
        foreach (var f in files.EnumerateArray())
        {
            Object(f);
            RequireFields(f, "path", "bytes", "sha256");
            string path = Text(f, "path");
            if (!AllowedFiles.Contains(path) || !result.TryAdd(path, new FileInfo(Number(f, "bytes"), Text(f, "sha256"))))
                Fail("unexpected or duplicate file");
            ValidateHash(result[path].Hash);
        }
        if (result.Count != AllowedFiles.Count || result.Any(f => f.Value.Bytes < 0)) Fail("incomplete file list");
        return result;
    }

    private static void ValidateManifest(JsonElement m, DictionaryPackDefinition d)
    {
        Object(m);
        if (d.DownloadBytes < 0 || d.DownloadBytes > 128L * 1024 * 1024 ||
            d.InstalledBytes < 0 || d.InstalledBytes > MaximumInstalledBytes) Fail("pack size limit");
        RequireFields(m, "schemaVersion", "converterSha256", "artifactSha256", "packId", "version", "sourceId", "sourceRevision", "sourceLanguage",
            "targetLanguage", "name", "license", "sourceUrl", "attribution", "qualityStatus", "humanReviewScope",
            "sourceArchiveSha256", "sourceDate", "entryCount", "indexRows", "files");
        if (Number(m, "schemaVersion") != 3 || Text(m, "packId") != d.PackId || Text(m, "version") != d.Version ||
            Text(m, "name") != d.Name || Text(m, "license") != d.License || Text(m, "sourceUrl") != d.SourceUrl ||
            Text(m, "sourceRevision") != d.SourceRevision || Text(m, "sourceLanguage") != "ja" ||
            Text(m, "targetLanguage") != "en" || Text(m, "sourceId") != "EDRDG-JMdict-e-legacy" ||
            d.SourceLanguage != Language.Japanese || d.TargetLanguage != Language.English ||
            Text(m, "sourceArchiveSha256") != d.SourceRevision || Text(m, "sourceRevision") != d.SourceRevision)
            Fail("untrusted or unsupported manifest");
        ValidateHash(d.ManifestSha256);
        ValidateHash(d.ArchiveSha256);
        ValidateHash(Text(m, "sourceArchiveSha256"));
        string converterHash = Text(m, "converterSha256");
        ValidateHash(converterHash);
        string artifactHash = Text(m, "artifactSha256");
        ValidateHash(artifactHash);
        if (d.Version.Length > 64 || !d.Version.EndsWith($"-s3-{artifactHash[..24]}", StringComparison.OrdinalIgnoreCase))
            Fail("artifact revision does not match pack version");
        _ = Text(m, "attribution");
        _ = Text(m, "qualityStatus");
        _ = Text(m, "humanReviewScope");
        _ = Number(m, "entryCount");
        if (Number(m, "indexRows") > MaximumIndexRows) Fail("index row limit");
    }

    private static ulong ValidateIndexAndPayloads(SafeFileHandle entries, SafeFileHandle keys,
        SafeFileHandle index, long entryBytes, long keyBytes, long indexBytes, long entryCount,
        long manifestRows, CancellationToken token)
    {
        ulong rows = ReadHeader(index, token);
        if (rows > MaximumIndexRows || rows != (ulong)manifestRows ||
            indexBytes != checked(HeaderBytes + (long)rows * IndexRowBytes) || entryCount <= 0 || entryCount > (long)rows)
            Fail("index size or count mismatch");

        // Validation buffers are temporary; published lookups retain only the verified file handles.
        byte[] indexCache = new byte[checked((int)indexBytes)];
        byte[] keyCache = new byte[checked((int)keyBytes)];
        ReadExactly(index, indexCache, 0, token);
        ReadExactly(keys, keyCache, 0, token);
        byte[] previousKey = [];
        ulong previousId = 0, lastKeyEnd = 0;
        var payloadRefs = new Dictionary<ulong, (ulong Id, uint Length, int Aliases)>();
        for (ulong i = 0; i < rows; i++)
        {
            token.ThrowIfCancellationRequested();
            var row = ReadCachedRow(indexCache, i);
            if (row.KeyLength is 0 or > 8000 || row.KeyOffset != lastKeyEnd ||
                checked(row.KeyOffset + row.KeyLength) > (ulong)keyBytes || row.PayloadLength is 0 or > MaximumEntryBytes ||
                row.PayloadOffset >= (ulong)entryBytes || checked(row.PayloadOffset + row.PayloadLength) > (ulong)entryBytes || row.Id == 0)
                Fail("invalid index range");
            byte[] keyBytesBuffer = new byte[checked((int)row.KeyLength)];
            keyCache.AsSpan(checked((int)row.KeyOffset), keyBytesBuffer.Length).CopyTo(keyBytesBuffer);
            string decoded = StrictUtf8.GetString(keyBytesBuffer);
            if (decoded.Length > 2000 || decoded != decoded.Normalize(NormalizationForm.FormC) ||
                !keyBytesBuffer.SequenceEqual(StrictUtf8.GetBytes(decoded)))
                Fail("invalid normalized key");
            int cmp = previousKey.Length == 0 ? -1 : CompareBytes(previousKey, keyBytesBuffer);
            if (cmp > 0 || (cmp == 0 && row.Id <= previousId)) Fail("unsorted index");
            previousKey = keyBytesBuffer;
            previousId = row.Id;
            lastKeyEnd = checked(row.KeyOffset + row.KeyLength);
            if (payloadRefs.TryGetValue(row.PayloadOffset, out var reference))
            {
                if (reference.Id != row.Id || reference.Length != row.PayloadLength) Fail("conflicting payload reference");
                payloadRefs[row.PayloadOffset] = (reference.Id, reference.Length, checked(reference.Aliases + 1));
            }
            else payloadRefs.Add(row.PayloadOffset, (row.Id, row.PayloadLength, 1));
        }
        if (lastKeyEnd != (ulong)keyBytes) Fail("unreferenced key bytes");

        long nextPayload = 0;
        ulong previousEntryId = 0;
        for (long i = 0; i < entryCount; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!payloadRefs.TryGetValue((ulong)nextPayload, out var reference)) Fail("entry without index aliases");
            byte[] line = new byte[checked((int)reference.Length)];
            ReadExactly(entries, line, nextPayload, token);
            if (line[^1] != (byte)'\n') Fail("entry line terminator");
            using var document = Parse(line);
            var entry = document.RootElement;
            Object(entry);
            long id = Number(entry, "id");
            if ((ulong)id != reference.Id || reference.Id <= previousEntryId) Fail("invalid entry ID order");
            previousEntryId = reference.Id;
            var aliases = Aliases(entry);
            if (aliases.Count == 0) Fail("entry without aliases");
            if (aliases.Count != reference.Aliases) Fail("unexpected index aliases");
            ValidateRestrictions(entry);
            foreach (var alias in aliases)
                if (!ContainsIndexAlias(indexCache, keyCache, rows, alias, reference.Id, (ulong)nextPayload, reference.Length, token))
                    Fail("entry/index alias mismatch");
            nextPayload = checked(nextPayload + reference.Length);
        }
        if (nextPayload != entryBytes || payloadRefs.Count != entryCount) Fail("entry payload coverage mismatch");
        return rows;
    }

    private static bool ContainsIndexAlias(byte[] index, byte[] keys, ulong rows,
        byte[] alias, ulong id, ulong payloadOffset, uint payloadLength, CancellationToken token)
    {
        ulong lo = 0, hi = rows;
        while (lo < hi)
        {
            ulong mid = lo + (hi - lo) / 2;
            token.ThrowIfCancellationRequested();
            var row = ReadCachedRow(index, mid);
            int cmp = CompareBytes(keys.AsSpan(checked((int)row.KeyOffset), checked((int)row.KeyLength)), alias);
            if (cmp < 0 || (cmp == 0 && row.Id < id)) lo = mid + 1;
            else hi = mid;
        }
        if (lo >= rows) return false;
        var found = ReadCachedRow(index, lo);
        return CompareBytes(keys.AsSpan(checked((int)found.KeyOffset), checked((int)found.KeyLength)), alias) == 0 && found.Id == id &&
            found.PayloadOffset == payloadOffset && found.PayloadLength == payloadLength;
    }

    private static IndexRow ReadCachedRow(byte[] index, ulong row)
    {
        int offset = checked(HeaderBytes + (int)row * IndexRowBytes);
        return new(U64(index, offset), U32(index, offset + 8), U64(index, offset + 12),
            U32(index, offset + 20), U64(index, offset + 24));
    }

    private static void ReadExactly(SafeFileHandle handle, Span<byte> bytes, long offset, CancellationToken token)
    {
        while (!bytes.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            int count = RandomAccess.Read(handle, bytes, offset);
            if (count == 0) Fail("truncated file");
            offset = checked(offset + count);
            bytes = bytes[count..];
        }
    }

    private static List<byte[]> Aliases(JsonElement entry)
    {
        RequireFields(entry, "id", "writtenForms", "readings", "senses");
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var written = entry.GetProperty("writtenForms").EnumerateArray().Select(form =>
        {
            Object(form);
            RequireFields(form, "text", "information");
            _ = Strings(form, "information", allowEmpty: true);
            return Text(form, "text");
        }).ToArray();
        var readings = entry.GetProperty("readings");
        if (readings.ValueKind != JsonValueKind.Array || readings.GetArrayLength() == 0) Fail("entry requires readings");
        foreach (string form in written) aliases.Add(NormalizedAlias(form));
        foreach (var reading in readings.EnumerateArray())
        {
            Object(reading);
            RequireFields(reading, "text", "restrictions", "noKanji", "information");
            aliases.Add(NormalizedAlias(Text(reading, "text")));
            _ = Strings(reading, "restrictions", allowEmpty: true);
            _ = Strings(reading, "information", allowEmpty: true);
            if (reading.GetProperty("noKanji").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail("invalid reading flag");
        }
        var senses = entry.GetProperty("senses");
        if (senses.ValueKind != JsonValueKind.Array || senses.GetArrayLength() == 0) Fail("entry requires senses");
        foreach (var sense in senses.EnumerateArray()) ValidateSense(sense);
        return aliases.Select(alias => StrictUtf8.GetBytes(alias)).ToList();
    }

    private static void ValidateRestrictions(JsonElement entry)
    {
        var forms = entry.GetProperty("writtenForms").EnumerateArray().Select(f => Text(f, "text")).ToArray();
        var readings = entry.GetProperty("readings").EnumerateArray().ToArray();
        var readingTexts = readings.Select(r => NormalizeValue(Text(r, "text"))).ToHashSet(StringComparer.Ordinal);
        foreach (var reading in readings)
        {
            var restrictions = Strings(reading, "restrictions", true);
            if (reading.GetProperty("noKanji").GetBoolean() && restrictions.Count != 0 ||
                restrictions.Any(value => !ContainsNormalized(forms, value)))
                Fail("invalid reading restriction");
        }
        foreach (var sense in entry.GetProperty("senses").EnumerateArray())
            if (Strings(sense, "writtenRestrictions", true).Any(value => !ContainsNormalized(forms, value)) ||
                Strings(sense, "readingRestrictions", true).Any(value => !readingTexts.Contains(NormalizeValue(value))))
                Fail("invalid sense restriction");
    }

    private static void ValidateSense(JsonElement s)
    {
        Object(s);
        RequireFields(s, "glosses", "partsOfSpeech", "writtenRestrictions", "readingRestrictions", "notes", "fields",
            "misc", "dialects", "crossReferences", "loanwordSources", "antonyms");
        var glosses = s.GetProperty("glosses");
        if (glosses.ValueKind != JsonValueKind.Array || glosses.GetArrayLength() == 0) Fail("sense requires glosses");
        foreach (var gloss in glosses.EnumerateArray())
        {
            Object(gloss);
            RequireFields(gloss, "text", "type");
            _ = Text(gloss, "text");
            if (gloss.GetProperty("type").ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) Fail("invalid gloss type");
        }
        foreach (string field in new[] { "partsOfSpeech", "writtenRestrictions", "readingRestrictions", "notes", "fields", "misc", "dialects", "crossReferences" })
            _ = Strings(s, field, allowEmpty: true);
        _ = Strings(s, "antonyms", allowEmpty: true);
        var sources = s.GetProperty("loanwordSources");
        if (sources.ValueKind != JsonValueKind.Array) Fail("invalid loanword sources");
        foreach (var source in sources.EnumerateArray())
        {
            Object(source);
            RequireFields(source, "text", "language", "type", "wasei");
            _ = Text(source, "text", allowEmpty: true);
            _ = Text(source, "language");
            _ = Text(source, "type");
            if (source.GetProperty("wasei").ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) Fail("invalid loanword source marker");
        }
    }

    private static DictionaryEntry ParseEntry(byte[] payload, ulong expectedId, string query)
    {
        using var document = Parse(payload);
        var e = document.RootElement;
        Object(e);
        RequireFields(e, "id", "writtenForms", "readings", "senses");
        ulong id = checked((ulong)Number(e, "id"));
        if (id != expectedId) Fail("entry ID mismatch");
        var forms = e.GetProperty("writtenForms").EnumerateArray().Select(form =>
        {
            Object(form);
            RequireFields(form, "text", "information");
            return new JapaneseWrittenForm(Text(form, "text"), Freeze(Strings(form, "information", true)));
        }).ToArray();
        var readings = e.GetProperty("readings").EnumerateArray().Select(r =>
        {
            Object(r);
            RequireFields(r, "text", "restrictions", "noKanji", "information");
            return new JapaneseReading(Text(r, "text"), Freeze(Strings(r, "restrictions", true)), r.GetProperty("noKanji").GetBoolean(),
                Freeze(Strings(r, "information", true)));
        }).ToArray();
        var senses = e.GetProperty("senses").EnumerateArray().Select(s =>
        {
            ValidateSense(s);
            var glosses = s.GetProperty("glosses").EnumerateArray().Select(g => new JapaneseGloss(Text(g, "text"),
                g.GetProperty("type").ValueKind == JsonValueKind.Null ? null : g.GetProperty("type").GetString())).ToArray();
            return new JapaneseSense(Freeze(glosses), Freeze(Strings(s, "partsOfSpeech", true)),
                Freeze(Strings(s, "writtenRestrictions", true)), Freeze(Strings(s, "readingRestrictions", true)),
                Freeze(Strings(s, "notes", true)), Freeze(Strings(s, "fields", true)), Freeze(Strings(s, "misc", true)),
                Freeze(Strings(s, "dialects", true)), Freeze(Strings(s, "crossReferences", true)))
            {
                LoanwordSources = Freeze(s.GetProperty("loanwordSources").EnumerateArray().Select(source =>
                    new JapaneseLoanwordSource(Text(source, "text", allowEmpty: true), Text(source, "language"), Text(source, "type"),
                        source.GetProperty("wasei").ValueKind == JsonValueKind.Null ? null : Text(source, "wasei", allowEmpty: true))).ToArray()),
                Antonyms = Freeze(Strings(s, "antonyms", true))
            };
        }).ToArray();
        bool writtenMatch = forms.Any(form => NormalizeValue(form.Text) == query);
        var matchingReadings = readings.Where(r => NormalizeValue(r.Text) == query).ToArray();
        if (!writtenMatch && matchingReadings.Length == 0) Fail("matched alias absent from entry");
        JapaneseWrittenForm[] allowedForms;
        if (writtenMatch) allowedForms = forms.Where(form => NormalizeValue(form.Text) == query).ToArray();
        else
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reading in matchingReadings)
                if (!reading.NoKanji)
                    foreach (var form in reading.Restrictions.Count == 0 ? forms : forms.Where(form => ContainsNormalized(reading.Restrictions, form.Text)))
                        allowed.Add(form.Text);
            allowedForms = forms.Where(form => allowed.Contains(form.Text)).ToArray();
        }
        JapaneseReading[] allowedReadings = writtenMatch ? readings.Where(r => !r.NoKanji &&
            (r.Restrictions.Count == 0 || ContainsNormalized(r.Restrictions, query))).ToArray() : matchingReadings;
        bool senseAllowed(JapaneseSense s) =>
            (s.WrittenRestrictions.Count == 0 || allowedForms.Any(form => ContainsNormalized(s.WrittenRestrictions, form.Text))) &&
            (s.ReadingRestrictions.Count == 0 || allowedReadings.Any(r => ContainsNormalized(s.ReadingRestrictions, r.Text)));
        var applicableSenses = senses.Where(senseAllowed).ToArray();
        if (applicableSenses.Length == 0) Fail("no applicable senses");
        var match = new JapaneseDictionaryMatch(query, writtenMatch ? JapaneseMatchKind.WrittenForm : JapaneseMatchKind.Reading,
            Freeze(allowedForms), Freeze(allowedReadings), Freeze(applicableSenses));
        string headword = forms.FirstOrDefault()?.Text ?? readings[0].Text;
        string[] glosses = applicableSenses.SelectMany(s => s.Glosses).Select(g => g.Text).ToArray();
        string[] pos = applicableSenses.SelectMany(s => s.PartsOfSpeech).Distinct(StringComparer.Ordinal).ToArray();
        return new DictionaryEntry(headword, Freeze(glosses), pos.Length == 0 ? null : string.Join(", ", pos), null,
            "EDRDG-JMdict-e-legacy", id.ToString(System.Globalization.CultureInfo.InvariantCulture), SourceOrigin: "EDRDG JMdict")
        {
            Japanese = new JapaneseDictionaryData(Freeze(forms), Freeze(readings), Freeze(senses), match)
        };
    }

    private IDisposable AcquireLease()
    {
        lock (_leaseLock)
        {
            if (_disposing || _disposed) throw new ObjectDisposedException(nameof(IndexedJapaneseDictionaryLookup));
            _leases++;
            return new Lease(this);
        }
    }

    private sealed class Lease(IndexedJapaneseDictionaryLookup owner) : IDisposable
    {
        private IndexedJapaneseDictionaryLookup? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseLease();
    }

    private void ReleaseLease()
    {
        lock (_leaseLock)
        {
            if (--_leases == 0 && _disposing) _drained?.TrySetResult();
        }
    }

    public ValueTask DisposeAsync()
    {
        Task wait;
        lock (_leaseLock)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposing = true;
            if (_leases == 0) wait = Task.CompletedTask;
            else wait = (_drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        return new ValueTask(DisposeAfterDrainAsync(wait));
    }

    private async Task DisposeAfterDrainAsync(Task wait)
    {
        await wait.ConfigureAwait(false);
        lock (_leaseLock)
        {
            if (_disposed) return;
            _entries.Dispose(); _keys.Dispose(); _index.Dispose();
            _disposed = true;
        }
    }

    private static ulong ReadHeader(SafeFileHandle index, CancellationToken token)
    {
        byte[] header = new byte[HeaderBytes];
        ReadExactly(index, header, 0, token);
        if (!header.AsSpan(0, 8).SequenceEqual(Magic) || U32(header, 8) != 1 || U32(header, 12) != 1 || U64(header, 24) != 0)
            Fail("unsupported index header");
        return U64(header, 16);
    }

    private static async Task<IndexRow> ReadIndexRowAsync(SafeFileHandle handle, ulong index, CancellationToken token)
    {
        byte[] b = new byte[IndexRowBytes];
        await ReadExactlyAsync(handle, b, checked(HeaderBytes + (long)index * IndexRowBytes), token).ConfigureAwait(false);
        return new IndexRow(U64(b, 0), U32(b, 8), U64(b, 12), U32(b, 20), U64(b, 24));
    }

    private static async Task<int> CompareKeyAsync(SafeFileHandle keys, IndexRow row, byte[] query, CancellationToken token)
    {
        byte[] bytes = new byte[checked((int)row.KeyLength)];
        await ReadExactlyAsync(keys, bytes, checked((long)row.KeyOffset), token).ConfigureAwait(false);
        return CompareBytes(bytes, query);
    }

    private static int CompareBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        int common = Math.Min(left.Length, right.Length);
        for (int i = 0; i < common; i++) if (left[i] != right[i]) return left[i] < right[i] ? -1 : 1;
        return left.Length.CompareTo(right.Length);
    }

    private static async Task ReadExactlyAsync(SafeFileHandle handle, Memory<byte> buffer, long offset, CancellationToken token)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            int read = await RandomAccess.ReadAsync(handle, buffer[done..], checked(offset + done), token).ConfigureAwait(false);
            if (read == 0) Fail("truncated file");
            done += read;
        }
    }

    private static void VerifyMember(SafeFileHandle handle, FileInfo expected, long maximum, CancellationToken token)
    {
        long length = RandomAccess.GetLength(handle);
        if (length != expected.Bytes || length > maximum) Fail("member size mismatch");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long offset = 0;
        while (offset < length)
        {
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, length - offset);
            ReadExactlyAsync(handle, buffer.AsMemory(0, count), offset, token).GetAwaiter().GetResult();
            hash.AppendData(buffer, 0, count);
            offset += count;
        }
        if (!HashEquals(Convert.ToHexString(hash.GetHashAndReset()), expected.Hash)) Fail("member hash mismatch");
    }

    private static string Member(string root, string name)
    {
        if (!AllowedFiles.Contains(name) && name != "manifest.json") Fail("invalid pack member");
        string path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Fail("invalid pack member");
        RejectReparse(path);
        return path;
    }

    private static void RejectReparse(string path)
    {
        var ancestors = new Stack<string>();
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
        foreach (string current in ancestors)
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) Fail("reparse point");
    }

    private static SafeFileHandle Open(string path)
    {
        RejectReparse(path);
        return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
    }

    private static byte[] ReadAllSmall(SafeFileHandle handle, int maximum, CancellationToken token)
    {
        long length = RandomAccess.GetLength(handle);
        if (length < 0 || length > maximum) Fail("metadata size limit");
        byte[] result = new byte[(int)length];
        ReadExactlyAsync(handle, result, 0, token).GetAwaiter().GetResult();
        return result;
    }

    private static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(StrictUtf8.GetString(bytes), new JsonDocumentOptions { MaxDepth = 32 });
    private static void Object(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) Fail("invalid JSON object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in e.EnumerateObject()) if (!names.Add(property.Name)) Fail("duplicate JSON property");
    }
    private static void RequireFields(JsonElement e, params string[] fields)
    {
        var expected = fields.ToHashSet(StringComparer.Ordinal);
        if (e.EnumerateObject().Count() != expected.Count || e.EnumerateObject().Any(p => !expected.Contains(p.Name)))
            Fail("unsupported JSON schema");
    }
    private static string Text(JsonElement e, string field, bool allowEmpty = false)
    {
        var value = e.GetProperty(field);
        if (value.ValueKind != JsonValueKind.String || (!allowEmpty && string.IsNullOrWhiteSpace(value.GetString()))) Fail("invalid text value");
        string text = value.GetString()!;
        if (text.Length > MaximumEntryBytes) Fail("text value limit");
        _ = StrictUtf8.GetByteCount(text);
        return text;
    }
    private static List<string> Strings(JsonElement e, string field, bool allowEmpty)
    {
        var array = e.GetProperty(field);
        if (array.ValueKind != JsonValueKind.Array) Fail("invalid string list");
        var result = new List<string>(array.GetArrayLength());
        foreach (var value in array.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String) Fail("invalid string list value");
            string item = value.GetString()!;
            if (string.IsNullOrWhiteSpace(item) || item.Length > MaximumEntryBytes) Fail("invalid string list value");
            _ = StrictUtf8.GetByteCount(item);
            result.Add(item);
        }
        return result;
    }
    private static string NormalizedAlias(string value)
    {
        if (value.Length > 2000 || string.IsNullOrWhiteSpace(value)) Fail("invalid alias");
        string normalized = value.Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0 || normalized.Length > 2000) Fail("invalid alias");
        return normalized;
    }
    private static string NormalizeValue(string value) => value.Normalize(NormalizationForm.FormC);
    private static bool ContainsNormalized(IReadOnlyList<string> values, string value) =>
        values.Any(candidate => NormalizeValue(candidate) == NormalizeValue(value));
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
    private static long Number(JsonElement e, string field)
    {
        if (!e.GetProperty(field).TryGetInt64(out long value) || value < 0) Fail("invalid number");
        return value;
    }
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));
    private static void ValidateHash(string hash)
    {
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) Fail("invalid SHA-256");
    }
    private static void VerifyHash(byte[] bytes, string expected)
    {
        ValidateHash(expected);
        if (!HashEquals(Convert.ToHexString(SHA256.HashData(bytes)), expected)) Fail("manifest digest mismatch");
    }
    private static bool HashEquals(string actual, string expected) => StringComparer.OrdinalIgnoreCase.Equals(actual, expected);
    private static void Fail(string category) => throw new DictionaryDataException(category);
}
