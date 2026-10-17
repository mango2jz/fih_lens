using FihLens.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FihLens.WinUI.Views;

public sealed partial class DictionaryManagementDialog : ContentDialog
{
    public DictionaryManagementViewModel ViewModel { get; }
    public DictionaryManagementDialog(DictionaryManagementViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Closing += OnClosing;
    }

    private async void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        var deferral = args.GetDeferral();
        try { await ViewModel.CloseAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { ViewModel.StatusText = "Setup preference could not be saved. You can skip again next time."; }
        finally { deferral.Complete(); }
    }
}
