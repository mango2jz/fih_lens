using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FihLens.Application.Translation;
using FihLens.Core.Enums;
using FihLens.Core.Models;

namespace FihLens.WinUI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly TranslationService _translationService;
    private readonly string _modeLabel;
    private bool _closed;

    public MainWindowViewModel(TranslationService translationService, string modeLabel = "Demo")
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _modeLabel = modeLabel ?? throw new ArgumentNullException(nameof(modeLabel));
    }

    public string ModeDescription => _modeLabel == "Demo"
        ? "Demo mode · supports only the exact text “Hello world”"
        : $"Live mode · input is sent to {_modeLabel}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    public partial string InputText { get; set; } = "Hello world";

    [ObservableProperty]
    public partial TranslationResult? Result { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingEnabled))]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool IsEditingEnabled => !_closed && !IsBusy;

    private bool CanTranslate() => !_closed && !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanTranslate))]
    private async Task TranslateAsync(CancellationToken cancellationToken)
    {
        if (!CanTranslate()) return;

        var text = InputText;
        Result = null;
        ErrorMessage = null;
        IsBusy = true;
        StatusText = $"Translating with {_modeLabel}…";

        try
        {
            var request = new TranslationRequest(text, Language.English, Language.Vietnamese);
            var result = await _translationService.TranslateAsync(request, cancellationToken);
            if (_closed) return;

            if (cancellationToken.IsCancellationRequested)
            {
                StatusText = "Translation canceled";
                return;
            }

            Result = result;
            StatusText = "Translation complete";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!_closed) StatusText = "Translation canceled";
        }
        catch (NotSupportedException)
        {
            if (!_closed)
            {
                ErrorMessage = "Demo mode supports only the exact text “Hello world”.";
                StatusText = "Translation unavailable";
            }
        }
        catch (TranslationProviderException exception)
        {
            if (!_closed)
            {
                ErrorMessage = exception.Message;
                StatusText = "Translation failed";
            }
        }
        catch (Exception)
        {
            if (!_closed)
            {
                ErrorMessage = "Translation failed. Please try again.";
                StatusText = "Translation failed";
            }
        }
        finally
        {
            if (!_closed) IsBusy = false;
        }
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        OnPropertyChanged(nameof(IsEditingEnabled));
        TranslateCommand.NotifyCanExecuteChanged();
        TranslateCancelCommand.Execute(null);
    }
}
