using FihLens.Application.Dictionary;
using FihLens.Core.Models;

namespace FihLens.Tests.Application;

public sealed class DictionaryWordsTests
{
    [Fact]
    public async Task OcrShapedTextMatchesContainedHeadwords()
    {
        var lookup = new HeadwordLookup("run", "every", "morning", "don't", "well-known");
        var result = await new DictionaryLookupService(lookup)
            .LookupWordsAsync("Tom can run,\r\nevery well-known morning. \"Don't\"", splitWords: true);

        Assert.False(result.Phrase.Result.IsMatch);
        Assert.Equal(["Tom", "can", "run", "every", "well-known", "morning", "Don't"], result.Words.Select(word => word.Word.Text));
        Assert.Equal(["run", "every", "well-known", "morning", "Don't"],
            result.Words.Where(word => word.Result.IsMatch).Select(word => word.Word.Text));
        var run = result.Words[2].Word;
        Assert.Equal((",", true), (run.Trailing, run.LineBreakAfter));
        Assert.Equal(".", result.Words[5].Word.Trailing);
        Assert.Equal(("\"", "\""), (result.Words[6].Word.Leading, result.Words[6].Word.Trailing));
    }

    [Theory]
    [InlineData("a piece of cake")]
    [InlineData("a piece of cake!")]
    [InlineData("“a piece\nof cake”")]
    public async Task WholePhraseMatchesWithPunctuationAndLineBreaksTrimmed(string input)
    {
        var lookup = new HeadwordLookup("a piece of cake");
        var result = await new DictionaryLookupService(lookup).LookupWordsAsync(input, splitWords: true);

        Assert.True(result.Phrase.Result.IsMatch);
        Assert.Equal(input, result.Phrase.Result.OriginalText);
        Assert.Equal("a piece of cake", result.Phrase.Word.Text);
        Assert.Equal(4, result.Words.Count);
    }

    [Fact]
    public async Task UnsplitTextIsOneCandidateWithoutASecondLookup()
    {
        var lookup = new HeadwordLookup("食べる");
        var result = await new DictionaryLookupService(lookup).LookupWordsAsync("「食べる」", splitWords: false);

        var word = Assert.Single(result.Words);
        Assert.Same(result.Phrase, word);
        Assert.Equal(("食べる", "「", "」"), (word.Word.Text, word.Word.Leading, word.Word.Trailing));
        Assert.True(word.Result.IsMatch);
        Assert.Equal(["「食べる」", "食べる"], lookup.Inputs);
    }

    [Fact]
    public async Task RepeatedWordsAreLookedUpOnce()
    {
        var lookup = new HeadwordLookup("run");
        await new DictionaryLookupService(lookup).LookupWordsAsync("run, run, run", splitWords: true);
        Assert.Equal(["run, run, run", "run"], lookup.Inputs);
    }

    [Theory]
    [InlineData("...")]
    [InlineData("— !")]
    public void PunctuationOnlyHasNoCandidates(string text)
    {
        Assert.Empty(DictionaryWords.Split(text));
        Assert.Null(DictionaryWords.Phrase(text));
    }

    private sealed class HeadwordLookup(params string[] headwords) : IDictionaryLookup
    {
        private static readonly DictionarySource Source = new("fixture", "Fixture", "test", "en", "vi", "fixture", "fixture",
            "fixture", "synthetic test data", "unreviewed fixture", new Dictionary<string, string>());
        public List<string> Inputs { get; } = [];
        public Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
        {
            Inputs.Add(input);
            string key = DictionaryLookupService.NormalizeInput(input);
            DictionaryEntry[] entries = headwords.Contains(key, StringComparer.OrdinalIgnoreCase)
                ? [new DictionaryEntry(key, ["nghĩa"], null, null, "fixture", "article", "origin")] : [];
            return Task.FromResult(new DictionaryLookupResult(input, Source, entries));
        }
    }
}
