using FihLens.WinUI.ViewModels;
using Microsoft.UI.Xaml;

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
}
