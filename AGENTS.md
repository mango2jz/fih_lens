# FIH Lens — Agent Instructions

## Purpose and default mode

Build a practical Windows translation and language-learning app while helping the developer understand its engineering. Correct software and developer understanding both matter.

**Default: autonomous implementation for action requests.** When asked to build, fix, change, or refactor, inspect the relevant code, implement, verify, and report. Do not stop at a plan or ask whether to proceed with ordinary local work already covered by the request.

For questions, explanations, design discussions, investigations, and reviews, deliver the requested answer without assuming permission to change code. When the user asks for mentor mode, hints, or an implementation exercise, guide them and let them write the code. Return to autonomous implementation for subsequent action requests unless the user asks to keep mentor mode active.

## Execution contract

1. Read the relevant instructions, inspect the working tree, and trace affected code and callers before choosing a solution. Preserve unrelated and uncommitted work.
2. Identify the requested outcome, constraints, and smallest check that will prove it. Resolve routine choices from repository patterns; state assumptions when useful.
3. For substantial features, briefly describe the boundaries, data flow, failure cases, resource ownership, and verification plan before implementing. Continue within the requested scope without a permission handoff.
4. Make the smallest complete change at the responsible layer. For bugs, establish the cause and add a regression check where practical. For refactors, establish existing behavior before changing structure.
5. Run the relevant checks, inspect their output, and fix failures caused by the change. Distinguish pre-existing failures from regressions; never weaken tests to obtain a pass.
6. Finish only when the requested outcome is verified or a concrete blocker remains after reasonable alternatives. Report verification gaps accurately.

Ask only when missing information materially changes the result, authority is missing, or the next action is destructive, irreversible, external-production, or materially outside scope. Respect tool permissions and sandbox limits. Never discard user changes, rewrite Git history, publish, or deploy without authorization.

For long work, keep a concise plan and record decisions, remaining work, and verification evidence in the relevant existing plan or handoff. Do not create tracking documents for trivial changes.

## Repository map and architecture

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for design context and the relevant document in [docs/plans](docs/plans) for the feature being changed. Plans may describe future work; inspect code and project files to establish current behavior. Historical files in `docs/archive/` and `docs/handoffs/` are references, not active agent instructions.

| Location | Responsibility | Allowed project dependencies |
| --- | --- | --- |
| `src/FihLens.Core/` | Domain types and rules | No other FIH Lens project |
| `src/FihLens.Application/` | Use cases, orchestration, external capability contracts | Core |
| `src/FihLens.Infrastructure/` | Providers, HTTP, persistence, configuration, parsing | Application, Core |
| `FihLens.Windows` (planned; not currently in the solution) | Win32, WinRT, capture, hotkeys, clipboard, OCR integration | Application, Core |
| `FihLens.WinUI/` | Views, ViewModels, UI dispatch, composition root | Application, Infrastructure, Core; Windows when introduced |
| `tests/FihLens.Tests/` | Automated tests | Projects and linked sources under test |
| `experiments/` | Bounded API and lifecycle experiments | Keep separate from production wiring |

Dependencies point inward. Core must not depend on UI, Windows APIs, HTTP, or SQLite. Application must not reference Infrastructure, Windows, or WinUI. ViewModels must not call Win32, SQLite, or provider SDKs directly. Compose implementations at the app boundary.

Use interfaces where they enforce an actual external capability boundary or enable useful tests. Avoid speculative interfaces, factories, generic base classes, custom frameworks, and scaffolding for future features. Do not create the planned Windows project until a requested capability needs it. If a request requires changing these architecture rules, explain the consequence and resolve that decision with the user before implementing the violation.

## Implementation rules

- Prefer existing repository patterns, then .NET and native Windows APIs, then already-installed dependencies. Do not add packages unless explicitly requested or approved; explain the need and tradeoff first.
- Keep diffs focused. A requested refactor authorizes its stated scope; propose unrelated broad refactors separately.
- Follow `.editorconfig`, `Directory.Build.props`, and adjacent code. Preserve nullable analysis and handle failure paths explicitly.
- Propagate cancellation through asynchronous work. Guard against stale results and concurrent requests; marshal UI updates to the UI thread. Avoid blocking asynchronous operations on the UI thread.
- Give native handles, COM objects, event subscriptions, hotkey registrations, streams, connections, and cancellation sources clear owners and cleanup paths. Respect thread affinity and window lifetime.
- Validate inputs and provider responses at trust boundaries. Never expose secrets or captured text in logs or test artifacts. Use fake providers for automated checks; live paid requests require authorization.
- For unfamiliar Windows APIs, use a small experiment when it resolves a concrete API or lifecycle uncertainty. Explain what wrappers abstract; do not turn an experiment into premature production architecture.
- Optimize only after measuring a relevant bottleneck. Consider idle CPU and memory, startup, capture, popup, and translation latency for this always-running utility.

## Verification

Run commands from the repository root. The current projects target .NET 10; WinUI additionally requires the Windows toolchain.

```powershell
# Build each affected library (choose the relevant commands).
dotnet build src/FihLens.Core/FihLens.Core.csproj
dotnet build src/FihLens.Application/FihLens.Application.csproj
dotnet build src/FihLens.Infrastructure/FihLens.Infrastructure.csproj

# Run a focused filter first when useful, then the suite for code changes.
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj

# Required when changing WinUI code or app wiring.
dotnet build FihLens.WinUI/FihLens.WinUI.csproj -p:Platform=x64
```

- Use the existing xUnit test project. Test domain rules, orchestration, prompt construction, parsing, routing, fallback, configuration, and ViewModel behavior where practical.
- Add the smallest meaningful regression test for changed behavior. Do not add tests solely to mirror implementation or increase coverage.
- The test project links selected WinUI source files; passing those tests does not prove XAML, app startup, packaging, or native UI behavior. Build WinUI separately when affected.
- Use integration or manual checks for hotkeys, UI Automation, clipboard, capture, DPI, multiple monitors, tray behavior, and resource cleanup. State which checks were performed and which remain.
- Inspect compiler warnings and use existing formatting/static-analysis checks where applicable. A test filter matching zero tests is not successful verification.
- Documentation-only changes need content, link/path, and diff checks rather than an application build. Missing SDKs or toolchain failures must be reported, not presented as passes.

## Delegation

Work directly for small, clear tasks. Use Codex native subagents for independent, bounded implementation, research, review, or verification slices when they materially improve throughput or correctness.

Give each child a concrete outcome, file ownership, constraints, and required evidence. Tell them they share the working tree and must preserve others' edits. Avoid overlapping writes. The parent owns integration and final verification. Use durable team orchestration only when explicitly requested or justified by coordination needs; do not require OMX or tmux for ordinary coding.

## Learning and reporting

Teach through the actual change. Briefly explain consequential architecture, Windows mechanisms, asynchronous behavior, resource lifetime, and testing choices. Link the relevant existing document in `docs/learning/` or `docs/ARCHITECTURE.md` when helpful. Expand explanations when requested; do not require quizzes or a lesson before routine implementation.

Keep progress updates short and evidence-based. Final reports state the result, changed files, checks and outcomes, and remaining limitations. For meaningful milestones, add a short note on the engineering concepts practiced. For reviews, prioritize actionable findings with file locations, consequences, and suggested corrections.


## Agent skills

### Issue tracker

Issues live in GitHub Issues for `mango2jz/fih_lens`, using the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: the glossary lives at `GLOSSARY.md` and ADRs live in `docs/adr/`. See `docs/agents/domain.md`.
