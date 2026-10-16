using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FihLens.Application.Pronunciation;
using FihLens.Core.Enums;

namespace FihLens.WinUI.ViewModels;

/// <summary>One window's cancellation ownership; the service owns native playback.</summary>
public sealed class PronunciationViewModel : ObservableObject
{
    private readonly IPronunciationService? _service;
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private bool _closed;
    private bool _isPlaying;
    private string _status = "";

    public PronunciationViewModel(IPronunciationService? service = null)
    {
        _service = service;
        SpeakCommand = new AsyncRelayCommand<string>(SpeakAsync, CanSpeak,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        StopCommand = new RelayCommand(Stop, () => !_closed && IsPlaying);
    }

    public string SourceText { get; private set; } = "";
    public Language SourceLanguage { get; private set; } = Language.English;
    public bool IsPlaying => _isPlaying;
    public string Status => _status;
    public string SourceUnavailableReason => GetUnavailableReason(SourceText) ?? "";
    public string SourceAutomationName => $"Pronounce original {SourceLanguage} text";
    public IAsyncRelayCommand<string> SpeakCommand { get; }
    public IRelayCommand StopCommand { get; }

    public string? GetUnavailableReason(string? text)
    {
        if (_closed) return "This window is closed.";
        if (_service is null) return "Pronunciation is unavailable in this preview.";
        try { return _service.GetUnavailableReason(text ?? "", SourceLanguage); }
        catch (Exception) { return "Speech voices are unavailable on this device."; }
    }

    public void SetSource(string text, Language language)
    {
        if (_closed || (SourceText == text && SourceLanguage == language)) return;
        Stop();
        SourceText = text;
        SourceLanguage = language;
        foreach (string property in new[] { nameof(SourceText), nameof(SourceLanguage),
            nameof(SourceUnavailableReason), nameof(SourceAutomationName) }) OnPropertyChanged(property);
        SpeakCommand.NotifyCanExecuteChanged();
    }

    private bool CanSpeak(string? text) => GetUnavailableReason(text) is null;

    private async Task SpeakAsync(string? text)
    {
        if (!CanSpeak(text)) return;
        Stop();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        long generation = ++_generation;
        SetProperty(ref _isPlaying, true, nameof(IsPlaying));
        SetProperty(ref _status, $"Speaking {SourceLanguage}…", nameof(Status));
        StopCommand.NotifyCanExecuteChanged();
        try
        {
            await _service!.SpeakAsync(text!, SourceLanguage, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_closed && generation == _generation)
                SetProperty(ref _status, "Pronunciation failed. Try again.", nameof(Status));
        }
        finally
        {
            if (!_closed && generation == _generation)
            {
                SetProperty(ref _isPlaying, false, nameof(IsPlaying));
                if (_status.StartsWith("Speaking ", StringComparison.Ordinal))
                    SetProperty(ref _status, "", nameof(Status));
                StopCommand.NotifyCanExecuteChanged();
            }
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
        }
    }

    public void Stop()
    {
        ++_generation;
        _cancellation?.Cancel();
        _cancellation = null;
        SetProperty(ref _isPlaying, false, nameof(IsPlaying));
        SetProperty(ref _status, "", nameof(Status));
        StopCommand.NotifyCanExecuteChanged();
    }

    public void Close()
    {
        if (_closed) return;
        Stop();
        _closed = true;
        SpeakCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SourceUnavailableReason));
    }
}
