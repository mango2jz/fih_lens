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
        var viewModel = new MainWindowViewModel(new TranslationService(new ControlledProvider()));
        viewModel.InputText = "  \t";
        Assert.False(viewModel.TranslateCommand.CanExecute(null));

        viewModel.InputText = "hello";
        Assert.True(viewModel.TranslateCommand.CanExecute(null));
    }

    [Fact]
    public async Task TranslateSnapshotsInputAndPublishesResult()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider));
        viewModel.InputText = "  Hello world  ";
        var operation = viewModel.TranslateCommand.ExecuteAsync(null);

        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.IsEditingEnabled);
        Assert.False(viewModel.TranslateCommand.CanExecute(null));
        Assert.Equal("  Hello world  ", provider.Request!.Text);
        Assert.Equal(Language.English, provider.Request.SourceLanguage);
        Assert.Equal(Language.Vietnamese, provider.Request.TargetLanguage);
        Assert.Equal(TranslationMode.Translate, provider.Request.Mode);
        Assert.Null(provider.Request.UserLevel);

        var result = new TranslationResult { OriginalText = provider.Request.Text, NaturalTranslation = "Xin chào" };
        provider.Completion.SetResult(result);
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Same(result, viewModel.Result);
        Assert.Equal("Translation complete", viewModel.StatusText);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.IsEditingEnabled);
    }

    [Fact]
    public async Task CancelSuppressesLateSuccessAndRemainsBusyUntilProviderCompletes()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider));
        var operation = viewModel.TranslateCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.TranslateCancelCommand.Execute(null);
        Assert.True(provider.Token.IsCancellationRequested);
        Assert.True(viewModel.IsBusy);

        provider.Completion.SetResult(new TranslationResult { OriginalText = "Hello world" });
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(viewModel.Result);
        Assert.Equal("Translation canceled", viewModel.StatusText);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RequestedCancellationIsReportedAfterProviderAcknowledgesIt()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider));
        var operation = viewModel.TranslateCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.TranslateCancelCommand.Execute(null);
        Assert.True(viewModel.IsBusy);
        provider.Completion.SetCanceled(provider.Token);
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("Translation canceled", viewModel.StatusText);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task CloseBeforeCompletionSuppressesAllLaterViewModelStateChanges()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider));
        var operation = viewModel.TranslateCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var displayChangesAfterClose = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(viewModel.Result) or nameof(viewModel.ErrorMessage)
                or nameof(viewModel.StatusText) or nameof(viewModel.IsBusy))
                displayChangesAfterClose.Add(args.PropertyName);
        };

        viewModel.Close();
        Assert.True(provider.Token.IsCancellationRequested);
        provider.Completion.SetResult(new TranslationResult { OriginalText = "late" });
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(displayChangesAfterClose);
        Assert.Null(viewModel.Result);
        Assert.True(viewModel.IsBusy);
    }

    [Fact]
    public async Task UnexpectedCancellationIsReportedAsFailure()
    {
        var provider = new ControlledProvider();
        var viewModel = new MainWindowViewModel(new TranslationService(provider));
        var operation = viewModel.TranslateCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        provider.Completion.SetException(new OperationCanceledException());
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("Translation failed", viewModel.StatusText);
        Assert.Equal("Translation failed. Please try again.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task UnsupportedDemoInputGetsSpecificGuidance()
    {
        var provider = new ControlledProvider { Failure = new NotSupportedException() };
        var viewModel = new MainWindowViewModel(new TranslationService(provider));
        await viewModel.TranslateCommand.ExecuteAsync(null);

        Assert.Equal("Demo mode supports only the exact text “Hello world”.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    private sealed class ControlledProvider : ITranslationProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TranslationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TranslationRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public Exception? Failure { get; init; }

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            Token = cancellationToken;
            Started.TrySetResult();
            return Failure is null ? Completion.Task : Task.FromException<TranslationResult>(Failure);
        }
    }
}
