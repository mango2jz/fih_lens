namespace FihLens.Application.Capture;

public interface ISelectedTextCaptureSource
{
    Task<SelectedTextCaptureResult> CaptureAsync(CancellationToken cancellationToken);
}
