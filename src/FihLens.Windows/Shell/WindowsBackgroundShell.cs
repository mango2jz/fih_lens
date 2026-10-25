using System.Runtime.InteropServices;

namespace FihLens.Windows.Shell;

/// <summary>Owns shell registrations attached to a borrowed top-level launcher window.</summary>
public sealed class WindowsBackgroundShell
{
    private const int HotkeyId = 0xF1;
    private const uint HotkeyMessage = 0x0312;
    private const uint TrayMessage = 0x8001;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmNull = 0x0000;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkSpace = 0x20;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifGuid = 0x00000020;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;
    private const uint MenuOpen = 1;
    private const uint MenuCapture = 2;
    private const uint MenuExit = 3;

    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

    private readonly nint _window;
    private readonly string _iconPath;
    private readonly string _openLabel;
    private readonly string _captureLabel;
    private readonly string _exitLabel;
    private readonly Func<FihLens.Windows.Capture.SelectedTextSource>? _captureSource;
    private readonly SubclassProc _subclassProc;
    private readonly int _ownerThreadId;
    private readonly Guid _iconGuid = new("A65C7E61-1D07-4C86-A274-78DDE902F69D");
    private nint _icon;
    private nint _menu;
    private bool _menuOpen;
    private int? _menuDestroyErrorCode;
    private bool _subclassAttached;
    private bool _hotkeyRegistered;
    private bool _iconAdded;
    private bool _trayAvailable;
    private bool _stopping;
    private bool _retired;

    public WindowsBackgroundShell(nint hwnd, string iconPath, string openLabel, string captureLabel, string exitLabel,
        Func<FihLens.Windows.Capture.SelectedTextSource>? captureSource = null)
    {
        if (hwnd == 0) throw new ArgumentOutOfRangeException(nameof(hwnd));
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(openLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(exitLabel);
        _window = hwnd;
        _iconPath = iconPath;
        _openLabel = openLabel;
        _captureLabel = captureLabel;
        _exitLabel = exitLabel;
        _captureSource = captureSource;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _subclassProc = SubclassCallback;
    }

    public event EventHandler? StateChanged;
    public event Action<BackgroundShellCommand>? CommandRequested;
    public event Action<FihLens.Windows.Capture.SelectedTextSource>? SelectedTextRequested;

    public bool IsHotkeyRegistered => _hotkeyRegistered;
    public bool IsTrayAvailable => _trayAvailable;
    public bool IsRetired => _retired;
    public int? HotkeyErrorCode { get; private set; }
    public int? TrayErrorCode { get; private set; }
    public const string HotkeyLabel = "Ctrl+Shift+Space";

    public void Initialize()
    {
        EnsureOwnerThread();
        if (_retired || _stopping) return;
        if (!EnsureSubclass())
        {
            int error = Marshal.GetLastWin32Error();
            HotkeyErrorCode = error;
            TrayErrorCode = error;
            RaiseStateChanged();
            return;
        }

        RetryHotkey();
        RetryTray();
    }

    public void RetryHotkey()
    {
        EnsureOwnerThread();
        if (_retired || _stopping || _hotkeyRegistered) return;
        if (!EnsureSubclass())
        {
            HotkeyErrorCode = Marshal.GetLastWin32Error();
            RaiseStateChanged();
            return;
        }

        if (RegisterHotKey(_window, HotkeyId, ModControl | ModShift | ModNoRepeat, VkSpace))
        {
            _hotkeyRegistered = true;
            HotkeyErrorCode = null;
        }
        else
        {
            HotkeyErrorCode = Marshal.GetLastWin32Error();
        }
        RaiseStateChanged();
    }

    public void RetryTray()
    {
        EnsureOwnerThread();
        if (_retired || _stopping) return;
        if (_menu != 0 && !TryDestroyMenu())
        {
            _trayAvailable = false;
            RaiseStateChanged();
            return;
        }
        if (!EnsureSubclass())
        {
            TrayErrorCode = Marshal.GetLastWin32Error();
            RaiseStateChanged();
            return;
        }

        if (_iconAdded && !DeleteTrayIcon())
        {
            RaiseStateChanged();
            return;
        }

        if (_icon == 0)
        {
            // Small-icon size picks the .ico's 16–24 px small-mark frame instead of shrinking the 32 px one.
            _icon = LoadImageW(0, _iconPath, ImageIcon, GetSystemMetrics(SmCxSmIcon), GetSystemMetrics(SmCySmIcon), LrLoadFromFile);
            if (_icon == 0)
            {
                TrayErrorCode = Marshal.GetLastWin32Error();
                _trayAvailable = false;
                RaiseStateChanged();
                return;
            }
        }

        var data = CreateNotifyIconData();
        if (!Shell_NotifyIconW(NimAdd, ref data))
        {
            TrayErrorCode = Marshal.GetLastWin32Error();
            _trayAvailable = false;
            RaiseStateChanged();
            return;
        }

        _iconAdded = true;
        data.TimeoutOrVersion = NotifyIconVersion4;
        if (!Shell_NotifyIconW(NimSetVersion, ref data))
        {
            int versionError = Marshal.GetLastWin32Error();
            _trayAvailable = false;
            if (DeleteTrayIcon()) TrayErrorCode = versionError;
            RaiseStateChanged();
            return;
        }

        TrayErrorCode = _menuDestroyErrorCode;
        _trayAvailable = true;
        RaiseStateChanged();
    }

    public void BeginShutdown()
    {
        EnsureOwnerThread();
        if (_retired || _stopping) return;
        _stopping = true;
        _trayAvailable = false;
        TryUnregisterHotkey();
        RaiseStateChanged();
    }

    public bool TryRetire()
    {
        EnsureOwnerThread();
        if (_retired) return true;
        _stopping = true;
        _trayAvailable = false;

        bool menuRemoved = TryDestroyMenu();
        bool hotkeyRemoved = TryUnregisterHotkey();
        bool iconRemoved = !_iconAdded || DeleteTrayIcon();
        if (menuRemoved && hotkeyRemoved && iconRemoved && _subclassAttached)
        {
            if (RemoveWindowSubclass(_window, _subclassProc, (nuint)HotkeyId))
                _subclassAttached = false;
            else
                TrayErrorCode = Marshal.GetLastWin32Error();
        }

        if (menuRemoved && hotkeyRemoved && iconRemoved && !_subclassAttached)
        {
            if (_icon != 0 && DestroyIcon(_icon)) _icon = 0;
            else if (_icon != 0) TrayErrorCode = Marshal.GetLastWin32Error();
        }

        _retired = menuRemoved && hotkeyRemoved && iconRemoved && !_subclassAttached && _icon == 0;
        if (!menuRemoved && _menuDestroyErrorCode is int menuError) TrayErrorCode = menuError;
        RaiseStateChanged();
        return _retired;
    }

    private bool EnsureSubclass()
    {
        if (_subclassAttached) return true;
        if (TaskbarCreatedMessage == 0 || !SetWindowSubclass(_window, _subclassProc, (nuint)HotkeyId, 0))
            return false;
        _subclassAttached = true;
        return true;
    }

    private bool TryUnregisterHotkey()
    {
        if (!_hotkeyRegistered) return true;
        if (UnregisterHotKey(_window, HotkeyId))
        {
            _hotkeyRegistered = false;
            HotkeyErrorCode = null;
            return true;
        }

        HotkeyErrorCode = Marshal.GetLastWin32Error();
        return false;
    }

    private bool DeleteTrayIcon()
    {
        if (!_iconAdded) return true;
        var data = CreateNotifyIconData();
        if (Shell_NotifyIconW(NimDelete, ref data))
        {
            _iconAdded = false;
            _trayAvailable = false;
            TrayErrorCode = _menuDestroyErrorCode;
            return true;
        }

        TrayErrorCode = Marshal.GetLastWin32Error();
        _trayAvailable = false;
        return false;
    }

    private NotifyIconData CreateNotifyIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
        Flags = NifMessage | NifIcon | NifTip | NifGuid,
        CallbackMessage = TrayMessage,
        Icon = _icon,
        ToolTip = "FIH Lens",
        Info = string.Empty,
        InfoTitle = string.Empty,
        Guid = _iconGuid
    };

    private nint SubclassCallback(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        try
        {
            if (message == HotkeyMessage && (int)wParam == HotkeyId)
            {
                if (!_stopping && _hotkeyRegistered)
                {
                    RaiseSelectedTextRequested(_captureSource?.Invoke() ?? default);
                }
                return 0;
            }

            if (message == TrayMessage)
            {
                HandleTrayCallback(wParam, lParam);
                return 0;
            }

            if (TaskbarCreatedMessage != 0 && message == TaskbarCreatedMessage)
            {
                _iconAdded = false;
                _trayAvailable = false;
                if (!_stopping) RetryTray();
                else RaiseStateChanged();
                return 0;
            }

            if (message == WmNcDestroy)
            {
                _subclassAttached = false;
                _hotkeyRegistered = false;
                _trayAvailable = false;
                HotkeyErrorCode = null;
                RaiseStateChanged();
            }
        }
        catch
        {
            // Managed handlers must never unwind through the native subclass callback.
        }

        try { return DefSubclassProc(hwnd, message, wParam, lParam); }
        catch { return 0; }
    }

    private void HandleTrayCallback(nuint wParam, nint lParam)
    {
        if (_stopping || !_trayAvailable) return;
        if (!BackgroundShellProtocol.TryDecodeVersion4(wParam, lParam, out var callback)) return;
        if (callback.EventCode is BackgroundShellProtocol.Select or BackgroundShellProtocol.KeySelect)
        {
            RaiseCommand(BackgroundShellCommand.Open);
            return;
        }

        if (callback.EventCode == BackgroundShellProtocol.ContextMenu)
            ShowTrayMenu(new Point { X = callback.X, Y = callback.Y });
    }

    private void ShowTrayMenu(Point position)
    {
        if (_menuOpen) return;
        if (_menu != 0 && !TryDestroyMenu()) return;
        _menu = CreatePopupMenu();
        if (_menu == 0)
        {
            TrayErrorCode = Marshal.GetLastWin32Error();
            RaiseStateChanged();
            return;
        }

        _menuOpen = true;
        uint command = 0;
        try
        {
            if (!AppendMenuW(_menu, MfString, MenuOpen, _openLabel) ||
                !AppendMenuW(_menu, MfString, MenuCapture, _captureLabel) ||
                !AppendMenuW(_menu, MfSeparator, 0, null) ||
                !AppendMenuW(_menu, MfString, MenuExit, _exitLabel))
            {
                TrayErrorCode = Marshal.GetLastWin32Error();
                RaiseStateChanged();
                return;
            }

            if (!SetForegroundWindow(_window))
            {
                TrayErrorCode = Marshal.GetLastWin32Error();
                RaiseStateChanged();
                return;
            }
            if (position.X == -1 && position.Y == -1 && !GetCursorPos(out position))
            {
                TrayErrorCode = Marshal.GetLastWin32Error();
                RaiseStateChanged();
                return;
            }

            command = TrackPopupMenu(_menu, TpmRightButton | TpmReturnCommand, position.X, position.Y, 0, _window, 0);
            if (!PostMessageW(_window, WmNull, 0, 0))
            {
                TrayErrorCode = Marshal.GetLastWin32Error();
                RaiseStateChanged();
            }
        }
        finally
        {
            _menuOpen = false;
            _ = TryDestroyMenu();
        }

        switch (command)
        {
            case MenuOpen: RaiseCommand(BackgroundShellCommand.Open); break;
            case MenuCapture: RaiseCommand(BackgroundShellCommand.Capture); break;
            case MenuExit: RaiseCommand(BackgroundShellCommand.Exit); break;
        }
    }

    private bool TryDestroyMenu()
    {
        if (_menuOpen) return false;
        if (_menu == 0) return true;
        if (!DestroyMenu(_menu))
        {
            _menuDestroyErrorCode = Marshal.GetLastWin32Error();
            TrayErrorCode = _menuDestroyErrorCode;
            RaiseStateChanged();
            return false;
        }

        int? previousMenuError = _menuDestroyErrorCode;
        _menu = 0;
        _menuDestroyErrorCode = null;
        if (TrayErrorCode == previousMenuError) TrayErrorCode = null;
        return true;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The background shell must be used on its owning UI thread.");
    }

    private void RaiseCommand(BackgroundShellCommand command)
    {
        try { CommandRequested?.Invoke(command); }
        catch { }
    }

    private void RaiseSelectedTextRequested(FihLens.Windows.Capture.SelectedTextSource source)
    {
        try { SelectedTextRequested?.Invoke(source); }
        catch { }
    }

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(this, EventArgs.Empty); }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ToolTip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterWindowMessageW(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadImageW(nint instance, string name, uint type, int width, int height, uint loadFlags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool AppendMenuW(nint menu, uint flags, uint id, string? text);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyMenu(nint menu);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint subclassId, nuint referenceData);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint subclassId);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
}
