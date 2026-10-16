namespace Phase1CompositionOverlayProbe;

internal readonly record struct Bounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => checked(Right - Left);
    public int Height => checked(Bottom - Top);
    public bool Contains(Bounds other) => other.Width > 0 && other.Height > 0 && other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
    public bool Inside(int x, int y) => x > Left && x < Right && y > Top && y < Bottom;
}

internal sealed class ProbePolicy
{
    internal uint[]? Markers;
    internal readonly uint?[] Live = new uint?[2];
    internal uint? StrokeBaseline, SurroundBaseline, Stroke;
    internal readonly bool[] Phases = new bool[2];
    internal bool Passed;
    internal bool Calibrated => Markers is not null && Live.All(v => v.HasValue) && Live[0] != Live[1] && StrokeBaseline.HasValue && SurroundBaseline.HasValue;
    internal void ResetRun() { Array.Clear(Phases); Stroke = null; Passed = false; }
    internal void ResetCalibration() { ResetRun(); Markers = null; Array.Clear(Live); StrokeBaseline = SurroundBaseline = null; }
    internal void Calibrate(uint[] colors, int phase)
    {
        if (colors.Length != 6 || colors.Take(3).Distinct().Count() != 3 || colors[2] != 0xFFFFFF || colors[4] != 0xFFFFFF || colors[5] != 0xFFFFFF)
            throw new InvalidOperationException("Owned calibration markers invalid.");
        Markers ??= colors[..3]; StrokeBaseline ??= colors[4]; SurroundBaseline ??= colors[5];
        if (!Markers.SequenceEqual(colors[..3]) || (Live[phase] is uint prior && prior != colors[3]))
            throw new InvalidOperationException("Calibration unstable.");
        Live[phase] = colors[3];
    }
    internal bool Accept(uint[] colors, int phase)
    {
        if (!Calibrated || colors.Length != 6) return false;
        var dim = colors[5];
        var valid = Markers!.SequenceEqual(colors[..3]) && colors[3] == Live[phase]
            && colors[4] == 0x00C8FF && colors[4] != StrokeBaseline && (Stroke is null || Stroke == colors[4])
            && dim != 0 && (dim & 255) < 255 && ((dim >> 8) & 255) < 255 && ((dim >> 16) & 255) < 255;
        if (valid) { Stroke = colors[4]; Phases[phase] = true; }
        Passed = Phases.All(v => v);
        return valid;
    }
}
