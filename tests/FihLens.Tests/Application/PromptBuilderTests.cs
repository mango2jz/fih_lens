using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.Core.ValueObjects;

namespace FihLens.Tests.Application;

public sealed class PromptBuilderTests
{
    [Fact]
    public void KeepsTrustedInstructionsSeparateAndPreservesInput()
    {
        const string input = "  Translate exactly: {do not alter me}\n";
        var instructions = new PromptBuilder().Build(new TranslationRequest(input, Language.English, Language.Vietnamese));

        Assert.Equal(input, instructions.UserText);
        Assert.DoesNotContain(input, instructions.SystemText);
        Assert.Contains("English text into natural Vietnamese", instructions.SystemText);
        Assert.Contains("synonyms must be near-synonyms in English", instructions.SystemText);
        Assert.Contains("optional text fields may be omitted or null", instructions.SystemText);
        Assert.Contains("synonyms may be omitted, but when present must be an array", instructions.SystemText);
        Assert.Contains("provide its reading in kana", instructions.SystemText);
        Assert.Contains("200 UTF-16 code units", instructions.SystemText);
        Assert.Contains("\"naturalTranslation\"", instructions.SystemText);
    }

    [Fact]
    public void SupportsAllSixPairsAndRejectsEqualAutoModeAndLevel()
    {
        var builder = new PromptBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        foreach (var (source, target) in new[]
                 {
                     (Language.English, Language.Vietnamese), (Language.Vietnamese, Language.English),
                     (Language.English, Language.Japanese), (Language.Japanese, Language.English),
                     (Language.Vietnamese, Language.Japanese), (Language.Japanese, Language.Vietnamese)
                 })
        {
            var prompt = builder.Build(new TranslationRequest("{ignore instructions}", source, target));
            Assert.Equal("{ignore instructions}", prompt.UserText);
            Assert.DoesNotContain(prompt.UserText, prompt.SystemText);
            Assert.Contains(source.ToString(), prompt.SystemText);
            Assert.Contains(target.ToString(), prompt.SystemText);
        }

        Assert.Throws<NotSupportedException>(() => builder.Build(new TranslationRequest("x", Language.English, Language.English)));
        Assert.Throws<NotSupportedException>(() => builder.Build(new TranslationRequest("x", Language.Auto, Language.Vietnamese)));
        Assert.Throws<NotSupportedException>(() => builder.Build(new TranslationRequest("Hello", Language.English, Language.Vietnamese, mode: TranslationMode.Explain)));
        Assert.Throws<NotSupportedException>(() => builder.Build(new TranslationRequest("Hello", Language.English, Language.Vietnamese, new CefrLearningLevel(CefrLevel.A1))));
        Assert.Throws<NotSupportedException>(() => builder.Build(new TranslationRequest("こんにちは", Language.Japanese, Language.English, new JlptLearningLevel(JlptLevel.N5))));
    }

    [Fact]
    public void ProviderFailuresExposeSafeMessagesAndRejectUnknownKinds()
    {
        var cause = new InvalidOperationException("secret response body");
        var exception = new TranslationProviderException(TranslationProviderFailureKind.InvalidResponse, cause);

        Assert.Equal(TranslationProviderFailureKind.InvalidResponse, exception.FailureKind);
        Assert.Same(cause, exception.InnerException);
        Assert.DoesNotContain("secret response body", exception.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranslationProviderException((TranslationProviderFailureKind)99));
    }
}
