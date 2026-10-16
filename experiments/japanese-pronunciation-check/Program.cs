using System.Reflection;
using FihLens.Core.Enums;
using FihLens.Windows.Pronunciation;
using Windows.Media.SpeechSynthesis;
using Windows.Media.Ocr;

try
{
    Console.WriteLine("Installed OCR languages: " + string.Join(", ", OcrEngine.AvailableRecognizerLanguages.Select(item => item.LanguageTag)));
}
catch (System.Runtime.InteropServices.COMException exception)
{
    Console.WriteLine($"OCR enumeration unavailable: 0x{exception.HResult:X8}");
}

await using var service = new WindowsPronunciationService();
var voicesAvailable = false;
try
{
    var voices = SpeechSynthesizer.AllVoices.ToArray();
    voicesAvailable = true;
    foreach (var voice in voices)
        Console.WriteLine($"Voice: {voice.Language} — {voice.DisplayName}");
}
catch (System.Runtime.InteropServices.COMException exception)
{
    Console.WriteLine($"Voice enumeration unavailable: 0x{exception.HResult:X8}");
}

var supported = new[] { Language.English, Language.Vietnamese, Language.Japanese };
foreach (var language in supported)
    Console.WriteLine($"{language}: {service.GetUnavailableReason("probe", language) ?? "available"}");

await AssertRejectedAsync(() => service.SpeakAsync("", Language.English), "blank input");
AssertNoCurrentRequest(service, "blank input");

var canceled = new CancellationToken(canceled: true);
await AssertRejectedAsync(() => service.SpeakAsync("probe", Language.English, canceled), "pre-canceled input");
AssertNoCurrentRequest(service, "pre-canceled input");

var missingLanguage = supported.FirstOrDefault(language => service.GetUnavailableReason("probe", language)
    is not null && voicesAvailable);
if (!voicesAvailable)
{
    await AssertRejectedAsync(() => service.SpeakAsync("probe", Language.English), "unavailable speech API");
    AssertNoCurrentRequest(service, "unavailable speech API");
}
else if (service.GetUnavailableReason("probe", missingLanguage) is not null)
{
    await AssertRejectedAsync(() => service.SpeakAsync("probe", missingLanguage), "missing voice");
    AssertNoCurrentRequest(service, "missing voice");
}
else
{
    Console.WriteLine(voicesAvailable
        ? "Missing voice check skipped: all three supported locales are installed."
        : "Missing locale check skipped: Windows could not enumerate installed voices.");
}

if (args.Contains("--play", StringComparer.Ordinal) && service.GetUnavailableReason("probe", Language.English) is null)
{
    using var oldOwner = new CancellationTokenSource();
    var first = service.SpeakAsync(string.Concat(Enumerable.Repeat("This is a synthetic pronunciation lifecycle check. ", 15)), Language.English, oldOwner.Token);
    await Task.Delay(250);
    var active = CurrentRequest(service);
    await AssertRejectedAsync(() => service.SpeakAsync("", Language.English), "blank input");
    if (!ReferenceEquals(active, CurrentRequest(service))) throw new InvalidOperationException("Invalid request displaced active speech.");
    var second = service.SpeakAsync("FIH Lens pronunciation check.", Language.English);
    await AssertCanceledAsync(first);
    oldOwner.Cancel();
    await second.WaitAsync(TimeSpan.FromSeconds(20));
    Console.WriteLine("Native supersession: old owner retired; later old-owner cancellation preserved new playback.");
    var closing = service.SpeakAsync("This speech should retire when the application closes.", Language.English);
    await service.DisposeAsync();
    await AssertCanceledAsync(closing);
    AssertNoCurrentRequest(service, "shutdown");
    Console.WriteLine("Native shutdown drained active speech.");
}
else Console.WriteLine("No speech was synthesized or played.");

static async Task AssertCanceledAsync(Task work)
{
    try { await work.WaitAsync(TimeSpan.FromSeconds(20)); }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Superseded speech did not terminate as cancellation.");
}

static object? CurrentRequest(WindowsPronunciationService service) =>
    typeof(WindowsPronunciationService).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(service);

static async Task AssertRejectedAsync(Func<Task> startRequest, string label)
{
    var rejected = false;
    try
    {
        await startRequest();
    }
    catch (InvalidOperationException) when (label != "pre-canceled input") { rejected = true; }
    catch (OperationCanceledException) when (label == "pre-canceled input") { rejected = true; }
    if (!rejected) throw new InvalidOperationException($"Expected {label} to be rejected.");
}

static void AssertNoCurrentRequest(WindowsPronunciationService service, string label)
{
    var current = typeof(WindowsPronunciationService).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic);
    if (current?.GetValue(service) is not null)
        throw new InvalidOperationException($"Rejected {label} changed the active request.");
}
