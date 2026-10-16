using Phase1CompositionOverlayProbe;
using System.Text.Json;
var monitor = new Bounds(-1920, -1080, 0, 0);
Require(monitor.Contains(new(-1900, -1000, -1200, -400)), "Negative desktop origin.");
Require(!monitor.Contains(new(-1921, -1000, -1200, -400)), "Reject cross-boundary.");
Require(!monitor.Contains(new(-1900, -1000, -1900, -400)), "Reject zero.");
var policy = new ProbePolicy();
policy.Calibrate([255, 16711680, 16777215, 65280, 16777215, 16777215], 0);
policy.Calibrate([255, 16711680, 16777215, 16711935, 16777215, 16777215], 1);
Require(policy.Calibrated, "Both distinct live phases calibrated.");
Require(!policy.Accept([0, 0, 0, 0, 51455, 7566195], 0), "Opaque black hole must fail.");
Require(!policy.Accept([255, 16711680, 16777215, 65280, 51455, 0], 0), "Black surround is not blend proof.");
Require(!policy.Accept([255, 16711680, 16777215, 65280, 51455, 16777215], 0), "Unchanged surround must fail.");
Require(policy.Accept([255, 16711680, 16777215, 65280, 51455, 7566195], 0) && !policy.Passed, "One live phase insufficient.");
Require(policy.Accept([255, 16711680, 16777215, 16711935, 51455, 7566195], 1) && policy.Passed, "Two phases pass.");
var statePath = Path.Combine(Path.GetTempPath(), $"composition-probe-check-{Guid.NewGuid():N}.json");
try
{
    File.WriteAllText(statePath, JsonSerializer.Serialize(new { policy.Passed, policy.Calibrated, verdict = "PASS owned fixture pixels only" }));
    policy.ResetCalibration();
    var recalibrationLog = new ProbeFailureLog();
    recalibrationLog.Record("calibration geometry", new InvalidOperationException(), (_, _) => {},
        () => File.WriteAllText(statePath, JsonSerializer.Serialize(new { policy.Passed, policy.Calibrated, verdict = "inconclusive" })));
    using var persisted = JsonDocument.Parse(File.ReadAllText(statePath));
    Require(!persisted.RootElement.GetProperty("Passed").GetBoolean()
        && !persisted.RootElement.GetProperty("Calibrated").GetBoolean()
        && persisted.RootElement.GetProperty("verdict").GetString() == "inconclusive", "Failed recalibration replaces persisted PASS with invalidated proof.");
    Require(recalibrationLog.First?.Stage == "calibration geometry" && !recalibrationLog.RecordingFailed, "Recalibration failure remains first after successful invalidation persistence.");
}
finally { File.Delete(statePath); }
policy.Calibrate([255, 16711680, 16777215, 65280, 16777215, 16777215], 0);
policy.Calibrate([255, 16711680, 16777215, 16711935, 16777215, 16777215], 1);
policy.Accept([255, 16711680, 16777215, 65280, 51455, 7566195], 0);
policy.Accept([255, 16711680, 16777215, 16711935, 51455, 7566195], 1);
policy.ResetRun();
Require(!policy.Passed && policy.Stroke is null && policy.Phases.All(v => !v) && policy.Calibrated, "New overlay/run clears proof.");
policy.ResetCalibration(); Require(!policy.Calibrated, "Calibration resets.");
var log = new ProbeFailureLog();
bool invalidationPersisted = false;
log.Record("initialization", new InvalidOperationException(), (_, _) => throw new IOException(), () => invalidationPersisted = log.RecordingFailed);
log.Record("cleanup", new Exception(), (_, _) => {});
Require(log.First?.Stage == "initialization" && log.RecordingFailed && invalidationPersisted, "First failure survives writer and later failures; invalidation persists with sticky recording failure.");
var persistenceLog = new ProbeFailureLog();
int persistenceAttempts = 0, recordingFailures = 0;
persistenceLog.Record("calibration geometry", new InvalidOperationException(), (failure, _) =>
{
    if (failure.Stage == "state recording") recordingFailures++;
}, () => { persistenceAttempts++; throw new IOException(); });
Require(persistenceAttempts == 1 && recordingFailures == 1 && persistenceLog.RecordingFailed
    && persistenceLog.First?.Stage == "calibration geometry", "State write failure is recorded once without recursion or loss of first failure.");
Console.WriteLine("PASS: geometry, calibration, black-hole rejection, dimming/control, two live phases, reset, failed-recalibration persistence, nonrecursive state-write failure, first-failure recording policy. No native reads.");
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
