# FIH Lens local dictionary first — revised PRD and handoff

Date: 2026-10-07. Complexity: **MEDIUM**. Review status is recorded separately in `local-first-ai-sentence-consensus.json`; older approval binds older bytes. This turn changes plans only. Prior full implementation authorization is retained, but the native aggregate goal is paused and is not resumed here.

## Desired result and binding scope

English→Vietnamese word/phrase definitions come from a local dictionary and appear inside FIH Lens. Full sentences and explanations use the existing selected Gemini/DeepSeek API **only after Ask AI**. Typing, Lookup dictionary, local misses/errors, startup and cancellation never invoke AI or HTTP. There is no automatic cloud retry or alternate-provider fallback.

The newest user decision, “for full sentence translation, use external AI api, no need to integrate an slm,” supersedes Round 1's offline sentence requirement in the [interview](../../../.omx/specs/deep-interview-local-dictionary-first.md). Manual Ask AI, in-app results, EN→VI, provenance and other scope exclusions remain binding; [revision context](../../../.omx/plans/local-first-ai-sentence-revision-context.md) records the change.

Remove active Translate offline, local sentence MT/SLM, CT2/Argos/ONNX adoption, model downloads, tokenizer/generation work, Python inference workers, interop, child-process lifecycle and model quality/CPU benchmarks. Existing offline experiments/evidence remain **inactive and untouched**, including the user-chosen external `D:/.omx/verification/local-dictionary-first/model` folder. No deletion or unregister action is needed. An optional standalone dictionary conversion tool may be reused; Python is never an app runtime requirement. Japanese/JLPT, OCR, capture, hotkeys, history, rich HTML/scripts, broad dictionary formats and settings/plugin frameworks remain excluded.

### Principles and top three drivers

1. Local actions cannot authorize sending text; only Ask AI does.
2. A real dictionary needs both inspectable provenance and reviewed meanings.
3. Dictionary entries and generated AI output retain separate source labels.
4. Reuse .NET JSON parsing, current AI services and current app/test patterns.

Drivers: (1) all results inside FIH Lens and explicit text egress; (2) useful EN→VI data with honest quality/licensing status; (3) smallest maintainable change without a model runtime or new dependencies.

## Inspected evidence

- [Current ViewModel](../../../FihLens.WinUI/ViewModels/MainWindowViewModel.cs) has one Translate command; typing sends no HTTP. [App startup](../../../FihLens.WinUI/App.xaml.cs) currently replaces the main window with a configuration-error window for invalid selected AI settings. Local dictionary production code does not exist yet.
- [TranslationService](../../../src/FihLens.Application/Translation/TranslationService.cs) forwards directly to `ITranslationProvider`; [startup selection](../../../FihLens.WinUI/TranslationStartup.cs) chooses Gemini/DeepSeek and reads only its key. Reuse its 30 s timeout, 1 MiB HTTP response cap and disabled redirects; retain [safe HTTP failures](../../../src/FihLens.Infrastructure/Translation/ProviderHttp.cs), parsing and cancellation.
- [Existing xUnit project](../../../tests/FihLens.Tests/FihLens.Tests.csproj) links ViewModel/startup sources. [Baseline TRX](../../../.omx/verification/local-dictionary-first/baseline/local-first-baseline.trx) records 195 passing portable tests; those tests do not prove the new dictionary or real XAML/startup behavior.
- [Corpus inspection](../../../.omx/verification/local-dictionary-first/dictionary/RESULT.md) and [manifest](../../../.omx/verification/local-dictionary-first/dictionary/converted/dictionary-manifest.json): thichhoc v0.9.0, source commit `ec3fa199aebdc4da4ebcaa7a11ac4b9c44046f75`, 155,139 articles, 147,192 Python-normalized headwords/phrases, 32,050,247 bytes, 96/100 query hits. **All human meaning verdicts remain pending; data is generated/unreviewed and not adoption-ready.** Python lookup timings are not .NET/WinUI performance proof.
- Exact archive SHA256 `f65cab4dad91ac4bcc8cdd588dabb7d718eb9e50588d3ae221e468e433b6eaf1`; converted corpus SHA256 `f2726c2e435e262eb87b051d0c1cf159a309d3f4a25f77a9ca762136117a6b4c`. CC BY-SA 4.0 data requires attribution, retained notices, identified conversion changes and same-license derivative data; upstream MIT code does not replace data terms.
- [Dictionary comparison evidence](../../../.omx/plans/local-dictionary-dependency-evidence.md) establishes GoldenDict-ng's CLI opens its own window and has no documented public definition-return API. Its old offline-sentence scope paragraph is historical, superseded by this PRD.

## Fair options and ADR

| Option | Benefit | Cost/risk | Decision |
|---|---|---|---|
| Narrow native .NET plain JSONL lookup | In-app sourced results; `System.Text.Json`; no new package/runtime | One corpus/schema; own bounded load/index and meaning gate | Recommended for this milestone |
| Extract/embed GoldenDict-ng native lookup | Mature format/index capabilities; potentially in-app after integration | Native/Qt/interop maintenance, rendering/lifetime boundaries and GPL obligations; no public .NET definition API | Viable broad-format direction, disproportionate to this scope; reconsider only for an explicit format requirement |

GoldenDict companion-window launch fails the all-results-inside contract and is not a compliant shortcut. Neither engine choice supplies or validates dictionary data.

**ADR:** Use one approved plain-text JSONL corpus with a .NET reader; retain `TranslationService`/`ITranslationProvider` for explicit Ask AI. Add one dictionary capability port because it crosses external file I/O and enables recording/failure tests. No generic router, plugin registry, extra SDK, Windows project or framework. Preserve Core ← Application ← Infrastructure and WinUI composition as required by [AGENTS](../../../AGENTS.md) and [architecture](../../ARCHITECTURE.md).

## Behavior, trust boundaries and ownership

- **Lookup dictionary** snapshots the whole nonblank input and performs exact word/phrase lookup only. It returns sourced entries or an explicit miss; a sentence without an exact entry gets a miss, never implicit translation. No sentence token extraction, morphology, stemming or fuzzy expansion in this slice.
- Normalize entry headwords and queries identically: trim + NFC, then `.NET StringComparer.OrdinalIgnoreCase`; retain original display spelling. Rebuild the .NET index from original `headword` fields. The importer used Python casefold, which is not equivalent for every Unicode character; do not use its computed key/count as an asserted .NET contract. Audit actual corpus collisions/count and sample behavior with this rule, including accented/decomposed forms and non-equivalent casefold expansions.
- **Ask AI** sends only the current input snapshot as EN→VI through exactly one selected existing provider. Show “Sends this text to Gemini/DeepSeek.” Do not send dictionary files, stored entries or prior outputs. Unset/demo mode is unavailable AI or an explicitly labeled developer fixture, never real AI.
- Startup opens the main window and local status without keys. Missing/invalid selected AI settings disable only Ask AI and show safe feedback. Read only the selected key; dictionary missing/corrupt disables lookup with local setup feedback but does not prevent valid explicit Ask AI. No automatic provider retry or fallback.
- Two distinct sections retain input snapshot, dictionary source/article/origin and generated AI provider provenance, plus availability/error states. Failure in one section does not erase successful output in the other for the same input; input changes invalidate/clear both. Plain selectable wrapping text, scrolling and keyboard access remain inside FIH Lens.
- Keep one operation at a time and existing disabled editing while busy. Blank input/duplicate clicks/closed window cannot start work. Propagate cancellation and generation checks; programmatic input changes, cancel and close invalidate late completions even when a capability ignores cancellation. Dispatch UI updates on the UI thread; all supported failures restore command availability. App/window owns cancellation sources and existing HTTP lifetime; dispose streams on every load path.
- JSONL records retain `headword`, nonempty VI senses, optional POS/example, `sourceId`, original article ID and origin. Keep multiple senses/articles; reject conflicting duplicate article IDs. Manifest pins schema, EN→VI, source/revision, exact file/notice hashes, attribution and generated/reviewed status. Plain text only; no script/HTML execution.
- Configure a single absolute dictionary root through the existing proposed `FIHLENS_LOCAL_ASSET_ROOT` policy, default `%LOCALAPPDATA%\FihLens\local-assets`; only dictionary data/notices are required. Validate local fully qualified paths before I/O; reject drive-relative/root-relative paths, mapped network drives, UNC/device/remote URI roots and traversal, absolute manifest members or reparse-point escapes outside the root. Never scan/download model resources. Explicit setup imports only the selected approved corpus and contains no input text.
- Load off the UI thread into a bounded read-only index, with cancellation and complete validation before publishing it. Caps: 2,000 UTF-16 input characters for lookup, 500 MiB file, 1 MiB UTF-8 row, 1,000,000 rows; reject before unbounded allocation and report row/schema/hash/limit failures safely, without text/secrets in logs. No partially valid dictionary advertised as ready. Measure actual .NET warm lookup p95 ≤100 ms and record startup/load time and memory; app opens to responsive loading/setup state rather than synchronously reading the corpus.

## Ordered execution handoff

### R001 — Approve dictionary data and freeze the contract

Inspect WIP and baseline; reuse [importer](../../../experiments/local-translation/dictionary/import_thichhoc.py) only as an optional standalone tool and retain its exact hashes/notices. Review [100 actual results](../../../.omx/verification/local-dictionary-first/dictionary/converted/review-sample.md): 70 common, 15 polysemous, 15 phrases. Require ≥10,000 valid distinct EN headwords/phrases, ≥90/100 appropriate VI senses and zero critical misleading reviewed meanings. Record every miss, judgment and disagreement; 96 hits cannot pass this semantic gate. Confirm CC BY-SA notices/modified-data obligations and runtime schema; freeze boundary tests before new adapters. **Exit:** corpus human review, distribution obligations and .NET matching contract resolved. If meanings fail or reviewer unavailable, block dataset adoption honestly; fixtures are not delivery.

### R002 — Add bounded local dictionary capability

Affected resources: proposed `src/FihLens.Core/Models/DictionaryEntry.cs`, `src/FihLens.Application/Dictionary/IDictionaryLookup.cs` plus one small lookup use case, and `src/FihLens.Infrastructure/Dictionary/JsonlDictionaryLookup.cs`. Names are proposed, not existing code. Parse/validate with `System.Text.Json`, build the exact-headword index and preserve source data. Add focused tests under existing `tests/FihLens.Tests/Application/` and `Infrastructure/`. **Exit:** approved real corpus load, normalization/provenance/limits/cancellation tests, zero AI/HTTP local paths and measured warm lookup pass. Do not add a general file abstraction or inference service.

### R003 — Compose two commands and optional existing AI

Affected existing resources: `FihLens.WinUI/TranslationStartup.cs`, `App.xaml.cs`, `ViewModels/MainWindowViewModel.cs`, `MainWindow.xaml`, and close wiring in `MainWindow.xaml.cs` as needed. Compose dictionary independently of optional AI; replace Translate with Lookup dictionary and Ask AI, separate sections/status/labels, snapshot/generation safeguards and cancellation. Reuse selected AI adapters unchanged unless a demonstrated contract regression requires a narrow fix. Extend existing linked-source startup/VM tests. **Exit:** local startup and every local outcome make zero AI/HTTP calls; accepted Ask AI makes exactly one selected request; configuration, concurrency and stale-result tests pass.

### R004 — Verify real app and preserved regressions

Run [test specification](../../../.omx/plans/test-spec-local-dictionary-first.md) commands, full portable suite, affected library builds and x64 WinUI build; inspect warnings and references. Activate the actual current standard-user FIH Lens package with approved local data, without keys/network, and record dictionary UI, missing/corrupt setup, keyboard/cancel/close/resize/themes and provenance observations. Record HTTP-handler/provider zero-call proof separately from actual UI observations; no model firewall/admin trace prerequisite remains. Preserve old live Gemini/DeepSeek checks as deferred by the user; mocks establish transport/wiring, not live AI quality. **Exit:** new acceptance matrix passes, or precise data/manual/toolchain gaps remain marked pending; no source/state/goals/ledger or inactive assets are discarded.

## Risks, stop condition and review handoff

Risks: unreviewed generated meanings (R001 human gate); normalization mismatch (reindex original headwords and audit .NET equivalence); hostile/oversized/path-escaping data (R002 caps/hash/path checks); accidental egress/startup lockout or stale results (R003 recording fakes/handlers and generation tests); XAML/accessibility gaps (R004 real package build and manual evidence).

This planning branch stops after these documents and local links are checked and separate Architect then Critic review assesses the **new bytes**; do not label old approval as current approval. Implementation completes only when matrix criteria pass with approved real data and actual WinUI evidence. No paid/live AI call, cleanup of obsolete experiments, or paused-goal resumption is part of this plan-only update. Executor should read this PRD, companion matrix and context, preserve shared WIP, then follow R001→R004 within retained implementation authority and runtime ownership rules.
