using FihLens.WinUI.ViewModels;
using System.ComponentModel;
using FihLens.WinUI.Windowing;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace FihLens.WinUI.Views;

public sealed partial class TranslationPopupWindow : Window
{
    private XamlRoot? _root;
    private DisplayAreaWatcher? _displays;
    private bool _closed;
    private bool _placementQueued;
    private readonly TranslationPopupViewModel _viewModel;

    public event EventHandler? DismissRequested;
    public event EventHandler? PlacementFailed;

    public TranslationPopupWindow(TranslationPopupViewModel viewModel, AppWindow launcher)
    {
        _viewModel = viewModel;
        try
        {
            InitializeComponent();
            Title = viewModel.IsPreview ? "FIH Lens · design preview · sample data" : "FIH Lens · captured text";
            PopupContent.ViewModel = viewModel;
            var presenter = (OverlappedPresenter)AppWindow.Presenter;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            var display = DisplayArea.GetFromWindowId(launcher.Id, DisplayAreaFallback.Nearest);
            // Fit wholly on the intended monitor before XAML can report its actual scale.
            Place(display, scale: 1, center: true);
            PopupRoot.Loaded += Root_Loaded;
            PopupRoot.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_KeyDown), true);
            AppWindow.Changed += AppWindow_Changed;
            _viewModel.PropertyChanged += ViewModel_Changed;
            Closed += Window_Closed;
        }
        catch
        {
            Close();
            throw;
        }
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        PopupRoot.Loaded -= Root_Loaded;
        try
        {
            _root = PopupRoot.XamlRoot;
            _root.Changed += Root_Changed;
            _displays = DisplayArea.CreateWatcher();
            _displays.Updated += Displays_Changed;
            _displays.Removed += Displays_Changed;
            _displays.Added += Displays_Changed;
            _displays.Start();
            ApplyPlacement(center: true);
            PopupRoot.Opacity = 1;
            PopupContent.FocusClose();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Phase1 preview stage=root-loaded exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
            PlacementFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidSizeChange) QueuePlacement();
    }

    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => QueuePlacement();

    private void ViewModel_Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TranslationPopupViewModel.IsExpanded)
            or nameof(TranslationPopupViewModel.PreferredHeight)) QueuePlacement();
    }

    private void Displays_Changed(DisplayAreaWatcher sender, DisplayArea args) => QueuePlacement();

    private void QueuePlacement()
    {
        if (_closed || _root is null || _placementQueued) return;
        _placementQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _placementQueued = false;
                if (_closed) return;
                try { ApplyPlacement(center: false); }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Phase1 preview stage=queued-placement exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
                    PlacementFailed?.Invoke(this, EventArgs.Empty);
                }
            }))
        {
            _placementQueued = false;
            // A stopped dispatcher cannot publish UI failure or touch the retired window.
        }
    }

    private void ApplyPlacement(bool center)
    {
        if (_root is null) return;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        Place(display, _root.RasterizationScale, center);
    }

    private void Place(DisplayArea display, double scale, bool center)
    {
        // Exact SDK native evidence: WorkArea is already desktop physical coordinates.
        var work = display.WorkArea;
        var bounds = display.OuterBounds;
        if (work.Width <= 0 || work.Height <= 0 || work.X < bounds.X || work.Y < bounds.Y ||
            (long)work.X + work.Width > (long)bounds.X + bounds.Width ||
            (long)work.Y + work.Height > (long)bounds.Y + bounds.Height)
            throw new InvalidOperationException("Display work area is not valid desktop geometry.");
        int extraWidth = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        int extraHeight = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        var area = new PixelRect(work.X, work.Y, work.Width, work.Height);
        int heightDip = _viewModel.PreferredHeight;
        var placement = PopupPlacement.Calculate(area, AppWindow.Position.X, AppWindow.Position.Y,
            400, heightDip, scale, extraWidth, extraHeight);
        if (center)
        {
            placement = PopupPlacement.Calculate(area,
                work.X + (work.Width - placement.OuterBounds.Width) / 2,
                work.Y + (work.Height - placement.OuterBounds.Height) / 2,
                400, heightDip, scale, extraWidth, extraHeight);
        }

        if (AppWindow.ClientSize.Width != placement.ClientWidth ||
            AppWindow.ClientSize.Height != placement.ClientHeight)
            AppWindow.ResizeClient(new SizeInt32(placement.ClientWidth, placement.ClientHeight));
        if (AppWindow.Position.X != placement.OuterBounds.X || AppWindow.Position.Y != placement.OuterBounds.Y)
            AppWindow.Move(new PointInt32(placement.OuterBounds.X, placement.OuterBounds.Y));
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            if (!PopupContent.CollapseSourceDisclosure()) DismissRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (args.Handled || args.Key is not (VirtualKey.C or VirtualKey.E)) return;
        bool hasModifiers = new[]
            {
                VirtualKey.Control, VirtualKey.Menu, VirtualKey.Shift, VirtualKey.LeftWindows,
                VirtualKey.RightWindows
            }
            .Any(key => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0);
        bool isEditing = IsEditable(args.OriginalSource as DependencyObject) ||
                         IsEditable(_root is null ? null : FocusManager.GetFocusedElement(_root) as DependencyObject);
        args.Handled = _viewModel.TryHandleShortcut(args.Key == VirtualKey.C ? 'C' : 'E', hasModifiers, isEditing);
    }

    private static bool IsEditable(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is TextBox or RichEditBox or PasswordBox)
                return true;
        return false;
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        PopupRoot.Loaded -= Root_Loaded;
        PopupRoot.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_KeyDown));
        AppWindow.Changed -= AppWindow_Changed;
        _viewModel.PropertyChanged -= ViewModel_Changed;
        if (_root is not null) _root.Changed -= Root_Changed;
        if (_displays is not null)
        {
            _displays.Updated -= Displays_Changed;
            _displays.Removed -= Displays_Changed;
            _displays.Added -= Displays_Changed;
            _displays.Stop();
        }

        Closed -= Window_Closed;
        PopupContent.ViewModel = null;
    }
}
