using FihLens.WinUI.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace FihLens.WinUI.Views;

public sealed partial class SelectionToolbarWindow : Window
{
    private readonly Action _confirm;
    private readonly Action _cancel;
    public SelectionOverlayViewModel ViewModel { get; }

    internal SelectionToolbarWindow(SelectionOverlayViewModel viewModel, Action confirm, Action cancel)
    {
        ViewModel = viewModel;
        _confirm = confirm;
        _cancel = cancel;
        InitializeComponent();
        Title = "FIH Lens — select region";
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        ToolbarRoot.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(KeyDown), true);
        ToolbarRoot.Loaded += (_, _) => CreateButton.Focus(FocusState.Programmatic);
    }

    private void Create_Click(object sender, RoutedEventArgs args) => ViewModel.CreateKeyboardSelection();
    private void Confirm_Click(object sender, RoutedEventArgs args) => _confirm();
    private void Cancel_Click(object sender, RoutedEventArgs args) => _cancel();
    private void KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape) _cancel();
        else if (args.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            // Let native buttons activate when focus is inside their visual template.
            for (DependencyObject? element = args.OriginalSource as DependencyObject; element is not null;
                 element = VisualTreeHelper.GetParent(element))
                if (element is Button) return;
            if (args.Key == VirtualKey.Enter) _confirm();
            else ViewModel.CreateKeyboardSelection();
        }
        else if (args.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            int x = args.Key == VirtualKey.Left ? -1 : args.Key == VirtualKey.Right ? 1 : 0;
            int y = args.Key == VirtualKey.Up ? -1 : args.Key == VirtualKey.Down ? 1 : 0;
            ViewModel.AdjustKeyboard(x, y,
                InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down));
        }
        else return;
        args.Handled = true;
    }
}
