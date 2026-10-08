using FihLens.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FihLens.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        Closed += (_, _) => ViewModel.Close();
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        if (ViewModel.LookupDictionaryCommand.CanExecute(null))
            ViewModel.LookupDictionaryCommand.Execute(null);
    }

    private void AskAi_Click(object sender, RoutedEventArgs e) => AiDetails.IsExpanded = true;
}
