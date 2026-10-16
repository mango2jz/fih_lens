using System.Globalization;
using FihLens.Application.Capture;

namespace FihLens.Windows.Capture;

internal static class CaptureValidation
{
    internal static string TopologyStamp(IEnumerable<(string Id, PhysicalPixelRect Bounds)> displays) =>
        string.Join(";", displays.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x =>
            string.Create(CultureInfo.InvariantCulture,
                $"{x.Id.Length}:{x.Id}:{x.Bounds.X},{x.Bounds.Y},{x.Bounds.Width},{x.Bounds.Height}")));

    internal static bool SourceMatches(CaptureSourceSnapshot frozen, CaptureSourceSnapshot current) =>
        frozen.SourceId == current.SourceId && frozen.DisplayBounds == current.DisplayBounds &&
        frozen.TopologyStamp == current.TopologyStamp;

    internal static RegionTextCaptureOutcome? ValidateCrop(PhysicalPixelRect display, PhysicalPixelRect crop, uint maxDimension)
    {
        if (!display.Contains(crop)) return RegionTextCaptureOutcome.InvalidRegion;
        if (crop.Width > maxDimension || crop.Height > maxDimension ||
            (long)crop.Width * crop.Height > 64 * 1024 * 1024 / 4)
            return RegionTextCaptureOutcome.TooLarge;
        return null;
    }

    internal static bool FrameMatches(PhysicalPixelRect display, int width, int height) =>
        display.IsValid && width == display.Width && height == display.Height;
}

internal static class FrameFreshness
{
    internal static bool IsAfter(long? frameTimeSpanTicks, long markerQpc, long frequency)
    {
        if (frameTimeSpanTicks is not > 0 || markerQpc < 0 || frequency <= 0) return false;
        // Allow one 100 ns timestamp quantum and one QPC tick of uncertainty.
        return ((Int128)frameTimeSpanTicks.Value - 1) * frequency
            > ((Int128)markerQpc + 1) * TimeSpan.TicksPerSecond;
    }
}
