namespace FihLens.WinUI.Windowing;

internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

internal readonly record struct PopupPlacement(PixelRect OuterBounds, int ClientWidth, int ClientHeight)
{
    // Work area and preferred outer position are already desktop physical pixels.
    public static PopupPlacement Calculate(
        PixelRect desktopWorkArea,
        int preferredX,
        int preferredY,
        double requestedWidthDip,
        double requestedHeightDip,
        double scale,
        int measuredExtraWidth,
        int measuredExtraHeight)
    {
        if (desktopWorkArea.Width <= 0 || desktopWorkArea.Height <= 0 ||
            (long)desktopWorkArea.X + desktopWorkArea.Width > int.MaxValue ||
            (long)desktopWorkArea.Y + desktopWorkArea.Height > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(desktopWorkArea));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(measuredExtraWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(measuredExtraHeight);
        if (measuredExtraWidth >= desktopWorkArea.Width || measuredExtraHeight >= desktopWorkArea.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(desktopWorkArea), "Work area must leave a positive client size.");
        }

        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        int clientWidth = Math.Min(ToPixels(requestedWidthDip, scale, nameof(requestedWidthDip)),
            desktopWorkArea.Width - measuredExtraWidth);
        int clientHeight = Math.Min(ToPixels(requestedHeightDip, scale, nameof(requestedHeightDip)),
            desktopWorkArea.Height - measuredExtraHeight);
        int outerWidth = clientWidth + measuredExtraWidth;
        int outerHeight = clientHeight + measuredExtraHeight;
        int x = Math.Clamp(preferredX, desktopWorkArea.X, desktopWorkArea.X + desktopWorkArea.Width - outerWidth);
        int y = Math.Clamp(preferredY, desktopWorkArea.Y, desktopWorkArea.Y + desktopWorkArea.Height - outerHeight);
        return new PopupPlacement(new PixelRect(x, y, outerWidth, outerHeight), clientWidth, clientHeight);
    }

    private static int ToPixels(double dip, double scale, string parameterName)
    {
        // Match the native feasibility probe: nearest physical pixel, midpoint to even.
        double pixels = Math.Round(dip * scale, MidpointRounding.ToEven);
        if (!double.IsFinite(dip) || dip <= 0 || !double.IsFinite(pixels) || pixels < 1 || pixels > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return (int)pixels;
    }
}
