using FihLens.Application.Dictionary;
using FihLens.Core.Models;

namespace FihLens.Tests.Application;

public sealed class DictionaryLookupServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  \t")]
    public async Task InvalidInputDoesNotReachCapability(string input)
    {
        var port = new RecordingLookup();
        await Assert.ThrowsAsync<ArgumentException>(() => new DictionaryLookupService(port).LookupAsync(input));
        Assert.Equal(0, port.Calls);
    }

    [Fact]
    public async Task InvalidUtf16DoesNotReachCapability()
    {
        var port = new RecordingLookup();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DictionaryLookupService(port).LookupAsync(new string((char)0xd800, 1)));
        Assert.Equal(0, port.Calls);
    }

    [Fact]
    public async Task LengthBoundariesAreCheckedBeforeCapability()
    {
        var port = new RecordingLookup();
        var service = new DictionaryLookupService(port);
        await Assert.ThrowsAsync<ArgumentException>(() => service.LookupAsync(new string('x', 2_001)));
        Assert.Equal(0, port.Calls);
        await service.LookupAsync(new string('x', 2_000));
        Assert.Equal(1, port.Calls);
    }

    [Fact]
    public async Task CancellationDoesNotReachCapability()
    {
        var port = new RecordingLookup();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DictionaryLookupService(port).LookupAsync("word", new CancellationToken(true)));
        Assert.Equal(0, port.Calls);
    }

    [Fact]
    public async Task PreservesWholeOriginalSnapshotAndResult()
    {
        var port = new RecordingLookup();
        const string input = "  Cafe\u0301 au lait  ";
        var result = await new DictionaryLookupService(port).LookupAsync(input);
        Assert.Equal(input, port.Input);
        Assert.Equal(input, result.OriginalText);
        Assert.Equal("Café au lait", DictionaryLookupService.NormalizeInput(input));
        Assert.False(result.IsMatch);
    }

    private sealed class RecordingLookup : IDictionaryLookup
    {
        public int Calls { get; private set; }
        public string? Input { get; private set; }
        public Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
        {
            Calls++;
            Input = input;
            return Task.FromResult(new DictionaryLookupResult(input,
                new DictionarySource("fixture", "Fixture", "test", "en", "vi", "fixture", "fixture",
                    "fixture", "synthetic test data", "unreviewed fixture", new Dictionary<string, string>()), []));
        }
    }
}
