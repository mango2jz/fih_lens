using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Core.ValueObjects;

namespace FihLens.Tests.Core;

public class TranslationRequestTests
{
    public static IEnumerable<object[]> CompatibilityCases()
    {
        var cefr = new CefrLearningLevel(CefrLevel.B1);
        var jlpt = new JlptLearningLevel(JlptLevel.N3);
        var unsupported = new UnsupportedLearningLevel();
        foreach (var (source, level, accepted) in new (Language, LearningLevel?, bool)[]
        {
            (Language.Auto, null, true), (Language.Auto, cefr, true), (Language.Auto, jlpt, true), (Language.Auto, unsupported, false),
            (Language.English, null, true), (Language.English, cefr, true), (Language.English, jlpt, false), (Language.English, unsupported, false),
            (Language.Vietnamese, null, true), (Language.Vietnamese, cefr, false), (Language.Vietnamese, jlpt, false), (Language.Vietnamese, unsupported, false),
            (Language.Japanese, null, true), (Language.Japanese, cefr, false), (Language.Japanese, jlpt, true), (Language.Japanese, unsupported, false)
        })
        {
            yield return [source, level!, accepted];
        }
    }

    [Theory]
    [MemberData(nameof(CompatibilityCases))]
    public void Constructor_EnforcesLearningLevelCompatibility(Language source, LearningLevel? level, bool accepted)
    {
        if (accepted)
        {
            Assert.Equal(level, new TranslationRequest("hello", source, Language.Vietnamese, level).UserLevel);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => new TranslationRequest("hello", source, Language.Vietnamese, level));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Constructor_RejectsNullOrBlankText(string? text)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TranslationRequest(text!, Language.English, Language.Japanese));
    }

    [Fact]
    public void Constructor_PreservesTextAndUsesDefaults()
    {
        const string text = "  hello  ";
        var request = new TranslationRequest(text, Language.English, Language.Japanese);

        Assert.Equal(text, request.Text);
        Assert.Null(request.UserLevel);
        Assert.Equal(TranslationMode.Translate, request.Mode);
    }

    [Fact]
    public void Constructor_AllowsAutoSourceAndEqualConcreteLanguages()
    {
        Assert.Equal(Language.Auto, new TranslationRequest("hello", Language.Auto, Language.Japanese).SourceLanguage);
        Assert.Equal(Language.English, new TranslationRequest("hello", Language.English, Language.English).TargetLanguage);
    }

    [Theory]
    [InlineData((Language)int.MaxValue, Language.Japanese)]
    [InlineData(Language.English, (Language)int.MaxValue)]
    [InlineData(Language.English, Language.Auto)]
    public void Constructor_RejectsInvalidLanguages(Language source, Language target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranslationRequest("hello", source, target));
    }

    [Theory]
    [InlineData((TranslationMode)int.MaxValue)]
    public void Constructor_RejectsUndefinedMode(TranslationMode mode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranslationRequest("hello", Language.English, Language.Japanese, mode: mode));
    }

    private sealed record UnsupportedLearningLevel : LearningLevel;
}
