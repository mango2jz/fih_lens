using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FihLens.Infrastructure.Dictionary;

namespace FihLens.Tests.Infrastructure;

public sealed class DictionaryLookupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FihLens-dictionary-test-" + Guid.NewGuid());
    public DictionaryLookupTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ReaderPreservesArticlesSensesAndOriginalSpellingWithDotNetMatching()
    {
        byte[] data = Encoding.UTF8.GetBytes(Row("cafe\u0301 au lait", "first") + "\r\n" +
            Row("CAFÉ AU LAIT", "second") + "\n" + Row("Straße", "third"));
        var (entries, count) = await Read(data);
        Assert.Equal(3, count);
        Assert.Equal(2, entries.Count);
        var group = entries["Café au lait"];
        Assert.Equal(2, group.Count);
        Assert.Equal("cafe\u0301 au lait", group[0].Headword);
        Assert.Equal(new[] { "nghĩa một", "nghĩa một", "nghĩa hai" }, group[0].Senses);
        Assert.Equal("n", group[0].PartOfSpeech);
        Assert.Equal("example", group[0].Example);
        Assert.Equal("first", group[0].SourceArticleId);
        Assert.Equal("wordnet+llm", group[0].SourceOrigin);
        Assert.False(entries.ContainsKey("café"));
        Assert.False(entries.ContainsKey("STRASSE"));
    }

    [Fact]
    public async Task ApostrophesHyphensAndWholePhrasesAreExactKeys()
    {
        var (entries, _) = await Read(Encoding.UTF8.GetBytes(Row("don't", "1") + "\n" +
            Row("well-being", "2") + "\n" + Row(" take off ", "3")));
        Assert.True(entries.ContainsKey("DON'T"));
        Assert.True(entries.ContainsKey("well-being"));
        Assert.True(entries.ContainsKey("take off"));
        Assert.False(entries.ContainsKey("well being"));
        Assert.False(entries.ContainsKey("take"));
        Assert.Equal(" take off ", entries["take off"][0].Headword);
    }

    [Theory]
    [InlineData("headword", "")]
    [InlineData("headword", "   ")]
    [InlineData("sourceId", "unknown")]
    [InlineData("sourceArticleId", "")]
    [InlineData("sourceOrigin", "")]
    [InlineData("partOfSpeech", " ")]
    [InlineData("example", " ")]
    [InlineData("sourceLanguage", "vi")]
    [InlineData("targetLanguage", "en")]
    public async Task InvalidRowFieldsFailWithoutPayload(string field, string value)
    {
        var row = JsonSerializer.Deserialize<Dictionary<string, object>>(Row("private captured text", "id"))!;
        row[field] = value;
        await Reject(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row)));
    }

    [Theory]
    [InlineData("{bad json}")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"headword\":\"a\",\"headword\":\"b\"}")]
    [InlineData("{\"headword\":\"\\ud800\"}")]
    public async Task MalformedSchemaFailsSafely(string json) => await Reject(Encoding.UTF8.GetBytes(json));

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"\"]")]
    [InlineData("[null]")]
    [InlineData("[1]")]
    [InlineData("null")]
    public async Task InvalidSensesFailSafely(string senses) =>
        await Reject(Encoding.UTF8.GetBytes(Row("private captured text", "id").Replace(
            JsonSerializer.Serialize(new[] { "nghĩa một", "nghĩa một", "nghĩa hai" }), senses)));

    [Fact]
    public async Task UnsupportedRowVersionAndConflictingArticleIdFail()
    {
        await Reject(Encoding.UTF8.GetBytes(Row("private captured text", "id").Replace("{", "{\"schemaVersion\":2,")));
        await Reject(Encoding.UTF8.GetBytes(Row("a", "id") + "\n" + Row("b", "id")), 2);
    }

    [Fact]
    public async Task InvalidUtf8AndOversizedRowAreRejected()
    {
        await Reject([0xc3, 0x28]);
        var error = await Assert.ThrowsAsync<DictionaryDataException>(() => Read(new byte[JsonlDictionaryLookup.MaximumRowBytes + 1]));
        Assert.Equal("row size limit", error.Category);
    }

    [Fact]
    public async Task ExactRowBoundaryIsAccepted()
    {
        byte[] json = Encoding.UTF8.GetBytes(Row("a", "id"));
        byte[] data = new byte[JsonlDictionaryLookup.MaximumRowBytes];
        Array.Fill(data, (byte)' ');
        json.CopyTo(data, 0);
        var result = await Read(data);
        Assert.Equal(1, result.Rows);
    }

    [Fact]
    public async Task HashSizeAndFileLimitsAreEnforced()
    {
        byte[] data = Encoding.UTF8.GetBytes(Row("a", "id"));
        string path = Path.Combine(_root, "dictionary.jsonl");
        await File.WriteAllBytesAsync(path, data);
        await Assert.ThrowsAsync<DictionaryDataException>(() => JsonlDictionaryLookup.ReadEntriesAsync(path, data.Length, "bad", default));
        await Assert.ThrowsAsync<DictionaryDataException>(() => JsonlDictionaryLookup.ReadEntriesAsync(path, data.Length + 1, Hash(data), default));
        await Assert.ThrowsAsync<DictionaryDataException>(() => JsonlDictionaryLookup.ReadEntriesAsync(path, JsonlDictionaryLookup.MaximumFileBytes + 1, "bad", default));
        using (var file = new FileStream(path, FileMode.Open)) file.SetLength(JsonlDictionaryLookup.MaximumFileBytes + 1);
        await Assert.ThrowsAsync<DictionaryDataException>(() => JsonlDictionaryLookup.ReadEntriesAsync(path, data.Length, Hash(data), default));
        JsonlDictionaryLookup.CheckRowCount(JsonlDictionaryLookup.MaximumRows);
        Assert.Throws<DictionaryDataException>(() => JsonlDictionaryLookup.CheckRowCount(JsonlDictionaryLookup.MaximumRows + 1));
    }

    [Fact]
    public async Task CancellationBeforeLoadAndDuringReadPublishesNoIndex()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => JsonlDictionaryLookup.LoadAsync(_root, new CancellationToken(true)));
        byte[] data = new byte[JsonlDictionaryLookup.MaximumRowBytes];
        Array.Fill(data, (byte)' ');
        Encoding.UTF8.GetBytes(Row("a", "id")).CopyTo(data, 0);
        string path = Path.Combine(_root, "dictionary.jsonl");
        await File.WriteAllBytesAsync(path, data);
        using var cts = new CancellationTokenSource();
        var pending = JsonlDictionaryLookup.ReadEntriesAsync(path, data.Length, Hash(data), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("D:relative")]
    [InlineData("\\relative")]
    [InlineData("\\\\server\\share")]
    [InlineData("\\\\?\\D:\\assets")]
    [InlineData("\\\\.\\D:\\assets")]
    [InlineData("https://example.com")]
    [InlineData("file:///D:/assets")]
    [InlineData("D:\\folder\\..\\assets")]
    public async Task InvalidRootFailsBeforeOpeningFiles(string root)
    {
        var error = await Assert.ThrowsAsync<DictionaryDataException>(() => JsonlDictionaryLookup.LoadAsync(root));
        Assert.Equal("invalid local root", error.Category);
        Assert.DoesNotContain(root, error.Message);
    }

    [Theory]
    [InlineData("../dictionary.jsonl")]
    [InlineData("nested/../dictionary.jsonl")]
    [InlineData("D:\\dictionary.jsonl")]
    [InlineData("/dictionary.jsonl")]
    [InlineData("\\\\server\\share")]
    [InlineData("dictionary.jsonl:stream")]
    [InlineData("nested//dictionary.jsonl")]
    [InlineData(".. /dictionary.jsonl")]
    [InlineData("NUL")]
    public void ManifestMembersCannotEscapeRoot(string member) =>
        Assert.Throws<DictionaryDataException>(() => JsonlDictionaryLookup.Member(_root, member));

    [Theory]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.Unknown)]
    [InlineData(DriveType.NoRootDirectory)]
    [InlineData(DriveType.CDRom)]
    public void NonlocalDriveTypesAreRejected(DriveType driveType) =>
        Assert.Throws<DictionaryDataException>(() => JsonlDictionaryLookup.CheckDriveType(driveType));

    [Fact]
    public async Task GeneratedUnreviewedFixtureCannotBecomeApprovedData()
    {
        var manifest = new
        {
            schemaVersion = 1, sourceId = "thichhoc-en-vi-v0.9.0", revision = "ec3fa199aebdc4da4ebcaa7a11ac4b9c44046f75",
            license = "CC-BY-SA-4.0", qualityStatus = "generated/unreviewed fixture"
        };
        await File.WriteAllTextAsync(Path.Combine(_root, "dictionary-manifest.json"), JsonSerializer.Serialize(manifest));
        var error = await Assert.ThrowsAsync<DictionaryDataException>(() => JsonlDictionaryLookup.LoadAsync(_root));
        Assert.Equal("unapproved source status", error.Category);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"sourceId\":\"fixture\",\"revision\":\"fake\"}")]
    [InlineData("{\"schemaVersion\":1,\"sourceId\":\"thichhoc-en-vi-v0.9.0\",\"revision\":\"ec3fa199aebdc4da4ebcaa7a11ac4b9c44046f75\",\"sourceLanguage\":\"vi\"}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("bad json")]
    public async Task InvalidManifestCannotPublishReadyDictionary(string json)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "dictionary-manifest.json"), json);
        var error = await Assert.ThrowsAsync<DictionaryDataException>(() => JsonlDictionaryLookup.LoadAsync(_root));
        Assert.DoesNotContain(_root, error.Message);
        Assert.Null(error.InnerException);
    }

    private async Task Reject(byte[] bytes, int row = 1)
    {
        var error = await Assert.ThrowsAsync<DictionaryDataException>(() => Read(bytes));
        Assert.Equal(row, error.Row);
        Assert.DoesNotContain("private captured text", error.Message);
        Assert.Null(error.InnerException);
    }
    private async Task<(Dictionary<string, List<FihLens.Core.Models.DictionaryEntry>> Entries, int Rows)> Read(byte[] data)
    {
        string path = Path.Combine(_root, "dictionary.jsonl");
        await File.WriteAllBytesAsync(path, data);
        return await JsonlDictionaryLookup.ReadEntriesAsync(path, data.Length, Hash(data), default);
    }
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static string Row(string headword, string id) => JsonSerializer.Serialize(new
    {
        headword, senses = new[] { "nghĩa một", "nghĩa một", "nghĩa hai" }, partOfSpeech = "n", example = "example",
        sourceId = "thichhoc-en-vi-v0.9.0", sourceArticleId = id, sourceOrigin = "wordnet+llm"
    });
}
