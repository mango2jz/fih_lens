using CommunityToolkit.Mvvm.ComponentModel;
using FihLens.Application.Capture;

namespace FihLens.WinUI.Presentation;

/// <summary>One selection/acquisition owner. Call its methods on the UI thread.</summary>
public sealed class RegionCaptureSession : ObservableObject
{
    private readonly IRegionTextCapture _capture;
    private readonly Func<Action, Task<bool>> _publishOnUi;
    private CancellationTokenSource? _cancellation;
    private bool _dismissed;
    private bool _dispatched;
    private bool _isBusy;
    private RegionTextCaptureRequest? _request;
    private RegionTextCaptureResult? _result;

    /// <param name="publishOnUi">True only after the UI action completes; false when the dispatcher is unavailable. Unexpected faults propagate.</param>
    public RegionCaptureSession(IRegionTextCapture capture, Func<Action, Task<bool>> publishOnUi)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _publishOnUi = publishOnUi ?? throw new ArgumentNullException(nameof(publishOnUi));
    }

    public bool IsBusy => _isBusy;
    public bool IsCanceled => _dismissed;
    public RegionTextCaptureRequest? Request => _request;
    public RegionTextCaptureResult? Result => _result;

    /// <summary>Freezes one request. The host must now request overlay closure, without dismissing this owner.</summary>
    public bool Confirm(RegionTextCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_dismissed || _request is not null) return false;
        _request = request;
        _cancellation = new CancellationTokenSource();
        _isBusy = true;
        OnPropertyChanged(nameof(Request));
        OnPropertyChanged(nameof(IsBusy));
        return true;
    }

    /// <summary>Call only after the actual confirmed, programmatic overlay Closed acknowledgement.</summary>
    public async Task OverlayRetiredAsync()
    {
        if (_dismissed || _dispatched || _request is null) return;
        _dispatched = true;
        var cancellation = _cancellation!;
        RegionTextCaptureResult? result;
        try
        {
            result = await _capture.CaptureAsync(_request, cancellation.Token);
            result ??= RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.Failed);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            result = null;
        }
        catch (Exception)
        {
            // Provider/native exception messages may contain captured content; expose only a typed failure.
            result = RegionTextCaptureResult.Failure(RegionTextCaptureOutcome.Failed);
        }
        finally
        {
            // Retain the CTS and busy gate until the underlying native operation has drained.
            cancellation.Dispose();
        }

        bool accepted = false;
        try
        {
            accepted = await _publishOnUi(() =>
            {
                if (!_isBusy) return;
                _cancellation = null;
                if (!_dismissed) _result = result;
                _isBusy = false;
                OnPropertyChanged(nameof(Result));
                OnPropertyChanged(nameof(IsBusy));
            });
        }
        finally
        {
            if (!accepted)
            {
                // Retire failed publication without notifications; unexpected faults remain observable.
                _cancellation = null;
                _dismissed = true;
                _result = null;
                _isBusy = false;
            }
        }
    }

    /// <summary>Escape, unexpected closure, result dismissal, reselect or shutdown invalidate publication.</summary>
    public void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        _result = null;
        if (_cancellation is { } cancellation)
        {
            // Cancellation callbacks are external code; they must not prevent retirement of this owner.
            try { cancellation.Cancel(); }
            catch (AggregateException) { }
            catch (ObjectDisposedException) { }
            if (!_dispatched)
            {
                cancellation.Dispose();
                _cancellation = null;
            }
        }
        if (!_dispatched) _isBusy = false;
        OnPropertyChanged(nameof(IsCanceled));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(IsBusy));
    }
}
