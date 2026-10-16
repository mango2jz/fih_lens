using System.Diagnostics;
using System.Runtime.InteropServices;
using FihLens.Application.Capture;
using Windows.Media.Ocr;

namespace FihLens.Windows.Capture;

public sealed class WindowsRegionTextCapture : IRegionTextCapture
{
    public async Task<RegionTextCaptureResult> CaptureAsync(RegionTextCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        var markerQpc = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            NativeCapture.RequireSource(request.Source);
            var invalid = CaptureValidation.ValidateCrop(request.Source.DisplayBounds, request.Crop, OcrEngine.MaxImageDimension);
            if (invalid is { } outcome) return RegionTextCaptureResult.Failure(outcome);
            var locale = request.SourceLanguage switch
            {
                FihLens.Core.Enums.Language.English => "en-US",
                FihLens.Core.Enums.Language.Japanese => "ja-JP",
                FihLens.Core.Enums.Language.Vietnamese => "vi-VN",
                _ => throw new ArgumentOutOfRangeException(nameof(request.SourceLanguage))
            };
            var engine = OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language(locale));
            if (engine is null) return RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.RecognizerUnavailable);
            linked.Token.ThrowIfCancellationRequested();
            using var bitmap = await NativeCapture.CropAsync(request, markerQpc, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            NativeCapture.RequireSource(request.Source);
            // Drain recognition before releasing its bitmap, then discard stale/canceled output.
            var recognized = await engine.RecognizeAsync(bitmap);
            linked.Token.ThrowIfCancellationRequested();
            NativeCapture.RequireSource(request.Source);
            return string.IsNullOrWhiteSpace(recognized.Text)
                ? RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.NoText)
                : RegionTextCaptureResult.Success(recognized.Text);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            return RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.Timeout);
        }
        catch (CaptureSourceChangedException) { return RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.SourceChanged); }
        catch (TimeoutException) { return RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.Timeout); }
        catch (UnauthorizedAccessException) { return RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.Denied); }
        catch (NotSupportedException) { return RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.Unsupported); }
        catch (COMException exception) when (exception.HResult == unchecked((int)0x80070005))
        {
            return RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.Denied);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"OCR exceptionType={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
            return RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.Failed);
        }
    }
}
