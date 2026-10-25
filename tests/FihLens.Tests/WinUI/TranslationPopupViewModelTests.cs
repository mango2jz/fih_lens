using FihLens.Core.Models;
using FihLens.WinUI.DesignPreview;
using FihLens.WinUI.ViewModels;

namespace FihLens.Tests.WinUI;

public sealed class TranslationPopupViewModelTests
{
    [Fact]
    public void TranslationPreservesOriginalOutputAndProvidedVocabularySnapshot()
    {
        var vocabulary = new List<VocabularyItem>
        {
            new() { Word = "application", PartOfSpeech = "noun", Meaning = "ứng dụng" }
        };
        var result = new TranslationResult
        { SourceLanguage = FihLens.Core.Enums.Language.English, TargetLanguage = FihLens.Core.Enums.Language.Vietnamese,
            OriginalText = "  Open the application.  ", NaturalTranslation = "Mở ứng dụng.",
            Vocabulary = vocabulary
        };

        TranslationPopupViewModel viewModel = TranslationPopupViewModel.FromTranslation(result, "Gemini", "Provider notice");
        vocabulary.Clear();

        Assert.Equal(result.OriginalText, viewModel.OriginalText);
        Assert.Equal(result.NaturalTranslation, viewModel.OutputText);
        Assert.Equal("Generated AI output · Gemini", viewModel.SourceLabel);
        Assert.Equal("Provider notice", viewModel.SourceDetails);
        Assert.Equal("application · noun · ứng dụng", viewModel.VocabularyText);
        Assert.True(viewModel.HasVocabulary);
        Assert.False(viewModel.IsPreview);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void MissingTranslationIsUnavailableWithoutFabricatedVocabulary(string? translation)
    {
        TranslationPopupViewModel viewModel = TranslationPopupViewModel.FromTranslation(
            new TranslationResult { SourceLanguage = FihLens.Core.Enums.Language.English, TargetLanguage = FihLens.Core.Enums.Language.Vietnamese, OriginalText = "time", NaturalTranslation = translation }, "DeepSeek");

        Assert.False(viewModel.HasOutput);
        Assert.Equal("", viewModel.OutputText);
        Assert.False(viewModel.HasVocabulary);
        Assert.Equal("No translation was returned.", viewModel.OutputUnavailableReason);
    }

    [Fact]
    public void DictionaryPreservesAllSensesArticleProvenanceAndFullSourceNotices()
    {
        var senses = new List<string> { "thời gian", "lần" };
        var notices = new Dictionary<string, string> { ["Full notice"] = "Do not omit this license condition." };
        DictionarySource source = Source(notices);
        var entry = new DictionaryEntry("time", senses, "noun", "on time", "source", "article-42", "original-dictionary");
        var result = new DictionaryLookupResult(" time ", source, new[] { entry });

        TranslationPopupViewModel viewModel = TranslationPopupViewModel.FromDictionary(result);
        senses.Clear();
        notices.Clear();

        Assert.Equal(" time ", viewModel.OriginalText);
        Assert.Equal("Dictionary definitions", viewModel.OutputLabel);
        Assert.Contains("thời gian\nlần", viewModel.OutputText);
        Assert.Contains("Article: article-42", viewModel.OutputText);
        Assert.Contains("Origin: original-dictionary", viewModel.OutputText);
        Assert.Contains("Full notice\nDo not omit this license condition.", viewModel.SourceDetails);
        Assert.Contains("License: license text", viewModel.SourceDetails);
        Assert.Contains("Revision: v1", viewModel.SourceDetails);
        Assert.False(viewModel.HasVocabulary);
    }

    [Fact]
    public void DictionaryMissHasNoInventedTranslationOrClassification()
    {
        TranslationPopupViewModel viewModel = TranslationPopupViewModel.FromDictionary(
            new DictionaryLookupResult("run out of", Source(new Dictionary<string, string>()), Array.Empty<DictionaryEntry>()));

        Assert.False(viewModel.HasOutput);
        Assert.False(viewModel.HasVocabulary);
        Assert.Equal("No exact dictionary entry for this text.", viewModel.OutputUnavailableReason);
        Assert.Contains("Local sample", viewModel.SourceLabel);
    }

    [Fact]
    public void CloseRequestsOnceAndKeepsTheDisplaySnapshot()
    {
        TranslationPopupViewModel viewModel = Phase1PreviewData.Compact();
        int closeCount = 0;
        viewModel.CloseRequested += (_, _) => closeCount++;
        string original = viewModel.OriginalText;

        viewModel.CloseCommand.Execute(null);
        viewModel.CloseCommand.Execute(null);

        Assert.Equal(1, closeCount);
        Assert.True(viewModel.IsClosed);
        Assert.False(viewModel.CloseCommand.CanExecute(null));
        Assert.Equal(original, viewModel.OriginalText);
        Assert.True(viewModel.HasOutput);
    }

    [Fact]
    public void FixtureIsExplicitlyPreviewWithoutFabricatedLevel()
    {
        TranslationPopupViewModel viewModel = Phase1PreviewData.Compact();

        Assert.True(viewModel.IsPreview);
        Assert.Contains("sample data", viewModel.PreviewNotice);
        Assert.Contains("no lookup or AI request", viewModel.PreviewNotice);
        Assert.Contains("design fixture", viewModel.SourceLabel);
        Assert.DoesNotContain("B1", viewModel.VocabularyText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingProviderIdentityCannotPresentAnUnattributedAiResult(string? provider)
    {
        Assert.ThrowsAny<ArgumentException>(() => TranslationPopupViewModel.FromTranslation(
            new TranslationResult { SourceLanguage = FihLens.Core.Enums.Language.English, TargetLanguage = FihLens.Core.Enums.Language.Vietnamese, OriginalText = "hello" }, provider!));
    }

    [Fact]
    public void ExplainOpensAttachedDetailInsteadOfExpanding()
    {
        var viewModel = Phase1PreviewData.Compact();
        int opened = 0;
        viewModel.SetOpenDetail(() => opened++);

        Assert.True(viewModel.ExplainCommand.CanExecute(null));
        viewModel.ExplainCommand.Execute(null);

        Assert.Equal(1, opened);
        Assert.False(viewModel.IsExpanded);
        viewModel.CloseCommand.Execute(null);
        Assert.False(viewModel.ExplainCommand.CanExecute(null));
    }

    [Fact]
    public void FailedCaptureShowsStageAlertAndCannotExplainUntilTextArrives()
    {
        var viewModel = TranslationPopupViewModel.ForInput("Selected screen region");
        viewModel.SetOpenDetail(() => { });

        viewModel.PresentCaptureStatus("No readable text found. Select a clearer region.", false, true);

        Assert.True(viewModel.ShowStageAlert);
        Assert.True(viewModel.ShowSelectAgain);
        Assert.False(viewModel.ShowAnswer);
        Assert.False(viewModel.ShowBackendStatuses);
        Assert.Equal("No readable text found. Select a clearer region.", viewModel.StageDescription);
        Assert.False(viewModel.ExplainCommand.CanExecute(null));

        viewModel.SetCapturedInput("time");

        Assert.False(viewModel.ShowStageAlert);
        Assert.True(viewModel.ShowAnswer);
        Assert.True(viewModel.ExplainCommand.CanExecute(null));
    }

    [Fact]
    public void ExplainExpandsInPlaceWithoutAttachedDetail()
    {
        var viewModel = Phase1PreviewData.Compact();

        viewModel.ExplainCommand.Execute(null);

        Assert.True(viewModel.IsExpanded);
    }

    [Fact]
    public void ExpandingAndCollapsingRetainsCurrentSnapshotWithoutClosingOrReacquiring()
    {
        var viewModel = Phase1PreviewData.Compact();
        string original = viewModel.OriginalText;
        string output = viewModel.OutputText;
        string source = viewModel.SourceDetails;
        string details = viewModel.ExpandedText;
        int closes = 0;
        viewModel.CloseRequested += (_, _) => closes++;

        viewModel.ToggleExpansionCommand.Execute(null);
        Assert.True(viewModel.IsExpanded);
        Assert.False(viewModel.IsCollapsed);
        Assert.Equal("Less", viewModel.ExplainButtonText);
        viewModel.ToggleExpansionCommand.Execute(null);

        Assert.False(viewModel.IsExpanded);
        Assert.Same(original, viewModel.OriginalText);
        Assert.Same(output, viewModel.OutputText);
        Assert.Same(source, viewModel.SourceDetails);
        Assert.Same(details, viewModel.ExpandedText);
        Assert.Equal(0, closes);
    }

    [Fact]
    public void ExpandedTranslationSnapshotsAllProvidedOptionalSectionsAndLongText()
    {
        string longExplanation = new('x', 4000);
        var grammar = new List<GrammarPoint>
        {
            new() { Pattern = "past simple", Explanation = "Completed action", ExampleSentence = "She told a joke." }
        };
        var alternatives = new List<TranslationAlternative>
        {
            new() { Text = "Cô ấy kể chuyện cười.", Explanation = "Shorter wording" }
        };
        var viewModel = TranslationPopupViewModel.FromTranslation(new TranslationResult
        { SourceLanguage = FihLens.Core.Enums.Language.English, TargetLanguage = FihLens.Core.Enums.Language.Vietnamese,
            OriginalText = "She told a joke.", NaturalTranslation = "Cô ấy kể một câu chuyện cười.",
            Explanation = longExplanation, Context = "A meeting", Tone = "Friendly",
            Vocabulary = new[] { new VocabularyItem { Word = "joke", Phonetic = "/dʒəʊk/", PartOfSpeech = "noun", Meaning = "chuyện cười", ExampleSentence = "Tell a joke." } },
            Grammar = grammar, Alternatives = alternatives
        }, "Gemini");
        grammar.Clear();
        alternatives.Clear();

        Assert.Contains(longExplanation, viewModel.ExpandedText);
        Assert.Contains("Context\nA meeting", viewModel.ExpandedText);
        Assert.Contains("Tone\nFriendly", viewModel.ExpandedText);
        Assert.Contains("Vocabulary\njoke · /dʒəʊk/ · noun · chuyện cười\nTell a joke.", viewModel.ExpandedText);
        Assert.Contains("Grammar\npast simple\nCompleted action\nShe told a joke.", viewModel.ExpandedText);
        Assert.Contains("Alternatives\nCô ấy kể chuyện cười.\nShorter wording", viewModel.ExpandedText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void AbsentOptionalSectionsDoNotInventExplanationOrEnableExpansion(string? missing)
    {
        var viewModel = TranslationPopupViewModel.FromTranslation(new TranslationResult
        { SourceLanguage = FihLens.Core.Enums.Language.English, TargetLanguage = FihLens.Core.Enums.Language.Vietnamese,
            OriginalText = "hello", NaturalTranslation = "xin chào", Explanation = missing, Context = missing, Tone = missing
        }, "DeepSeek");

        Assert.False(viewModel.HasExpandedContent);
        Assert.Empty(viewModel.ExpandedText);
        Assert.False(viewModel.ToggleExpansionCommand.CanExecute(null));
        viewModel.ToggleExpansionCommand.Execute(null);
        Assert.False(viewModel.IsExpanded);
    }

    [Fact]
    public void ProvidedOptionalContextCanExpandWithoutInventedExplanation()
    {
        var viewModel = TranslationPopupViewModel.FromTranslation(new TranslationResult
        { SourceLanguage = FihLens.Core.Enums.Language.English, TargetLanguage = FihLens.Core.Enums.Language.Vietnamese,
            OriginalText = "hello", Context = "Greeting", Tone = " "
        }, "Gemini");

        Assert.Equal("Context\nGreeting", viewModel.ExpandedText);
        Assert.True(viewModel.ToggleExpansionCommand.CanExecute(null));
    }

    [Fact]
    public void LocalExpansionUsesActualSensesAndFullSourceInsteadOfAiExplanation()
    {
        var viewModel = TranslationPopupViewModel.FromDictionary(new DictionaryLookupResult("time",
            Source(new Dictionary<string, string> { ["License condition"] = "Retain this full notice." }),
            new[] { new DictionaryEntry("time", new[] { "thời gian", "lần" }, "noun", null, "source", "article-1", "origin") }));

        viewModel.ToggleExpansionCommand.Execute(null);

        Assert.True(viewModel.IsExpanded);
        Assert.Equal("Dictionary senses and source", viewModel.ExpandedTitle);
        Assert.Contains(viewModel.OutputText, viewModel.ExpandedText);
        Assert.Contains(viewModel.SourceDetails, viewModel.ExpandedText);
        Assert.DoesNotContain("AI", viewModel.ExpandedText);
    }

    [Fact]
    public void ClosedSnapshotCannotToggleExpansionEvenThroughDirectCommandExecution()
    {
        var viewModel = Phase1PreviewData.Compact();
        viewModel.ToggleExpansionCommand.Execute(null);
        viewModel.CloseCommand.Execute(null);

        Assert.False(viewModel.ToggleExpansionCommand.CanExecute(null));
        viewModel.ToggleExpansionCommand.Execute(null);
        Assert.True(viewModel.IsExpanded);
    }

    private static DictionarySource Source(IReadOnlyDictionary<string, string> notices) =>
        new("source", "Local sample", "v1", "en", "vi", "license text", "https://example.test/dictionary",
            "original attribution", "sample reviewed", "100 entries", notices);
}
