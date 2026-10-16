using FihLens.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FihLens.WinUI.Views;

public sealed partial class VocabularyCards : UserControl
{
    public VocabularyCards() => InitializeComponent();

    public IEnumerable<VocabularyCardViewModel>? Items
    {
        get => (IEnumerable<VocabularyCardViewModel>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable<VocabularyCardViewModel>), typeof(VocabularyCards),
        new PropertyMetadata(null));
}
