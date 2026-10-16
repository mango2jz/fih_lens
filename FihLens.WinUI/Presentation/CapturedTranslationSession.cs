using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FihLens.Application.Capture;
using FihLens.Application.Clipboard;
using FihLens.Core.Enums;
using FihLens.WinUI.ViewModels;

namespace FihLens.WinUI.Presentation;

/// <summary>UI-thread owner for one capture and its independent dictionary-first backend.</summary>
public sealed class CapturedTranslationSession : IDisposable
{
    private bool _closed;
    private bool _localLookupStarted;
    private bool _retired;
    private bool _hasCapturedInput;
    private readonly RelayCommand _cancel;
    public RegionCaptureSession Capture { get; }
    public MainWindowViewModel Backend { get; }
    public TranslationPopupViewModel Popup { get; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public Task LocalLookupCompletion { get; private set; } = Task.CompletedTask;

    public CapturedTranslationSession(IRegionTextCapture capture, Func<Action, Task<bool>> publishOnUi,
        MainWindowViewModel backend, IClipboardWriter clipboard)
    {
        Backend = backend;
        Backend.InputText = "";
        Capture = new RegionCaptureSession(capture, publishOnUi);
        Popup = TranslationPopupViewModel.ForInput("Selected screen region");
        Popup.BindPronunciation(Backend.Pronunciation, Backend.SourceLanguage, Backend.TargetLanguage);
        _cancel = new RelayCommand(Cancel, () => !_closed && (Capture.IsBusy || Backend.CancelCommand.CanExecute(null)));
        Popup.BindActions(Backend.AskAiCommand, _cancel, Backend.AiSendNotice, clipboard);
        Popup.PresentCaptureStatus("Select a screen region.", false, false);
        Backend.PropertyChanged += BackendChanged;
        Popup.CloseRequested += PopupClosed;
    }

    public bool Confirm(RegionTextCaptureRequest request)
    {
        if (_closed || !Capture.Confirm(request)) return false;
        Popup.PresentCaptureStatus("Reading selected text locallyâ€¦", true, false);
        _cancel.NotifyCanExecuteChanged();
        return true;
    }

    public Task OverlayRetiredAsync()
    {
        if (_closed) return Task.CompletedTask;
        if (!_retired)
        {
            _retired = true;
            Completion = FinishCaptureAsync();
        }
        return Completion;
    }

    private async Task FinishCaptureAsync()
    {
        await Capture.OverlayRetiredAsync();
        if (_closed) return;
        if (Capture.IsCanceled)
        {
            Popup.PresentCaptureStatus("Capture canceled. Select the region again.", false, false);
            _cancel.NotifyCanExecuteChanged();
            return;
        }
        if (Capture.Result is { Outcome: RegionTextCaptureOutcome.Success, Text: { } text })
        {
            Backend.InputText = text;
            Popup.SetCapturedInput(text);
            _hasCapturedInput = true;
            Popup.RefreshBackendResult(Backend);
            StartLocalLookup();
            await LocalLookupCompletion;
        }
        else Popup.PresentCaptureStatus(Capture.Result?.Outcome switch
        {
            RegionTextCaptureOutcome.NoText => "No readable text found. Select a clearer region.",
            RegionTextCaptureOutcome.SourceChanged => "Display changed. Select the region again.",
            RegionTextCaptureOutcome.RecognizerUnavailable => $"{LanguageName(Backend.SourceLanguage)} OCR is unavailable. Install the Windows {LanguageName(Backend.SourceLanguage)} language OCR feature, or type/paste the text.",
            RegionTextCaptureOutcome.Timeout => "Capture timed out. Select the region again.",
            RegionTextCaptureOutcome.TooLarge => "Selection is too large. Select a smaller region.",
            RegionTextCaptureOutcome.Denied => "Screen capture was denied.",
            RegionTextCaptureOutcome.Unsupported => "Screen capture is unsupported on this device.",
            RegionTextCaptureOutcome.InvalidRegion => "Selection is invalid. Select the region again.",
            _ => "Screen capture failed. Select the region again."
        }, false, true);
        _cancel.NotifyCanExecuteChanged();
    }

    private static string LanguageName(Language language) => language switch
    {
        Language.English => "English",
        Language.Vietnamese => "Vietnamese",
        Language.Japanese => "Japanese",
        _ => language.ToString()
    };

    private void BackendChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed || !_hasCapturedInput || Capture.IsBusy || Capture.Result?.Outcome != RegionTextCaptureOutcome.Success) return;
        Popup.RefreshBackendResult(Backend);
        _cancel.NotifyCanExecuteChanged();
        StartLocalLookup();
    }

    private void StartLocalLookup()
    {
        if (_closed || _localLookupStarted || !Backend.LookupDictionaryCommand.CanExecute(null)) return;
        _localLookupStarted = true;
        LocalLookupCompletion = Backend.LookupDictionaryCommand.ExecuteAsync(null);
    }

    private void Cancel()
    {
        if (Capture.IsBusy)
        {
            Capture.Dismiss();
            Popup.PresentCaptureStatus("Capture canceled; waiting for native work to finish.", Capture.IsBusy, false);
        }
        if (Backend.CancelCommand.CanExecute(null)) Backend.CancelCommand.Execute(null);
        _cancel.NotifyCanExecuteChanged();
    }

    private void PopupClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        Capture.Dismiss();
        Backend.PropertyChanged -= BackendChanged;
        Popup.CloseRequested -= PopupClosed;
        Backend.Close();
        Popup.CloseCommand.Execute(null);
        _cancel.NotifyCanExecuteChanged();
    }
}
