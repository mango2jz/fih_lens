using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FihLens.WinUI.Views;

/// <summary>Lays children out left to right in wrapping rows (.fl-words); a <see cref="WordLineBreak"/> child starts a new row.</summary>
public sealed partial class WordWrapPanel : Panel
{
    public double ColumnSpacing { get; set; } = 4;
    public double RowSpacing { get; set; }

    protected override Size MeasureOverride(Size availableSize) => Layout(availableSize, arrange: false);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize, arrange: true);
        return finalSize;
    }

    private Size Layout(Size available, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, width = 0;
        foreach (var child in Children)
        {
            if (!arrange) child.Measure(new Size(available.Width, double.PositiveInfinity));
            if (child is WordLineBreak)
            {
                (x, y, rowHeight) = (0, y + rowHeight + RowSpacing, 0);
                continue;
            }
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > available.Width) (x, y, rowHeight) = (0, y + rowHeight + RowSpacing, 0);
            if (arrange) child.Arrange(new Rect(x, y, size.Width, size.Height));
            width = Math.Max(width, x + size.Width);
            x += size.Width + ColumnSpacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(width, y + rowHeight);
    }
}

/// <summary>Marks a line break from the captured text inside a <see cref="WordWrapPanel"/>.</summary>
public sealed partial class WordLineBreak : FrameworkElement { }
