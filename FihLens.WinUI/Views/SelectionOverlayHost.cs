using System.ComponentModel;
using System.Runtime.InteropServices;
using FihLens.Application.Capture;
using FihLens.Windows.Capture;
using FihLens.WinUI.ViewModels;
using FihLens.WinUI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace FihLens.WinUI.Views;

/// <summary>Owns both selection windows until their native destruction is acknowledged.</summary>
public sealed class SelectionOverlayHost : IDisposable
{
    private static readonly HashSet<SelectionOverlayHost> LiveHosts = [];
    private readonly SelectionOverlayViewModel _viewModel;
    private readonly Window _owner;
    private readonly Native.WndProc _procedure;
    private readonly Native.SubclassProc _toolbarProcedure;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly string _className = "FihLensSelection_" + Guid.NewGuid().ToString("N");
    private readonly List<SpriteVisual> _visuals = [];
    private readonly List<CompositionColorBrush> _brushes = [];
    private ushort _atom;
    private nint _hwnd;
    private nint _toolbarHwnd;
    private uint _dpi;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private ContainerVisual? _root;
    private SelectionToolbarWindow? _toolbar;
    private bool _dragging;
    private bool _closing;
    private bool _closed;
    private bool _confirmed;
    private bool _shown;
    private bool _ready;
    private bool _toolbarSubclassAttached;
    private Exception? _failure;

    public event EventHandler<RegionTextCaptureRequest>? Confirmed;
    public event EventHandler? Closed;
    public event EventHandler<Exception>? Failed;
    public Exception? CleanupFailure => _failure;

    public SelectionOverlayHost(SelectionOverlayViewModel viewModel, Window owner)
    {
        _viewModel = viewModel;
        _owner = owner;
        _procedure = Procedure;
        _toolbarProcedure = ToolbarProcedure;
    }

    public void Show()
    {
        CheckThread();
        if (_shown || _closed) throw new InvalidOperationException("Selection host cannot be reopened.");
        _shown = true;
        LiveHosts.Add(this);
        try
        {
            ValidateSource();
            _dispatcher.EnsureSystemDispatcherQueue();
            var wc = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(),
                Procedure = _procedure, Instance = Native.GetModuleHandleW(null), ClassName = _className,
                Cursor = Native.LoadCursorW(0, 32515) };
            _atom = Native.RegisterClassExW(ref wc);
            if (_atom == 0) throw Native.Error();
            var bounds = _viewModel.Source.DisplayBounds;
            _hwnd = Native.CreateWindowExW(0x00200080, _className, "FIH Lens region selection", 0x80000000,
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                WinRT.Interop.WindowNative.GetWindowHandle(_owner), 0, wc.Instance, 0);
            if (_hwnd == 0) throw Native.Error();
            _dpi = Native.GetDpiForWindow(_hwnd);
            ValidateClient();
            _compositor = new Compositor();
            nint interop = 0, abi = 0;
            try
            {
                var iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(((WinRT.IWinRTObject)_compositor).NativeObject.ThisPtr,
                    in iid, out interop));
                var create = Marshal.GetDelegateForFunctionPointer<Native.CreateTarget>(
                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(interop), 3 * nint.Size));
                Marshal.ThrowExceptionForHR(create(interop, _hwnd, 0, out abi));
                _target = WinRT.MarshalInterface<DesktopWindowTarget>.FromAbi(abi);
            }
            finally { if (abi != 0) Marshal.Release(abi); if (interop != 0) Marshal.Release(interop); }
            _root = _compositor.CreateContainerVisual();
            _root.Size = new(bounds.Width, bounds.Height);
            _target.Root = _root;
            for (int i = 0; i < 12; i++)
            {
                var accent = ((SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources["Phase1AccentBrush"]).Color;
                var brush = _compositor.CreateColorBrush(i < 4 ? Color.FromArgb(140, 0, 0, 0) : accent);
                _brushes.Add(brush);
                var visual = _compositor.CreateSpriteVisual();
                _visuals.Add(visual);
                visual.Brush = brush;
                _root.Children.InsertAtTop(visual);
            }
            _toolbar = new(_viewModel, Confirm, Cancel);
            _toolbarHwnd = WinRT.Interop.WindowNative.GetWindowHandle(_toolbar);
            Marshal.SetLastPInvokeError(0);
            if (Native.SetWindowLongPtrW(_toolbarHwnd, -8, _hwnd) == 0 && Marshal.GetLastPInvokeError() != 0)
                throw Native.Error();
            PlaceToolbar();
            if (!Native.SetWindowSubclass(_toolbarHwnd, _toolbarProcedure, 1, 0)) throw Native.Error();
            _toolbarSubclassAttached = true;
            _viewModel.PropertyChanged += ViewModelChanged;
            Render();
            Native.ShowWindow(_hwnd, 5);
            _toolbar.Activate();
            _ready = true;
        }
        catch (Exception error) { ReportFailure(error); Close(); }
    }

    private void ValidateSource()
    {
        if (!WindowsDisplayCatalog.GetDisplays().Any(display => display.Source == _viewModel.Source))
            throw new InvalidOperationException("Display configuration changed. Select again.");
    }

    private void ValidateClient()
    {
        if (!Native.GetClientRect(_hwnd, out var rect)) throw Native.Error();
        var point = new Native.Point();
        if (!Native.ClientToScreen(_hwnd, ref point)) throw Native.Error();
        if (new PhysicalPixelRect(point.X, point.Y, rect.Right, rect.Bottom) != _viewModel.Source.DisplayBounds)
            throw new InvalidOperationException("Selection surface does not match physical display coordinates.");
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SelectionOverlayViewModel.Selection) || _closing) return;
        try { Render(); } catch (Exception error) { ReportFailure(error); Cancel(); }
    }

    private void Render()
    {
        var bounds = _viewModel.Source.DisplayBounds;
        var rect = _viewModel.Selection;
        float x = rect?.X - bounds.X ?? 0, y = rect?.Y - bounds.Y ?? 0;
        float w = rect?.Width ?? 0, h = rect?.Height ?? 0;
        SetVisual(0, 0, 0, bounds.Width, rect is null ? bounds.Height : y);
        SetVisual(1, 0, y, x, h);
        SetVisual(2, x + w, y, bounds.Width - x - w, h);
        SetVisual(3, 0, y + h, bounds.Width, rect is null ? 0 : bounds.Height - y - h);
        float border = 2 * _dpi / 96f, handle = 6 * _dpi / 96f;
        SetVisual(4, x, y, w, Math.Min(border, h));
        SetVisual(5, x, y + h - Math.Min(border, h), w, Math.Min(border, h));
        SetVisual(6, x, y, Math.Min(border, w), h);
        SetVisual(7, x + w - Math.Min(border, w), y, Math.Min(border, w), h);
        for (int i = 0; i < 4; i++)
            SetVisual(8 + i, x + (i % 2 == 0 ? 0 : w) - handle / 2, y + (i < 2 ? 0 : h) - handle / 2,
                rect is null ? 0 : handle, rect is null ? 0 : handle);
        PlaceToolbar();
    }

    private void SetVisual(int index, float x, float y, float width, float height)
    {
        _visuals[index].Offset = new(x, y, 0);
        _visuals[index].Size = new(Math.Max(0, width), Math.Max(0, height));
    }

    private void PlaceToolbar()
    {
        if (_toolbar is null) return;
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfoW(Native.MonitorFromWindow(_hwnd, 0), ref info)) throw Native.Error();
        var work = info.Work;
        var appWindow = _toolbar.AppWindow;
        if (Native.MonitorFromWindow(_toolbarHwnd, 0) != Native.MonitorFromWindow(_hwnd, 0))
            appWindow.Move(new PointInt32(work.Left, work.Top));
        var placement = PopupPlacement.Calculate(new(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top),
            work.Left, work.Top, 584, 46, _dpi / 96d,
            appWindow.Size.Width - appWindow.ClientSize.Width, appWindow.Size.Height - appWindow.ClientSize.Height);
        if (appWindow.ClientSize.Width != placement.ClientWidth || appWindow.ClientSize.Height != placement.ClientHeight)
            appWindow.ResizeClient(new SizeInt32(placement.ClientWidth, placement.ClientHeight));
        // Placement uses measured outer geometry; the 46-DIP reference describes the client content.
        int width = appWindow.Size.Width, height = appWindow.Size.Height;
        if (width <= 0 || height <= 0 || width > work.Right - work.Left || height > work.Bottom - work.Top)
            throw new InvalidOperationException("Toolbar outer bounds do not fit the display work area.");
        int gap = (int)Math.Ceiling(12 * _dpi / 96d);
        int x = work.Left + Math.Max(0, (work.Right - work.Left - width) / 2);
        int y = work.Top;
        if (_viewModel.Selection is { } selection)
        {
            if ((long)selection.Y + selection.Height + gap + height <= work.Bottom) y = selection.Y + selection.Height + gap;
            else if ((long)selection.Y - gap - height >= work.Top) y = selection.Y - gap - height;
            else if ((long)selection.X + selection.Width + gap + width <= work.Right) x = selection.X + selection.Width + gap;
            else if ((long)selection.X - gap - width >= work.Left) x = selection.X - gap - width;
        }
        x = Math.Clamp(x, work.Left, work.Right - width);
        y = Math.Clamp(y, work.Top, work.Bottom - height);
        if (appWindow.Position.X != x || appWindow.Position.Y != y) appWindow.Move(new PointInt32(x, y));
    }

    private void Confirm()
    {
        if (_closing || _confirmed) return;
        try
        {
            ValidateSource();
            ValidateClient();
            if (Native.GetDpiForWindow(_hwnd) != _dpi) throw new InvalidOperationException("Display scale changed. Select again.");
            if (_viewModel.Confirm() is not { } request) return;
            _confirmed = true;
            Confirmed?.Invoke(this, request);
            Close();
        }
        catch (Exception error) { ReportFailure(error); Close(); }
    }

    private void Cancel() { if (!_confirmed) _viewModel.Cancel(); Close(); }

    private nint Procedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case 0x84: return 1; // HTCLIENT consumes input even inside the clear hole.
                case 0x14: return 1;
                case 0x10: Cancel(); return 0;
                case 0x82:
                    _hwnd = 0;
                    QueueClose();
                    break;
                case 0x7E: case 0x2E0: case 0x1A:
                    ReportFailure(new InvalidOperationException("Display configuration changed. Select again."));
                    Cancel(); return 0;
                case 0x100:
                    if (wParam == 0x1B) Cancel();
                    else if (wParam == 0x0D) Confirm();
                    else if (wParam == 0x20) _viewModel.CreateKeyboardSelection();
                    else if (wParam >= 0x25 && wParam <= 0x28)
                        _viewModel.AdjustKeyboard(wParam == 0x25 ? -1 : wParam == 0x27 ? 1 : 0,
                            wParam == 0x26 ? -1 : wParam == 0x28 ? 1 : 0, Native.GetKeyState(0x10) < 0);
                    else if (wParam == 0x09) _toolbar?.Activate();
                    return 0;
                case 0x201:
                    if (!Native.GetCursorPos(out var start)) throw Native.Error();
                    int anchorX = start.X, anchorY = start.Y;
                    if (_viewModel.Selection is { } selected)
                    {
                        int radius = (int)Math.Ceiling(22 * _dpi / 96d);
                        for (int i = 0; i < 4; i++)
                        {
                            int cx = selected.X + (i % 2 == 0 ? 0 : selected.Width);
                            int cy = selected.Y + (i < 2 ? 0 : selected.Height);
                            if (Math.Abs((long)start.X - cx) > radius || Math.Abs((long)start.Y - cy) > radius) continue;
                            anchorX = selected.X + (i % 2 == 0 ? selected.Width : 0);
                            anchorY = selected.Y + (i < 2 ? selected.Height : 0);
                            break;
                        }
                    }
                    _dragging = true;
                    Native.SetCapture(hwnd);
                    _viewModel.BeginDrag(anchorX, anchorY);
                    _viewModel.MoveDrag(start.X, start.Y);
                    return 0;
                case 0x200: case 0x202:
                    if (!_dragging) return 0;
                    if (!Native.GetCursorPos(out var point)) throw Native.Error();
                    if (message == 0x202)
                    {
                        _dragging = false;
                        _viewModel.EndDrag(point.X, point.Y);
                        Native.ReleaseCapture();
                    }
                    else _viewModel.MoveDrag(point.X, point.Y);
                    return 0;
                case 0x215:
                    if (_dragging) { _dragging = false; Cancel(); }
                    return 0;
            }
        }
        catch (Exception error) { ReportFailure(error); QueueClose(); }
        return Native.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private nint ToolbarProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == 0x82) { _toolbarHwnd = 0; QueueClose(); }
            else if (_ready && message is 0x2E0 or 0x7E) { ReportFailure(new InvalidOperationException("Display configuration changed. Select again.")); QueueClose(); }
        }
        catch (Exception error) { ReportFailure(error); QueueClose(); }
        return Native.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void QueueClose()
    {
        if (!_closed && !_dispatcher.TryEnqueue(() => { try { Close(); } catch (Exception error) { ReportFailure(error); } }))
            ReportFailure(new InvalidOperationException("Could not enqueue selection cleanup."));
    }

    public bool Close()
    {
        CheckThread();
        if (_closed) return true;
        if (_closing) return false;
        _closing = true;
        try
        {
            if (!_confirmed) _viewModel.Cancel();
            _viewModel.PropertyChanged -= ViewModelChanged;
            _dragging = false;
            if (Native.GetCapture() == _hwnd && _hwnd != 0) Native.ReleaseCapture();
            if (_toolbarHwnd != 0)
            {
                if (!Release(() => _toolbar!.Close())) return false;
                if (!_toolbarSubclassAttached && !Native.IsWindow(_toolbarHwnd)) _toolbarHwnd = 0;
                if (_toolbarHwnd != 0) return false; // Retain callback until WM_NCDESTROY, not Window.Closed.
            }
            _toolbar = null;
            if (_hwnd != 0 && !Native.DestroyWindow(_hwnd)) { ReportFailure(Native.Error()); return false; }
            if (_hwnd != 0) return false;
            bool detached = Release(() => { if (_target is not null) _target.Root = null; });
            bool cleared = Release(() => _root?.Children.RemoveAll());
            foreach (var visual in _visuals.ToArray()) if (Release(visual.Dispose)) _visuals.Remove(visual);
            foreach (var brush in _brushes.ToArray()) if (Release(brush.Dispose)) _brushes.Remove(brush);
            if (detached && cleared && _visuals.Count == 0 && Release(() => _root?.Dispose())) _root = null;
            if (detached && _root is null && Release(() => _target?.Dispose())) _target = null;
            if (_root is null && _target is null && _brushes.Count == 0 && Release(() => _compositor?.Dispose())) _compositor = null;
            if (_atom != 0)
            {
                if (Native.UnregisterClassW(_className, Native.GetModuleHandleW(null))) _atom = 0;
                else ReportFailure(Native.Error());
            }
            if (_atom != 0 || _root is not null || _target is not null || _compositor is not null || _visuals.Count != 0 || _brushes.Count != 0) return false;
            _closed = true;
            LiveHosts.Remove(this);
            try { Closed?.Invoke(this, EventArgs.Empty); } catch (Exception error) { ReportFailure(error); }
            GC.KeepAlive(_procedure);
            GC.KeepAlive(_toolbarProcedure);
            return true;
        }
        finally { _closing = false; }
    }

    private bool Release(Action action) { try { action(); return true; } catch (Exception error) { ReportFailure(error); return false; } }
    private void ReportFailure(Exception error)
    {
        _failure ??= error;
        try { Failed?.Invoke(this, error); } catch { }
    }
    private void CheckThread() { if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI thread required."); }
    public void Dispose() => Close();

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int CreateTarget(nint self, nint hwnd, int topmost, out nint target);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass
        {
            internal uint Size, Style; internal WndProc Procedure; internal int ClassExtra, WindowExtra;
            internal nint Instance, Icon, Cursor, Background; internal string? MenuName; internal string ClassName; internal nint SmallIcon;
        }
        internal static Exception Error() => new Win32Exception(Marshal.GetLastWin32Error());
        [DllImport("user32.dll", SetLastError = true)] internal static extern ushort RegisterClassExW(ref WindowClass value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool UnregisterClassW(string name, nint instance);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandleW(string? name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowExW(uint extended, string name, string title, uint style, int x, int y, int w, int h, nint owner, nint menu, nint instance, nint parameter);
        [DllImport("user32.dll")] internal static extern nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool DestroyWindow(nint hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
        [DllImport("user32.dll")] internal static extern nint SetCapture(nint hwnd);
        [DllImport("user32.dll")] internal static extern nint GetCapture();
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
        [DllImport("user32.dll")] internal static extern nint LoadCursorW(nint instance, int cursor);
        [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
        [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
        [DllImport("comctl32.dll", SetLastError = true)] internal static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
        [DllImport("comctl32.dll")] internal static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    }
}
