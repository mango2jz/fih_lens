using FihLens.Windows.Windowing;
using FihLens.WinUI.ViewModels;
using FihLens.WinUI.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace FihLens.WinUI;

/// <summary>One transient presentation owner; shared dictionary/provider lifetime stays with App.</summary>
internal sealed class Phase1Session : IDisposable
{
    private readonly TranslationPopupViewModel _viewModel;
    private readonly TranslationPopupWindow _window;
    private readonly WindowFocusSnapshot _priorForeground;
    private bool _closed;
    private bool _closing;
    private bool _restoreForeground;

    public event EventHandler? Closed;
    public event EventHandler? Failed;

    public Phase1Session(TranslationPopupViewModel viewModel, AppWindow launcher)
    {
        _viewModel = viewModel;
        _priorForeground = WindowFocus.Capture();
        try { _window = new TranslationPopupWindow(viewModel, launcher); }
        catch
        {
            _viewModel.CloseCommand.Execute(null);
            throw;
        }
        _viewModel.CloseRequested += Dismiss;
        _window.DismissRequested += Dismiss;
        _window.PlacementFailed += PlacementFailed;
        _window.Closed += Window_Closed;
    }

    public void Show(ElementTheme theme = ElementTheme.Default)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_window.Content is FrameworkElement root) root.RequestedTheme = theme;
        _window.Activate();
    }

    public void Dispose()
    {
        if (_closed || _closing) return;
        _closing = true;
        try
        {
            _restoreForeground = WindowFocus.IsForeground(WinRT.Interop.WindowNative.GetWindowHandle(_window));
            _window.Close();
        }
        catch
        {
            // Only the native Closed event proves retirement; keep the owner available for retry.
            if (!_closed) _closing = false;
            throw;
        }
    }

    private void Dismiss(object? sender, EventArgs args)
    {
        try { Dispose(); }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Phase1 preview stage=dismiss exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
            Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PlacementFailed(object? sender, EventArgs args)
    {
        try { Failed?.Invoke(this, EventArgs.Empty); }
        finally { Dismiss(sender, args); }
    }

    private void Window_Closed(object sender, WindowEventArgs args) => FinishClose();

    private void FinishClose()
    {
        if (_closed) return;
        _closed = true;
        _viewModel.CloseRequested -= Dismiss;
        _window.DismissRequested -= Dismiss;
        _window.PlacementFailed -= PlacementFailed;
        _window.Closed -= Window_Closed;
        _viewModel.CloseCommand.Execute(null);
        if (_restoreForeground) WindowFocus.TryRestore(_priorForeground);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
