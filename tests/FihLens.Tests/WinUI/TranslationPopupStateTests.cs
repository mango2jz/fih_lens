using CommunityToolkit.Mvvm.Input;
using FihLens.Application.Clipboard;
using FihLens.Application.Translation;
using FihLens.Core.Models;
using FihLens.WinUI.DesignPreview;
using FihLens.WinUI.ViewModels;

namespace FihLens.Tests.WinUI;

public sealed class TranslationPopupStateTests
{
    [Fact]
    public async Task ProductionActionsRequireActualCapabilitiesAndNeverSave()
    {
        var model = Result();
        Assert.False(model.CopyCommand.CanExecute(null));
        Assert.False(model.AskAiCommand.CanExecute(null));
        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.False(model.UndoCommand.CanExecute(null));
        model.SaveCommand.Execute(null);
        model.UndoCommand.Execute(null);
        Assert.Empty(model.SaveFeedback);
        Assert.Contains("not supported", model.UnavailableActionsReason);
        var writer = new ControlledWriter();
        model.BindActions(null, null, "", writer);
        Task copy = model.CopyCommand.ExecuteAsync(null);
        Assert.Equal("Mở ứng dụng.", writer.Text);
        Assert.Empty(model.CopyFeedback);
        Assert.False(model.CopyCommand.CanExecute(null));
        writer.Completion.SetResult();
        await copy;
        Assert.Equal("Copied.", model.CopyFeedback);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriterFailureOrCancellationDoesNotReportSuccess(bool cancel)
    {
        var model = Result();
        var writer = new ControlledWriter();
        model.BindActions(null, null, "", writer);
        Task copy = model.CopyCommand.ExecuteAsync(null);
        if (cancel) writer.Completion.SetCanceled();
        else writer.Completion.SetException(new InvalidOperationException("private text must not escape"));
        await copy;
        Assert.DoesNotContain("Copied", model.CopyFeedback);
        Assert.DoesNotContain("private", model.CopyFeedback);
        Assert.Equal(!cancel, model.IsErrorStatus);
        Assert.True(model.CopyCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("cancel", false)]
    [InlineData("close", false)]
    [InlineData("rebind", false)]
    [InlineData("cancel", true)]
    [InlineData("close", true)]
    [InlineData("rebind", true)]
    public async Task RetiredCopyCompletionCannotPublishFeedback(string retirement, bool failure)
    {
        var model = Result();
        var writer = new ControlledWriter();
        model.BindActions(null, null, "", writer);
        Task copy = model.CopyCommand.ExecuteAsync(null);
        switch (retirement)
        {
            case "cancel": model.CancelCommand.Execute(null); break;
            case "close": model.CloseCommand.Execute(null); break;
            default: model.BindActions(null, null, "", new ControlledWriter()); break;
        }
        Assert.True(writer.Token.IsCancellationRequested);
        if (failure) writer.Completion.SetException(new InvalidOperationException());
        else writer.Completion.SetResult();
        await copy;
        Assert.Empty(model.CopyFeedback);
        if (retirement == "close") Assert.False(model.CopyCommand.CanExecute(null));
    }

    [Fact]
    public void BackendProjectionRetainsIndependentStatusesAndRejectsDifferentInputOrClosedPublication()
    {
        var model = Result();
        var backend = new MainWindowViewModel { InputText = model.OriginalText, DictionaryStatusText = "Dictionary match", AiStatusText = "AI failed", ErrorMessage = "Safe error" };
        model.PresentBackendState(backend);
        Assert.Equal("Dictionary match", model.DictionaryStatus);
        Assert.Equal("AI failed", model.AiStatus);
        Assert.True(model.IsErrorStatus);
        Assert.Equal("Mở ứng dụng.", model.OutputText);
        backend.InputText = "different input";
        model.PresentBackendState(backend);
        Assert.Equal("AI failed", model.AiStatus);
        backend.InputText = model.OriginalText;
        model.CloseCommand.Execute(null);
        model.PresentBackendState(backend);
        Assert.Equal("AI failed", model.AiStatus);
    }

    [Fact]
    public async Task ExplicitAiCommandUsesExistingBackendSerialGateThroughCancellationDrain()
    {
        var model = Result();
        var provider = new ControlledProvider();
        var backend = new MainWindowViewModel(new TranslationService(provider), "Gemini") { InputText = model.OriginalText };
        model.BindActions(backend.AskAiCommand, backend.CancelCommand, backend.AiSendNotice);
        model.PresentBackendState(backend);
        Assert.Equal(0, provider.Calls);
        Assert.True(model.AskAiCommand.CanExecute(null));
        Assert.Contains("Gemini", model.AiSendNotice);
        model.AskAiCommand.Execute(null);
        model.PresentBackendState(backend);
        Assert.Equal(1, provider.Calls);
        Assert.True(model.IsBusy);
        model.CancelCommand.Execute(null);
        model.PresentBackendState(backend);
        Assert.True(provider.Token.IsCancellationRequested);
        Assert.True(model.IsBusy);
        Assert.False(model.CancelCommand.CanExecute(null));
        model.AskAiCommand.Execute(null);
        Assert.Equal(1, provider.Calls);
        provider.Completion.SetResult(new TranslationResult { SourceLanguage = FihLens.Core.Enums.Language.English, TargetLanguage = FihLens.Core.Enums.Language.Vietnamese, OriginalText = model.OriginalText });
        await backend.AskAiCommand.ExecutionTask!;
        model.PresentBackendState(backend);
        Assert.Null(backend.Result);
        Assert.False(model.IsBusy);
        Assert.True(model.AskAiCommand.CanExecute(null));
        model.CloseCommand.Execute(null);
        model.AskAiCommand.Execute(null);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void EmptyOutputCannotCopyEvenWithWriter()
    {
        var model = TranslationPopupViewModel.ForInput("time");
        model.BindActions(null, null, "", new ControlledWriter());
        Assert.False(model.CopyCommand.CanExecute(null));
        Assert.Contains("Awaiting", model.OutputUnavailableReason);
    }

    [Fact]
    public void ExpandedPreviewSaveAndUndoPreserveExpansionWithoutStorage()
    {
        var model = Phase1PreviewData.Compact();
        model.SetPreviewState(PopupPreviewState.Expanded);
        model.SaveCommand.Execute(null);
        Assert.Equal("Preview only · ExpandedSaving", model.PreviewStateName);
        Assert.True(model.IsExpanded);
        model.SetPreviewState(PopupPreviewState.ExpandedSaved);
        model.UndoCommand.Execute(null);
        Assert.Equal("Preview only · Expanded", model.PreviewStateName);
        Assert.True(model.IsExpanded);
    }

    [Theory]
    [InlineData(PopupPreviewState.OcrLoading)]
    [InlineData(PopupPreviewState.OcrError)]
    [InlineData(PopupPreviewState.EmptyOcr)]
    [InlineData(PopupPreviewState.Translating)]
    [InlineData(PopupPreviewState.TranslationError)]
    public void PreviewStagesDoNotDisplayTheSuccessfulFixture(PopupPreviewState state)
    {
        var model = Phase1PreviewData.Compact();
        model.SetPreviewState(state);
        Assert.False(model.ShowAnswer);
        Assert.False(model.HasOutput);
        Assert.False(model.HasVocabulary);
        Assert.False(model.ShowResultActions);
        Assert.False(model.CopyCommand.CanExecute(null));
        Assert.False(model.ToggleExpansionCommand.CanExecute(null));
        model.ToggleExpansionCommand.Execute(null);
        Assert.False(model.IsExpanded);
    }

    [Theory]
    [InlineData(PopupPreviewState.Saving, 355, 599)]
    [InlineData(PopupPreviewState.ExpandedSaving, 355, 599)]
    [InlineData(PopupPreviewState.Saved, 373, 617)]
    [InlineData(PopupPreviewState.ExpandedSaved, 373, 617)]
    [InlineData(PopupPreviewState.SaveError, 373, 617)]
    public void SavePhaseHeightFollowsActualExpansionInEitherStartingVariant(PopupPreviewState state, int compactHeight, int expandedHeight)
    {
        var model = Phase1PreviewData.Compact();
        model.SetPreviewState(state);
        string feedback = model.SaveFeedback;
        bool initiallyExpanded = model.IsExpanded;
        Assert.Equal(initiallyExpanded ? expandedHeight : compactHeight, model.PreferredHeight);
        model.ToggleExpansionCommand.Execute(null);
        Assert.Equal(initiallyExpanded ? compactHeight : expandedHeight, model.PreferredHeight);
        model.ToggleExpansionCommand.Execute(null);
        Assert.Equal(initiallyExpanded ? expandedHeight : compactHeight, model.PreferredHeight);
        Assert.Equal(feedback, model.SaveFeedback);
    }

    [Theory]
    [InlineData(PopupPreviewState.Copied)]
    [InlineData(PopupPreviewState.CopyError)]
    public void CopyFixtureFeedbackReservesOneStripAndFollowsExpansion(PopupPreviewState state)
    {
        var model = Phase1PreviewData.Compact();
        model.SetPreviewState(state);
        Assert.True(model.HasFeedbackStrip);
        Assert.Equal(371, model.PreferredHeight);
        model.ToggleExpansionCommand.Execute(null);
        Assert.Equal(615, model.PreferredHeight);
        model.ToggleExpansionCommand.Execute(null);
        Assert.Equal(371, model.PreferredHeight);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LiveCopyFeedbackChangesHeightOnlyAfterCompletion(bool preview, bool failure)
    {
        var model = preview ? Phase1PreviewData.Compact() : Result();
        var writer = new ControlledWriter();
        model.BindActions(null, null, "", writer);
        int heightNotifications = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.PreferredHeight)) heightNotifications++; };
        Task copy = model.CopyCommand.ExecuteAsync(null);
        Assert.Empty(model.CopyFeedback);
        Assert.Equal(325, model.PreferredHeight);
        if (failure) writer.Completion.SetException(new InvalidOperationException());
        else writer.Completion.SetResult();
        await copy;
        Assert.True(model.HasFeedbackStrip);
        Assert.Equal(371, model.PreferredHeight);
        Assert.True(heightNotifications >= 2);
        model.ToggleExpansionCommand.Execute(null);
        Assert.Equal(615, model.PreferredHeight);
        model.ToggleExpansionCommand.Execute(null);
        Assert.Equal(371, model.PreferredHeight);

        writer.Completion = new TaskCompletionSource();
        Task retry = model.CopyCommand.ExecuteAsync(null);
        Assert.Empty(model.CopyFeedback);
        Assert.Equal(325, model.PreferredHeight);
        writer.Completion.SetResult();
        await retry;
        Assert.Equal(371, model.PreferredHeight);

        int beforeRebind = heightNotifications;
        var nextWriter = new ControlledWriter();
        model.BindActions(null, null, "", nextWriter);
        Assert.Equal(325, model.PreferredHeight);
        Assert.True(heightNotifications > beforeRebind);
        Task nextCopy = model.CopyCommand.ExecuteAsync(null);
        int beforeCancel = heightNotifications;
        model.CancelCommand.Execute(null);
        Assert.Equal(325, model.PreferredHeight);
        Assert.True(heightNotifications > beforeCancel);
        nextWriter.Completion.SetResult();
        await nextCopy;
        Assert.Empty(model.CopyFeedback);
        Assert.Equal(325, model.PreferredHeight);
    }

    [Theory]
    [InlineData(PopupPreviewState.Saving, 355)]
    [InlineData(PopupPreviewState.ExpandedSaving, 599)]
    [InlineData(PopupPreviewState.Saved, 391)]
    [InlineData(PopupPreviewState.ExpandedSaved, 635)]
    [InlineData(PopupPreviewState.SaveError, 391)]
    public async Task CopySharesSaveStripAndReservesItsAdditionalVisibleCaption(PopupPreviewState state, int height)
    {
        var model = Phase1PreviewData.Compact();
        model.SetPreviewState(state);
        await model.CopyCommand.ExecuteAsync(null);
        Assert.NotEmpty(model.CopyFeedback);
        Assert.Equal(!model.IsPreviewSaving, model.HasFeedbackStrip);
        Assert.Equal(height, model.PreferredHeight);
        bool initiallyExpanded = model.IsExpanded;
        model.ToggleExpansionCommand.Execute(null);
        Assert.Equal(height + (initiallyExpanded ? -244 : 244), model.PreferredHeight);
        model.ToggleExpansionCommand.Execute(null);
        Assert.Equal(height, model.PreferredHeight);
    }

    [Theory]
    [InlineData('C', true, false)]
    [InlineData('C', false, true)]
    [InlineData('E', true, false)]
    [InlineData('E', false, true)]
    public void ShortcutsLeaveModifiersAndEditableTextUntouched(char key, bool hasModifiers, bool isEditing)
    {
        var model = Phase1PreviewData.Compact();
        Assert.False(model.TryHandleShortcut(key, hasModifiers, isEditing));
        Assert.False(model.IsExpanded);
        Assert.Empty(model.CopyFeedback);
    }

    [Fact]
    public async Task UnmodifiedShortcutsUseExistingCommandsAndTheirAvailability()
    {
        var model = Phase1PreviewData.Compact();
        Assert.True(model.TryHandleShortcut('E', false, false));
        Assert.True(model.IsExpanded);
        Assert.True(model.TryHandleShortcut('E', false, false));
        Assert.False(model.IsExpanded);
        Assert.True(model.TryHandleShortcut('C', false, false));
        await model.CopyCommand.ExecutionTask!;
        Assert.Equal("Preview: copied (simulated).", model.CopyFeedback);
        model.SetPreviewState(PopupPreviewState.OcrLoading);
        Assert.False(model.TryHandleShortcut('E', false, false));
        Assert.False(model.TryHandleShortcut('C', false, false));
        model.CloseCommand.Execute(null);
        Assert.False(model.TryHandleShortcut('E', false, false));
        Assert.False(model.TryHandleShortcut('C', false, false));
    }

    [Fact]
    public void PreviewInventoryIsSelectedWithoutTimersOrPersistenceAndUndoIsOnlySimulated()
    {
        var model = Phase1PreviewData.Compact();
        foreach (PopupPreviewState state in Enum.GetValues<PopupPreviewState>())
        {
            model.SetPreviewState(state);
            Assert.Equal($"Preview only · {state}", model.PreviewStateName);
            Assert.InRange(model.PreferredHeight, 276, 617);
            if (state is PopupPreviewState.Saved or PopupPreviewState.ExpandedSaved)
            {
                Assert.True(model.UndoCommand.CanExecute(null));
                Assert.Contains("nothing was stored", model.SaveFeedback);
            }
            if (state is PopupPreviewState.Saving or PopupPreviewState.ExpandedSaving)
                Assert.False(model.SaveCommand.CanExecute(null));
        }
        model.SetPreviewState(PopupPreviewState.Compact);
        Assert.Equal("Preview only · Compact", model.PreviewStateName);
        model.SaveCommand.Execute(null);
        Assert.Equal("Saving…", model.SaveButtonText);
        model.SetPreviewState(PopupPreviewState.Saved);
        Assert.Equal("Saved", model.SaveButtonText);
        model.UndoCommand.Execute(null);
        Assert.Equal("Preview only · Compact", model.PreviewStateName);
    }

    private static TranslationPopupViewModel Result() => TranslationPopupViewModel.FromTranslation(
        new TranslationResult { SourceLanguage = FihLens.Core.Enums.Language.English, TargetLanguage = FihLens.Core.Enums.Language.Vietnamese, OriginalText = "Open the application.", NaturalTranslation = "Mở ứng dụng.", Explanation = "The software application." }, "Gemini");

    private sealed class ControlledWriter : IClipboardWriter
    {
        public TaskCompletionSource Completion { get; set; } = new();
        public string? Text { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task WriteTextAsync(string text, CancellationToken cancellationToken)
        {
            Text = text;
            Token = cancellationToken;
            return Completion.Task;
        }
    }

    private sealed class ControlledProvider : ITranslationProvider
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource<TranslationResult> Completion { get; } = new();
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Token = cancellationToken;
            return Completion.Task;
        }
    }
}
