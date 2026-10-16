using FihLens.Application.Pronunciation;
using FihLens.Core.Enums;
using System.Runtime.InteropServices;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace FihLens.Windows.Pronunciation;

public sealed class WindowsPronunciationService : IPronunciationService, IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private readonly HashSet<Task> _work = [];
    private Task? _lastPlayback;
    private Task? _disposeTask;
    private bool _disposed;

    public string? GetUnavailableReason(string text, Language language)
    {
        var inputReason = PronunciationInput.GetUnavailableReason(text, language);
        if (inputReason is not null) return inputReason;

        var locale = GetLocale(language);
        var voices = GetInstalledVoices();
        if (voices is null) return "Windows speech voices are unavailable.";
        return voices.Any(voice => MatchesLocale(voice.Language, locale))
            ? null
            : $"No installed {LanguageName(language)} speech voice is available.";
    }

    public Task SpeakAsync(string text, Language language, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reason = PronunciationInput.GetUnavailableReason(text, language);
        if (reason is not null) return Task.FromException(new InvalidOperationException(reason));

        var voices = GetInstalledVoices();
        if (voices is null) return Task.FromException(new InvalidOperationException("Windows speech voices are unavailable."));
        var voice = voices.FirstOrDefault(item => MatchesLocale(item.Language, GetLocale(language)));
        if (voice is null) return Task.FromException(new InvalidOperationException($"No installed {LanguageName(language)} speech voice is available."));
        CancellationTokenSource owner;
        Task playback;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            owner = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var displaced = _current;
            var priorPlayback = _lastPlayback;
            _current = owner;
            playback = RunSpeechAsync(text, voice, owner, priorPlayback);
            _lastPlayback = playback;
            _work.Add(playback);
            _ = playback.ContinueWith(completed =>
            {
                lock (_gate) _work.Remove(completed);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            displaced?.Cancel();
        }
        return playback;
    }

    private async Task RunSpeechAsync(string text, VoiceInformation voice, CancellationTokenSource owner, Task? priorPlayback)
    {
        await Task.Yield();
        try
        {
            if (priorPlayback is not null) await RetirePriorPlaybackAsync(priorPlayback).ConfigureAwait(false);
            owner.Token.ThrowIfCancellationRequested();
            using var synthesizer = new SpeechSynthesizer { Voice = voice };
            using var stream = await synthesizer.SynthesizeTextToStreamAsync(text).AsTask().ConfigureAwait(false);
            owner.Token.ThrowIfCancellationRequested();
            using var source = MediaSource.CreateFromStream(stream, stream.ContentType);
            using var player = new MediaPlayer { Source = source };
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnEnded(MediaPlayer sender, object args) => finished.TrySetResult();
            void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
                finished.TrySetException(new InvalidOperationException("Speech playback failed."));
            player.MediaEnded += OnEnded;
            player.MediaFailed += OnFailed;
            try
            {
                owner.Token.ThrowIfCancellationRequested();
                player.Play();
                await finished.Task.WaitAsync(owner.Token).ConfigureAwait(false);
            }
            finally
            {
                player.Pause();
                player.MediaEnded -= OnEnded;
                player.MediaFailed -= OnFailed;
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, owner)) _current = null;
            }
            owner.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposed = true;
            var current = _current;
            _current = null;
            current?.Cancel();
            _disposeTask = DrainAsync(_work.ToArray());
            return new ValueTask(_disposeTask);
        }
    }

    private static async Task DrainAsync(Task[] work)
    {
        foreach (var task in work)
        {
            try { await task.ConfigureAwait(false); }
            catch { /* Each speech failure was already reported to its caller. */ }
        }
    }

    private static async Task RetirePriorPlaybackAsync(Task playback)
    {
        try { await playback.ConfigureAwait(false); }
        catch { /* The displaced caller receives its own cancellation or playback failure. */ }
    }

    private static string GetLocale(Language language) => language switch
    {
        Language.English => "en",
        Language.Vietnamese => "vi",
        Language.Japanese => "ja",
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };

    private static bool MatchesLocale(string voiceLanguage, string locale) =>
        voiceLanguage.Equals(locale, StringComparison.OrdinalIgnoreCase) ||
        voiceLanguage.StartsWith(locale + "-", StringComparison.OrdinalIgnoreCase);

    private static VoiceInformation[]? GetInstalledVoices()
    {
        try { return SpeechSynthesizer.AllVoices.ToArray(); }
        catch (COMException) { return null; }
    }

    private static string LanguageName(Language language) => language switch
    {
        Language.English => "English",
        Language.Vietnamese => "Vietnamese",
        Language.Japanese => "Japanese",
        _ => "selected-language"
    };
}
