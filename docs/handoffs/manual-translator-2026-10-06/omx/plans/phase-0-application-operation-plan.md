# Phase 0 Application — agent-executable Ralplan / PRD

Planning status: draft for sequential Architect → Critic review (cycle 1).
Execution intent: agents implement the plan in a future execution run; no manual developer assignments or learning quizzes gate delivery. This conversion request ends after planning and consensus evidence. It does not start application implementation.

Source: [Application teaching guide](../../docs/plans/phase-0-application-implementation-plan.md). This replaces the executable section and duplicated appendix of the earlier operation plan. The source guide remains learning reference; its manual-assignment rules and old toolchain note do not govern this execution plan. Domain rules remain in the [Core guide](../../docs/plans/phase-0-core-implementation-plan.md) and existing Core implementation.

## Requirements and grounded baseline

Deliver one translation use case: caller → TranslationService → ITranslationProvider → FakeTranslationProvider → existing TranslationResult. Implement the Application/Infrastructure libraries, focused tests, manual constructor composition and a minimal WinUI display. Keep this small enough to understand from its final diff.

| Current fact | Evidence | Planning consequence |
|---|---|---|
| Core request validation already exists | `src/FihLens.Core/Models/TranslationRequest.cs:6-48,51-77` | Reuse it; service guards only null. |
| Result exposes optional strings and nonnull read-only list interfaces | `src/FihLens.Core/Models/TranslationResult.cs:3-28` | Populate a deterministic fixture; own collection storage rather than claiming deep immutability. |
| No Application/Infrastructure projects yet | `FihLens.slnx:1-13` and source inventory at intake | Add exactly these portable libraries. Recheck for intervening edits before execution. |
| Tests use net10.0 and existing xUnit packages | `tests/FihLens.Tests/FihLens.Tests.csproj:4,10-22` | Extend this test project, keeping its Core reference/packages. |
| Shared nullable/implicit-using/language settings exist | `Directory.Build.props:3-8` | Inherit them; avoid configuration churn. |
| Startup constructs an empty window | `FihLens.WinUI/App.xaml.cs:43-46`; `MainWindow.xaml:16-18` | Compose once at startup; add only a demonstration button/result. |
| WinUI has Windows-specific tooling | `FihLens.WinUI/FihLens.WinUI.csproj:4-14,40-44` | Preserve package/version/packaging settings; verify on Windows separately. |
| Fake fixture and support policy already specified | `docs/plans/phase-0-core-implementation-plan.md:409-417` | Implement once; do not duplicate Core-plan Application assignments. |
| .NET 10 SDK available on current macOS host | Fresh intake: `dotnet --list-sdks`, 10.0.401 | Portable verification can run here. Old .NET-8-only note is superseded. |

The mentor objective remains: briefly explain dependency direction, task forwarding, cancellation ownership and UI-thread/error ownership in the implementation report. Developer code-writing and prediction exercises are optional learning activities, not execution prerequisites.

## RALPLAN-DR (short mode)

Principles:
1. Preserve the existing domain model and inward dependency direction.
2. Keep forwarding behavior observable: no hidden state, task wrapping, failure conversion or cancellation policy in the service.
3. Reuse the existing test project and native .NET APIs; add no dependencies or speculative abstractions.
4. Make deterministic portable evidence precede Windows integration; report platform gaps honestly.

Top decision drivers: faithful conversion of the original Phase 0 scope; minimal implementation that can be verified; autonomous delivery with retained developer understanding.

| Viable option | Advantages | Costs / limits |
|---|---|---|
| A — service + single Infrastructure fake + focused tests + minimal WinUI display (chosen) | Preserves original end-to-end milestone and both production/test provider paths; small graph | Requires a Windows gate for the final UI goal. |
| B — Application + focused test doubles now, Infrastructure/UI in a later milestone | Fastest portable slice; smaller immediate diff | Leaves source assignment F unfinished and does not prove startup composition. Viable if milestone scope is intentionally reduced, but not selected for this conversion. |

Do not add a DI container, mediator, ITranslationService, factory, mock framework or generic provider base: none solves a current requirement. An Application-owned provider port is justified by the outer implementation and controlled tests; that does not justify interfaces for every class.

## Architecture and behavior contract

Project references: Application → Core; Infrastructure → Application + Core; WinUI → Application + Infrastructure, retaining existing Core; tests → Core + Application + Infrastructure. Core gains no references. Application has no Windows, HTTP, UI, database or SDK types.

| Boundary | Exact behavior |
|---|---|
| Port | Public `FihLens.Application.Translation.ITranslationProvider`, one `Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)`. Briefly document nonnull success, unsupported failure, faults and cooperative cancellation; no background-thread promise. |
| Service | Public sealed `TranslationService` in the same namespace. One readonly provider dependency; constructor and request null guards. Call provider once with the original request/token; return its exact task directly. Preserve synchronous exceptions, task faults, cancellation and result identity. No catches, extra validation, replacement tokens or per-request fields. |
| Fake | Public sealed `FihLens.Infrastructure.Translation.FakeTranslationProvider`, implements the port. Support exact ordinal `Hello world`, English → Vietnamese, Translate mode, null or any valid CEFR level. Other valid requests throw synchronous `NotSupportedException`. |
| Fake ordering | Null request throws synchronously first; then an already-canceled token returns `Task.FromCanceled<TranslationResult>(token)` even for unsupported valid fixtures; then support check; then immediate `Task.FromResult(result)`. Post-completion cancellation cannot change success. |
| Result ownership | Fresh result and private collection storage per success; expose read-only views using existing .NET collection APIs, never mutate after return. No changes to Core collection policy or new immutable package. |
| Composition | App.OnLaunched constructs fake and service once and passes service into MainWindow. Button handler constructs the fixture request, awaits service and displays NaturalTranslation on the UI thread. No provider creation per click. |
| UI errors/lifetime | Await inside try/catch; show explicit error/cancellation text; re-enable button in finally. `async void` only for required event handler. Service/fake own no disposable resources, cancellation sources or permanent event subscriptions. No blocking waits or Task.Run. |

### Fixed demonstration payload

Implement and assert these literals using the existing Core records; this is fake demonstration data, not translation quality evidence.

| Field | Value |
|---|---|
| OriginalText | `Hello world` |
| NaturalTranslation | `Xin chào thế giới` |
| Explanation | `A simple greeting to the world.` |
| Context | `Greeting` |
| Tone | `Friendly` |
| Vocabulary (one item) | Word `hello`; Phonetic `/həˈləʊ/`; PartOfSpeech `interjection`; Meaning `xin chào`; ExampleSentence `Hello, everyone!` |
| Grammar (one item) | Pattern `Greeting + addressee`; Explanation `A greeting can be followed by the person or group addressed.`; ExampleSentence `Hello, world!` |
| Alternatives (one item) | Text `Chào thế giới`; Explanation `A shorter Vietnamese greeting.` |

## Ordered implementation goals

All paths below are planned unless identified as existing. Goal exit criteria map to the [test specification](test-spec-phase-0-application.md). Reuse any intervening valid implementation instead of overwriting it.

### G0 — Verify prerequisites and create the portable graph

Owner: executor; leader integrates. Read the current diff and Core models/tests; run the existing Core suite with a .NET 10 SDK before downstream changes. If it fails, diagnose without expanding into a domain rewrite; report any unresolved prerequisite.

Create `src/FihLens.Application/FihLens.Application.csproj` and `src/FihLens.Infrastructure/FihLens.Infrastructure.csproj` targeting net10.0. Add the inward references above, add both to existing `FihLens.slnx`, and extend `tests/FihLens.Tests/FihLens.Tests.csproj`. No new packages or empty future folders.

Exit: Core regression suite passes; new libraries build; the project graph matches the contract. Build the affected projects, not the Windows solution on macOS.

### G1 — Implement the Application boundary and deterministic tests

Owner: executor. Create `src/FihLens.Application/Translation/ITranslationProvider.cs` and `TranslationService.cs` exactly as specified. Add `tests/FihLens.Tests/Application/TranslationServiceTests.cs` using compact local handwritten recording/controlled providers. No shared mocking framework.

Cover AC1–AC6: null guards, one-call and identity/token forwarding, task/result identity, synchronous throw, fault, precanceled/default token and deliberately pending completion. Use TaskCompletionSource with RunContinuationsAsynchronously; no sleeps or delay-based ordering. Bound awaited completion so a broken service cannot hang the suite. Do not duplicate Core validation tests.

Exit: targeted Application tests and Application build pass; no per-request state or outward dependency.

### G2 — Implement the single demonstration fake and composed portable flow

Owner: executor. Create `src/FihLens.Infrastructure/Translation/FakeTranslationProvider.cs`, `tests/FihLens.Tests/Infrastructure/FakeTranslationProviderTests.cs`, and one real service/fake composition case in `TranslationServiceTests.cs` (or a separate flow file only if clearer).

Cover AC7–AC10 with valid Core requests: exact payload, null/all CEFR levels, unsupported dimensions one at a time, cancellation precedence/token, immediate completion and independent collection ownership. Fixed literals are the oracle; do not compute expected values by calling production fixture code.

Exit: Infrastructure build, focused fake tests and full portable suite pass. No network/platform use; retain existing Core tests. Record commands, counts and attributable warnings.

### G3 — Wire the minimal WinUI demonstration

Owner: executor; dependent on G2. Modify only existing `FihLens.WinUI/{FihLens.WinUI.csproj,App.xaml.cs,MainWindow.xaml,MainWindow.xaml.cs}`. Add project references; preserve package/packaging settings. Compose once in startup; inject the concrete service into the window. Add a labeled button and text/status element with keyboard access; await and display the fixed result, handling errors and repeat-click state.

Exit: Windows x64 build and launch/repeat-click/close smoke pass (AC11). If Windows is unavailable, complete reviewable source wiring, record `awaiting_windows_verification`, and stop only the Windows-dependent branch. Do not call source inspection or cross-targeting proof of UI behavior.

### G4 — Final verification and explanation

Owner: leader/verifier. Review the final diff, references and warning output; run portable checks after any relevant UI/reference change. Report portable and Windows outcomes separately, modified files, remaining risks and the four concepts above.

Exit: AC1–AC12 proven; or portable completion recorded with a precise unresolved Windows/tooling blocker. Full Phase 0 stays incomplete until Windows evidence exists. Stop at this milestone; no Phase 1, commit, deployment or new package changes.

## Acceptance and verification

AC1–AC12 and exact runnable checks live in [test-spec-phase-0-application.md](test-spec-phase-0-application.md). Inspect fresh build/test output, nonzero discovered test counts and warnings. Existing warnings may be reported; new warnings attributable to the change must be resolved. Build/compiler analysis plus focused source/reference inspection is the static check; no new lint tool is needed.

## Risks and mitigations

- Overlapping Core/Application guides → this executable plan owns port/service/fake/UI once, while existing Core owns domain rules. Do not repeat the old manual assignments or appendix.
- Immediate fake conceals asynchronous defects → independently controlled pending, faulted and canceled tasks exercise service forwarding.
- Failure timing or cancellation ambiguity → explicit synchronous guard/support failures and null-before-cancel-before-support ordering, tested directly.
- Mutable result collections → fresh privately owned read-only views per fake call; do not refactor Core or overclaim generic immutability.
- Windows availability/package restore → separate portable evidence and pending Windows state; diagnose existing tooling without silent upgrades. Capture any exact failure and supported recovery path.
- Existing developer changes → re-read diff before editing; preserve unrelated changes; integrate bounded work rather than resetting files.

## ADR — minimal end-to-end boundary

Decision: option A, existing Core models, concrete service + Application-owned provider interface, single fixture adapter, constructor composition and existing xUnit. The external-capability port keeps Application independent of Infrastructure while enabling controllable tests. Direct task forwarding preserves the source contract's task/exception timing and avoids an unnecessary async wrapper.

Alternatives: option B is a valid staged portable milestone but defers original scope; a DI container or generalized routing adds dependencies/behavior without current need. Consequences: the fake only demonstrates one fixture, manual composition is explicit, real provider/response validation remain future work, and Windows evidence is a separate final gate. Follow-ups: real provider and richer UI require their own contracts/plans when requested.

## Execution staffing and handoff

Default follow-up: `$oh-my-codex:ultragoal` using this PRD and its test specification. This turn requests conversion only: no authorized implementation handoff is asserted. A later explicit execution request can bind these approved artifacts and start the execution lane; no second manual developer-writing approval is needed inside that run.

Use one executor for G0–G3 and leader/verifier for G4; normal implementation reasoning, higher scrutiny for async/cancellation and verification. Sequential work is simplest because new interfaces/project references are shared prerequisites. Do not launch Team for this small scope. If later parallel delivery is justified after G0/G1, an Infrastructure executor can own only its provider/test files while a WinUI executor owns only its four files; leader alone merges shared solution/test-project changes and runs the complete suite. `omx team 2:executor "Execute approved Phase 0 Application plan with Infrastructure and WinUI file ownership"` is only a future CLI launch hint, requiring the appropriate runtime and explicit execution scope.

Local native Architect→Critic reviews and byte-bound artifacts establish planning lifecycle evidence. They do not claim host-issued authority, and planning artifacts alone do not authorize implementation.

## Official evidence input

Bounded native researcher review, retrieved 2026-10-06 against .NET 10 API documentation:

- [Task-based Asynchronous Pattern](https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap): distinguish synchronous call/usage errors from task faults. Preserve both in service tests.
- [Task.FromCanceled](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.fromcanceled?view=net-10.0): requires an already-canceled token; returns a task associated with it.
- [TaskCompletionSource](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1?view=net-10.0) and [TaskCreationOptions](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcreationoptions?view=net-10.0): controlled completion and asynchronously scheduled continuations; explicit completion determines test ordering.
- [Keep the UI thread responsive](https://learn.microsoft.com/en-us/windows/apps/develop/performance/keep-ui-thread-responsive) and [async scenarios](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios): keep UI updates on its thread, handle async event errors within the handler.
- [Windows development environment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/set-up-your-development-environment): Windows tooling/runtime gate. Current docs do not authorize upgrading this repo's existing packages.
