using CommunityToolkit.Mvvm.ComponentModel;
using FihLens.Application.Capture;
using FihLens.Core.Enums;
using FihLens.WinUI.Presentation;

namespace FihLens.WinUI.ViewModels;

/// <summary>Physical selection only; the native host owns pointer capture, focus and scale invalidation.</summary>
public sealed class SelectionOverlayViewModel(CaptureSourceSnapshot source, Language sourceLanguage = Language.English) : ObservableObject
{
    private int _startX;
    private int _startY;
    private bool _dragging;
    private bool _departed;
    private bool _closed;
    private PhysicalPixelRect? _selection;
    private string _status = "Drag to select. Keyboard: create, arrows move, Shift+arrows resize, Enter confirm, Escape cancel.";

    public CaptureSourceSnapshot Source { get; } = source ?? throw new ArgumentNullException(nameof(source));
    public Language SourceLanguage { get; } = sourceLanguage is Language.English or Language.Vietnamese or Language.Japanese
        ? sourceLanguage : throw new ArgumentOutOfRangeException(nameof(sourceLanguage));
    public PhysicalPixelRect? Selection => _selection;
    public string Dimensions => _selection is { } rect ? $"{rect.Width} × {rect.Height} px" : "No region selected";
    public string Status => _status;
    public bool CanConfirm => !_closed && !_dragging && !_departed && _selection is not null;

    public void BeginDrag(int x, int y)
    {
        if (_closed) return;
        _startX = x;
        _startY = y;
        _dragging = true;
        _departed = !SelectionGeometry.ContainsPoint(Source.DisplayBounds, x, y);
        Update(null, _departed ? "Select within one display." : "Drag to select.");
    }

    public void MoveDrag(int x, int y)
    {
        if (_closed || !_dragging) return;
        _departed |= !SelectionGeometry.ContainsPoint(Source.DisplayBounds, x, y);
        Update(_departed ? null : SelectionGeometry.Drag(Source.DisplayBounds, _startX, _startY, x, y),
            _departed ? "Cross-display selection rejected. Start a new selection." : "Release to finish selection.");
    }

    public void EndDrag(int x, int y)
    {
        if (_closed || !_dragging) return;
        MoveDrag(x, y);
        _dragging = false;
        Update(_selection, _departed ? _status : _selection is null ? "Select a nonzero region." : "Enter to confirm.");
    }

    public void CreateKeyboardSelection()
    {
        if (_closed) return;
        _dragging = false;
        _departed = false;
        var bounds = Source.DisplayBounds;
        Update(new(bounds.X, bounds.Y, 1, 1), "Arrows move; Shift+arrows resize. Enter confirms.");
    }

    public void AdjustKeyboard(int deltaX, int deltaY, bool resize)
    {
        if (_closed || _dragging || _departed || _selection is not { } rect) return;
        var adjusted = SelectionGeometry.Adjust(Source.DisplayBounds, rect, deltaX, deltaY, resize);
        // Preserve the prior valid region rather than silently clipping an invalid adjustment.
        Update(adjusted ?? rect, adjusted is null ? "Adjustment rejected: keep a positive region within this display." : "Enter to confirm.");
    }

    public RegionTextCaptureRequest? Confirm()
    {
        if (!CanConfirm) return null;
        var request = new RegionTextCaptureRequest(Source, _selection!.Value, SourceLanguage);
        _closed = true;
        Update(_selection, "Selection confirmed; waiting for overlay retirement.");
        return request;
    }

    public void Cancel()
    {
        _closed = true;
        _dragging = false;
        Update(null, "Selection canceled.");
    }

    private void Update(PhysicalPixelRect? selection, string status)
    {
        _selection = selection;
        _status = status;
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(Dimensions));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanConfirm));
    }
}
