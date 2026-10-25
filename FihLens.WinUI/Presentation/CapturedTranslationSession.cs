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
    private bool _lookupReady;
    private readonly RelayCommand _cancel;
    public RegionCaptureSession? Capture { get; }
    public MainWindowViewModel Backend { get; }
    public TranslationPopupViewModel Popup { get; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public Task LocalLookupCompletion { get; private set; } = Task.CompletedTask;
    public bool IsClosed => _closed;

    public CapturedTranslationSession(IRegionTextCapture capture, Func<Action, Task<bool>> publishOnUi,
        MainWindowViewModel backend, IClipboardWriter clipboard)
        : this(new RegionCaptureSession(capture, publishOnUi), backend, clipboard, "Selected screen region")
    {
        // A region holds running text, so the popup offers its words one at a time.
        Backend.SplitDictionaryWords = true;
        Popup.PresentCaptureStatus("Select a screen region.", false, false);
    }

    public CapturedTranslationSession(MainWindowViewModel backend, IClipboardWriter clipboard)
        : this(null, backend, clipboard, "Selected text")
    {
        Popup.PresentCaptureStatus("Reading the selected region with OCR locally…", true, false);
    }

    private CapturedTranslationSession(RegionCaptureSession? capture, MainWindowViewModel backend,
        IClipboardWriter clipboard, string title)
    {
        Backend = backend;
        Backend.InputText = "";
        Capture = capture;
        Popup = TranslationPopupViewModel.ForInput(title);
        Popup.BindPronunciation(Backend.Pronunciation, Backend.SourceLanguage, Backend.TargetLanguage);
        _cancel = new RelayCommand(Cancel, () => !_closed && (Capture?.IsBusy == true || Backend.CancelCommand.CanExecute(null)));
        Popup.BindActions(Backend.AskAiCommand, _cancel, Backend.AiSendNotice, clipboard);
        Backend.PropertyChanged += BackendChanged;
        Popup.CloseRequested += PopupClosed;
    }

    public bool AdoptInput(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (_closed || _hasCapturedInput) return false;
        _hasCapturedInput = true;
        Backend.InputText = text;
        Popup.SetCapturedInput(text);
        Popup.RefreshBackendResult(Backend);
        return true;
    }

    public void PopupShown()
    {
        if (_closed || _lookupReady) return;
        _lookupReady = true;
        if (_hasCapturedInput) StartLocalLookup();
    }

    public bool Confirm(RegionTextCaptureRequest request)
    {
        if (_closed || Capture is null || !Capture.Confirm(request)) return false;
        Popup.PresentCaptureStatus("Reading selected text locally…", true, false);
        _cancel.NotifyCanExecuteChanged();
        return true;
    }

    public Task OverlayRetiredAsync()
    {
        if (_closed || Capture is null) return Task.CompletedTask;
        if (!_retired)
        {
            _retired = true;
            Completion = FinishCaptureAsync();
        }
        return Completion;
    }

    private async Task FinishCaptureAsync()
    {
        var capture = Capture;
        if (capture is null) return;
        await capture.OverlayRetiredAsync();
        if (_closed) return;
        if (capture.IsCanceled)
        {
            Popup.PresentCaptureStatus("Capture canceled. Select the region again.", false, false);
            _cancel.NotifyCanExecuteChanged();
            return;
        }
        if (capture.Result is { Outcome: RegionTextCaptureOutcome.Success, Text: { } text })
        {
            AdoptInput(text);
            _lookupReady = true;
            StartLocalLookup();
            await LocalLookupCompletion;
        }
        else Popup.PresentCaptureStatus(capture.Result?.Outcome switch
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
        if (_closed || !_lookupReady || !_hasCapturedInput || Capture?.IsBusy == true ||
            (Capture is not null && Capture.Result?.Outcome != RegionTextCaptureOutcome.Success)) return;
        Popup.RefreshBackendResult(Backend);
        _cancel.NotifyCanExecuteChanged();
        StartLocalLookup();
    }

    private void StartLocalLookup()
    {
        if (_closed || !_lookupReady || _localLookupStarted || !Backend.LookupDictionaryCommand.CanExecute(null)) return;
        _localLookupStarted = true;
        LocalLookupCompletion = Backend.LookupDictionaryCommand.ExecuteAsync(null);
    }

    private void Cancel()
    {
        if (Capture?.IsBusy == true)
        {
            Capture.Dismiss();
            Popup.PresentCaptureStatus("Capture canceled; waiting for native work to finish.", Capture?.IsBusy == true, false);
        }
        if (Backend.CancelCommand.CanExecute(null)) Backend.CancelCommand.Execute(null);
        _cancel.NotifyCanExecuteChanged();
    }

    private void PopupClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        Capture?.Dismiss();
        Backend.PropertyChanged -= BackendChanged;
        Popup.CloseRequested -= PopupClosed;
        Backend.Close();
        Popup.CloseCommand.Execute(null);
        _cancel.NotifyCanExecuteChanged();
    }
}
