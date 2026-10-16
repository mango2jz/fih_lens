# Test specification — combined manual translator Phase 0–2

Companion [PRD](prd-manual-translator-phase-0-2.md). These are future implementation checks; this planning turn does not add production tests or contact providers. Use existing `tests/FihLens.Tests` xUnit packages and small handwritten providers/`HttpMessageHandler` fixtures. No mock/UI-test package or sleep-based ordering. Existing 45 passing tests are an intake baseline, not evidence that planned behavior works.

## Acceptance matrix

| ID | Observable acceptance | Evidence / planned location |
|---|---|---|
| F1 | `TranslationService` null guards are synchronous; null request never calls provider. Valid call invokes once with original request/token and returns exact provider task/result. Omitted token remains default. Synchronous throw, fault, canceled task and caller token all preserve their original behavior. Pending and reverse-completed overlapping tasks do not cross-contaminate. | `tests/FihLens.Tests/Application/TranslationServiceTests.cs`; local recording/controlled provider and bounded `TaskCompletionSource` waits. |
| F2 | Fake's exact fixed payload has one vocabulary, grammar, alternative item; null/all valid CEFR levels accepted; each unsupported dimension rejected; null-before-cancel-before-support precedence; default immediate result; later cancellation does not alter success. Optional one-second startup latency is token-cancelable; invalid delay rejected; two calls own independent read-only collection storage. Real service+fake composition works in-process. | `tests/FihLens.Tests/Infrastructure/FakeTranslationProviderTests.cs`, service composition case; no sleep. |
| F3 | Application references only Core, Infrastructure references Application/Core, test project references portable libraries; Core remains unchanged; no new package except same pinned Toolkit in WinUI/test project. New libraries build without Windows. | Project/solution diff and portable build output. |
| U1 | VM blank input disables Translate; typing nonblank updates eligibility immediately. Pending operation snapshots input, sets busy/progress, clears old output/error, disables input/Translate; success publishes one result and restores readiness. Failure shows safe feedback and restores readiness; demo unsupported text shows exact-fixture guidance. | `tests/FihLens.Tests/WinUI/MainWindowViewModelTests.cs` compiling linked production VM source; controlled provider. |
| U2 | Cancel token reaches provider; requested cancellation remains busy until task finishes; noncooperative late success is suppressed. Unexpected cancellation follows failure path. Close before completion freezes VM-owned result/error/status/busy state and suppresses late writes. No overlapping invocation. | Same VM tests with `TaskCompletionSource`; command `ExecutionTask` awaited with bounded guard. |
| U3 | Windows x64 app builds and runs; known fixture shows structured output; wrong demo text gets guidance; input/command binding updates without focus change; progress/cancel/close behavior, keyboard labels, selectable scrolling output, resize and light/dark readability observed. | Windows build plus manual UI smoke; inspect XAML/runtime binding and Toolkit diagnostics. |
| C1 | Application `PromptBuilder` returns `TranslationInstructions(SystemText, UserText)`, rejects unsupported language/mode/non-null level before handler invocation, preserves input verbatim in UserText separate from trusted SystemText. Infrastructure parser accepts valid required arrays (including empty) and optional absent/null fields, maps `OriginalText` from request, rejects root null/malformed/wrong-type/missing/null arrays/null items/blank required text. Item fields match the PRD's exact camelCase names and Core mapping. | `tests/FihLens.Tests/Application/PromptBuilderTests.cs` and `tests/FihLens.Tests/Infrastructure/TranslationParserTests.cs`; hand-authored JSON oracle. |
| C2 | Gemini handler observes exact v1beta URL/method/model, request-scoped key, system/user separation, 2,048 output-token budget and current response-format schema. Exactly one STOP candidate with ordered text parts maps analysis; thought-marked parts are skipped when remaining ordered text yields valid JSON, while only-thought/no-text content fails. Blocked/refused, multiple candidates, non-STOP, unsupported nontext and incomplete content map specified failures. No deprecated/mixed API fields. | `tests/FihLens.Tests/Infrastructure/GeminiTranslationProviderTests.cs`; captured outgoing JSON and canned envelope fixtures. |
| C3 | DeepSeek handler observes exact chat URL/method, bearer auth, `deepseek-flash`, disabled thinking, system/user roles, JSON-object response format, token cap and nonstreaming. One stopped choice yields analysis; empty/multiple/length/filter/resource/aborted/tool cases map specified failures. No schema-strength claim from JSON mode. | `tests/FihLens.Tests/Infrastructure/DeepSeekTranslationProviderTests.cs`; captured outgoing JSON and canned envelope fixtures. |
| C4 | For **each** adapter: caller cancel, timeout-shaped cancel, auth/account, rate limit, request rejection, unavailable, refusal, invalid body, 1 MiB bound, network error and malformed structured output remain distinct as specified. Test both declared-length and unknown-length body overflow; .NET 10 `ConfigurationLimitExceeded` maps to `InvalidResponse` without message matching. One send per invocation; request/response disposed and injected client survives. Neither raw body nor key enters UI-safe exception message. | Both handler suites with cancellation-aware handler and custom content; inspect exception category/message and ownership; no network. |
| C5 | Startup `demo|gemini|deepseek` selects exactly one provider, demo default, provider label/privacy text present. Unknown mode and missing selected key fail clearly without sending or selecting fake; unselected key absent is allowed. VM presents failure categories safely. | Focused startup/composition inspection or small seam tests where practical; Windows smoke for visible behavior. Do not extract a general startup framework solely for testing. |
| C6 | A short non-sensitive English sentence yields a valid Vietnamese `NaturalTranslation` through Gemini, then DeepSeek, in separately configured Windows runs; result's OriginalText matches submitted input. Cancel/error feedback checked; no exact model wording assertion or secret capture. | Manual live smoke log recording mode/model, status, time and sanitized outcome only. Paid/account gate is separate for each provider. |
| X1 | Final graph, warning and diff review show no HTTP/JSON in Application/VM, no direct UI provider parsing, no silent fallback/retries/SDK/new dependency, no leaked credentials, no shared-client disposal on Window.Closed, and no later-phase features. | Final source/project inspection; portable tests, Windows build and smoke outputs. |

## Runnable verification path

Recheck SDK, current diff and prior baseline before future edits. Run gates after their dependent step changes; inspect nonzero discovered test counts and actual warning/error output. Test filters below assume planned namespaces; adjust filters to actual names and confirm they select tests.

```sh
dotnet --list-sdks
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --filter 'FullyQualifiedName~FihLens.Tests.Core'
dotnet build src/FihLens.Application/FihLens.Application.csproj
dotnet build src/FihLens.Infrastructure/FihLens.Infrastructure.csproj
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --filter 'FullyQualifiedName~TranslationServiceTests|FullyQualifiedName~FakeTranslationProviderTests'
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --filter 'FullyQualifiedName~MainWindowViewModelTests'
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --filter 'FullyQualifiedName~PromptBuilderTests|FullyQualifiedName~TranslationParserTests|FullyQualifiedName~GeminiTranslationProviderTests|FullyQualifiedName~DeepSeekTranslationProviderTests'
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --logger 'trx;LogFileName=manual-translator.trx'
```

On a Windows host with the repository's established WinUI tooling:

```sh
dotnet build FihLens.WinUI/FihLens.WinUI.csproj -p:Platform=x64
```

Run the app in demo mode for U3, then once per selected provider for C6 using credentials supplied only through that process's environment. Check actual model availability/pricing and official contract before live work. Never put the environment values, HTTP bodies or user input in test artifacts. No automated paid integration test is required. Current macOS portable gates cannot prove WinUI runtime behavior.

## Gate accounting and failure behavior

| Gate | Pass condition | If unavailable/failing |
|---|---|---|
| Foundation | F1–F3 and focused/full portable suite pass. | Diagnose actual failure before G2/G3 dependencies; preserve unaffected work. |
| MVVM portable | U1–U2 and relevant portable suite pass. | Do not claim UI wiring from source alone. |
| Windows demo | U3 passes on Windows x64. | Mark `awaiting_windows_verification` with host/tooling reason; G3 offline work may proceed. |
| Cloud offline | C1–C5 and both provider fixture suites pass. | Adapter-specific failure stays open; one provider pass does not cover the other. |
| Gemini live / DeepSeek live | Each independent C6 smoke passes with a configured account. | Mark that provider `awaiting_credentials_or_provider_access` or exact API failure; never claim live complete from fake/handler tests. |
| Final | X1 and all above pass with no new attributable warnings. | Report partial evidence and blockers; full combined milestone remains open. |

**Expanded validation levels:** Unit tests protect task/VM/parser rules; in-process composition and fake HTTP handler tests cover project/adapter integration; Windows demo and two live smokes are end-to-end gates; observability is safe on-screen status and a sanitized per-gate execution report (mode, category, elapsed time, no prompt/secret/body). If the response bound, cancellation or resource lifetime fails, stop the affected adapter path and correct it before a live call. Preserve independent passed checkpoints; rerun only checks affected by new edits.
