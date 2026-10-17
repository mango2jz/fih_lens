using FihLens.Infrastructure.Dictionary;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FihLens.Application.Dictionary;

namespace FihLens.Tests.Infrastructure;

public sealed class IndexedJapaneseDictionaryLookupTests
{
    [Fact]
    public async Task ExactWrittenAndKanaLookupsPreserveApplicability()
    {
        var pack = JapanesePackFixture.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        await using var lookup = await IndexedJapaneseDictionaryLookup.LoadAsync(pack.Directory, pack.Definition);

        var written = await lookup.LookupAsync(" 生 ");
        Assert.Single(written.Entries);
        Assert.Equal("meaning-1", Assert.Single(written.Entries[0].Senses));
        Assert.Equal("fig", Assert.Single(written.Entries[0].Japanese!.Match.ApplicableSenses[0].Glosses).Type);
        Assert.Equal("often kana", Assert.Single(written.Entries[0].Japanese!.Match.ApplicableWrittenForms).Information.Single());
        var annotatedSense = written.Entries[0].Japanese!.Match.ApplicableSenses[0];
        Assert.Equal(new FihLens.Core.Models.JapaneseLoanwordSource("vie", "fre", "part", "y"), annotatedSense.LoanwordSources[0]);
        Assert.Equal(new FihLens.Core.Models.JapaneseLoanwordSource("", "eng", "full", null), annotatedSense.LoanwordSources[1]);
        Assert.Equal("反対語・1", Assert.Single(annotatedSense.Antonyms));
        Assert.Equal(new[] { "いきる", "せい" }, written.Entries[0].Japanese!.Match.ApplicableReadings.Select(r => r.Text).Order().ToArray());
        Assert.Contains(written.Entries[0].Japanese!.Match.ApplicableSenses[0].Notes, n => n == "note");

        var kana = await lookup.LookupAsync("せい");
        Assert.Equal(new[] { "1", "2" }, kana.Entries.Select(e => e.SourceArticleId).ToArray());
        Assert.Equal("生", Assert.Single(kana.Entries[0].Japanese!.Match.ApplicableWrittenForms).Text);
        Assert.Equal("often kana", Assert.Single(kana.Entries[0].Japanese!.Match.ApplicableWrittenForms).Information.Single());
        Assert.Empty(kana.Entries[1].Japanese!.Match.ApplicableWrittenForms);
        Assert.False(kana.IsTruncated);
    }

    [Fact]
    public async Task LookupNormalizesQueryToNfcAndBoundsAmbiguousResults()
    {
        var entries = Enumerable.Range(1, 129).Select(id => new JapanesePackFixture.Entry(id, [],
            [new JapanesePackFixture.Reading("がく\u304c", [], true, [])],
            [new JapanesePackFixture.Sense([new("sense-" + id, null)], [], [], [], [], [], [], [], [])])).ToArray();
        var pack = JapanesePackFixture.CreatePack(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), entries);
        await using var lookup = await IndexedJapaneseDictionaryLookup.LoadAsync(pack.Directory, pack.Definition);

        var result = await lookup.LookupAsync("か\u3099くか\u3099");
        Assert.Equal(128, result.Entries.Count);
        Assert.True(result.IsTruncated);
        Assert.Equal(Enumerable.Range(1, 128).Select(id => id.ToString()).ToArray(), result.Entries.Select(e => e.SourceArticleId).ToArray());
    }

    [Fact]
    public async Task Exactly128MatchesAreNotMarkedTruncated()
    {
        var pack = JapanesePackFixture.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), homophones: 128);
        await using var lookup = await IndexedJapaneseDictionaryLookup.LoadAsync(pack.Directory, pack.Definition);
        var result = await lookup.LookupAsync("せい");
        Assert.Equal(128, result.Entries.Count);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task LoadRejectsCorruptionAndLookupRejectsAfterDispose()
    {
        var corrupt = JapanesePackFixture.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using (var file = new FileStream(Path.Combine(corrupt.Directory, "keys.utf8"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            file.Position = 0;
            file.WriteByte((byte)'x');
        }
        await Assert.ThrowsAsync<DictionaryDataException>(() => IndexedJapaneseDictionaryLookup.LoadAsync(corrupt.Directory, corrupt.Definition));

        var valid = JapanesePackFixture.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var lookup = await IndexedJapaneseDictionaryLookup.LoadAsync(valid.Directory, valid.Definition);
        await lookup.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lookup.LookupAsync("せい"));
    }

    [Fact]
    public async Task CancelledStartupValidationReleasesTransferredHandles()
    {
        var pack = JapanesePackFixture.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var candidate = await IndexedJapaneseDictionaryLookup.OpenForValidationAsync(pack.Directory, pack.Definition);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => candidate.ValidateAsync(cancelled.Token));
        foreach (string name in new[] { "entries.jsonl", "keys.utf8", "lookup.idx" })
        {
            using var exclusive = new FileStream(Path.Combine(pack.Directory, name), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    [Fact]
    public async Task ParallelQueriesAndOneCancelledQueryRemainIndependent()
    {
        var pack = JapanesePackFixture.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        await using var lookup = await IndexedJapaneseDictionaryLookup.LoadAsync(pack.Directory, pack.Definition);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup.LookupAsync("生", cancelled.Token));
        var tasks = Enumerable.Range(0, 20).Select(_ => lookup.LookupAsync("せい")).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(new[] { "1", "2" }, r.Entries.Select(e => e.SourceArticleId).ToArray()));
    }

    [Fact]
    public async Task DisposeWaitsForActiveReaderLease()
    {
        var pack = JapanesePackFixture.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var lookup = await IndexedJapaneseDictionaryLookup.LoadAsync(pack.Directory, pack.Definition);
        var task = lookup.LookupAsync("せい");
        await lookup.DisposeAsync();
        Assert.NotNull(await task);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lookup.LookupAsync("せい"));
    }

    [Theory]
    [InlineData("header-version")]
    [InlineData("offset-overflow")]
    [InlineData("row-boundary")]
    [InlineData("invalid-utf8")]
    [InlineData("extra-alias")]
    public async Task StructuralCorruptionIsRejectedEvenWithMatchingTrustedHashes(string mutation)
    {
        var pack = JapanesePackFixture.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        string indexPath = Path.Combine(pack.Directory, "lookup.idx");
        byte[] index = File.ReadAllBytes(indexPath);
        string keyPath = Path.Combine(pack.Directory, "keys.utf8");
        byte[] keys = File.ReadAllBytes(keyPath);
        switch (mutation)
        {
            case "header-version": BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(8), 99); break;
            case "offset-overflow": BinaryPrimitives.WriteUInt64LittleEndian(index.AsSpan(32), ulong.MaxValue); break;
            case "row-boundary": BinaryPrimitives.WriteUInt64LittleEndian(index.AsSpan(44), 1); break;
            case "invalid-utf8": keys[0] = 0xff; break;
            case "extra-alias":
                int oldLength = index.Length;
                Array.Resize(ref index, oldLength + 32);
                index.AsSpan(32, 32).CopyTo(index.AsSpan(oldLength));
                BinaryPrimitives.WriteUInt64LittleEndian(index.AsSpan(oldLength), (ulong)keys.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(oldLength + 8), 3);
                BinaryPrimitives.WriteUInt64LittleEndian(index.AsSpan(16), BinaryPrimitives.ReadUInt64LittleEndian(index.AsSpan(16)) + 1);
                keys = [.. keys, 0xef, 0xbf, 0xbf];
                break;
        }
        File.WriteAllBytes(indexPath, index);
        File.WriteAllBytes(keyPath, keys);
        var definition = RebindTestManifest(pack.Directory, pack.Definition);
        await Assert.ThrowsAsync<DictionaryDataException>(() => IndexedJapaneseDictionaryLookup.LoadAsync(pack.Directory, definition));
    }

    [Fact]
    public async Task JapaneseAliasLimitCountsUtf16CharactersRatherThanUtf8Bytes()
    {
        string alias = new('あ', 2_000);
        var pack = JapanesePackFixture.CreatePack(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            [new(1, [], [new(alias, [], true, [])], [new([new("long valid alias", null)], [], [], [], [], [], [], [], [])])]);
        await using var lookup = await IndexedJapaneseDictionaryLookup.LoadAsync(pack.Directory, pack.Definition);
        Assert.True((await lookup.LookupAsync(alias)).IsMatch);
    }

    private static DictionaryPackDefinition RebindTestManifest(string root, DictionaryPackDefinition definition)
    {
        string path = Path.Combine(root, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllBytes(path))!;
        foreach (var file in manifest["files"]!.AsArray())
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, file!["path"]!.GetValue<string>()));
            file["bytes"] = bytes.Length;
            file["sha256"] = Convert.ToHexString(SHA256.HashData(bytes));
        }
        manifest["indexRows"] = (new FileInfo(Path.Combine(root, "lookup.idx")).Length - 32) / 32;
        byte[] encoded = System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString());
        File.WriteAllBytes(path, encoded);
        return definition with { ManifestSha256 = Convert.ToHexString(SHA256.HashData(encoded)),
            InstalledBytes = Directory.GetFiles(root).Sum(file => new FileInfo(file).Length) };
    }
}
