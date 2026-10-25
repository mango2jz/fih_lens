using FihLens.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FihLens.WinUI.Views;

public sealed partial class DictionaryCards : UserControl
{
    public DictionaryCards() => InitializeComponent();
    public IEnumerable<DictionaryCardViewModel>? Items
    {
        get => (IEnumerable<DictionaryCardViewModel>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable<DictionaryCardViewModel>), typeof(DictionaryCards), new PropertyMetadata(null));

    /// <summary>Full attribution, license and notices of the dictionary the cards came from.</summary>
    public string SourceDetails
    {
        get => (string)GetValue(SourceDetailsProperty);
        set => SetValue(SourceDetailsProperty, value);
    }
    public static readonly DependencyProperty SourceDetailsProperty = DependencyProperty.Register(
        nameof(SourceDetails), typeof(string), typeof(DictionaryCards), new PropertyMetadata(""));

    private void Source_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DictionaryCardViewModel card } link) return;
        var text = string.IsNullOrEmpty(SourceDetails) ? card.Provenance : $"{card.Provenance}\n\n{SourceDetails}";
        var content = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["Phase1CaptionTextStyle"],
        };
        new Flyout
        {
            Content = new ScrollViewer { Content = content, MaxHeight = 320, MaxWidth = 420 },
            Placement = FlyoutPlacementMode.Top,
        }.ShowAt(link);
    }
}
