using Phase1SelectionProbe;

var log = new ProbeFailureLog();
var original = new UnauthorizedAccessException();
log.Record("compositor constructor", original, (_, _) => throw new IOException());
Require(log.RecordingFailed && log.First?.Stage == "compositor constructor", "Failed writer must retain first failure without throwing.");
var laterWasFirst = true;
log.Record("cleanup", new InvalidOperationException(), (_, first) => laterWasFirst = first);
Require(!laterWasFirst && log.First?.HResult == original.HResult, "Later failure must not replace initiating failure.");
log.Record("getter", new Exception(), (_, _) => throw new UnauthorizedAccessException());
Require(log.First?.Stage == "compositor constructor", "Metadata getter failure must not escape or replace first failure.");

var releases = new List<string>();
var failures = new List<ProbeFailure>();
var brushError = new IOException();
Exception? firstError = original;
firstError = ProbeFailureLog.Release("brush dispose", () => { releases.Add("brush"); throw brushError; }, firstError, failures);
firstError = ProbeFailureLog.Release("compositor dispose", () => { releases.Add("compositor"); throw new InvalidOperationException(); }, firstError, failures);
Require(ReferenceEquals(firstError, original) && releases.SequenceEqual(new[] { "brush", "compositor" }) && failures.Count == 2,
    "All releases must run and preserve a prior target-clear error.");
firstError = ProbeFailureLog.Release("brush dispose", () => throw brushError, null, failures);
firstError = ProbeFailureLog.Release("compositor dispose", () => throw new InvalidOperationException(), firstError, failures);
Require(ReferenceEquals(firstError, brushError), "Later disposal must not replace first disposal error.");
for (var i = 0; i < 20; i++) ProbeFailureLog.Release("repeat", () => throw new Exception(), null, failures);
Require(failures.Count == 8, "Cleanup diagnostics must be bounded.");
Console.WriteLine("PASS: nonthrowing diagnostic writes/getters; retained initiating failure; all releases attempted; first cleanup exception preserved; bounded metadata.");
Require(ScreenPixelProbe.MarkersAndControlMatch([1, 2, 3, 4, 6], [1, 2, 3], 3, [4, 5], null), "Matching calibrated markers need a distinct positive control.");
Require(!ScreenPixelProbe.MarkersAndControlMatch([1, 2, 3, 4, 0], [1, 2, 3], 3, [4, 5], null), "Black is not positive control proof.");
Require(!ScreenPixelProbe.MarkersAndControlMatch([1, 2, 3, 4, 3], [1, 2, 3], 3, [4, 5], null), "Unchanged background must not prove an overlay.");
Require(!ScreenPixelProbe.MarkersAndControlMatch([1, 2, 9, 4, 6], [1, 2, 3], 3, [4, 5], null), "All calibrated markers must match.");
Require(!ScreenPixelProbe.MarkersAndControlMatch([1, 2, 3, 4, 7], [1, 2, 3], 3, [4, 5], 6), "Positive control must remain stable across live phases.");
Console.WriteLine("PASS: calibrated marker and stable distinct positive-control policy; no native reads in checks.");
var livePhases = new[] { true, true }; uint? stroke = 6; var proofPassed = true;
ScreenPixelProbe.ResetProof(livePhases, ref stroke, ref proofPassed);
Require(!proofPassed && stroke is null && livePhases.All(v => !v), "A new overlay/run must retire prior successful proof.");
livePhases[0] = livePhases[1] = true; stroke = 6; proofPassed = true;
try { throw new IOException("Simulated metadata append failure."); }
catch { ScreenPixelProbe.ResetProof(livePhases, ref stroke, ref proofPassed); }
Require(!proofPassed && stroke is null && livePhases.All(v => !v), "Failed result recording must not leave successful current proof.");
Console.WriteLine("PASS: previous success resets for new instance/run and failed result recording.");

static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
