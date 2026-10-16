using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Phase1NativeCapture;

internal static class FixtureOcr
{
    internal record Result(string Text, string[] AvailableLanguages, string RecognizerLanguage, uint MaxImageDimension, bool BlankIsEmpty);

    internal static async Task<Result> RecognizeAsync(SoftwareBitmap crop, CancellationToken cancellation, Action<Windows.Foundation.AsyncStatus>? recognitionStarted = null, Action? recognitionDrained = null)
    {
        cancellation.ThrowIfCancellationRequested();
        _ = Windows.ApplicationModel.Package.Current.Id.FullName;
        var available = OcrEngine.AvailableRecognizerLanguages;
        var languages = new List<Language>();
        for (var index = 0; index < available.Count; index++) languages.Add(available[index]);
        var english = SelectLanguage(languages, "en-US")
            ?? languages.FirstOrDefault(x => x.LanguageTag.StartsWith("en-", StringComparison.OrdinalIgnoreCase));
        if (english is null) throw new NotSupportedException("No installed English OCR recognizer.");
        var engine = RequireEngine(OcrEngine.TryCreateFromLanguage(english));
        var max = OcrEngine.MaxImageDimension;
        CheckDimensions(crop.PixelWidth, crop.PixelHeight, max);
        await SelfCheckAsync(crop, languages, max);
        cancellation.ThrowIfCancellationRequested();
        var nativeRecognition = engine.RecognizeAsync(crop);
        Windows.Media.Ocr.OcrResult recognized;
        try
        {
            try { recognitionStarted?.Invoke(nativeRecognition.Status); }
            catch { await nativeRecognition; throw; }
            recognized = await nativeRecognition;
        }
        finally { recognitionDrained?.Invoke(); }
        cancellation.ThrowIfCancellationRequested();
        CheckDimensions(64, 64, max);
        using var blank = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 64, 64, BitmapAlphaMode.Premultiplied);
        var blankPixels = new byte[64 * 64 * 4];
        Array.Fill(blankPixels, (byte)255);
        blank.CopyFromBuffer(blankPixels.AsBuffer());
        var empty = await engine.RecognizeAsync(blank);
        cancellation.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(empty.Text)) throw new InvalidOperationException("Blank fixture was not empty.");
        var text = recognized.Text;
        if (!text.Contains("42", StringComparison.Ordinal) || !text.Contains("books", StringComparison.OrdinalIgnoreCase)
            || !text.Contains("quick brown fox", StringComparison.OrdinalIgnoreCase) || !text.Contains("FIH LENS CAPTURE TEST", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Known English fixture assertions failed.");
        return new(text, languages.Select(x => x.LanguageTag).ToArray(), engine.RecognizerLanguage.LanguageTag, max, true);
    }

    internal static void CheckDimensions(int width, int height, uint max)
    {
        if (width <= 0 || height <= 0 || width > max || height > max || checked((long)width * height * 4) > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(width), "OCR dimensions rejected before allocation.");
    }

    private static OcrEngine RequireEngine(OcrEngine? engine) => engine ?? throw new NotSupportedException("OCR recognizer unavailable.");

    private static Language? SelectLanguage(List<Language> languages, string tag) => languages.FirstOrDefault(x => x.LanguageTag.Equals(tag, StringComparison.OrdinalIgnoreCase));

    private static async Task SelfCheckAsync(SoftwareBitmap crop, List<Language> languages, uint max)
    {
        if (SelectLanguage(languages, "zz-ZZ") is not null)
            throw new InvalidOperationException("Unavailable language was selected.");
        try { RequireEngine(null); throw new InvalidOperationException("Missing recognizer accepted."); }
        catch (NotSupportedException) { }
        try { CheckDimensions(checked((int)max + 1), 1, max); throw new InvalidOperationException("Oversize OCR accepted."); }
        catch (ArgumentOutOfRangeException) { }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await RecognizeAsync(crop, canceled.Token); throw new InvalidOperationException("Canceled OCR accepted."); }
        catch (OperationCanceledException) { }
    }
}
