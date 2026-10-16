using FihLens.Application.Capture;

namespace FihLens.WinUI.Presentation;

public static class SelectionGeometry
{
    public static bool ContainsPoint(PhysicalPixelRect bounds, int x, int y) => bounds.IsValid &&
        x >= bounds.X && y >= bounds.Y && (long)x <= (long)bounds.X + bounds.Width &&
        (long)y <= (long)bounds.Y + bounds.Height;

    public static PhysicalPixelRect? Drag(PhysicalPixelRect bounds, int startX, int startY, int endX, int endY)
    {
        if (!ContainsPoint(bounds, startX, startY) || !ContainsPoint(bounds, endX, endY)) return null;
        var rect = new PhysicalPixelRect(Math.Min(startX, endX), Math.Min(startY, endY),
            (int)Math.Abs((long)endX - startX), (int)Math.Abs((long)endY - startY));
        return bounds.Contains(rect) ? rect : null;
    }

    public static PhysicalPixelRect? Adjust(PhysicalPixelRect bounds, PhysicalPixelRect current,
        int deltaX, int deltaY, bool resize)
    {
        if (!bounds.Contains(current)) return null;
        long x = (long)current.X + (resize ? 0 : deltaX);
        long y = (long)current.Y + (resize ? 0 : deltaY);
        long width = (long)current.Width + (resize ? deltaX : 0);
        long height = (long)current.Height + (resize ? deltaY : 0);
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue ||
            width < 1 || width > int.MaxValue || height < 1 || height > int.MaxValue) return null;
        var rect = new PhysicalPixelRect((int)x, (int)y, (int)width, (int)height);
        return bounds.Contains(rect) ? rect : null;
    }

    /// <summary>Convert a root-relative DIP endpoint once, using the frozen physical client origin.</summary>
    public static int ToDesktopPixel(double dip, double scale, int clientOrigin)
    {
        if (!double.IsFinite(dip) || !double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));
        double pixel = Math.Round(dip * scale, MidpointRounding.ToEven) + clientOrigin;
        if (!double.IsFinite(pixel) || pixel < int.MinValue || pixel > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(dip));
        return (int)pixel;
    }
}
