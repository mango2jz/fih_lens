namespace Phase1NativeCapture;

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
