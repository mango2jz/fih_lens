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
        Assert.Contains("natural Vietnamese", instructions.SystemText);
        Assert.Contains("\"naturalTranslation\"", instructions.SystemText);
    }

    [Fact]
    public void RejectsOtherLanguageModeAndLearningLevel()
    {
        var builder = new PromptBuilder();
        Assert.Throws<NotSupportedException>(() => builder.Build(new TranslationRequest("こんにちは", Language.Japanese, Language.Vietnamese)));
        Assert.Throws<NotSupportedException>(() => builder.Build(new TranslationRequest("Hello", Language.English, Language.Vietnamese, mode: TranslationMode.Explain)));
        Assert.Throws<NotSupportedException>(() => builder.Build(new TranslationRequest("Hello", Language.English, Language.Vietnamese, new CefrLearningLevel(CefrLevel.A1))));
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
