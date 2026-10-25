using FihLens.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

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

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void ShowAttachedFlyout_Click(object sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);

    // The switch reflects the pack, not the click: a change that cannot start flips back; a finished one reloads the rows.
    private async void Pack_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { Tag: DictionaryPackRowViewModel pack } toggle || toggle.IsOn == pack.IsOn) return;
        bool started;
        try { started = await ViewModel.SetPackEnabledAsync(pack, toggle.IsOn); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            started = false;
            ViewModel.StatusText = "Dictionary operation could not complete. The last working installation is preserved; retry when ready.";
        }
        if (!started) toggle.IsOn = pack.IsOn;
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
