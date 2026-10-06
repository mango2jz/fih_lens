using FihLens.Core.Enums;
using FihLens.Core.ValueObjects;

namespace FihLens.Core.Models;

public sealed record TranslationRequest
{
    public string Text { get; }
    public Language SourceLanguage { get; }
    public Language TargetLanguage { get; }
    public LearningLevel? UserLevel { get; }
    public TranslationMode Mode { get; }

    public TranslationRequest(string text, Language sourceLanguage, Language targetLanguage,
        LearningLevel? userLevel = null,
        TranslationMode mode = TranslationMode.Translate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (!Enum.IsDefined(sourceLanguage))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLanguage), sourceLanguage, "Unknown source language");
        }

        if (!Enum.IsDefined(targetLanguage) || targetLanguage == Language.Auto)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLanguage), targetLanguage,
                "Target language must be a concrete, defined language");
        }

        ValidateUserLevel(userLevel, sourceLanguage);

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode");
        }

        Text = text;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        UserLevel = userLevel;
        Mode = mode;
    }

    private static void ValidateUserLevel(LearningLevel? userLevel, Language sourceLanguage)
    {
        if (userLevel is null)
        {
            return;
        }

        switch (sourceLanguage)
        {
            case Language.English when userLevel is not CefrLearningLevel:
                throw new ArgumentException("English source language only supports CEFR learning levels",
                    nameof(userLevel));
            case Language.Japanese when userLevel is not JlptLearningLevel:
                throw new ArgumentException("Japanese source language only supports JLPT learning levels",
                    nameof(userLevel));
            case Language.Vietnamese:
                throw new ArgumentException("Vietnamese does not support learning levels", nameof(userLevel));
            case Language.Auto:
                if (userLevel is CefrLearningLevel or JlptLearningLevel)
                {
                    return;
                }

                throw new ArgumentException("Auto source language supports only CEFR or JLPT learning levels",
                    nameof(userLevel));
        }
    }
}
