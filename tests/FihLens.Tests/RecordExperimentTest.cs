using Xunit.Abstractions;

namespace FihLens.Tests;

public class RecordExperimentTest
{
    private readonly ITestOutputHelper _output;
    public RecordExperimentTest(ITestOutputHelper output)
    {
        _output = output;
    }

    public sealed record Lesson
    {
        public required string Title { get; init; }
        public string? Note { get; init; }
    }

    [Fact]
    public void TestRecordOutput()
    {
        var lesson = new Lesson { Title = "nigga" };
        var updated = lesson with { Note = "ball sack" };

        _output.WriteLine(updated.ToString());
    }
}
