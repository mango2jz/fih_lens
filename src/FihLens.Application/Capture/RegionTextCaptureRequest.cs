using FihLens.Core.Enums;

namespace FihLens.Application.Capture;

public sealed record CaptureSourceSnapshot
{
    public CaptureSourceSnapshot(string sourceId, PhysicalPixelRect displayBounds, string topologyStamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(topologyStamp);
        if (!displayBounds.IsValid) throw new ArgumentOutOfRangeException(nameof(displayBounds));
        SourceId = sourceId;
        DisplayBounds = displayBounds;
        TopologyStamp = topologyStamp;
    }

    public string SourceId { get; }
    public PhysicalPixelRect DisplayBounds { get; }
    public string TopologyStamp { get; }
}

public sealed record RegionTextCaptureRequest
{
    public RegionTextCaptureRequest(CaptureSourceSnapshot source, PhysicalPixelRect crop, Language sourceLanguage = Language.English)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.DisplayBounds.Contains(crop)) throw new ArgumentOutOfRangeException(nameof(crop));
        if (sourceLanguage is not (Language.English or Language.Vietnamese or Language.Japanese))
            throw new ArgumentOutOfRangeException(nameof(sourceLanguage));
        Source = source;
        Crop = crop;
        SourceLanguage = sourceLanguage;
    }

    public CaptureSourceSnapshot Source { get; }
    public PhysicalPixelRect Crop { get; }
    public Language SourceLanguage { get; }
}
