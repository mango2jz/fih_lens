using FihLens.Application.Pronunciation;
using FihLens.Core.Enums;

namespace FihLens.Tests.Application;

public sealed class PronunciationInputTests
{
    [Theory]
    [InlineData(Language.English)]
    [InlineData(Language.Vietnamese)]
    [InlineData(Language.Japanese)]
    public void AcceptsSupportedLanguageAndExactly2000Characters(Language language)
    {
        Assert.Null(PronunciationInput.GetUnavailableReason(new string('a', 2000), language));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsBlankText(string text)
    {
        Assert.NotNull(PronunciationInput.GetUnavailableReason(text, Language.English));
    }

    [Fact]
    public void RejectsTextOverLimitAndMalformedUnicode()
    {
        Assert.NotNull(PronunciationInput.GetUnavailableReason(new string('a', 2001), Language.English));
        Assert.NotNull(PronunciationInput.GetUnavailableReason("\ud800", Language.Japanese));
    }

    [Theory]
    [InlineData(Language.Auto)]
    [InlineData((Language)int.MaxValue)]
    public void RejectsNonConcreteLanguage(Language language)
    {
        Assert.NotNull(PronunciationInput.GetUnavailableReason("word", language));
    }
}
