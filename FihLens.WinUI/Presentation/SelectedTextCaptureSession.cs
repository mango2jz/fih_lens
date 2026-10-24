using FihLens.Application.Capture;

namespace FihLens.WinUI.Presentation;

/// <summary>Owns one selected-text attempt; native draining remains owned by its source lease.</summary>
public sealed class SelectedTextCaptureSession : IDisposable
{
    private readonly ISelectedTextCaptureSource _capture;
    private readonly Func<Action, Task<bool>> _publishOnUi;
    private readonly Func<SelectedTextCaptureOutcome?> _validateForCommit;
    private readonly Func<SelectedTextCaptureOutcome?> _validateForOffer;
    private readonly Action<string> _commitText;
    private readonly Action<SelectedTextCaptureOutcome> _offerRegion;
    private readonly Action<SelectedTextCaptureOutcome> _reportFailure;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Func<Task> _drainCompletion;
    private readonly object _lifetime = new();
    private Task? _cancellationRetirement;
    private bool _started;
    private volatile bool _closed;
    private bool _cancellationDisposed;
    private bool _committed;
    private bool _offered;

    public SelectedTextCaptureSession(ISelectedTextCaptureSource capture, Func<Task> drainCompletion,
        Func<Action, Task<bool>> publishOnUi, Func<SelectedTextCaptureOutcome?> validateForCommit,
        Func<SelectedTextCaptureOutcome?> validateForOffer,
        Action<string> commitText, Action<SelectedTextCaptureOutcome> offerRegion,
        Action<SelectedTextCaptureOutcome> reportFailure)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _drainCompletion = drainCompletion ?? throw new ArgumentNullException(nameof(drainCompletion));
        _publishOnUi = publishOnUi ?? throw new ArgumentNullException(nameof(publishOnUi));
        _validateForCommit = validateForCommit ?? throw new ArgumentNullException(nameof(validateForCommit));
        _validateForOffer = validateForOffer ?? throw new ArgumentNullException(nameof(validateForOffer));
        _commitText = commitText ?? throw new ArgumentNullException(nameof(commitText));
        _offerRegion = offerRegion ?? throw new ArgumentNullException(nameof(offerRegion));
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    public Task DrainCompletion => _drainCompletion();
    public Task Completion { get; private set; } = Task.CompletedTask;
    internal Task CancellationRetirement => _cancellationRetirement ?? Task.CompletedTask;
    internal bool IsCancellationRetired => _cancellationDisposed;
    public SelectedTextCaptureOutcome? Outcome { get; private set; }
    public bool IsClosed => _closed;
    public bool IsCommitted => _committed;
    public bool IsOffered => _offered;

    public bool Start()
    {
        if (_started || _closed) return false;
        _started = true;
        Completion = CaptureAsync();
        lock (_lifetime) _cancellationRetirement ??= RetireCancellationAsync();
        return true;
    }

    private async Task CaptureAsync()
    {
        SelectedTextCaptureResult result;
        try { result = await _capture.CaptureAsync(_cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { return; }
        catch { result = SelectedTextCaptureResult.Failure(SelectedTextCaptureOutcome.Failed); }

        Outcome = result.Outcome;
        if (_cancellation.IsCancellationRequested || _closed) return;
        if (result.Outcome == SelectedTextCaptureOutcome.Success && result.Text is { } text)
        {
            await PublishAsync(() =>
            {
                if (_closed || _cancellation.IsCancellationRequested) return;
                var invalid = _validateForCommit();
                if (invalid is { } reason)
                {
                    Outcome = reason;
                    if (SelectedTextCapturePolicy.CanOfferRegion(reason))
                    {
                        var offerInvalid = _validateForOffer();
                        if (offerInvalid == SelectedTextCaptureOutcome.SourceChanged) { Outcome = offerInvalid; return; }
                        Offer(reason);
                    }
                    else if (reason != SelectedTextCaptureOutcome.SourceChanged) _reportFailure(reason);
                    return;
                }
                _committed = true;
                _commitText(text);
            }).ConfigureAwait(false);
            return;
        }

        await PublishAsync(() =>
        {
            if (_closed || _cancellation.IsCancellationRequested) return;
            var invalid = _validateForOffer();
            if (invalid == SelectedTextCaptureOutcome.SourceChanged) { Outcome = invalid; return; }
            if (SelectedTextCapturePolicy.CanOfferRegion(result.Outcome))
            {
                var outcome = invalid ?? result.Outcome;
                if (SelectedTextCapturePolicy.CanOfferRegion(outcome)) Offer(outcome);
                else { Outcome = outcome; _reportFailure(outcome); }
                return;
            }
            Outcome = result.Outcome;
            _reportFailure(Outcome.Value);
        }).ConfigureAwait(false);
    }

    private async Task<bool> PublishAsync(Action action)
    {
        try
        {
            bool published = await _publishOnUi(action).ConfigureAwait(false);
            if (!published) Cancel();
            return published;
        }
        catch
        {
            Cancel();
            throw;
        }
    }

    private void Offer(SelectedTextCaptureOutcome outcome)
    {
        Outcome = outcome;
        _offered = true;
        _offerRegion(outcome);
    }

    public void Cancel()
    {
        lock (_lifetime)
        {
            if (_closed) return;
            _closed = true;
            if (!_cancellationDisposed) _cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        Cancel();
        lock (_lifetime) _cancellationRetirement ??= RetireCancellationAsync();
    }

    private async Task RetireCancellationAsync()
    {
        try { await DrainCompletion.ConfigureAwait(false); }
        catch { }
        lock (_lifetime)
        {
            if (_cancellationDisposed) return;
            _cancellationDisposed = true;
            _cancellation.Dispose();
        }
    }
}
