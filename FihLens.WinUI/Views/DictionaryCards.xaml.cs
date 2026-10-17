using FihLens.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
}
