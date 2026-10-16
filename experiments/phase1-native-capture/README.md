# Native capture feasibility probe

Preparation milestone only: exact WindowsAppSDK 2.5.1 / .NET 10 compilation. This experiment is outside the production solution and does not define the T05 application contract.

Build from the repository root:

```powershell
dotnet build experiments/phase1-native-capture/Phase1NativeCapture.csproj -p:Platform=x64 --no-restore
& ./experiments/phase1-native-capture/prepare-layout.ps1
```

The layout script copies the build recipe's declared files into this experiment's `bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/AppX`, with destination containment checks. It does not register, launch or capture.

After the parent explicitly signals **RUNE2**, register that layout's `AppxManifest.xml` using the ordinary development-package path. Identity is uniquely `FihLens.Phase1NativeCaptureProbe`, publisher `CN=ndduy`, version `1.0.0.0`, x64; it cannot replace the production FIH Lens identity. Generated entrypoint is `Windows.FullTrustApplication`, only declared capability is `runFullTrust`, Windows minimum 19041, runtime dependency `Microsoft.WindowsAppRuntime.2 >= 2.5.1.0`. No certificate trust or security/permission setting changes are part of this experiment. A registration or consent failure is an honest blocker.

Registration/launch commands for the parent-authorized runtime stage (not executed during preparation):

```powershell
Add-AppxPackage -Register ./experiments/phase1-native-capture/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/AppX/AppxManifest.xml
$capturePackage = Get-AppxPackage -Name FihLens.Phase1NativeCaptureProbe
Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$($capturePackage.PackageFamilyName)!App"
```

Keep the harmless white fixture unobscured. Only its explicit **Capture fixture** button starts acquisition. It freezes the native monitor/topology/physical rectangle, captures one frame with the normal system capture border, and rectangle-copies the fixture into a crop-sized BGRA SoftwareBitmap. Assertions check exact size and red/blue/white marker pixels. No bitmap or arbitrary text is saved. Safe result metadata goes to `docs/handoffs/phase1-figma-native-ui/capture-probe/runtime/`; exception evidence contains only type/HRESULT/time. Cancel and close invalidate publication and retain copy resources until the awaited native copy drains. The eight-second acquisition/copy token timeout is not proof that every driver copy completes in that time.

Bounds self-checks run at app launch, before capture: negative-origin valid region, cross-monitor rejection, zero-width rejection and outside-monitor rejection. Their presence/compilation is not an executed test claim before launch.

Still required: real package registration/startup; permissions; GPU-to-CPU crop fidelity/alpha; overlay-marker exclusion; edge captures; source-change/item-close races; cancellation/timeout fault injection and 20-cycle resource observations; actual mixed-DPI/negative-origin hardware; packaged English OCR/language/size/empty-image evidence; explicit clipboard success/failure/external-editor persistence. No E2/E3 gate is closed by compilation.

Official API references are recorded in the adjacent [capture feasibility research](../../docs/handoffs/phase1-figma-native-ui/capture-feasibility-research.md): CreateForMonitor ABI, D3D11 bridge, free-threaded framepool, rectangle VideoFrame copy and current C#/WinRT projection requirements.

## Explicit E3 extension

The first successful capture's exact source, DLL and safe result are frozen in `docs/handoffs/phase1-figma-native-ui/capture-probe/first-capture-frozen/`; do not replace those files. It passed packaged capture at actual125%, crop600×250, BGRA/premultiplied, three red/blue/white assertions, no provider/image persistence. It did not prove OCR or clipboard.

The new revision writes only safe metadata to `capture-probe/runtime-e3/`. Under parent **RUNE3** authorization, launch normally, keep the harmless fixture unobscured and click **Capture fixture**. After its marker checks pass, **OCR fixture** becomes available. This explicit action uses an independent in-memory copy of that actual crop, enumerates installed recognizers, selects English, validates the actual maximum and byte bounds, awaits native OCR, checks four known English text fragments including42/books, and verifies an explicitly white blank64×64 image yields empty OCR. Missing English/null engine is unavailable; no language pack is installed. Oversize/missing-language/null-engine/pre-canceled guards are small deterministic self-checks, not proof of native installation failures or late cancellation races. Native awaits drain before bitmap release; cancel/close suppress output publication. The fixture requests Light locally for readability, without changing Windows theme.

**Copy fixture result** enables only after those OCR checks pass. The user click writes the current known-fixture OCR snapshot on the Window's UI thread with `DataPackage.SetText`, `Clipboard.SetContent`, then `Flush`. No clipboard read/restoration occurs. Native write/flush failures show failure rather than success; recognized text remains only in memory/UI/explicit clipboard output. The parent must inspect the known result before clicking Copy, then verify paste in an external editor while open and after normal fixture closure. External-editor persistence and native copy-failure behavior remain unproved until observed. No OCR, copy or launch occurs automatically on startup or build.

## RUNE4 staged cancellation/cycle checks

E3's successful source/DLL/safe metadata are now frozen in `capture-probe/e3-success-frozen/`; the attributed external-editor persistence proof remains in `RUNTIME-E3-REPORT.md`. The current revision uses separate `runtime-r4/` output and does not change earlier results/bindings.

**Do not launch before parent RUNE4.** With no live capture process, build and prepare the same unique AppX layout. Once authorized, launch normally, keep the known fixture unobscured, explicitly click **Capture fixture**, then **Run bounded checks (20 cycles)**. Copy is unnecessary and remains explicit. Startup never runs these checks.

That button runs exactly20 sequential native resource cycles:10 create the real device/pool/session/event registration and cancel before StartCapture (zero frame publication);10 start real capture, copy the fixed owned fixture and repeat its three marker assertions. Each call must exit its owner scopes and balance retained-frame disposal, subscription removal and awaited copy work. Per-call callback entry/exit counts plus process handles/private-memory are sampled; the first4 are marked warmup. These are bounded observations, not proof of every OS/GPU allocation or a universal leak threshold. No synthetic bitmap closes a capture cycle.

Next, two actual native RecognizeAsync dispatches use an independent copy of the retained real fixture crop. One cancels its token immediately after dispatch; one activates/closes a separate harmless owned child Window whose Closed event cancels that OCR token. Both must drain the native await and suppress output. The native AsyncStatus at cancellation is recorded: an already-completed operation does not count as in-flight cancellation. A callback failure also drains native OCR before releasing its bitmap. The child is closed deterministically while the launcher remains alive. This does not prove main-app shutdown or real GraphicsCaptureItem.Closed behavior.

Only safe counters/size/metadata are written to `runtime-r4/bounded-checks-result.json`; failure writes type/HRESULT only. Cancel/close rejects success publication. Real frame-timeout/device loss/item closure, source/topology change, production overlay-marker exclusion, edge and mixed/negative-origin hardware checks, native clipboard failure and broader lifetime proof remain separate gates. No injected source mismatch is included in this revision.

## RUNE5 activation-failure repair

RUNE4 passed its normal fixture checks and both OCR actions were actually Started at cancellation; its exact source/DLL/results are preserved in `capture-probe/r4-success-frozen/`. Review found child creation/Closed subscription/Activate happened before cleanup protection. RUNE5 moves all setup inside the protected try, uses a named Closed handler, and retires/detaches it before the linked cancellation source is disposed. Cleanup still requests actual child closure; a close/detach exception propagates as failure rather than false success. A retired queued callback cannot cancel a disposed source.

**No launch until parent RUNE5.** Same unique package and button sequence: **Capture fixture → verify PASS → Run bounded checks (20 cycles)**. The sequence now includes one injected activation failure immediately after real child creation/subscription and before Activate. That branch must close the unactivated real child, observe Closed, detach the handler and dispatch no OCR/output. This is controlled failure injection at the activation call boundary, not an observed OS activation error. Normal cancel/child-close checks are rerun with the repaired ownership. Safe current outputs exclusively use `runtime-r5/`; prior raw results/frozen binaries remain unchanged. Copy is unnecessary for this stage. All remaining E2/E3 limits still apply.

## RUNE6 staged owned-overlay retirement proof

The passed RUNE5 repair/cycle revision is frozen in `capture-probe/r5-success-frozen/`. **Do not launch before parent RUNE6.** Same registered unique app; explicit sequence **Capture fixture → verify PASS → Check overlay retirement**. No OCR, Copy or 20-cycle action is needed. Keep the harmless owned fixture unobscured; the check briefly activates a small magenta owned overlay over its red marker, then closes only that overlay. Current outputs exclusively use `runtime-r6/`.

Each phase allows at most eight real one-shot capture attempts under one retained operation/token and a30-second cancellation budget. The first phase qualifies only pixels with magenta at the expected red-marker position plus unchanged blue/white fixture markers. After actual overlay Closed, the original acquisition owner and token must remain active. The second phase qualifies only zero magenta pixels across the crop plus all three exact original fixture markers. A rejected capture is disposed before retry. There is no fixed sleep or assumption that the first frame is current. Frozen source/topology checks and owned frame/subscription/copy balance run on every attempt. The native copy must drain after cancellation; the budget is not an enforceable driver-copy deadline.

Safe result `runtime-r6/overlay-retirement-result.json` records qualifying/rejected attempt counts, magenta pixel counts, ownership and cleanup counters only. No bitmap/text is persisted, no provider/clipboard action occurs. Setup/cleanup owns the overlay in protected try/finally; success is recorded only after its handler is detached. Close/detach failures report truthful cleanup failure. The retained operation uses a fresh native session per attempt; this is not one continuously retained framepool/session and is not production selection-overlay integration. Monitor edges, actual source/topology changes, itemClosed/device-loss/timeout/post-start-cancel/native clipboard failures and mixed/negative-origin hardware remain separate proof gaps.

## RUNE7 preparation: render-time admission after overlay Closed

Use **R7: Check timestamp-gated retirement** directly, with no prior Capture fixture action, only in an authorized native run. Clicking Capture fixture would overwrite prior R6 capture metadata. R6's original button/metadata remain unchanged. R7 first qualifies actual magenta overlay pixels, closes that owned window and observes its Closed event, then takes `Stopwatch.GetTimestamp()`. The retired phase rejects and disposes frames whose `SystemRelativeTime` is missing/invalid or not strictly beyond the marker with one 100 ns quantum plus one QPC tick uncertainty margin. Each attempt admits at most eight rejected timestamps and keeps the existing eight-second capture/30-second operation cancellation budgets; native copies still drain, so these are not hard copy deadlines.

Every accepted retired crop must independently have zero magenta pixels and all three restored fixture markers. A timestamp alone cannot prove overlay absence. The separate `runtime-r7` directory preserves prior R6 raw evidence. `timestamp-retirement-result.json` records ordinary pixel/resource observations; `timestamp-admission-result.json` records timing inputs, rejected counts and pixel results. No image persistence, OCR, provider or clipboard operation occurs in this route. Existing source/topology validation and native lifetime ownership remain in force. The optional `WDA_EXCLUDEFROMCAPTURE` arm is deferred; this preparation proves neither exclusion nor runtime freshness.

Dependency-free arithmetic self-check:
`dotnet run --project docs/handoffs/phase1-figma-native-ui/capture-probe/r7-preparation/FreshnessSelfCheck.csproj`
