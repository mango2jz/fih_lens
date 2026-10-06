using FihLens.Core.Enums;
using FihLens.Core.ValueObjects;

namespace FihLens.Tests.Core;

public class LearningLevelTests
{
    [Theory]
    [InlineData(CefrLevel.A1)]
    [InlineData(CefrLevel.A2)]
    [InlineData(CefrLevel.B1)]
    [InlineData(CefrLevel.B2)]
    [InlineData(CefrLevel.C1)]
    [InlineData(CefrLevel.C2)]
    public void CefrLearningLevel_AcceptsEveryNamedLevel(CefrLevel value)
    {
        Assert.Equal(value, new CefrLearningLevel(value).Value);
    }

    [Theory]
    [InlineData(CefrLevel.Unknown)]
    [InlineData((CefrLevel)int.MaxValue)]
    public void CefrLearningLevel_RejectsSentinelAndUndefinedValues(CefrLevel value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new CefrLearningLevel(value));
    }

    [Theory]
    [InlineData(JlptLevel.N5)]
    [InlineData(JlptLevel.N4)]
    [InlineData(JlptLevel.N3)]
    [InlineData(JlptLevel.N2)]
    [InlineData(JlptLevel.N1)]
    public void JlptLearningLevel_AcceptsEveryNamedLevel(JlptLevel value)
    {
        Assert.Equal(value, new JlptLearningLevel(value).Value);
    }

    [Theory]
    [InlineData(JlptLevel.None)]
    [InlineData((JlptLevel)int.MaxValue)]
    public void JlptLearningLevel_RejectsSentinelAndUndefinedValues(JlptLevel value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new JlptLearningLevel(value));
    }

    [Fact]
    public void LearningLevels_UseValueEquality()
    {
        Assert.Equal(new CefrLearningLevel(CefrLevel.B1), new CefrLearningLevel(CefrLevel.B1));
        Assert.NotEqual(new CefrLearningLevel(CefrLevel.B1), new CefrLearningLevel(CefrLevel.B2));
        Assert.Equal(new JlptLearningLevel(JlptLevel.N3), new JlptLearningLevel(JlptLevel.N3));
        Assert.NotEqual(new JlptLearningLevel(JlptLevel.N3), new JlptLearningLevel(JlptLevel.N2));
    }
}
