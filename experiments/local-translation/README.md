# Dictionary tooling and retired model experiment

The active plan uses local word/phrase dictionary lookup and explicit **Ask AI**
through the selected Gemini/DeepSeek API for full sentences. See the
[revised PRD](../../.omx/plans/prd-local-dictionary-first.md).

Use the optional [dictionary converter](dictionary/README.md) for the pinned
EN→VI corpus. It runs only during explicit data setup; Python is not an app
runtime dependency. The user reviewed the frozen 100-query sample: 96 acceptable hits, four misses, and zero critical misleading meanings reported. Remaining generated senses are not fully human-reviewed.

The obsolete local-model setup, inference worker, benchmarks, locks and standalone
WinUI host source were removed after the user's code cleanup request on 2026-10-07.
Exact original source bytes and notices are recoverable from the
[verified archive](../../.omx/verification/local-dictionary-first/obsolete-code-cleanup/retired-model-source.zip)
and [SHA256 inventory](../../.omx/verification/local-dictionary-first/obsolete-code-cleanup/retired-source-inventory.json).
The cleanup audit and checks are recorded beside those files.

Historical verification/model assets and the already registered experiment's
ignored `bin/obj` output remain untouched. No production project references that
experiment, and this cleanup does not uninstall or change its deployed layout.
The external `D:\.omx\verification\local-dictionary-first\model` folder remains
untouched as requested. No model setup or inference command is part of current
product setup.
