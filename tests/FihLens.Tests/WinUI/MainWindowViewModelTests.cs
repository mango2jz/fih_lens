using FihLens.Application.Dictionary;
using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.WinUI.ViewModels;

namespace FihLens.Tests.WinUI;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void BlankInputDisablesTranslateUntilInputChanges()
    {
        var viewModel = new MainWindowViewModel(new TranslationService(new ControlledProvider()), "Gemini");
        viewModel.InputText = "  \t";
        Assert.False(viewModel.AskAiCommand.CanExecute(null));

        viewModel.InputText = "hello";
        Assert.True(viewModel.AskAiCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(Language.English, Language.Vietnamese)]
    [InlineData(Language.Vietnamese, Language.English)]
    [InlineData(Language.English, Language.Japanese)]
    [InlineData(Language.Japanese, Language.English)]
    [InlineData(Language.Vietnamese, Language.Japanese)]
    [InlineData(Language.Japanese, Language.Vietnamese)]
    public async Task AskAiUsesSelectedLanguagePair(Language source, Language target)
    {
        var provider = new ControlledProvider();
        provider.Completion.SetResult(Result("hello", source, target));
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini")
        {
            SourceLanguage = source,
            TargetLanguage = target
        };

        await viewModel.AskAiCommand.ExecuteAsync(null);

        Assert.Equal(source, provider.Request!.SourceLanguage);
        Assert.Equal(target, provider.Request.TargetLanguage);
        Assert.Equal($"{source} → {target}", viewModel.LanguagePairLabel);
        Assert.Equal(source, viewModel.Result!.SourceLanguage);
    }

    [Theory]
    [InlineData(Language.English, Language.English)]
    [InlineData(Language.Auto, Language.Japanese)]
    [InlineData(Language.English, Language.Auto)]
    [InlineData((Language)99, Language.Japanese)]
    public void InvalidPairDisablesAskAi(Language source, Language target)
    {
        var viewModel = new MainWindowViewModel(new TranslationService(new ControlledProvider()), "Gemini")
        {
            SourceLanguage = source,
            TargetLanguage = target
        };

        Assert.False(viewModel.AskAiCommand.CanExecute(null));
        Assert.NotNull(viewModel.LanguagePairUnavailableReason);
    }

    [Fact]
    public async Task ChangingLanguagePairCancelsAndClearsPriorResultAndDelayedDictionaryStaysDisabled()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        var operation = viewModel.AskAiCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.TargetLanguage = Language.Japanese;
        Assert.True(provider.Token.IsCancellationRequested);
        Assert.Null(viewModel.Result);
        Assert.False(viewModel.LookupDictionaryCommand.CanExecute(null));
        provider.Completion.SetResult(Result("stale"));
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(viewModel.Result);
        Assert.Contains("English→Vietnamese only", viewModel.DictionaryStatusText);
        viewModel.SetDictionaryAvailability(null, null, "late load failure");
        Assert.False(viewModel.LookupDictionaryCommand.CanExecute(null));
        Assert.Contains("English→Vietnamese only", viewModel.DictionaryStatusText);
        viewModel.TargetLanguage = Language.Vietnamese;
        Assert.Equal("late load failure", viewModel.DictionaryStatusText);
        Assert.False(viewModel.LookupDictionaryCommand.CanExecute(null));
        viewModel.SetDictionaryAvailability(new DictionaryLookupService(new ControlledLookup()), Source);
        Assert.True(viewModel.LookupDictionaryCommand.CanExecute(null));
    }

    [Fact]
    public async Task TranslateSnapshotsInputAndPublishesResult()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        viewModel.InputText = "  Hello world  ";
        var operation = viewModel.AskAiCommand.ExecuteAsync(null);

        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.IsEditingEnabled);
        Assert.False(viewModel.AskAiCommand.CanExecute(null));
        Assert.Equal("  Hello world  ", provider.Request!.Text);
        Assert.Equal(Language.English, provider.Request.SourceLanguage);
        Assert.Equal(Language.Vietnamese, provider.Request.TargetLanguage);
        Assert.Equal(TranslationMode.Translate, provider.Request.Mode);
        Assert.Null(provider.Request.UserLevel);

        var result = new TranslationResult { OriginalText = provider.Request.Text, SourceLanguage = provider.Request.SourceLanguage, TargetLanguage = provider.Request.TargetLanguage, NaturalTranslation = "Xin chào" };
        provider.Completion.SetResult(result);
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Same(result, viewModel.Result);
        Assert.Equal("AI translation complete", viewModel.AiStatusText);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.IsEditingEnabled);
    }

    [Fact]
    public async Task CancelSuppressesLateSuccessAndRemainsBusyUntilProviderCompletes()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        var operation = viewModel.AskAiCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.CancelCommand.Execute(null);
        Assert.True(provider.Token.IsCancellationRequested);
        Assert.True(viewModel.IsBusy);

        provider.Completion.SetResult(Result("Hello world"));
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(viewModel.Result);
        Assert.Equal("AI translation canceled", viewModel.AiStatusText);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RequestedCancellationIsReportedAfterProviderAcknowledgesIt()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        var operation = viewModel.AskAiCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CancelCommand.Execute(null);
        Assert.True(viewModel.IsBusy);
        provider.Completion.SetCanceled(provider.Token);
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("AI translation canceled", viewModel.AiStatusText);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task CloseBeforeCompletionSuppressesAllLaterViewModelStateChanges()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        var operation = viewModel.AskAiCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var displayChangesAfterClose = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(viewModel.Result) or nameof(viewModel.ErrorMessage)
                or nameof(viewModel.AiStatusText) or nameof(viewModel.IsBusy))
                displayChangesAfterClose.Add(args.PropertyName);
        };

        viewModel.Close();
        Assert.True(provider.Token.IsCancellationRequested);
        provider.Completion.SetResult(Result("late"));
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(displayChangesAfterClose);
        Assert.Null(viewModel.Result);
        Assert.True(viewModel.IsBusy);
    }

    [Fact]
    public async Task UnexpectedCancellationIsReportedAsFailure()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        var operation = viewModel.AskAiCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        provider.Completion.SetException(new OperationCanceledException());
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("AI translation failed", viewModel.AiStatusText);
        Assert.Equal("Translation failed. Please try again.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task UnsupportedRequestGetsSafeFeedback()
    {
        var provider = new ControlledProvider { Failure = new NotSupportedException() };
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        await viewModel.AskAiCommand.ExecuteAsync(null);

        Assert.Equal("Translation failed. Please try again.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task StartingNextRequestClearsPreviousResultAndErrorAndRejectsOverlap()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini")
        {
            Result = Result("old"),
            ErrorMessage = "old failure"
        };
        var operation = viewModel.AskAiCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(viewModel.Result);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Contains("Asking", viewModel.AiStatusText);
        viewModel.InputText = "changed after snapshot";
        await viewModel.AskAiCommand.ExecuteAsync(null);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal("Hello world", provider.Request!.Text);
        provider.Completion.SetResult(Result("Hello world"));
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task UnexpectedFailureShowsSafeFeedbackAndRestoresReadiness()
    {
        var provider = new ControlledProvider { Failure = new InvalidOperationException("secret raw body") };
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        await viewModel.AskAiCommand.ExecuteAsync(null);
        Assert.Equal("Translation failed. Please try again.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.IsEditingEnabled);
        Assert.True(viewModel.AskAiCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseSuppressesLateFailureAndCancellation(bool cancellation)
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        var operation = viewModel.AskAiCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Close();
        viewModel.Close();
        var status = viewModel.AiStatusText;
        if (cancellation) provider.Completion.SetCanceled(provider.Token);
        else provider.Completion.SetException(new InvalidOperationException("late"));
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(status, viewModel.AiStatusText);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(viewModel.Result);
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.IsEditingEnabled);
        Assert.False(viewModel.AskAiCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(TranslationProviderFailureKind.Authentication)]
    [InlineData(TranslationProviderFailureKind.RateLimited)]
    [InlineData(TranslationProviderFailureKind.Unavailable)]
    [InlineData(TranslationProviderFailureKind.Timeout)]
    [InlineData(TranslationProviderFailureKind.InvalidResponse)]
    [InlineData(TranslationProviderFailureKind.Refused)]
    [InlineData(TranslationProviderFailureKind.RequestRejected)]
    public async Task CloudFailuresPresentOnlySafeCategoryMessage(TranslationProviderFailureKind kind)
    {
        var failure = new TranslationProviderException(kind, new Exception("secret diagnostic"));
        var provider = new ControlledProvider { Failure = failure };
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        await viewModel.AskAiCommand.ExecuteAsync(null);
        Assert.Equal(failure.Message, viewModel.ErrorMessage);
        Assert.DoesNotContain("secret", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
        Assert.Contains("Gemini", viewModel.AiSendNotice);
        Assert.Contains("Sends this text", viewModel.AiSendNotice);
    }
    [Fact]
    public async Task UnsupportedLiveRequestDoesNotShowDemoGuidance()
    {
        var provider = new ControlledProvider { Failure = new NotSupportedException("raw details") };
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "DeepSeek");
        await viewModel.AskAiCommand.ExecuteAsync(null);
        Assert.Equal("Translation failed. Please try again.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }
    [Theory]
    [InlineData("hit")]
    [InlineData("miss")]
    [InlineData("failure")]
    [InlineData("oversized")]
    [InlineData("invalid-unicode")]
    [InlineData("blank")]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("close")]
    public async Task EveryLocalOutcomeMakesZeroAiAndHttpCalls(string outcome)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var provider = new RecordingHttpProvider(client);
        var lookup = new ControlledLookup();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        viewModel.SetDictionaryAvailability(new DictionaryLookupService(lookup), Source);
        viewModel.InputText = "  exact phrase  ";
        Assert.Equal(0, provider.CallCount);
        Assert.Equal(0, handler.CallCount);
        if (outcome is "missing" or "corrupt")
            viewModel.SetDictionaryAvailability(null, null, "Local dictionary unavailable. Install approved data.");
        if (outcome == "oversized") viewModel.InputText = new string('a', 2_001);
        if (outcome == "invalid-unicode") viewModel.InputText = "\ud800";
        if (outcome == "blank") viewModel.InputText = " \t";
        if (outcome == "close") viewModel.Close();
        if (outcome == "failure") lookup.Completion.SetException(new InvalidOperationException("private body/path"));
        else lookup.Completion.SetResult(new DictionaryLookupResult("  exact phrase  ", Source,
            outcome == "hit" ? [Entry] : []));
        await viewModel.LookupDictionaryCommand.ExecuteAsync(null);
        Assert.Equal(0, provider.CallCount);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(outcome is "hit" or "miss" or "failure" ? 1 : 0, lookup.CallCount);
        Assert.False(viewModel.IsBusy);
        if (outcome == "hit")
        {
            Assert.Same(Entry, Assert.Single(viewModel.DictionaryResult!.Entries));
            Assert.Equal("  exact phrase  ", lookup.Input);
            Assert.Equal(lookup.Input, viewModel.DictionaryInputSnapshot);
            Assert.Contains("nghĩa một\nnghĩa một", viewModel.DictionaryText);
            Assert.Contains("original-article", viewModel.DictionaryText);
            Assert.Contains("original-origin", viewModel.DictionaryText);
        }
        if (outcome == "miss") Assert.Contains("No exact", viewModel.DictionaryStatusText);
        if (outcome is "failure" or "oversized" or "invalid-unicode")
        {
            Assert.NotNull(viewModel.DictionaryErrorMessage);
            Assert.DoesNotContain("private", viewModel.DictionaryErrorMessage);
            Assert.True(viewModel.LookupDictionaryCommand.CanExecute(null));
        }
        if (outcome is "missing" or "corrupt") Assert.True(viewModel.AskAiCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("Gemini")]
    [InlineData("DeepSeek")]
    public async Task OnlyAskAiSendsOneCurrentSnapshotAndNeverDictionaryOrPreviousOutput(string label)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var provider = new RecordingHttpProvider(client);
        var lookup = new ControlledLookup();
        lookup.Completion.SetResult(new DictionaryLookupResult("exact phrase", Source, [Entry]));
        var viewModel = new MainWindowViewModel(new TranslationService(provider), label) { InputText = "exact phrase" };
        viewModel.SetDictionaryAvailability(new DictionaryLookupService(lookup), Source);
        await viewModel.LookupDictionaryCommand.ExecuteAsync(null);
        var dictionaryResult = viewModel.DictionaryResult;
        viewModel.Result = Result("previous output");
        await viewModel.AskAiCommand.ExecuteAsync(null);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("exact phrase", handler.Payload);
        Assert.Equal("exact phrase", provider.Request!.Text);
        Assert.Equal("exact phrase", viewModel.AiInputSnapshot);
        Assert.Same(dictionaryResult, viewModel.DictionaryResult);
        Assert.Equal($"Sends this text to {label}.", viewModel.AiSendNotice);
        Assert.Equal($"Generated AI output · {label}", viewModel.AiSourceLabel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailureInOneSectionPreservesOtherSuccessForSameInput(bool dictionaryFails)
    {
        var provider = new ControlledProvider();
        var lookup = new ControlledLookup();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        viewModel.SetDictionaryAvailability(new DictionaryLookupService(lookup), Source);
        if (dictionaryFails)
        {
            provider.Completion.SetResult(Result(viewModel.InputText));
            await viewModel.AskAiCommand.ExecuteAsync(null);
            var success = viewModel.Result;
            lookup.Completion.SetException(new InvalidOperationException("private"));
            await viewModel.LookupDictionaryCommand.ExecuteAsync(null);
            Assert.Same(success, viewModel.Result);
            Assert.NotNull(viewModel.DictionaryErrorMessage);
        }
        else
        {
            lookup.Completion.SetResult(new DictionaryLookupResult(viewModel.InputText, Source, [Entry]));
            await viewModel.LookupDictionaryCommand.ExecuteAsync(null);
            var success = viewModel.DictionaryResult;
            provider.Completion.SetException(new InvalidOperationException("private"));
            await viewModel.AskAiCommand.ExecuteAsync(null);
            Assert.Same(success, viewModel.DictionaryResult);
            Assert.NotNull(viewModel.ErrorMessage);
        }
        Assert.True(viewModel.AskAiCommand.CanExecute(null));
        Assert.True(viewModel.LookupDictionaryCommand.CanExecute(null));
        viewModel.InputText = "different";
        Assert.Null(viewModel.Result);
        Assert.Null(viewModel.DictionaryResult);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(viewModel.DictionaryErrorMessage);
        Assert.Empty(viewModel.AiInputSnapshot);
        Assert.Empty(viewModel.DictionaryInputSnapshot);
    }

    [Theory]
    [InlineData(true, "cancel", "success")]
    [InlineData(true, "cancel", "fault")]
    [InlineData(true, "cancel", "canceled")]
    [InlineData(true, "edit", "success")]
    [InlineData(true, "edit", "fault")]
    [InlineData(true, "edit", "canceled")]
    [InlineData(true, "close", "success")]
    [InlineData(true, "close", "fault")]
    [InlineData(true, "close", "canceled")]
    [InlineData(false, "cancel", "success")]
    [InlineData(false, "cancel", "fault")]
    [InlineData(false, "edit", "success")]
    [InlineData(false, "edit", "fault")]
    [InlineData(false, "edit", "canceled")]
    public async Task InvalidatedOperationsIgnoreAllLateOutcomesAndPreventOverlap(bool dictionary, string action, string completion)
    {
        var provider = new ControlledProvider();
        var lookup = new ControlledLookup();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        viewModel.SetDictionaryAvailability(new DictionaryLookupService(lookup), Source);
        var command = dictionary ? viewModel.LookupDictionaryCommand : viewModel.AskAiCommand;
        var operation = command.ExecuteAsync(null);
        await (dictionary ? lookup.Started.Task : provider.Started.Task).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(viewModel.IsEditingEnabled);
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        await command.ExecuteAsync(null);
        await (dictionary ? viewModel.AskAiCommand : viewModel.LookupDictionaryCommand).ExecuteAsync(null);
        Assert.Equal(1, dictionary ? lookup.CallCount : provider.CallCount);
        Assert.Equal(0, dictionary ? provider.CallCount : lookup.CallCount);
        if (action == "edit") viewModel.InputText = "new input";
        else if (action == "close") { viewModel.Close(); viewModel.Close(); }
        else viewModel.CancelCommand.Execute(null);
        var token = dictionary ? lookup.Token : provider.Token;
        Assert.True(token.IsCancellationRequested);
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        var dictionaryStatus = viewModel.DictionaryStatusText;
        var aiStatus = viewModel.AiStatusText;
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        if (dictionary)
        {
            if (completion == "success") lookup.Completion.SetResult(new DictionaryLookupResult("stale", Source, [Entry]));
            else if (completion == "fault") lookup.Completion.SetException(new InvalidOperationException("private late failure"));
            else lookup.Completion.SetCanceled(token);
        }
        else
        {
            if (completion == "success") provider.Completion.SetResult(Result("stale"));
            else if (completion == "fault") provider.Completion.SetException(new InvalidOperationException("private late failure"));
            else provider.Completion.SetCanceled(token);
        }
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(viewModel.Result);
        Assert.Null(viewModel.DictionaryResult);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(viewModel.DictionaryErrorMessage);
        Assert.Equal(dictionaryStatus, viewModel.DictionaryStatusText);
        Assert.Equal(aiStatus, viewModel.AiStatusText);
        if (action == "close")
        {
            Assert.Empty(changes);
            viewModel.SetDictionaryAvailability(null, null, "late load failure");
            Assert.Empty(changes);
            Assert.True(viewModel.IsBusy);
            Assert.False(viewModel.AskAiCommand.CanExecute(null));
            Assert.False(viewModel.LookupDictionaryCommand.CanExecute(null));
        }
        else
        {
            Assert.False(viewModel.IsBusy);
            Assert.True(viewModel.IsEditingEnabled);
            Assert.True(viewModel.AskAiCommand.CanExecute(null));
            Assert.True(viewModel.LookupDictionaryCommand.CanExecute(null));
        }
    }

    [Fact]
    public async Task NoKeysStillAllowsLocalLookupAndDisplaysFullPlainProvenance()
    {
        var lookup = new ControlledLookup();
        lookup.Completion.SetResult(new DictionaryLookupResult("Hello world", Source, [Entry]));
        var viewModel = new MainWindowViewModel();
        Assert.False(viewModel.AskAiCommand.CanExecute(null));
        Assert.False(viewModel.LookupDictionaryCommand.CanExecute(null));
        viewModel.SetDictionaryAvailability(new DictionaryLookupService(lookup), Source);
        Assert.True(viewModel.LookupDictionaryCommand.CanExecute(null));
        await viewModel.LookupDictionaryCommand.ExecuteAsync(null);
        Assert.Equal(1, lookup.CallCount);
        Assert.Contains("generated", viewModel.DictionarySourceLabel);
        Assert.Contains("sample review only", viewModel.DictionarySourceLabel);
        foreach (var value in new[] { Source.Name, Source.SourceId, Source.Revision, Source.License,
            Source.SourceUrl, Source.Attribution, Source.HumanReviewScope, Source.Notices["NOTICE"] })
            Assert.Contains(value, viewModel.DictionarySourceDetails);
        Assert.Contains("<script>plain text only</script>", viewModel.DictionaryText);
    }

    [Fact]
    public async Task BlankAndClosedCommandsStartNeitherCapabilityAndAiRetainsItsLargerInputLimit()
    {
        var provider = new ControlledProvider();
        provider.Completion.SetResult(Result("large"));
        var lookup = new ControlledLookup();
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        viewModel.SetDictionaryAvailability(new DictionaryLookupService(lookup), Source);
        viewModel.InputText = "  ";
        await viewModel.LookupDictionaryCommand.ExecuteAsync(null);
        await viewModel.AskAiCommand.ExecuteAsync(null);
        Assert.Equal(0, lookup.CallCount);
        Assert.Equal(0, provider.CallCount);
        viewModel.InputText = new string('a', 2_001);
        await viewModel.AskAiCommand.ExecuteAsync(null);
        Assert.Equal(viewModel.InputText, provider.Request!.Text);
        Assert.Equal(1, provider.CallCount);
        viewModel.Close();
        await viewModel.LookupDictionaryCommand.ExecuteAsync(null);
        await viewModel.AskAiCommand.ExecuteAsync(null);
        Assert.Equal(0, lookup.CallCount);
        Assert.Equal(1, provider.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("demo")]
    [InlineData("unknown")]
    [InlineData("gemini")]
    [InlineData("deepseek")]
    public async Task UnavailableAiConfigurationLeavesDictionaryUsable(string? mode)
    {
        var startup = FihLens.WinUI.TranslationStartup.Create(name =>
            name == "FIHLENS_PROVIDER_MODE" ? mode : null);
        Assert.Null(startup.Provider);
        Assert.Null(startup.Client);
        var lookup = new ControlledLookup();
        lookup.Completion.SetResult(new DictionaryLookupResult("Hello world", Source, [Entry]));
        var viewModel = new MainWindowViewModel(null, startup.Label, startup.UnavailableReason);
        viewModel.SetDictionaryAvailability(new DictionaryLookupService(lookup), Source);

        await viewModel.LookupDictionaryCommand.ExecuteAsync(null);

        Assert.Equal(1, lookup.CallCount);
        Assert.True(viewModel.DictionaryResult!.IsMatch);
        Assert.False(viewModel.AskAiCommand.CanExecute(null));
        Assert.Contains("Ask AI is unavailable", viewModel.AiSendNotice);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task UnavailableDictionaryStillAllowsOneExplicitAiRequest(string state)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var provider = new RecordingHttpProvider(client);
        var viewModel = new MainWindowViewModel(new TranslationService(provider), "Gemini");
        viewModel.SetDictionaryAvailability(null, null, $"Dictionary {state}. Install approved data.");
        viewModel.InputText = "A software application.";
        Assert.False(viewModel.LookupDictionaryCommand.CanExecute(null));
        Assert.Equal(0, handler.CallCount);

        await viewModel.AskAiCommand.ExecuteAsync(null);

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(viewModel.InputText, handler.Payload);
        Assert.NotNull(viewModel.Result);
        Assert.Contains(state, viewModel.DictionaryStatusText);
    }

    private static readonly DictionarySource Source = new("fixture", "Fixture dictionary", "revision", "en", "vi",
        "CC BY-SA 4.0", "https://example.test/source", "Complete attribution", "generated",
        "100 frozen queries only", new Dictionary<string, string> { ["NOTICE"] = "Full notice\n" + new string('x', 10_000) });
    private static TranslationResult Result(string text, Language source = Language.English, Language target = Language.Vietnamese) =>
        new() { OriginalText = text, SourceLanguage = source, TargetLanguage = target };
    private static readonly DictionaryEntry Entry = new("Exact phrase", ["nghĩa một", "nghĩa một", "<script>plain text only</script>"],
        "noun", "example", "fixture", "original-article", "original-origin");

    private sealed class ControlledLookup : IDictionaryLookup
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DictionaryLookupResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Input { get; private set; }
        public CancellationToken Token { get; private set; }
        public int CallCount { get; private set; }
        public Task<DictionaryLookupResult> LookupAsync(string input, CancellationToken cancellationToken = default)
        {
            Input = input;
            Token = cancellationToken;
            CallCount++;
            Started.TrySetResult();
            return Completion.Task;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Payload { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    private sealed class RecordingHttpProvider(HttpClient client) : ITranslationProvider
    {
        public int CallCount { get; private set; }
        public TranslationRequest? Request { get; private set; }
        public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Request = request;
            using var content = new StringContent(request.Text);
            using var response = await client.PostAsync("https://example.test/ai", content, cancellationToken);
            return new TranslationResult { OriginalText = request.Text, SourceLanguage = request.SourceLanguage, TargetLanguage = request.TargetLanguage, NaturalTranslation = "synthetic output" };
        }
    }
    private sealed class ControlledProvider : ITranslationProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TranslationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TranslationRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public Exception? Failure { get; init; }
        public int CallCount { get; private set; }

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Request = request;
            Token = cancellationToken;
            Started.TrySetResult();
            return Failure is null ? Completion.Task : Task.FromException<TranslationResult>(Failure);
        }
    }
}
