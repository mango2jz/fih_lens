using FihLens.Application.Dictionary;
using FihLens.Core.Enums;
using FihLens.Core.Models;

namespace FihLens.Tests.Application;

public sealed class DictionaryLookupRouterTests
{
    [Fact]
    public async Task RoutesOnlyTheRegisteredDirectionAndRejectsMismatchedMetadata()
    {
        var japanese = Source("ja", "en");
        var english = Source("en", "vi");
        var jaLookup = new Lookup(japanese);
        var enLookup = new Lookup(english);
        var router = new DictionaryLookupRouter();
        router.SetAvailability(Language.Japanese, Language.English, new(jaLookup), japanese);
        router.SetAvailability(Language.English, Language.Vietnamese, new(enLookup), english);
        Assert.Same(japanese, (await router.LookupAsync("生", Language.Japanese, Language.English)).Source);
        Assert.Equal(1, jaLookup.Calls);
        Assert.Equal(0, enLookup.Calls);
        Assert.Null(router.GetAvailability(Language.Japanese, Language.Vietnamese).Service);
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.LookupAsync("生", Language.Japanese, Language.Vietnamese));
        Assert.Throws<ArgumentException>(() => router.SetAvailability(Language.Japanese, Language.English, new(enLookup), english));
    }

    private static DictionarySource Source(string from, string to) =>
        new("fixture", "Fixture", "1", from, to, "fixture", "https://example.test", "test", "fixture", "fixture", new Dictionary<string, string>());

    private sealed class Lookup(DictionarySource source) : IDictionaryLookup
    {
        public int Calls { get; private set; }
        public Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new DictionaryLookupResult(input, source, []));
        }
    }
}
