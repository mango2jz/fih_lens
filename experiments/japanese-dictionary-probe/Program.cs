using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FihLens.Application.Dictionary;
using FihLens.Core.Enums;
using FihLens.Infrastructure.Dictionary;

if (args.Length == 3 && args[0] == "--installed")
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    using var manager = new DictionaryPackManager(args[1]);
    while (!File.Exists(args[2])) await Task.Delay(20, deadline.Token);
    var timer = Stopwatch.StartNew();
    await using var installed = await manager.OpenInstalledAsync(deadline.Token);
    timer.Stop();
    if (installed is null || !(await installed.LookupAsync("食べる", deadline.Token)).IsMatch)
        throw new InvalidDataException("installed candidate was not available after concurrent startup");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        probe = "concurrent-installed-startup", processId = Environment.ProcessId,
        machine = Environment.MachineName, loadMs = timer.Elapsed.TotalMilliseconds,
        sourceRevision = installed.Source.Revision, exactLookupPassed = true
    }));
    return 0;
}

if (args.Length != 4 && !(args.Length == 6 && args[4] == "--review"))
{
    Console.Error.WriteLine("Usage: dotnet run -- <pack-directory> <trusted-manifest-sha256> <trusted-archive-sha256> <archive-bytes>");
    Console.Error.WriteLine("   or: dotnet run -- --installed <local-asset-root> <start-barrier-file>");
    Console.Error.WriteLine("Append --review <30-query-json> to export the actual reader results for semantic review.");
    return 2;
}

string root = Path.GetFullPath(args[0]);
string trustedManifestHash = args[1];
if (!long.TryParse(args[3], out long downloadBytes) || downloadBytes < 0)
    throw new ArgumentException("archive-bytes must be a nonnegative integer");
string trustedArchiveHash = args[2];

byte[] manifestBytes = await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json"));
string manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
if (!StringComparer.OrdinalIgnoreCase.Equals(manifestHash, trustedManifestHash))
    throw new InvalidDataException("manifest does not match the supplied trusted digest");
using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(manifestBytes));
var manifest = document.RootElement;
string text(string name) => manifest.GetProperty(name).GetString()!;
long number(string name) => manifest.GetProperty(name).GetInt64();
long installedBytes = manifest.GetProperty("files").EnumerateArray().Sum(file => file.GetProperty("bytes").GetInt64()) + manifestBytes.LongLength;
var definition = new DictionaryPackDefinition(text("packId"), text("version"), text("name"),
    Language.Japanese, Language.English, text("license"), text("sourceUrl"), text("sourceRevision"),
    downloadBytes, installedBytes, trustedArchiveHash, trustedManifestHash);

if (args.Length == 6)
{
    var reviewQueries = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(args[5]))!;
    if (reviewQueries.Length != 30 || reviewQueries.Distinct(StringComparer.Ordinal).Count() != 30)
        throw new InvalidDataException("review requires exactly 30 distinct frozen queries");
    await using var lookup = await IndexedJapaneseDictionaryLookup.LoadAsync(root, definition);
    var results = new List<FihLens.Core.Models.DictionaryLookupResult>();
    foreach (var query in reviewQueries) results.Add(await lookup.LookupAsync(query));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        probe = "semantic-reader-results", version = definition.Version,
        manifestSha256 = trustedManifestHash, sourceArchiveSha256 = text("sourceArchiveSha256"), results
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

// The first 100 distinct keys are a repeatable corpus-bound query set; each is also a successful hit.
var queries = new List<string>(100);
using (var index = new FileStream(Path.Combine(root, "lookup.idx"), FileMode.Open, FileAccess.Read, FileShare.Read))
using (var keys = new FileStream(Path.Combine(root, "keys.utf8"), FileMode.Open, FileAccess.Read, FileShare.Read))
{
    byte[] header = new byte[32];
    index.ReadExactly(header);
    ulong rowCount = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(16, 8));
    string? last = null;
    byte[] row = new byte[32];
    for (ulong i = 0; i < rowCount && queries.Count < 100; i++)
    {
        index.ReadExactly(row);
        ulong keyOffset = BinaryPrimitives.ReadUInt64LittleEndian(row.AsSpan(0, 8));
        uint keyLength = BinaryPrimitives.ReadUInt32LittleEndian(row.AsSpan(8, 4));
        byte[] keyBytes = new byte[keyLength];
        keys.Position = checked((long)keyOffset);
        keys.ReadExactly(keyBytes);
        string key = new UTF8Encoding(false, true).GetString(keyBytes);
        if (key != last) queries.Add(key);
        last = key;
    }
}
if (queries.Count < 100) throw new InvalidDataException("pack has fewer than 100 unique query keys");

var coldRuns = new List<object>(5);
var timings = new List<double>(100);
long baselineWorkingSet = Process.GetCurrentProcess().WorkingSet64;
long baselineManaged = GC.GetTotalMemory(forceFullCollection: true);
long peakWorkingSet = baselineWorkingSet;
long peakManaged = baselineManaged;
var idleRuns = new List<object>();
// Scope disposal before the final retained-memory measurement.
{
    var loadTimer = Stopwatch.StartNew();
    await using var lookup = await IndexedJapaneseDictionaryLookup.LoadAsync(root, definition);
    loadTimer.Stop();
    var firstTimer = Stopwatch.StartNew();
    var first = await lookup.LookupAsync(queries[0]);
    firstTimer.Stop();
    if (!first.IsMatch) throw new InvalidDataException("fixed query unexpectedly missed");
    coldRuns.Add(new { loadMs = loadTimer.Elapsed.TotalMilliseconds, firstLookupMs = firstTimer.Elapsed.TotalMilliseconds });
    for (int i = 0; i < queries.Count; i++)
    {
        var timer = Stopwatch.StartNew();
        _ = await lookup.LookupAsync(queries[i]);
        timer.Stop();
        timings.Add(timer.Elapsed.TotalMilliseconds);
    }
    peakWorkingSet = Math.Max(peakWorkingSet, Process.GetCurrentProcess().PeakWorkingSet64);
    peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(forceFullCollection: false));
    long idleManaged = GC.GetTotalMemory(forceFullCollection: true);
    idleRuns.Add(new { processWorkingSetDeltaBytes = Process.GetCurrentProcess().WorkingSet64 - baselineWorkingSet,
        managedHeapDeltaBytes = idleManaged - baselineManaged });
}
timings.Sort();
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
long finalWorkingSet = Process.GetCurrentProcess().WorkingSet64;
long finalManaged = GC.GetTotalMemory(forceFullCollection: true);
Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "D01-indexed-reader-v1", packId = definition.PackId, version = definition.Version,
    machine = Environment.MachineName, processorCount = Environment.ProcessorCount,
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, is64BitProcess = Environment.Is64BitProcess,
    manifestSha256 = trustedManifestHash, archiveSha256 = definition.ArchiveSha256,
    sourceArchiveSha256 = text("sourceArchiveSha256"),
    installedBytes, downloadBytes, entryCount = number("entryCount"), indexRows = number("indexRows"),
    queryCount = timings.Count, coldRuns, lookupMs = new { p50 = Percentile(timings, .50), p95 = Percentile(timings, .95), min = timings[0], max = timings[^1] },
    processWorkingSetDeltaBytes = peakWorkingSet - baselineWorkingSet,
    managedHeapDeltaBytes = peakManaged - baselineManaged,
    idleRuns,
    afterDispose = new { processWorkingSetDeltaBytes = finalWorkingSet - baselineWorkingSet, managedHeapDeltaBytes = finalManaged - baselineManaged },
    notes = "One process-cold load and 100 fixed exact-hit queries. OS file cache is not flushed. Peak process working set uses the OS high-water counter across validation and queries; managed heap is sampled after queries. Idle managed memory is measured after a diagnostic full GC; process working set includes allocator high-water effects. Launch five fresh processes for startup evidence."
}, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static double Percentile(IReadOnlyList<double> values, double percentile) =>
    values[Math.Clamp((int)Math.Ceiling(percentile * values.Count) - 1, 0, values.Count - 1)];
