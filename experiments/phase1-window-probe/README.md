# E1 native desktop window feasibility probe

Isolated native WinUI experiment; no production host, capture, OCR, clipboard or provider calls. Uses the repository's exact WindowsAppSDK 2.5.1, .NET 10/x64 and linked T01 resources. It creates only its own launcher, second session and two sequential transient windows, then closes every owned window in `finally`.

Run from the repository root:

```powershell
dotnet restore experiments/phase1-window-probe/Phase1WindowProbe.csproj -p:Platform=x64 --ignore-failed-sources -p:NuGetAudit=false
& ./experiments/phase1-window-probe/check-runner-cleanup.ps1
& ./experiments/phase1-window-probe/run.ps1
```

The offline restore switch permits existing cache packages; it does not change versions. NuGet vulnerability retrieval is skipped for this experiment because the endpoint is unreachable; this is not vulnerability audit evidence. The runner builds without a second restore and records compiler output. Its timeout/failure cleanup kills only the `Process` instance it started, restores the previous output environment variable and working directory, and is tested against real harmless child processes.

Assertions establish the XAML root's association with `Window.AppWindow`, 400-DIP client width and 325/569-DIP height after physical-pixel rounding, taskbar-aware placement, retained T01 style rendering, close/reopen identity and surviving launcher/second-session lifetime. `DisplayArea.WorkArea` is display-relative; `OuterBounds` supplies its screen-space origin exactly once. `ResizeClient` avoids guessing nonclient metrics. `OverlappedPresenter.SetBorderAndTitleBar(false,false)` gives the experiment a borderless native result host without creating a picture-in-picture presenter.

PNG files are **own-root RenderTargetBitmap renders**, not desktop screenshots. They cannot prove z-order, external-app activation, native shadows/borders or external focus restoration. No input automation or arbitrary desktop capture occurs. Delay calls only allow native layout to settle; measurements and assertions determine success.

Full E1/N1/N1b stays open: real external-app activation and focus restoration, negative-origin and unequal-DPI physical monitors, live DPI changes, 100/150/200% modes, small/portrait work area, accessibility and visual native chrome still require their specified evidence. A single current-scale run is not the T02 gate.

Primary references used before implementation:

- [Manage app windows](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows) — associated AppWindow and OverlappedPresenter.
- [AppWindow.ResizeClient](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.resizeclient?view=windows-app-sdk-2.0) — client sizing.
- [DisplayArea.WorkArea](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.displayarea.workarea?view=windows-app-sdk-2.0) and [OuterBounds](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.displayarea.outerbounds?view=windows-app-sdk-2.0) — coordinate spaces.

Those documentation versions are references; exact 2.5.1 compilation and recorded runtime are the feasibility proof.

## Mixed-display extension

After the parent prepares the physical display configuration and foreground reader fixture, run:

```powershell
& ./experiments/phase1-window-probe/run.ps1 -Mixed -EvidenceName mixed-positive-100-200
```

Use a new simple evidence name each time. Existing run evidence is refused rather than overwritten. Original primary100% evidence remains unchanged; its original source hashes describe the original program revision.

The extension enumerates real DisplayAreas by indexed `Count/[i]` (WinRT LINQ enumeration was unsupported in the other probe). It moves the same Window/XamlRoot across each actual display and back, measures native scale, sizes compact/expanded content, clamps requested client size using actual nonclient overhead and work area, and verifies final native display identity and bounds. `displays.json` preserves raw API rectangles before interpretation.

Secondary `WorkArea` may already contain desktop X/Y in the actual installed SDK despite the documentation's relative wording. The extension compares raw and origin-added candidates against actual display bounds; it rejects ambiguous/noncontained candidates and records the selected interpretation. It does not silently apply a double display-origin offset. This is experiment evidence for correcting the production contract, not a speculative compatibility layer.

Minimal UI-owned User32 interop reads only foreground handle/bounds and own native window bounds. It never reads window text or sends input. The parent identifies the known external reader through the normal computer-use surface. Native `Window.Activate` results and overlap are recorded honestly. After every owned window closes, exactly one `SetForegroundWindow` restoration attempt is made; refusal is recorded, never bypassed with input/thread tricks.

- [GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)
- [GetWindowRect](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect) — includes possible invisible borders and DPI virtualization; own process is PerMonitorV2.
- [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow) — foreground permission is conditional.

## Real launcher-input activation extension

The background mixed-display run can legitimately leave the separate application foreground: [`Window.Activate`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.window.activate?view=windows-app-sdk-2.0) only attempts activation. Do not call this success just because native layout/rendering passed.

```powershell
& ./experiments/phase1-window-probe/run.ps1 -Activation -EvidenceName activation-builtin-125
```

The parent first foregrounds the separate fixture app. Once `ready-for-launcher-click.json` appears, the parent uses the supported computer-use surface to activate **E1 activation launcher — click Open E1 transient** and click the normal **Open E1 transient** button. No alternative input protocol is implemented. The app waits at most 120 seconds; the runner terminates only its owned process after 150 seconds if necessary.

The click handler records foreground ownership, then native `Window.Activate` runs from the real UI-input continuation. Compact, expanded and reopened transients must genuinely become foreground and overlap the prior separate-app rectangle, or the experiment fails. Launcher/second-session lifetime and final cleanup are also asserted. No own-window `SetForegroundWindow` call, forced activation loop or foreground restriction bypass is used. The single prior-app restore attempt occurs after all owned windows close.

## Actual T02 content fixture

```powershell
& ./experiments/phase1-window-probe/run.ps1 -Content -EvidenceName content-builtin-125
```

Only after the parent releases the desktop, this opt-in mode hosts the actual linked product `TranslationPopupContent.xaml`/code-behind, ViewModel and `Phase1PreviewData.Compact` in one isolated experiment Window. Existing Core and MVVM8.4.2 are reused; no versions are introduced. It does not implement or prove the production desktop host.

Actual compact and long known-data fixtures retain their native controls through Light→Dark→Light. Native assertions check exact bindings/preview notice, 400×325DIP target with physical rounding, header/close/actions containment, reference action widths136/112/84 and8DIP gaps, all five original20DIP icon slots with nonempty alpha rendering, no horizontal scroll overflow, complete selectable source notices and scroll reachability of the final notice. The source Expander is tested only as attribution disclosure, not T03 expanded translation UI. Root renders are confined to known fixture content; no provider/capture/clipboard/user text is acquired.

Fresh Figma4:115/4:663 context and screenshots are kept separately under `window-probe/content-references`; they are comparison targets, never implementation assets. Source/preview/unavailable-action additions and native disabled controls/font differences are explicitly reported rather than disguised as exact Figma parity.
