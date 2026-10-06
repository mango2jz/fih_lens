using FihLens.Core.Enums;

namespace FihLens.Core.ValueObjects;

public abstract record LearningLevel
{
}

public sealed record CefrLearningLevel : LearningLevel
{
    public CefrLevel Value { get; }

    public CefrLearningLevel(CefrLevel value)
    {
        if (value == CefrLevel.Unknown)
        {
            throw new ArgumentException("Cefr level must be known", nameof(value));
        }

        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown Cefr level");
        }

        Value = value;
    }
}

public sealed record JlptLearningLevel : LearningLevel
{
    public JlptLevel Value { get; }

    public JlptLearningLevel(JlptLevel value)
    {
        if (value == JlptLevel.None)
        {
            throw new ArgumentException("Jlpt level must be known", nameof(value));
        }

        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown Jlpt level");
        }

        Value = value;
    }
}
