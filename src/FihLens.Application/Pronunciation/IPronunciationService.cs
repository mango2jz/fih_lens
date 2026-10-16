using FihLens.Core.Enums;

namespace FihLens.Application.Pronunciation;

public interface IPronunciationService
{
    /// <summary>Returns a reason when text, language or installed voice cannot support this request.</summary>
    string? GetUnavailableReason(string text, Language language);

    /// <summary>Completes after playback retires. A valid new request supersedes prior speech with cancellation; callers cancel only their own token.</summary>
    Task SpeakAsync(string text, Language language, CancellationToken cancellationToken = default);
}

public static class PronunciationInput
{
    public static string? GetUnavailableReason(string text, Language language)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Enter a word to pronounce.";
        if (text.Length > 2000) return "Text must be 2,000 characters or fewer.";
        if (!IsWellFormedUnicode(text)) return "Text contains invalid Unicode.";
        if (language is not (Language.English or Language.Vietnamese or Language.Japanese))
            return "Choose English, Vietnamese, or Japanese.";
        return null;
    }

    private static bool IsWellFormedUnicode(string text)
    {
        for (var index = 0; index < text.Length;)
        {
            var status = System.Text.Rune.DecodeFromUtf16(text.AsSpan(index), out _, out var consumed);
            if (status != System.Buffers.OperationStatus.Done) return false;
            index += consumed;
        }

        return true;
    }
}
