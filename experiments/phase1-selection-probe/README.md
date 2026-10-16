# Isolated transparent selection probe

This is a feasibility app, not the T05 production contract or implementation. It references only the already-used Windows App SDK 2.5.1. No full-image capture, OCR, provider, clipboard, hotkey, static desktop image, Acrylic or OS-setting action is implemented. RUNSELECTION4 explicitly reads five known owned-fixture screen pixels.

Build with `dotnet build experiments/phase1-selection-probe/Phase1SelectionProbe.csproj -p:Platform=x64`. Run `prepare-layout.ps1` only with no live probe process. Register the generated AppX/AppxManifest.xml as an ordinary development layout (`Add-AppxPackage -Register`); do not use `-DisableDevelopmentMode` for this unpacked layout.

**Do not launch before parent RUNSELECTION4.** Unique application ID: `FihLens.Phase1SelectionProbe_s0gm13pdwbspm!App`.

Parent Computer Use sequence:

1. Launch the unique app normally. Observe the harmless white fixture, sharp red/blue bottom blocks and changing counter. Keep the entire fixture on its current monitor.
2. Click **Show transparent probe**. An ordinary borderless OverlappedPresenter covers the chosen monitor. It uses `ResizeClient`, then measured `ClientToScreen` compensation; input is enabled only after exact physical client equality to the frozen monitor. FullScreenPresenter is not used.
3. Observe the underlying counter changing through the yellow-outlined clear hole. Four independent dark rectangles surround it. Confirm the actual desktop remains visible through the hole and dim surround, without blur or an opaque flat background. Observe the taskbar separately; matching taskbar rectangles alone do not prove visibility or z-order.
4. Click inside the clear hole. It must be consumed by the overlay, not reach the fixture. Press **Escape**. The owner must remain alive and the status must show one clear-hole press. Alternatively reopen and use **Cancel overlay (Escape)**. Each current attempt writes only safe metadata in `selection-probe/runtime-r3/result.json`.
5. Close the owner normally. Verify only this probe's windows retire, its timer stops, and backdrop connect/disconnect/disposal counters balance. Failure metadata contains only stage, exception type and HRESULT.

The transparent backdrop is a UI-thread-owned `Windows.UI.Composition.Compositor` and alpha-zero color brush assigned to the lifted target interface. It is cleared and disposed when disconnected. A transparent XAML hit-test surface covers the hole; the live underlying fixture distinguishes actual transparency from a copied image.

## RUNSELECTION2 diagnostic revision (not launched)

RUNSELECTION1 reached its real button action but failed with UnauthorizedAccessException 0x80070005 at the broad `show` stage. Its source, preparation binding and raw runtime bytes are preserved in `selection-probe/runselection1-failed/`. It proved neither display geometry nor transparency.

The current revision writes only to `selection-probe/runtime-r2/`. It labels Window construction/content, backdrop construction/assignment, system compositor construction, brush creation, target assignment, presenter setup, ResizeClient, initial Move and Activate separately. `failure.json` retains the initiating stage/type/HRESULT and system-dispatcher-queue presence; later cleanup failures append to `additional-failures.jsonl`. No exception messages, screenshots, private text or permission changes are recorded.

Closed is subscribed immediately after Window creation, before potentially failing backdrop setup. Cleanup detaches handlers and clears the backdrop, resets a successfully retired overlay reference, and leaves a failed Close owned for the owner-close retry rather than orphaning it. A synchronous Loaded failure no longer disables the restored Show button after Activate returns. Disposal failures cannot report `ResourcesDisposed=true`.

No queue initializer or alternate compositor has been added. If the next real diagnostic proves the system compositor constructor is the failing boundary, Microsoft's documented [EnsureSystemDispatcherQueue](https://learn.microsoft.com/en-us/windows/apps/develop/dispatcherqueue#system-dispatcher-management) is the bounded ordinary-lifecycle candidate; the current failure alone does not establish that cause. Computer Use remains stopped; parent alone may authorize and conduct a later native run.

The focused review repair makes metadata capture/writes best effort: the initiating safe failure remains in memory even when a getter or filesystem operation fails, and a recording-failure flag is retained. Failure retirement and owner timer shutdown run in finally. Backdrop cleanup attempts every release, records at most eight safe cleanup failures and rethrows the first exception rather than replacing it with a later disposal error. Run `dotnet run --project experiments/phase1-selection-probe/checks/Checks.csproj` for plain .NET assertions covering these logging/cleanup rules. These use throwing delegates, not Windows UI or simulated native success.

Self-assertions cover negative-origin containment and physical scaling, followed by actual runtime monitor/client/taskbar geometry checks. These do not substitute for negative-origin hardware, production overlay state flow, actual capture exclusion, accessibility or multi-display/DPI proof. `actualDesktopTransparencyVerified` remains false in app-authored results until an external observer provides separate attributed evidence; the app cannot prove its own composited desktop alpha.

## RUNSELECTION3 system dispatcher candidate (not launched)

The parent observed RUNSELECTION2 fail specifically at `backdrop compositor constructor`, with `systemDispatcherQueuePresent=false`. Its eleven source files, binding and raw runtime output are frozen unchanged under `selection-probe/runselection2-failed/`. Cleanup reported one overlay close and disposed backdrop resources; this was not transparency proof.

Before constructing the system compositor, the current candidate calls the installed App SDK UI dispatcher's documented `EnsureSystemDispatcherQueue()`. Safe metadata records system queue presence before and after this call and identifies this setup stage separately. The App SDK dispatcher manages the associated system queue lifetime; this backdrop does not manually shut down a shared queue. Existing brush/compositor disposal, window ownership and first-failure handling are preserved. Only `runtime-r3/` receives new runtime output. No successful native result is claimed before the parent's controlled run.

Primary API/lifecycle references: [system dispatcher management](https://learn.microsoft.com/en-us/windows/apps/develop/dispatcherqueue#system-dispatcher-management), [EnsureSystemDispatcherQueue](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.dispatching.dispatcherqueue.ensuresystemdispatcherqueue?view=windows-app-sdk-2.0), [visual layer compositor dispatcher prerequisite](https://learn.microsoft.com/en-us/windows/uwp/composition/using-the-visual-layer-with-win32).

## RUNSELECTION4 explicit host-specific pixel diagnostic (not launched)

RUNSELECTION3 proved queue setup, exact client geometry, clear-hole input and lifecycle cleanup on this host. Computer Use window capture displayed black and did not prove desktop alpha. Its source, binding, raw runtime and attributed parent observations are frozen in `runselection3-lifecycle/`.

Parent Sky sequence:

1. Launch the unique fixture normally. Keep it fully visible on the same monitor; observe its green/magenta square alternating with the existing timer. No screen read occurs at startup.
2. Click **Calibrate known fixture pixels only**. The existing timer samples only three static red/blue/white centers, one live-square center and one future border position, with the fixture foreground. Wait for **Calibration ready**. This uses measured device COLORREF values for both live phases, not authored RGB assumptions. An unstable/unsupported/occluded calibration is inconclusive; explicitly retry.
3. Click **Show transparent probe**. On the overlay click **Sample known fixture pixels only**. The same physical points are sampled only while the overlay is foreground and frozen owner/client/hole/display/scale checks pass before and after reading. Wait for PASS or Inconclusive; there are at most twelve sample attempts. Existing timer events drive attempts; no fixed sleep or first-frame assumption is introduced. Overlay input still consumes clear-hole clicks.
4. A host-specific pass requires all three calibrated static values, both distinct calibrated live values, and a stable distinct nonblack opaque-border positive control. Only numeric coordinates/COLORREF, safe phase/tick/count and flags are written to `runtime-r4/screen-pixel-samples.jsonl`; no bitmap or text is read or stored. Escape closes only the overlay; close the owner normally afterward and inspect cleanup metadata.

`GetDC(NULL)`/`GetPixel` reads are explicit native capture observations counted as `screenDcReadCalls`; `wgcFullImageCaptureCalls=0` refers only to full-image capture. The DC is acquired/read/released on one UI thread without await, requires MM_TEXT and the existing PerMonitorV2 awareness, and checks null/CLR_INVALID. The app does not change DPI awareness or OS settings. `empiricalScreenSampleProofPassed` covers only these known fixture points on this host. `actualDesktopTransparencyVerified` remains externally unproven; display DC sampling is not a universal final-scanout/HDR/MPO guarantee.

Primary references: [DWM display-DC considerations](https://learn.microsoft.com/en-us/windows/win32/dwm/bestpractices-ovw), [GetDC](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdc), [GetPixel](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getpixel), [ReleaseDC](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-releasedc), [default transformations/MM_TEXT](https://learn.microsoft.com/en-us/windows/win32/gdi/default-transformations), [high-DPI desktop development](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows).

R4 review repair: current proof resets for every new overlay and explicit sample run, plus failed/inconclusive sampling or metadata recording. Historical JSONL is retained; it cannot make a subsequent instance report success.
