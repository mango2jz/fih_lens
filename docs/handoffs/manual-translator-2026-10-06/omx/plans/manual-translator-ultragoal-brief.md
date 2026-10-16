# Implement approved manual translator Phase0–2

User explicitly authorized implementation via $oh-my-codex:ultragoal. Approved PRD: .omx/plans/prd-manual-translator-phase-0-2.md; test spec: .omx/plans/test-spec-manual-translator-phase-0-2.md; source-bound consensus .omx/plans/manual-translator-phase-0-2-consensus.json; new explicit execution handoff .omx/plans/manual-translator-execution-handoff.json.

Implement Application/Infrastructure foundation, one MVVM WinUI screen and BOTH Gemini/DeepSeek REST adapters, startup demo|gemini|deepseek, strict parser, cancellation/close/resource ownership, safe config/error presentation. Preserve existing Core, original guides and developer .gitignore edit. Only CommunityToolkit.Mvvm8.4.2 allowed new package. No SDKs, retries, router/fallback, streaming, persistence or later phases. Skip temporary Phase0 UI. Revalidate official contracts before adapters.

Use sequential portable implementation checkpoints, then independent cleaner/code-reviewer+architect and invariant audit. External Windows and both live provider gates remain explicit separate stories; lack Windows/credentials does not prevent portable implementation and review. Full milestone stays incomplete until platform/live gates pass. Native aggregate goal must remain active while those gates remain. No commit/deploy.
