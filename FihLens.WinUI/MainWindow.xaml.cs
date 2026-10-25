using FihLens.WinUI.ViewModels;
using FihLens.WinUI.Views;
using FihLens.WinUI.Presentation;
using FihLens.Application.Capture;
using FihLens.Windows.Capture;
using FihLens.Windows.Windowing;
using FihLens.Windows.Clipboard;
using FihLens.WinUI.DesignPreview;
using FihLens.Application.Dictionary;
using FihLens.Core.Enums;
using FihLens.Core.Models;
using FihLens.WinUI.Windowing;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using NativeBackgroundShell = FihLens.Windows.Shell.WindowsBackgroundShell;
using NativeShellCommand = FihLens.Windows.Shell.BackgroundShellCommand;

namespace FihLens.WinUI;

public sealed partial class MainWindow : Window
{
    private enum Detail { None, Dictionary, Ai }
    private const double BarWidth = 680;
    // The dictionary dialog needs more room than the bar; the window grows while it is open.
    private const double DialogHeight = 640;
    private const double DetailMaxHeight = 560, DetailMinHeight = 120;
    private const string CopiedMessage = "Copied.";
    // A short answer (a word or phrase) takes the hero size; a sentence takes the answer size.
    private const int HeroMaxLength = 32;
    private Detail _detail;
    private bool _dialogOpen;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _lookupTimer;
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
    private readonly IDictionaryPackManager? _dictionaryPacks;
    private readonly WindowsSelectedTextCapture? _selectedTextCapture;
    private readonly CancellationTokenSource _dictionaryLifetime = new();
    private DictionaryManagementDialog? _dictionaryDialog;
    private Task _dictionaryDialogTask = Task.CompletedTask;
    private DictionarySource? _legacySource;
    private string? _legacyReason = "Loading existing approved local data.";
    private SelectionOverlayHost? _selectionHost;
    private CapturedTranslationSession? _activeCapture;
    private SelectedTextCaptureSession? _activeSelectedCapture;
    private Task _captureTask = Task.CompletedTask;
    private readonly BackgroundShellController _shellController;
    private NativeBackgroundShell? _shell;
    private bool _hiddenToTray;
    public Task ShutdownCompletion { get; private set; } = Task.CompletedTask;
    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel, Func<MainWindowViewModel> createBackend, Action<MainWindowViewModel> releaseBackend, Action cancelSharedWork, Func<Task> finalizeShutdown,
        IDictionaryPackManager? dictionaryPacks = null, WindowsSelectedTextCapture? selectedTextCapture = null)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _cancelSharedWork = cancelSharedWork;
        _finalizeShutdown = finalizeShutdown;
        _createBackend = createBackend;
        _releaseBackend = releaseBackend;
        _dictionaryPacks = dictionaryPacks;
        _selectedTextCapture = selectedTextCapture;
        _shellController = new(ShowLauncher, RequestRegionCapture, BeginExit);
        ViewModel.InputText = "";
        InitializeComponent();
        // Borderless, always on top, out of the taskbar and Alt+Tab: shown by the hotkey, tray or a second launch.
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.IsShownInSwitchers = false;
        _lookupTimer = DispatcherQueue.CreateTimer();
        _lookupTimer.Interval = TimeSpan.FromMilliseconds(150);
        _lookupTimer.IsRepeating = false;
        _lookupTimer.Tick += LookupTimer_Tick;
        // Status lines take no space while empty.
        foreach (var status in new[] { PairStatus, ShellStatus, CaptureStatus, PronunciationStatus })
            status.RegisterPropertyChangedCallback(TextBlock.TextProperty, (line, _) => line.SetValue(UIElement.VisibilityProperty, Shown(((TextBlock)line).Text)));
        ((FrameworkElement)Content).Loaded += LauncherLoaded;
        BarRoot.SizeChanged += BarRoot_SizeChanged;
        WindowRoot.PreviewKeyDown += Root_PreviewKeyDown;
        WindowRoot.KeyDown += Root_KeyDown;
        ResultsList.PreviewKeyDown += ResultsList_PreviewKeyDown;
        Activated += Launcher_Activated;
        RefreshDisplays();
        UpdateBody();
        UpdateAiDetail();
        ShowTabPanel();
        ViewModel.AskAiCommand.PropertyChanged += AskAiCommand_PropertyChanged;
        Closed += MainWindow_Closed;
        AppWindow.Closing += AppWindow_Closing;
        ViewModel.PropertyChanged += LauncherBackendChanged;
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

    public void SetLegacyDictionarySource(DictionarySource? source, string? reason)
    {
        _legacySource = source;
        _legacyReason = reason;
        _dictionaryDialog?.ViewModel.SetLegacySource(source, reason);
    }

    public void AttachShell(NativeBackgroundShell shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        shell.CommandRequested += Shell_CommandRequested;
        shell.SelectedTextRequested += Shell_SelectedTextRequested;
        shell.StateChanged += Shell_StateChanged;
        shell.Initialize();
        PublishShellState();
    }

    public void SetShellUnavailable(int? errorCode)
    {
        _shellController.SetAvailability(false, errorCode, false, errorCode);
        ShellStatus.Text = "The tray icon and hotkey could not start. FIH Lens stays open; use Exit to quit.";
        RetryHotkeyButton.IsEnabled = false;
        RetryShellButton.IsEnabled = false;
        ExitButton.Visibility = Visibility.Visible;
    }

    public void ReopenFromActivation() => _shellController.Execute(ShellCommand.Open);

    private void Shell_CommandRequested(NativeShellCommand command)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var action = command switch
                {
                    NativeShellCommand.Open => ShellCommand.Open,
                    NativeShellCommand.Capture => ShellCommand.Capture,
                    _ => ShellCommand.Exit
                };
                var outcome = _shellController.Execute(action);
                if (action == ShellCommand.Capture)
                {
                    if (outcome == RegionCaptureRequestOutcome.Busy && _selectionHost is null)
                        ShowLauncher();
                    if (outcome != RegionCaptureRequestOutcome.Busy || _selectionHost is null)
                        ShowCaptureRequestResult(outcome ?? RegionCaptureRequestOutcome.Stopping);
                }
            }
            catch (Exception exception) { ReportShutdownFailure(exception); }
        }))
            System.Diagnostics.Debug.WriteLine("Background shell command was dropped because the UI dispatcher is stopping.");
    }

    private void Shell_SelectedTextRequested(SelectedTextSource source)
    {
        if (!DispatcherQueue.TryEnqueue(() => StartSelectedTextCapture(source)))
            System.Diagnostics.Debug.WriteLine("Selected-text invocation was dropped because the UI dispatcher is stopping.");
    }

    private void Shell_StateChanged(object? sender, EventArgs args)
    {
        if (!DispatcherQueue.TryEnqueue(PublishShellState))
            System.Diagnostics.Debug.WriteLine("Background shell state update was dropped because the UI dispatcher is stopping.");
    }

    private void PublishShellState()
    {
        if (_shell is null) return;
        _shellController.SetAvailability(_shell.IsHotkeyRegistered, _shell.HotkeyErrorCode,
            _shell.IsTrayAvailable, _shell.TrayErrorCode);
        bool trayFailed = !_shell.IsTrayAvailable || _shell.TrayErrorCode is not null;
        if (_hiddenToTray && !_shellController.CanHide(!trayFailed, _shell.IsHotkeyRegistered, false)) ShowLauncher();
        ExitButton.Visibility = trayFailed ? Visibility.Visible : Visibility.Collapsed;
        if (_shellController.IsStopping)
        {
            ShellStatus.Text = _shellController.ExitFailed
                ? "FIH Lens could not finish closing. Use Exit or Close to retry cleanup."
                : "FIH Lens is closing. New captures are unavailable.";
        }
        else
        {
            var failures = new List<string>();
            if (trayFailed)
                failures.Add($"The tray icon needs attention (Windows error {_shell.TrayErrorCode?.ToString() ?? "unknown"}). Retry tray, or use Exit to quit.");
            if (!_shell.IsHotkeyRegistered)
                failures.Add(_shell.HotkeyErrorCode == 1409
                    ? $"{NativeBackgroundShell.HotkeyLabel} is already in use. Selected-text capture is unavailable; select a screen region here or from the tray. Retry hotkey when it is free."
                    : $"{NativeBackgroundShell.HotkeyLabel} could not be registered (Windows error {_shell.HotkeyErrorCode?.ToString() ?? "unknown"}). Select a screen region here or from the tray; Retry hotkey.");
            ShellStatus.Text = string.Join(" ", failures);
        }
        RetryHotkeyButton.IsEnabled = !_shell.IsHotkeyRegistered && !_shellController.IsStopping;
        RetryShellButton.IsEnabled = (!_shell.IsHotkeyRegistered || !_shell.IsTrayAvailable ||
            _shell.HotkeyErrorCode is not null || _shell.TrayErrorCode is not null) && !_shellController.IsStopping;
    }

    private void ShowLauncher()
    {
        InvalidateSelectedCapture();
        if (_shellController.IsStopping)
            ShellStatus.Text = _shellController.ExitFailed
                ? "FIH Lens could not finish closing. Use Exit or Close to retry cleanup."
                : "FIH Lens is closing. New captures are unavailable.";
        RaiseLauncher();
    }

    private void RaiseLauncher()
    {
        _hiddenToTray = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.State == OverlappedPresenterState.Minimized) presenter.Restore();
            PlaceLauncher(onCursorDisplay: true);
            AppWindow.Show();
            PinOnTop(presenter);
        }
        else AppWindow.Show();
        Activate();
        InputBox.Focus(FocusState.Programmatic);
        InputBox.SelectAll();
    }

    // Activation is refused while another app is foreground; topmost still raises the bar above it.
    // The toggle re-applies topmost, which does not stick when set before the window is first shown.
    private static void PinOnTop(OverlappedPresenter presenter)
    {
        presenter.IsAlwaysOnTop = false;
        presenter.IsAlwaysOnTop = true;
    }

    private bool HideLauncher()
    {
        bool trayAvailable = _shell is { IsTrayAvailable: true, TrayErrorCode: null };
        if (_closing || !AppWindow.IsVisible ||
            !_shellController.CanHide(trayAvailable, _shell?.IsHotkeyRegistered == true, !_dictionaryDialogTask.IsCompleted)) return false;
        InvalidateSelectedCapture();
        _hiddenToTray = true;
        AppWindow.Hide();
        return true;
    }

    private void Launcher_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) HideLauncher();
    }

    private void BarRoot_SizeChanged(object sender, SizeChangedEventArgs args) => PlaceLauncher(onCursorDisplay: false);

    private void PlaceLauncher(bool onCursorDisplay)
    {
        if (Content.XamlRoot is not { } root) return;
        try
        {
            var display = onCursorDisplay && GetCursorPos(out var cursor)
                ? DisplayArea.GetFromPoint(cursor, DisplayAreaFallback.Primary)
                : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var work = display.WorkArea;
            // The Canvas root never limits the bar, so cap the detail pane to what the work area leaves.
            double spare = work.Height / root.RasterizationScale - BarRoot.ActualHeight;
            double current = DetailPanel.Visibility == Visibility.Visible ? DetailPanel.ActualHeight : DetailPanel.MaxHeight;
            DetailPanel.MaxHeight = Math.Clamp(current + spare, DetailMinHeight, DetailMaxHeight);
            double height = Math.Max(BarRoot.ActualHeight, _dialogOpen ? DialogHeight : 0);
            if (height <= 0) return;
            var bounds = PopupPlacement.CalculateSpotlight(new PixelRect(work.X, work.Y, work.Width, work.Height),
                BarWidth, height, root.RasterizationScale);
            AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            BarRoot.Width = bounds.Width / root.RasterizationScale;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Launcher placement exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointInt32 point);

    private void StartSelectedTextCapture(SelectedTextSource source)
    {
        if (_closing || _shellController.IsStopping || _selectedTextCapture is not { } owner || owner.IsBusy) return;
        if (_activeSelectedCapture is { } active && !active.IsOffered) return;
        if (_activeCapture is not null || _selectionHost is not null || !_dictionaryDialogTask.IsCompleted) return;
        InvalidateSelectedCapture();
        if (ViewModel.LanguagePairUnavailableReason is { } pairReason)
        {
            ShowLauncher();
            CaptureStatus.Text = pairReason;
            return;
        }

        var lease = owner.CreateSource(source);
        SelectedTextCaptureSession? session = null;
        session = new SelectedTextCaptureSession(lease, () => lease.DrainCompletion, PublishOnUiAsync,
            () => lease.ValidateForCommit(out var outcome) ? null : outcome,
            () => lease.ValidateForOffer(out var outcome) ? null : outcome,
            text => CommitSelectedText(session!, source, text),
            outcome => OfferSelectedRegion(session!, outcome),
            ReportSelectedCaptureFailure);
        _activeSelectedCapture = session;
        CancelCaptureButton.IsEnabled = true;
        CaptureStatus.Text = "Reading selected text from the active application…";
        session.Start();
        _draining.Add(ObserveSelectedCaptureAsync(session));
    }

    private async Task ObserveSelectedCaptureAsync(SelectedTextCaptureSession session)
    {
        try { await session.Completion; }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Selected capture stage=publication exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
        }

        if (!ReferenceEquals(_activeSelectedCapture, session) || session.IsOffered || session.IsCommitted) return;
        _activeSelectedCapture = null;
        session.Dispose();
        CancelCaptureButton.IsEnabled = false;
        if (!_closing && session.Outcome == SelectedTextCaptureOutcome.SourceChanged)
        {
            CaptureStatus.Text = "The source selection changed. Invoke capture again with the text selected.";
        }
    }

    private void CommitSelectedText(SelectedTextCaptureSession attempt, SelectedTextSource source, string text)
    {
        if (_closing || !ReferenceEquals(_activeSelectedCapture, attempt) || attempt.IsClosed) return;
        var backend = _createBackend();
        CapturedTranslationSession? session = null;
        Phase1Session? popup = null;
        bool popupOwnsBackend = false;
        try
        {
            backend.SourceLanguage = ViewModel.SourceLanguage;
            backend.TargetLanguage = ViewModel.TargetLanguage;
            session = new CapturedTranslationSession(backend, new WindowsClipboardWriter());
            if (!session.AdoptInput(text)) throw new InvalidOperationException("Selected input was not accepted.");
            popup = new Phase1Session(session.Popup, AppWindow, source.Origin);
            session.Popup.SetOpenDetail(() => ExplainCapture(session));
            _capturePopups.Add(popup, session);
            popupOwnsBackend = true;
            popup.Closed += CapturePopupClosed;
            popup.Failed += CapturePopupFailed;
            popup.Show();
            if (_capturePopups.ContainsValue(session)) session.PopupShown();
            CaptureStatus.Text = "Selected text is ready. Ask AI sends only this text when you choose it.";
        }
        catch
        {
            if (session is not null && !popupOwnsBackend)
            {
                session.Dispose();
                _releaseBackend(backend);
            }
            else if (popup is not null && _capturePopups.ContainsKey(popup))
            {
                try { popup.Dispose(); }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine($"Selected capture stage=popup-close exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
                }
            }
            _activeSelectedCapture = null;
            attempt.Dispose();
            throw;
        }
        finally
        {
            if (!popupOwnsBackend && session is null) _releaseBackend(backend);
        }

        _activeSelectedCapture = null;
        CancelCaptureButton.IsEnabled = false;
        attempt.Dispose();
    }

    private void OfferSelectedRegion(SelectedTextCaptureSession attempt, SelectedTextCaptureOutcome outcome)
    {
        if (_closing || !ReferenceEquals(_activeSelectedCapture, attempt) || attempt.IsClosed) return;
        CaptureStatus.Text = $"{SelectedCaptureMessage(outcome)} Select a screen region or cancel.";
        SelectRegionButton.IsEnabled = true;
        OfferRegionButton.Visibility = Visibility.Visible;
        CancelCaptureButton.IsEnabled = true;
        RaiseLauncher();
    }

    private void ReportSelectedCaptureFailure(SelectedTextCaptureOutcome outcome)
    {
        if (_closing) return;
        if (outcome == SelectedTextCaptureOutcome.SourceChanged)
        {
            CaptureStatus.Text = SelectedCaptureMessage(outcome);
            return;
        }
        ShowLauncher();
        CaptureStatus.Text = SelectedCaptureMessage(outcome);
    }

    private static string SelectedCaptureMessage(SelectedTextCaptureOutcome outcome) => outcome switch
    {
        SelectedTextCaptureOutcome.Sensitive => "The selected control is marked sensitive. FIH Lens did not read it.",
        SelectedTextCaptureOutcome.SourceChanged => "The source selection changed. Invoke capture again with the text selected.",
        SelectedTextCaptureOutcome.Timeout => "Selected-text capture timed out.",
        SelectedTextCaptureOutcome.Unavailable => "Selected text is unavailable from this control.",
        SelectedTextCaptureOutcome.Unsupported => "This control does not provide selected text.",
        SelectedTextCaptureOutcome.NoText => "No selected text was found.",
        SelectedTextCaptureOutcome.Denied => "Windows denied access to the selected text.",
        SelectedTextCaptureOutcome.MultipleRanges => "This control has multiple selected ranges.",
        SelectedTextCaptureOutcome.TooLarge => "The selection is too large to capture.",
        _ => "Selected-text capture failed safely."
    };

    private void InvalidateSelectedCapture()
    {
        if (_activeSelectedCapture is not { } active) return;
        _activeSelectedCapture = null;
        active.Dispose();
        SelectRegionButton.IsEnabled = true;
        CancelCaptureButton.IsEnabled = false;
        OfferRegionButton.Visibility = Visibility.Collapsed;
    }

    private void LauncherBackendChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainWindowViewModel.SourceLanguage) or nameof(MainWindowViewModel.TargetLanguage))
            InvalidateSelectedCapture();
        if (args.PropertyName is nameof(MainWindowViewModel.InputText) or nameof(MainWindowViewModel.SourceLanguage)
            or nameof(MainWindowViewModel.TargetLanguage))
        {
            if (CaptureStatus.Text == CopiedMessage) CaptureStatus.Text = "";
            _detail = Detail.None;
            UpdateBody();
            _lookupTimer.Stop();
            _lookupTimer.Start();
        }
        if (args.PropertyName is nameof(MainWindowViewModel.Result) or nameof(MainWindowViewModel.ErrorMessage)
            or nameof(MainWindowViewModel.WordCards) or nameof(MainWindowViewModel.IsBusy) or nameof(MainWindowViewModel.AiStatusText))
        {
            UpdateAiDetail();
            UpdateBody();
        }
        else if (args.PropertyName is nameof(MainWindowViewModel.DictionaryCards))
        {
            UpdateBody();
            // x:Bind refreshes ItemsSource from the same notification; select after it has run.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ResultsList.Items.Count > 0 && ResultsList.SelectedIndex < 0) ResultsList.SelectedIndex = 0;
            });
        }
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateBody()
    {
        bool empty = string.IsNullOrWhiteSpace(ViewModel.InputText);
        QuickTiles.Visibility = Show(empty && _detail == Detail.None);
        ResultsPanel.Visibility = Show(!empty && _detail == Detail.None);
        DetailPanel.Visibility = Show(_detail != Detail.None);
        DictionaryDetailCard.Visibility = Show(_detail == Detail.Dictionary);
        AiDetail.Visibility = Show(_detail == Detail.Ai);
        DictionaryStatusLine.Visibility = Show(ViewModel.DictionaryCards.Count == 0);
        PrivacyFooter.Visibility = Show(empty && _detail == Detail.None);
        KeyHints.Visibility = Show(!empty && _detail == Detail.None);
        DetailHints.Visibility = Show(_detail != Detail.None);
        TabHint.Visibility = Show(_detail == Detail.Ai && ViewModel.Result is not null);
    }

    // Loading, failure and answer states of the AI detail; the answer text picks hero or answer size by length.
    private void UpdateAiDetail()
    {
        var result = ViewModel.Result;
        bool asking = ViewModel.AskAiCommand.IsRunning;
        string? error = ViewModel.IsAiAvailable ? ViewModel.ErrorMessage : ViewModel.AiSendNotice;
        AiLoading.Visibility = Show(asking);
        AiError.Visibility = Show(!asking && !string.IsNullOrEmpty(error));
        AiErrorText.Text = error ?? "";
        // The failure strip already says what happened; the status line would repeat it.
        AiStatusLine.Visibility = Show(string.IsNullOrEmpty(error) || asking);
        AiRetryButton.Visibility = Show(ViewModel.IsAiAvailable);
        AnswerText.Visibility = Show(result is not null);
        AnswerActions.Visibility = Show(result is not null);
        AiTabs.Visibility = Show(result is not null);
        AnswerText.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[
            (result?.NaturalTranslation?.Length ?? 0) <= HeroMaxLength ? "Phase1HeroTextStyle" : "Phase1AnswerTextStyle"];
        NoWordsText.Visibility = Show(ViewModel.WordCards.Count == 0);
        NoGrammarText.Visibility = Show(result is { Grammar.Count: 0 });
        NoOtherWaysText.Visibility = Show(result is { Alternatives.Count: 0 });
    }

    private void ShowTabPanel()
    {
        // Checked fires while InitializeComponent is still creating the panels.
        if (WordsPanel is null || ExplainPanel is null || GrammarPanel is null || OtherWaysPanel is null) return;
        WordsPanel.Visibility = Show(WordsTab.IsChecked == true);
        ExplainPanel.Visibility = Show(ExplainTab.IsChecked == true);
        GrammarPanel.Visibility = Show(GrammarTab.IsChecked == true);
        OtherWaysPanel.Visibility = Show(OtherWaysTab.IsChecked == true);
    }

    private void AnswerTab_Checked(object sender, RoutedEventArgs e) => ShowTabPanel();

    private void CopyAnswer_Click(object sender, RoutedEventArgs e) => CopyAnswer();

    private void AskAiRetry_Click(object sender, RoutedEventArgs e) => AskAi();

    private void AskAiCommand_PropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateAiDetail();

    private void LookupTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => LookupNow();

    // Typing searches the local dictionary; AI never runs from here.
    private void LookupNow()
    {
        if (_closing || string.IsNullOrWhiteSpace(ViewModel.InputText) || ViewModel.DictionaryInputSnapshot == ViewModel.InputText) return;
        if (ViewModel.LookupDictionaryCommand.CanExecute(null)) ViewModel.LookupDictionaryCommand.Execute(null);
        else if (ViewModel.IsBusy) _lookupTimer.Start();
    }

    private void OpenCard(DictionaryCardViewModel card)
    {
        DictionaryDetailCard.Items = [card];
        ShowDetail(Detail.Dictionary);
    }

    private void ShowDetail(Detail detail)
    {
        if (detail == Detail.Ai) WordsTab.IsChecked = true;
        // Park focus on the input first, so collapsing a focused result row does not send it to the footer.
        InputBox.Focus(FocusState.Programmatic);
        _detail = detail;
        UpdateBody();
        DetailPanel.ChangeView(null, 0, null, true);
        // The button was collapsed until now; focus it once layout has made it focusable.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => BackButton.Focus(FocusState.Keyboard));
    }

    private void Back()
    {
        _detail = Detail.None;
        UpdateBody();
        InputBox.Focus(FocusState.Programmatic);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Back();

    private async void AskAi()
    {
        if (_closing || string.IsNullOrWhiteSpace(ViewModel.InputText)) return;
        bool answered = ViewModel.Result is not null && ViewModel.AiInputSnapshot == ViewModel.InputText;
        // A keystroke lookup may still be running; it finishes in milliseconds.
        if (!answered && ViewModel.LookupDictionaryCommand.ExecutionTask is { IsCompleted: false } lookup)
        {
            try { await lookup; }
            catch (Exception) { /* The view model already published the lookup failure. */ }
        }
        if (_closing) return;
        ShowDetail(Detail.Ai);
        if (!answered && ViewModel.AskAiCommand.CanExecute(null)) ViewModel.AskAiCommand.Execute(null);
    }

    private bool CopyAnswer()
    {
        string? text = _detail == Detail.Ai ? ViewModel.Result?.NaturalTranslation
            : (ResultsList.SelectedItem as DictionaryCardViewModel)?.CopyText;
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            new WindowsClipboardWriter().WriteTextAsync(text, CancellationToken.None).GetAwaiter().GetResult();
            CaptureStatus.Text = CopiedMessage;
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
        {
            CaptureStatus.Text = "Could not copy. Another app is using the clipboard; try again.";
        }
        return true;
    }

    private bool IsFocusInResults()
    {
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot) as DependencyObject;
        return ReferenceEquals(focused, InputBox) || (focused is not null && ResultsList.IndexFromContainer(focused) >= 0);
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsDown(VirtualKey.Control);
        if (e.Key == VirtualKey.Tab && !ctrl && !IsDown(VirtualKey.Shift) && _detail == Detail.None &&
            !string.IsNullOrWhiteSpace(ViewModel.InputText) && IsFocusInResults())
        {
            e.Handled = true;
            AskAi();
        }
        else if (ctrl && (int)e.Key == 190) // VK_OEM_PERIOD
        {
            e.Handled = true;
            SwapLanguages();
        }
        else if (ctrl && e.Key == VirtualKey.C &&
                 FocusManager.GetFocusedElement(Content.XamlRoot) is not (TextBox { SelectionLength: > 0 } or TextBlock { SelectedText.Length: > 0 }))
        {
            e.Handled = CopyAnswer();
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            if (!HideLauncher() && _detail != Detail.None) Back();
        }
        else if (_detail == Detail.Ai && ViewModel.Result is not null && TabForKey(e.Key) is { } tab &&
                 FocusManager.GetFocusedElement(Content.XamlRoot) is not TextBox)
        {
            e.Handled = true;
            tab.IsChecked = true;
        }
        else if (e.Key == VirtualKey.Left && _detail != Detail.None && FocusManager.GetFocusedElement(Content.XamlRoot) is not TextBox)
        {
            e.Handled = true;
            Back();
        }
    }

    private RadioButton? TabForKey(VirtualKey key) => key switch
    {
        VirtualKey.Number1 or VirtualKey.NumberPad1 => WordsTab,
        VirtualKey.Number2 or VirtualKey.NumberPad2 => ExplainTab,
        VirtualKey.Number3 or VirtualKey.NumberPad3 => GrammarTab,
        VirtualKey.Number4 or VirtualKey.NumberPad4 => OtherWaysTab,
        _ => null
    };

    private void ResultsList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Up || ResultsList.SelectedIndex > 0) return;
        e.Handled = true;
        InputBox.Focus(FocusState.Keyboard);
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DictionaryCardViewModel card) OpenCard(card);
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var card in e.RemovedItems.OfType<DictionaryCardViewModel>()) card.IsSelected = false;
        foreach (var card in e.AddedItems.OfType<DictionaryCardViewModel>()) card.IsSelected = true;
    }

    private void LanguageMenu_Opening(object sender, object e)
    {
        var menu = (MenuFlyout)sender;
        var current = ReferenceEquals(menu, SourceMenu) ? ViewModel.SourceLanguage : ViewModel.TargetLanguage;
        foreach (var item in menu.Items.OfType<RadioMenuFlyoutItem>())
            item.IsChecked = Enum.Parse<Language>((string)item.Tag) == current;
    }

    private void SourceLanguage_Click(object sender, RoutedEventArgs e) =>
        ViewModel.SourceLanguage = Enum.Parse<Language>((string)((FrameworkElement)sender).Tag);

    private void TargetLanguage_Click(object sender, RoutedEventArgs e) =>
        ViewModel.TargetLanguage = Enum.Parse<Language>((string)((FrameworkElement)sender).Tag);

    private void SwapLanguages_Click(object sender, RoutedEventArgs e) => SwapLanguages();

    private void SwapLanguages() =>
        (ViewModel.SourceLanguage, ViewModel.TargetLanguage) = (ViewModel.TargetLanguage, ViewModel.SourceLanguage);

    public static Visibility Shown(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static string LanguageCode(Language language) => TranslationPopupViewModel.LanguageCode(language);

    public static string LanguageButtonName(string action, Language language) => $"{action} {language}";


    private void RetryHotkey_Click(object sender, RoutedEventArgs args)
    {
        if (!_shellController.IsStopping) _shell?.RetryHotkey();
        PublishShellState();
    }

    private void RetryShell_Click(object sender, RoutedEventArgs args)
    {
        _shellController.RetryShell(() => _shell?.RetryHotkey(), () => _shell?.RetryTray());
        PublishShellState();
    }

    private void Exit_Click(object sender, RoutedEventArgs args) => _shellController.Execute(ShellCommand.Exit);

    private void BeginExit()
    {
        SelectRegionButton.IsEnabled = false;
        RetryHotkeyButton.IsEnabled = false;
        RetryShellButton.IsEnabled = false;
        SingleInstanceActivation.BeginShutdown();
        try { _shell?.BeginShutdown(); }
        catch (Exception exception) { ReportShutdownFailure(exception); }
        ShowLauncher();
        if (_shutdownInProgress) return;
        _shutdownInProgress = true;
        ShutdownCompletion = ShutdownAndCloseAsync();
    }

    private void LauncherLoaded(object sender, RoutedEventArgs args)
    {
        ((FrameworkElement)Content).Loaded -= LauncherLoaded;
        Content.XamlRoot.Changed += (_, _) => PlaceLauncher(onCursorDisplay: false);
        PlaceLauncher(onCursorDisplay: true);
        PinOnTop((OverlappedPresenter)AppWindow.Presenter);
        InputBox.Focus(FocusState.Programmatic);
        if (!_closing) _dictionaryDialogTask = OfferDictionarySetupAsync();
    }

    private async Task OfferDictionarySetupAsync()
    {
        try
        {
            if (_dictionaryPacks is not null && !await _dictionaryPacks.GetSetupDismissedAsync(_dictionaryLifetime.Token) && !_closing)
                await ShowDictionariesAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { if (!_closing) CaptureStatus.Text = "Dictionary setup could not open. The app remains usable; open Dictionaries to retry."; }
    }

    private void Dictionaries_Click(object sender, RoutedEventArgs args)
    {
        if (_closing || !_dictionaryDialogTask.IsCompleted) return;
        SettingsButton.Flyout.Hide();
        InvalidateSelectedCapture();
        _dictionaryDialogTask = ShowDictionariesAsync();
    }

    private async Task ShowDictionariesAsync()
    {
        var model = new DictionaryManagementViewModel(_dictionaryPacks);
        model.SetLegacySource(_legacySource, _legacyReason);
        var dialog = new DictionaryManagementDialog(model) { XamlRoot = ((FrameworkElement)Content).XamlRoot };
        _dictionaryDialog = dialog;
        try
        {
            await model.LoadAsync();
            if (!_closing)
            {
                _dialogOpen = true;
                PlaceLauncher(onCursorDisplay: false);
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { if (!_closing) CaptureStatus.Text = "Dictionary management could not open. Try again when ready."; }
        finally
        {
            await model.CloseAsync();
            if (ReferenceEquals(_dictionaryDialog, dialog)) _dictionaryDialog = null;
            _dialogOpen = false;
            PlaceLauncher(onCursorDisplay: false);
            if (!_closing) InputBox.Focus(FocusState.Programmatic);
        }
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
        ShowCaptureRequestResult(_shellController.Execute(ShellCommand.Capture) ?? RegionCaptureRequestOutcome.Stopping);
    }

    private RegionCaptureRequestOutcome RequestRegionCapture()
    {
        InvalidateSelectedCapture();
        if (_closing || _shellController.IsStopping) return RegionCaptureRequestOutcome.Stopping;
        if (_activeCapture is not null || _selectionHost is not null) return RegionCaptureRequestOutcome.Busy;
        if (!_dictionaryDialogTask.IsCompleted)
        {
            ShowLauncher();
            CaptureStatus.Text = "Finish or close Dictionaries, then try again.";
            return RegionCaptureRequestOutcome.ModalBlocked;
        }
        if (ViewModel.LanguagePairUnavailableReason is { } pairReason)
        {
            ShowLauncher();
            CaptureStatus.Text = pairReason;
            return RegionCaptureRequestOutcome.Unavailable;
        }
        var selected = CaptureDisplayPicker.SelectedItem as CaptureDisplay;
        RefreshDisplays();
        if (CaptureDisplayPicker.ItemsSource is IReadOnlyList<CaptureDisplay> displays && selected is not null)
            CaptureDisplayPicker.SelectedItem = displays.FirstOrDefault(display => display.Source.SourceId == selected.Source.SourceId) ?? displays.FirstOrDefault();
        if (CaptureDisplayPicker.SelectedItem is not CaptureDisplay display)
        {
            ShowLauncher();
            CaptureStatus.Text = "No display is available for capture. Choose a display and try again.";
            return RegionCaptureRequestOutcome.Unavailable;
        }
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
            return RegionCaptureRequestOutcome.Started;
        }
        catch (Exception)
        {
            CancelRegionCapture();
            ShowLauncher();
            CaptureStatus.Text = "Screen selection could not open. Try again.";
            if (_activeCapture is null) _releaseBackend(backend);
            return RegionCaptureRequestOutcome.Unavailable;
        }
    }

    private void ShowCaptureRequestResult(RegionCaptureRequestOutcome outcome)
    {
        if (outcome == RegionCaptureRequestOutcome.Busy)
            CaptureStatus.Text = "A capture is already in progress. Finish or cancel it before starting another.";
        else if (outcome == RegionCaptureRequestOutcome.Stopping)
            CaptureStatus.Text = "FIH Lens is closing. Capture is unavailable.";
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
        if (!_closing && session.Capture is { Request: not null, IsCanceled: false })
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
            if (_closing || session.Capture!.IsCanceled) return;
            var popup = new Phase1Session(session.Popup, AppWindow);
            session.Popup.SetOpenDetail(() => ExplainCapture(session));
            session.Popup.SetSelectAgain(() =>
            {
                session.Popup.CloseCommand.Execute(null);
                SelectRegion_Click(this, new RoutedEventArgs());
            });
            _capturePopups.Add(popup, session);
            popup.Closed += CapturePopupClosed;
            popup.Failed += CapturePopupFailed;
            popup.Show();
            if (_capturePopups.ContainsValue(session)) session.PopupShown();
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
                if (session.Capture?.IsCanceled == true) CaptureStatus.Text = "Capture canceled. Native work has finished.";
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
        if (_activeSelectedCapture is not null)
        {
            InvalidateSelectedCapture();
            CaptureStatus.Text = "No screen capture was started.";
            return;
        }
        CancelRegionCapture();
    }

    private void CancelRegionCapture()
    {
        _activeCapture?.Dispose();
        if (_selectionHost is not null)
        {
            if (!_selectionHost.Close()) CaptureStatus.Text = "Selection windows are still closing. Cancel retries cleanup.";
        }
        else if (_activeCapture is { } session && session.Capture?.IsBusy != true)
            FinishCapture(session, "Selection canceled.");
        else CaptureStatus.Text = "Capture canceled; waiting for native work to finish.";
    }

    // Explain in a lens popup opens the same text and answer in the bar's Result detail.
    private void ExplainCapture(CapturedTranslationSession session)
    {
        if (_closing || _shellController.IsStopping) return;
        CaptureStatus.Text = "";
        ViewModel.AdoptResult(session.Backend);
        ShowLauncher();
        if (ViewModel.Result is not null) ShowDetail(Detail.Ai);
        else if (session.Popup.ShowWordPicker && session.Popup.SelectedWord is { } word)
        {
            // The word picker opens the selected word's cards, with the word in the input.
            ViewModel.InputText = word.Word.Text;
            DictionaryDetailCard.Items = word.Result.Entries.Select(entry => new DictionaryCardViewModel(entry, ViewModel.Pronunciation)).ToArray();
            ShowDetail(Detail.Dictionary);
        }
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
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (ResultsList.SelectedItem is DictionaryCardViewModel card) OpenCard(card);
            else
            {
                _lookupTimer.Stop();
                LookupNow();
            }
        }
        else if (e.Key == VirtualKey.Down && ResultsList.Items.Count > 0 && _detail == Detail.None)
        {
            e.Handled = true;
            int index = ResultsList.SelectedIndex == 0 && ResultsList.Items.Count > 1 ? 1 : Math.Max(ResultsList.SelectedIndex, 0);
            ResultsList.SelectedIndex = index;
            (ResultsList.ContainerFromIndex(index) as Control)?.Focus(FocusState.Keyboard);
        }
    }

    private void AskAi_Click(object sender, RoutedEventArgs e) => AskAi();

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
        switch (_shellController.OnClose(_shell?.IsTrayAvailable == true, !_dictionaryDialogTask.IsCompleted))
        {
            case LauncherCloseAction.Hide:
                InvalidateSelectedCapture();
                _hiddenToTray = true;
                AppWindow.Hide();
                break;
            case LauncherCloseAction.KeepVisible:
                if (!_dictionaryDialogTask.IsCompleted) Activate();
                break;
            case LauncherCloseAction.BeginExit:
            case LauncherCloseAction.RetryExit:
                _shellController.RequestExit();
                break;
        }
    }

    private async Task ShutdownAndCloseAsync()
    {
        _closing = true;
        _lookupTimer.Stop();
        bool retired = true;
        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception exception) { retired = false; ReportShutdownFailure(exception); }
        }
        try
        {
            Attempt(_cancelSharedWork);
            Attempt(InvalidateSelectedCapture);
            Attempt(_dictionaryLifetime.Cancel);
            if (_selectedTextCapture is { } selectedCapture)
            {
                try { retired &= selectedCapture.TryRetireObservers(); }
                catch (Exception exception) { retired = false; ReportShutdownFailure(exception); }
            }
            if (_dictionaryDialog is { } dialog)
            {
                Attempt(dialog.Hide);
                _draining.Add(dialog.ViewModel.CloseAsync());
            }
            _draining.Add(_dictionaryDialogTask);
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
            await FihLens.WinUI.Presentation.ShutdownCompletion.DrainAsync(_draining, ReportShutdownFailure);
            if (!retired || _selectionHost is not null || _capturePopups.Count > 0 || _previewSessions.Count > 0)
            {
                CaptureStatus.Text = "Some windows could not close. Close the launcher again to retry cleanup.";
                _shellController.CompleteExit(false);
                ShowLauncher();
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
            _shellController.CompleteExit(false);
            ShowLauncher();
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
        _shellController.CompleteExit(true);
        SingleInstanceActivation.Detach();
        if (_shell is not null)
        {
            _shell.CommandRequested -= Shell_CommandRequested;
            _shell.SelectedTextRequested -= Shell_SelectedTextRequested;
            _shell.StateChanged -= Shell_StateChanged;
        }
        ViewModel.PropertyChanged -= LauncherBackendChanged;
        ViewModel.AskAiCommand.PropertyChanged -= AskAiCommand_PropertyChanged;
        _lookupTimer.Tick -= LookupTimer_Tick;
        Activated -= Launcher_Activated;
        AppWindow.Closing -= AppWindow_Closing;
        Closed -= MainWindow_Closed;
        _dictionaryLifetime.Dispose();
    }
}
