# Combined manual translator Ralplan intake

## Full task and desired outcome
User: "proceed to trigger $oh-my-codex:ralplan on this current plan, combine all 3 plans into 1". Sources are docs/plans/phase-0-application-implementation-plan.md, phase-1-winui-implementation-plan.md, phase-2-translation-engine-implementation-plan.md; reuse approved .omx/plans/phase-0-application-operation-plan.md. Prior discussion accepted one umbrella with separately verifiable foundation, MVVM demo, real translation milestones, skipping Phase0 throwaway button-handler UI. Latest provider clarification: "both gemini and deepseek". This overrides Phase2's single-adapter/student-selection default: plan TWO concrete REST adapters and startup demo|gemini|deepseek selection, not automatic routing. Planning only; no application implementation or live calls now.

## Known repo facts and evidence
- Core records/enums/validated request already implemented (src/FihLens.Core); TranslationResult lists are nonnull IReadOnlyList but not deeply immutable. Preserve Core.
- Existing solution FihLens.slnx:1-13 has Core, empty WinUI and tests. No Application/Infrastructure implementations yet; planned components are not existing code.
- tests/FihLens.Tests/FihLens.Tests.csproj:4,10-22: net10.0, xUnit 2.9.3, runner 3.1.4, existing test SDK/coverage; references Core only. Extend this project, not a Windows test executable reference.
- FihLens.WinUI/App.xaml.cs:43-46 constructs parameterless window; MainWindow.xaml:16-18 empty Grid. WinUI csproj:4-14 targets net10.0-windows and existing packaging/tooling; WindowsAppSDK version at 41 is 2.5.1. Preserve existing package/tooling settings unless separately requested.
- Directory.Build.props:3-8 supplies Nullable, ImplicitUsings, LangVersion latest. dotnet --list-sdks freshly gives 8.0.417 and 10.0.401; host macOS. Windows runtime evidence unavailable locally.
- Prior Application plan cycle1 sequential Architect/Critic approved; reuse its service/fake payload and tests. It is superseded for FUTURE combined execution only: old review evidence remains historical and does not approve this new scope.
- Fresh existing portable test retry outside sandbox: result is recorded below. Prior 45-test result is historical baseline, not future implementation evidence.
- Existing developer change: .gitignore adds .omx-state-locks. Preserve its exact bytes; no unrelated edits. Prior Phase1 intake snapshot is reference only and its old ambiguities are resolved by current combined scope/agent-owned plan.

## Constraints and boundary decisions
Application -> Core; Infrastructure -> Application+Core; WinUI -> Application+Infrastructure (+existing Core). One UI, one VM, one concrete service, one fake, TWO cloud adapters by explicit user request. CommunityToolkit.Mvvm 8.4.2 is the only planned new package per Phase1; use same pinned version in WinUI/test-linked VM. Native HTTP/JSON, no SDK, container, mediator, parser/builder ports, automatic provider failover/router, streaming, retries, persistence, capture/OCR/hotkeys, CEFR Explain or language selectors.
Direct task forwarding remains service contract. Fake optional latency defaults zero (immediate fixture), demo startup uses 1s cancellable latency; null-before-cancel-before-support ordering and fresh owned result collections. VM one request, stays busy until completion, cancellation suppresses noncooperative late success, closure suppresses later display-state writes. Prompt/parser only EN->VI Translate null-level for live; fake retains null/CEFR fixture policy.

## Unknowns/open questions and mitigations
Exact cloud model/endpoints/JSON envelope rules: native official research underway for chosen Gemini/DeepSeek. Credentials/account access not required to finish planning or deterministic tests; live gates tracked separately per provider. Windows gate separate; missing platform must not stop portable unrelated goals. Do not claim full completion from demo/offline success.

## Planning artifacts and review process
Create .omx/plans/prd-manual-translator-phase-0-2.md and test-spec-manual-translator-phase-0-2.md, plus official evidence/contract worksheet, sequential Architect then Critic evidence and exact-byte consensus manifest. Main owns context/state/closeout, Planner owns PRD/test-spec initial draft. Deliberate review is appropriate for credentials/untrusted cloud responses: three-scenario pre-mortem plus unit/integration/e2e/observability checks. No fake host authority or execution authorization from local artifacts.

## Future touchpoints
New Application/Infrastructure projects; Translation port/service/prompt/instructions/failure files; fake/parser/Gemini/DeepSeek files/internal DTOs/provider schema; existing WinUI project/App/window plus single ViewModels/MainWindowViewModel.cs; same existing tests linked to portable VM source and focused handler/parser tests. Only planning/context/state files may be written in this turn.

Fresh baseline command `dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --no-restore --verbosity minimal` passed: 45 passed, 0 failed, 0 skipped. Official .NET/Toolkit evidence is `.omx/plans/manual-translator-dotnet-evidence.md`.

Official cloud worksheet is `.omx/plans/manual-translator-cloud-contracts.md`: selected current Gemini gemini-3.5-flash-lite and DeepSeek deepseek-flash, with distinct GenerateContent schema versus Chat Completions JSON-only contracts and per-provider live gates. No credentials or live calls were used. .NET response-cap classification uses typed HttpRequestError.ConfigurationLimitExceeded per official evidence, not string matching.
