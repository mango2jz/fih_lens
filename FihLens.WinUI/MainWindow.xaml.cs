using FihLens.WinUI.ViewModels;
using FihLens.WinUI.Views;
using FihLens.WinUI.Presentation;
using FihLens.Windows.Capture;
using FihLens.Windows.Clipboard;
using FihLens.WinUI.DesignPreview;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FihLens.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly List<Phase1Session> _previewSessions = new();
    private bool _closing;
    private bool _allowClose;
    private bool _shutdownInProgress;
    private readonly Action _cancelSharedWork;
    private readonly Func<Task> _finalizeShutdown;
    private readonly Func<MainWindowViewModel> _createBackend;
    private readonly Action<MainWindowViewModel> _releaseBackend;
    private readonly Dictionary<Phase1Session, CapturedTranslationSession> _capturePopups = new();
    private readonly List<Task> _draining = new();
    private SelectionOverlayHost? _selectionHost;
    private CapturedTranslationSession? _activeCapture;
    private Task _captureTask = Task.CompletedTask;
    public Task ShutdownCompletion { get; private set; } = Task.CompletedTask;
    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel, Func<MainWindowViewModel> createBackend, Action<MainWindowViewModel> releaseBackend, Action cancelSharedWork, Func<Task> finalizeShutdown)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _cancelSharedWork = cancelSharedWork;
        _finalizeShutdown = finalizeShutdown;
        _createBackend = createBackend;
        _releaseBackend = releaseBackend;
        InitializeComponent();
        RefreshDisplays();
        Closed += MainWindow_Closed;
        AppWindow.Closing += AppWindow_Closing;
#if DEBUG
        DesignPreviewButton.Visibility = Visibility.Visible;
        DesignPreviewStatePicker.ItemsSource = Enum.GetValues<PopupPreviewState>();
        DesignPreviewStatePicker.SelectedIndex = 0;
        DesignPreviewStatePicker.Visibility = Visibility.Visible;
        DesignPreviewThemePicker.ItemsSource = new[] { "System", "Light", "Dark" };
        DesignPreviewThemePicker.SelectedIndex = 0;
        DesignPreviewThemePicker.Visibility = Visibility.Visible;
#endif
    }

    private void RefreshDisplays()
    {
        try
        {
            CaptureDisplayPicker.ItemsSource = WindowsDisplayCatalog.GetDisplays();
            CaptureDisplayPicker.SelectedIndex = 0;
        }
        catch (Exception)
        {
            CaptureStatus.Text = "Displays could not be enumerated. Try selecting again.";
        }
    }

    private void SelectRegion_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _activeCapture is not null || _selectionHost is not null) return;
        if (ViewModel.LanguagePairUnavailableReason is { } pairReason)
        {
            CaptureStatus.Text = pairReason;
            return;
        }
        var selected = CaptureDisplayPicker.SelectedItem as CaptureDisplay;
        RefreshDisplays();
        if (CaptureDisplayPicker.ItemsSource is IReadOnlyList<CaptureDisplay> displays && selected is not null)
            CaptureDisplayPicker.SelectedItem = displays.FirstOrDefault(display => display.Source.SourceId == selected.Source.SourceId) ?? displays.FirstOrDefault();
        if (CaptureDisplayPicker.SelectedItem is not CaptureDisplay display) return;
        var backend = _createBackend();
        backend.SourceLanguage = ViewModel.SourceLanguage;
        backend.TargetLanguage = ViewModel.TargetLanguage;
        try
        {
            _activeCapture = new CapturedTranslationSession(new WindowsRegionTextCapture(), PublishOnUiAsync,
                backend, new WindowsClipboardWriter());
            _selectionHost = new SelectionOverlayHost(new SelectionOverlayViewModel(display.Source, backend.SourceLanguage), this);
            _selectionHost.Confirmed += SelectionConfirmed;
            _selectionHost.Closed += SelectionClosed;
            _selectionHost.Failed += SelectionFailed;
            SelectRegionButton.IsEnabled = false;
            CancelCaptureButton.IsEnabled = true;
            CaptureStatus.Text = "Select a region on the chosen display. Enter confirms; Escape cancels.";
            _selectionHost.Show();
        }
        catch (Exception)
        {
            CancelCapture_Click(sender, e);
            CaptureStatus.Text = "Screen selection could not open. Try again.";
            if (_activeCapture is null) _releaseBackend(backend);
        }
    }

    private Task<bool> PublishOnUiAsync(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.FromResult(true);
        }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { action(); completion.TrySetResult(true); }
            catch (Exception exception) { completion.TrySetException(exception); }
        })) completion.TrySetResult(false);
        return completion.Task;
    }

    private void SelectionConfirmed(object? sender, FihLens.Application.Capture.RegionTextCaptureRequest request)
    {
        if (!_closing && _activeCapture?.Confirm(request) == true)
            CaptureStatus.Text = "Reading selected text locally. Cancel remains available while native work drains.";
    }

    private void SelectionClosed(object? sender, EventArgs args)
    {
        if (sender is not SelectionOverlayHost host || !ReferenceEquals(host, _selectionHost)) return;
        host.Confirmed -= SelectionConfirmed;
        host.Closed -= SelectionClosed;
        host.Failed -= SelectionFailed;
        _selectionHost = null;
        if (_activeCapture is not { } session) return;
        if (!_closing && session.Capture.Request is not null && !session.Capture.IsCanceled)
            _captureTask = CompleteCaptureAsync(session);
        else FinishCapture(session, "Selection canceled.");
    }

    private void SelectionFailed(object? sender, Exception exception)
    {
        _activeCapture?.Dispose();
        CaptureStatus.Text = "Screen selection failed. Cancel retries closing the selection windows.";
        // Keep native roots and the busy gate until the host acknowledges actual retirement.
    }

    private async Task CompleteCaptureAsync(CapturedTranslationSession session)
    {
        try
        {
            await session.OverlayRetiredAsync();
            if (_closing || session.Capture.IsCanceled) return;
            var popup = new Phase1Session(session.Popup, AppWindow);
            _capturePopups.Add(popup, session);
            popup.Closed += CapturePopupClosed;
            popup.Failed += CapturePopupFailed;
            popup.Show();
            CaptureStatus.Text = "Capture finished. The popup shows local lookup; Ask AI sends only its recognized text.";
        }
        catch (Exception)
        {
            session.Dispose();
            CaptureStatus.Text = "Capture or popup could not complete. Try selecting again.";
        }
        finally
        {
            bool retained = _capturePopups.ContainsValue(session);
            if (!retained)
            {
                session.Dispose();
                _releaseBackend(session.Backend);
            }
            if (ReferenceEquals(_activeCapture, session)) _activeCapture = null;
            if (!_closing)
            {
                SelectRegionButton.IsEnabled = true;
                CancelCaptureButton.IsEnabled = false;
                if (session.Capture.IsCanceled) CaptureStatus.Text = "Capture canceled. Native work has finished.";
            }
        }
    }

    private void FinishCapture(CapturedTranslationSession session, string status)
    {
        session.Dispose();
        _releaseBackend(session.Backend);
        if (ReferenceEquals(_activeCapture, session)) _activeCapture = null;
        if (_closing) return;
        SelectRegionButton.IsEnabled = true;
        CancelCaptureButton.IsEnabled = false;
        CaptureStatus.Text = status;
    }

    private void CancelCapture_Click(object sender, RoutedEventArgs e)
    {
        _activeCapture?.Dispose();
        if (_selectionHost is not null)
        {
            if (!_selectionHost.Close()) CaptureStatus.Text = "Selection windows are still closing. Cancel retries cleanup.";
        }
        else if (_activeCapture is { } session && !session.Capture.IsBusy)
            FinishCapture(session, "Selection canceled.");
        else CaptureStatus.Text = "Capture canceled; waiting for native work to finish.";
    }

    private void CapturePopupClosed(object? sender, EventArgs args)
    {
        if (sender is not Phase1Session popup || !_capturePopups.Remove(popup, out var session)) return;
        popup.Closed -= CapturePopupClosed;
        popup.Failed -= CapturePopupFailed;
        TrackBackendWork(session.Backend);
        session.Dispose();
        _releaseBackend(session.Backend);
    }

    private void CapturePopupFailed(object? sender, EventArgs args)
    {
        if (!_closing) CaptureStatus.Text = "The capture popup could not fit or close. Select again to retry.";
    }

    private void TrackBackendWork(MainWindowViewModel backend)
    {
        if (backend.LookupDictionaryCommand.ExecutionTask is { } lookup) _draining.Add(lookup);
        if (backend.AskAiCommand.ExecutionTask is { } ai) _draining.Add(ai);
        _draining.RemoveAll(task => task.IsCompletedSuccessfully);
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        if (ViewModel.LookupDictionaryCommand.CanExecute(null))
            ViewModel.LookupDictionaryCommand.Execute(null);
    }

    private void AskAi_Click(object sender, RoutedEventArgs e) => AiDetails.IsExpanded = true;

    private void DesignPreview_Click(object sender, RoutedEventArgs e)
    {
#if DEBUG
        if (_closing) return;
        Phase1Session? session = null;
        try
        {
            DesignPreviewError.Visibility = Visibility.Collapsed;
            var preview = ViewModel.SourceLanguage == FihLens.Core.Enums.Language.Japanese
                ? Phase1PreviewData.Japanese(ViewModel.TargetLanguage) : Phase1PreviewData.Compact();
            if (DesignPreviewStatePicker.SelectedItem is PopupPreviewState state) preview.SetPreviewState(state);
            session = new Phase1Session(preview, AppWindow);
            session.Closed += Preview_Closed;
            session.Failed += Preview_Failed;
            _previewSessions.Add(session);
            session.Show(DesignPreviewThemePicker.SelectedIndex switch
            {
                1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default
            });
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Phase1 preview stage=open exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
            try { session?.Dispose(); }
            catch (Exception cleanupException)
            {
                System.Diagnostics.Debug.WriteLine($"Phase1 preview stage=open-cleanup exceptionType={cleanupException.GetType().FullName} hresult=0x{cleanupException.HResult:X8}");
            }
            finally { ShowPreviewFailure(); }
        }
#endif
    }

    private void Preview_Closed(object? sender, EventArgs args)
    {
        if (sender is not Phase1Session session) return;
        session.Closed -= Preview_Closed;
        session.Failed -= Preview_Failed;
        _previewSessions.Remove(session);
    }

    private void Preview_Failed(object? sender, EventArgs args) => ShowPreviewFailure();

    private void ShowPreviewFailure()
    {
        if (_closing) return;
        DesignPreviewError.Text = "Design preview could not open, fit, or close correctly. The dictionary remains available.";
        DesignPreviewError.Visibility = Visibility.Visible;
    }

    private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_shutdownInProgress) return;
        _shutdownInProgress = true;
        ShutdownCompletion = ShutdownAndCloseAsync();
    }

    private async Task ShutdownAndCloseAsync()
    {
        _closing = true;
        bool retired = true;
        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception exception) { retired = false; ReportShutdownFailure(exception); }
        }
        try
        {
            Attempt(_cancelSharedWork);
            TrackBackendWork(ViewModel);
            Attempt(ViewModel.Close);
            if (_activeCapture is { } active) Attempt(active.Dispose);
            if (_selectionHost is not null)
            {
                try { retired &= _selectionHost.Close(); }
                catch (Exception exception) { retired = false; ReportShutdownFailure(exception); }
            }
            foreach (var popup in _capturePopups.Keys.ToArray())
            {
                try { popup.Dispose(); }
                catch (Exception exception) { retired = false; ReportShutdownFailure(exception); }
            }
            foreach (var preview in _previewSessions.ToArray())
            {
                try { preview.Dispose(); }
                catch (Exception exception) { retired = false; ReportShutdownFailure(exception); }
            }
            _draining.Add(_captureTask);
            CaptureStatus.Text = "Closing: waiting for native capture and backend requests to finish.";
            await Task.WhenAll(_draining);
            if (!retired || _selectionHost is not null || _capturePopups.Count > 0 || _previewSessions.Count > 0)
            {
                CaptureStatus.Text = "Some windows could not close. Close the launcher again to retry cleanup.";
                return;
            }
            await _finalizeShutdown();
            // Let the canceled native Closing callback return before issuing the final close.
            await Task.Yield();
            _allowClose = true;
            Close();
        }
        catch (Exception exception)
        {
            ReportShutdownFailure(exception);
            CaptureStatus.Text = "Shutdown could not finish. Close the launcher again to retry cleanup.";
        }
        finally
        {
            _shutdownInProgress = false;
            // A failed shutdown keeps native owners rooted and the dispatcher alive for another close.
        }
    }

    private static void ReportShutdownFailure(Exception exception) =>
        System.Diagnostics.Debug.WriteLine($"Phase1 shutdown exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        AppWindow.Closing -= AppWindow_Closing;
        Closed -= MainWindow_Closed;
    }
}
