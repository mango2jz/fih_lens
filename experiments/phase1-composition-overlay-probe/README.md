# Native composition overlay feasibility candidate

Status: source/build READY for independent review, not launched. This is one bounded alternative to the failed WinUI alpha-backdrop experiment. It is not a T05 selection implementation or an acceptance pass.

Ownership: only experiments/phase1-composition-overlay-probe and docs/handoffs/phase1-figma-native-ui/composition-overlay-probe. No production classes, capture contracts, provider/OCR/clipboard/frame APIs, or runtime goal state are changed. Existing mandatory WinUI result Window/AppWindow stays unchanged. No new dependency; existing WindowsAppSDK 2.5.1.

## Mechanics and proof boundary

A WinUI fixture owns a WS_POPUP native HWND with WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW. No layered, transparent or no-activate styles and no GDI background brush. Uniform HTCLIENT means its visually clear hole receives input. One Windows.UI.Composition.Compositor targets that HWND through ICompositorDesktopInterop GUID 29E691FA-4567-4DCA-B319-D0F207EB6807, IUnknown slot 3. NativeObject.ThisPtr plus QueryInterface and CsWinRT FromAbi use explicit ABI reference release; no legacy Marshal.GetObjectForIUnknown RCW. The WinUI thread ensures its system dispatcher queue first and never shuts the shared queue down.

Four dim rectangles surround a genuinely unpainted hole; four opaque yellow rectangles mark the border. Composition dimensions/offsets are physical pixels relative to the frozen monitor origin once. A clear visual is not assumed to prove transparency: later installed samples must prove it.

The fixture has white/red/blue static markers and a green/magenta live patch. Explicit calibration reads six owned fixed coordinates only: three static, live, future stroke and surround. Overlay S explicitly starts at most 12 attempts on the existing one-second UI timer (sampling before the next live-color change). Both distinct calibrated live phases, all three unchanged static colors, exact stable yellow COLORREF 51455 replacing white, and a nonblack darker-than-white surround are required. GetDC(NULL)/GetPixel/ReleaseDC run on the UI thread with PMv2 and MM_TEXT checks, foreground, source/client geometry, monitor and scale validation before/after. No screenshots, image buffers, background points, captured text or source content persist.

A new overlay, run, error, or recording failure resets current proof. The first exception's bounded stage/type/HRESULT survives later failures. Metadata recording failure makes UI proof inconclusive. Passing pixels alone does not prove click ownership: fixturePresses must remain zero while holePresses and pointerMoves/pointerReleases establish real overlay input.

Native callbacks remain rooted by the app's owned NativeOverlay until actual WM_NCDESTROY. No managed exception crosses WndProc, no PostQuitMessage is sent. Close tries DestroyWindow first; failure retains the HWND for retry. After destruction acknowledgement it detaches the target, clears children, disposes visuals/brushes, root, target, compositor, then unregisters the class. Failed releases retain their references for retry; all releases are attempted. This is this experiment's explicit ownership order, not a claimed Windows guarantee of rendering/drain behavior. The shared queue is not owned. Owner close stops the timer and closes the owned overlay. Partial-init injection happens immediately after HWND creation, before compositor/target/activation, and must record actual destruction acknowledgement. It does not simulate proof of actual transparency.

## Build and prepare (no launch)

From repository root:

    dotnet restore experiments/phase1-composition-overlay-probe/Phase1CompositionOverlayProbe.csproj -p:Platform=x64 -p:NuGetAudit=false --source C:/Users/ndduy/.nuget/packages
    dotnet build experiments/phase1-composition-overlay-probe/Phase1CompositionOverlayProbe.csproj -p:Platform=x64 --no-restore
    dotnet run --project experiments/phase1-composition-overlay-probe/checks/Checks.csproj -p:NuGetAudit=false
    ./experiments/phase1-composition-overlay-probe/prepare-layout.ps1

The local package-source option avoids the unavailable NuGet DNS endpoint and uses already installed packages. prepare-layout copies every SDK recipe entry, validates destination containment, and hashes source, recipe and every copied layout file into the handoff ready-file-hashes.json. Rebuild/reprepare after source edits; hashes bind the reviewed DLL/layout to sources.

## Root-owned exact native proof sequence

1. Independently review source, policy checks and ready-file-hashes.json. Verify every hash and fresh absence of this exact probe process/package before registering; no display/security setting changes.
2. Root may ordinarily register the prepared AppX/AppxManifest.xml with Add-AppxPackage -Register. Identity is FihLens.Phase1CompositionOverlayProbe, app ID App; query the resulting package family instead of guessing it. Launch through its registered AUMID using actual Sky UI. The worker deliberately does neither registration nor launch.
3. Verify WinUI fixture live counter and startup state reads=0. Keep the fixture fully visible/unobscured on the existing monitor. Click Calibrate; wait for calibrated (both live colors).
4. Click Inject failure after native HWND creation. Read injectedCleanupAcknowledged=true and actual WM_NCDESTROY evidence, no native overlay remaining. The first injected exception remains recorded. A later ordinary run may succeed independently.
5. Click Show native composition overlay. Observe dimmed surroundings, live visible fixture hole and yellow border. Actual keyboard S starts bounded six-point sampling. Read both live-phase sample rows and current state verdict, not a screenshot or old PASS. Any error/recording failure is inconclusive.
6. Actual pointer click and drag inside the clear hole; verify holePresses>0, pointerMoves>0, pointerReleases>0 and fixturePresses=0 separately from pixels. Escape closes only overlay; verify destruction acknowledged, owner still live and timer still advancing. Repeat show/S/Escape to exercise fresh proof reset.
7. Close owner while an overlay is open; verify timerStopped, destruction acknowledgement, no process/HWND orphan. If DestroyWindow fails, retained ownership/retry is diagnostic evidence, not a successful cleanup.
8. Root records actual SDK/process/package identity, source/layout hashes, available-monitor results and failures in this handoff. Negative-origin/mixed-DPI and production native accessibility are still unproved. Do not claim T05, N2a or T06 completion from this fixed-hole probe.

Metadata lives only under this handoff/native-run when running from the prepared repo layout; fallback outside the repository is the app's LocalApplicationData/composition-overlay-probe. There is no automatic screen sampling on startup/show.

## Official basis

- https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/june/windows-with-c-high-performance-window-layering-using-the-windows-composition-engine — no-redirection composition window and uniform hit testing.
- https://learn.microsoft.com/en-us/windows/uwp/composition/using-the-visual-layer-with-win32 — compositor, DesktopWindowTarget and visual tree hosting.
- https://learn.microsoft.com/en-us/windows/apps/develop/dispatcherqueue — shared dispatcher queue lifetime.
- https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles
- https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-destroywindow
- https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-ncdestroy

No second backend, hooks, custom production controls, acquisition or provider integration was added.
