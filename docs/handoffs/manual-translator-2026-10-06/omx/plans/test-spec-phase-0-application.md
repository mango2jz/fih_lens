# Test specification — Phase 0 Application

Companion PRD: [phase-0-application-operation-plan.md](phase-0-application-operation-plan.md). Uses existing `tests/FihLens.Tests/FihLens.Tests.csproj` packages and Core models. All listed new tests are future implementation work; this planning conversion does not create them.

## Acceptance matrix

| ID | Case and observable assertion | Level / location |
|---|---|---|
| AC1 | Null provider constructor and null request invocation throw synchronous ArgumentNullException; null request leaves provider count zero. | Unit, `Application/TranslationServiceTests.cs` |
| AC2 | A valid call invokes once, receives original request and supplied token; omitted token is default. Success preserves result identity and exact provider task identity. | Unit, same file |
| AC3 | A deliberately synchronous provider exception is the same exception observed synchronously by caller. A Task.FromException fault is observable on await with same failure, never success. | Unit, same file |
| AC4 | A provider's canceled task is returned unchanged, IsCanceled is true, and await throws an OperationCanceledException subtype carrying the supplied token. Also use a canceled caller token with a provider that chooses to succeed to prove the service adds no cancellation short circuit. | Unit, same file |
| AC5 | Controlled TaskCompletionSource with RunContinuationsAsynchronously: invocation recorded, returned task still pending, SetResult releases it with identical result. No sleeps; completion await is bounded with WaitAsync(TimeSpan.FromSeconds(5)) only as a hang guard. | Unit, same file |
| AC6 | Two controlled requests complete in reverse order and each gets its own task/result without cross-contamination. Keep the double local; do not claim this proves arbitrary provider thread safety. | Unit, same file |
| AC7 | Exact fixture with null level and each valid CEFR A1–C2 completes immediately, matches every PRD payload scalar/list field and has exactly one item per list. | Unit, `Infrastructure/FakeTranslationProviderTests.cs` |
| AC8 | Change each supported dimension independently with otherwise valid Core requests: text case/whitespace/other text, source Auto or Japanese with null level, target Japanese, mode Explain. Synchronous NotSupportedException; no bogus successful payload. Do not try invalid Core level/language combinations to test the fake. | Unit, same file |
| AC9 | Fake null request throws before cancellation. Both supported/unsupported valid requests with an already-canceled token return canceled tasks and cancellation exception token matches. Noncanceled fixture succeeds immediately; canceling its source afterward leaves success unchanged. | Unit, same file |
| AC10 | Two success results have distinct collection storage/read-only views; no list is caller-mutable via IList/ICollection (mutation throws if that interface is present), and a later call leaves earlier payload unchanged. Real TranslationService + FakeTranslationProvider returns the exact translation and learning payload without network/Windows access. | Unit + in-process composition, existing focused files |
| AC11 | Windows x64 build succeeds. Launch, use keyboard/mouse to activate the labeled button, see Xin chào thế giới, repeat and close; responsive UI, no unhandled exception. Error handling/finally/repeat-click state reviewed in source; do not simulate inaccessible failure paths or add UI test frameworks. | Windows build + UI smoke |
| AC12 | Reference graph equals PRD; no Core edits/new packages, no request/result fields, provider construction in service, blocking waits, speculative interfaces or future-feature code. Preserve original guides and existing developer edits. No new attributable warnings. | Diff/project/source/compiler inspection |

Tests belong at unit level because these contracts involve object/task forwarding and a deterministic in-memory adapter. The composition case verifies the real boundary; the Windows smoke covers the platform UI. Neither proves a real AI provider, translation quality, DPI or native capture.

## Runnable gates (during future implementation)

Prerequisite: `dotnet --list-sdks` confirms .NET 10 and the existing Core suite succeeds. Recheck the worktree before mutation.

```sh
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --filter 'FullyQualifiedName~FihLens.Tests.Core'
dotnet build src/FihLens.Core/FihLens.Core.csproj
dotnet build src/FihLens.Application/FihLens.Application.csproj
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --filter 'FullyQualifiedName~FihLens.Tests.Application'
dotnet build src/FihLens.Infrastructure/FihLens.Infrastructure.csproj
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --filter 'FullyQualifiedName~FihLens.Tests.Infrastructure'
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --logger 'trx;LogFileName=phase-0-application.trx'
```

Run these in dependency order as goals complete. Final full suite includes existing Core and learning experiment tests; inspect nonzero counts and errors/warnings rather than treating an empty filtered run as success. Do not repeatedly rerun passed gates without relevant changes. TRX can remain in the existing test output location; record path/outcome in the execution report.

On Windows with the existing WinUI tooling and .NET 10:

```sh
dotnet build FihLens.WinUI/FihLens.WinUI.csproj -p:Platform=x64
```

Use the existing Windows/Rider launch/deployment setup for AC11. Record host, build outcome, warning review and observed UI behavior. Existing package/tooling failures require diagnosis, not an automatic upgrade. No full-solution macOS build as a portable gate.

## Static checks and completion rules

Inspect csproj references, solution entries and the final diff; use available compiler/Rider inspections without adding tooling. CancellationTokenSource is disposed by test callers, never by service/fake. No service-owned native handles or resource cleanup requirements are introduced.

- Portable complete: AC1–AC10 and portable parts of AC12 pass with fresh output.
- Full milestone complete: portable complete plus AC11 and final AC12 review pass.
- Windows unavailable: retain completed portable evidence, label G3/full milestone awaiting Windows verification, and report the exact missing host/tooling. Do not ask the developer to implement code; no UI completion claim until verified.
- Planning complete: final PRD/test spec have approving sequential native Architect→Critic evidence; this is distinct from passing the future implementation gates.
