# One-corpus dictionary experiment

Candidate: thichhoc English–Vietnamese v0.9.0, commit `ec3fa199aebdc4da4ebcaa7a11ac4b9c44046f75`.
**Generated Vietnamese senses; zero upstream human reviews. The pinned 100-query sample was reviewed by the user on 2026-10-08: 96 acceptable hits, 4 misses, zero critical misleading meanings reported. This is sample review, not full-corpus validation.**

Explicit setup from the repository root (Python standard library only; no package installation):

```powershell
powershell -NoProfile -File experiments/local-translation/dictionary/setup.ps1
```

This downloads a pinned source ZIP and attribution only into ignored `.omx/verification/local-dictionary-first/dictionary/assets/`, checks the inspected SHA256 values, runs parser self-checks and converts offline. Network setup never sends translation input. The app never calls this script. Cached matching assets allow the entire command to run without downloading. Use **Python 3.11+** for `hashlib.file_digest`.

`queries.json` freezes 70 common, 15 polysemous and 15 phrase queries. Its SHA256 is recorded before evaluating coverage. They are synthetic evaluation inputs, not captured user text.

Output JSONL contains one record per source article (several records can share a headword):

```json
{"headword":"test","senses":["thử nghiệm"],"partOfSpeech":"n","sourceId":"thichhoc-en-vi-v0.9.0","sourceArticleId":"en:test:n","sourceOrigin":"wordnet+llm"}
```

The example illustrates the schema; it is not an asserted source quotation. Conversion copies the actual `headword`, `senses_vi`, `pos`, `id` and `source` fields. It deliberately drops inflections, English glosses, IPA and generation metadata from the runtime schema; the complete original articles remain in the pinned archive. There is no morphology or rich article rendering. Dictionary strings remain plain text; no HTML/JS/program is executed. This is a fixed corpus converter, not a StarDict/MDX importer.

Artifacts in `converted/`:

- `dictionary.jsonl` and `dictionary-manifest.json`: original/converted hashes, exact 93 source-file hashes, original record count, unique normalized headwords, source-origin counts and corpus/license metadata.
- `LICENSE-DATA`, `ATTRIBUTION.txt`, `UPSTREAM-CODE-LICENSE`, `UPSTREAM-README.md`: unchanged source notices/quality disclosure.
- `review-sample.md` and `review-sample.json`: all 100 frozen queries, actual Vietnamese senses and source article identifiers, with human verdicts initially pending after conversion; the retained reviewed sample and report record the later user review.

Data conversion is a modification. The derivative data remains **CC BY-SA 4.0**; preserve full upstream notices and attribution, indicate schema conversion, provide readable in-app attribution if adopted, and distribute the modified data under the same license without added restrictions. The upstream code MIT license does not replace the data license. See the exact retained notices for underlying WordNet/CMUdict/Wiktionary obligations.

OVDP's official archive endpoint returned non-ZIP HTML during this bounded selection. Its SourceForge GPLv2 metadata alone could not prove archive terms, so it was not adopted. Skypedia remains excluded due unresolved mixed provenance. thichhoc has inspectable pinned source and explicit data notices, but that is not semantic quality approval.

The retained human report passes the sample acceptance gate for these exact corpus bytes. Python lookup timing is an experiment measurement only, not .NET, UI or packaged-app proof. The setup/conversion command does not install app assets.

## Explicit installation of reviewed app data

`setup.ps1` is the optional download/conversion experiment. Its newly converted output has pending human verdicts; rerunning it does not recreate approval and can replace reviewed experiment output. Keep reviewed artifacts separately before rerunning setup. Python 3.11+ is needed only by that converter, never by the application or installer.

`install.ps1` performs a separate, explicit local installation. It downloads nothing and installs no programs, packages, model, Python runtime, registration, or environment changes. By default it reads the retained reviewed output at `.omx/verification/local-dictionary-first/dictionary/converted` and copies exactly seven files to `%LOCALAPPDATA%\FihLens\local-assets`: the pinned JSONL corpus, four readable upstream notices, `human-review-20261008.json`, and `dictionary-manifest.json`.

```powershell
# Install already-reviewed data to the standard user asset root.
powershell -NoProfile -File experiments/local-translation/dictionary/install.ps1

# Or select explicit fully qualified local roots.
powershell -NoProfile -File experiments/local-translation/dictionary/install.ps1 -SourceRoot 'D:\reviewed-dictionary' -AssetRoot 'D:\FihLens-assets'

# When using a custom target, set the existing app policy for this shell and its child processes.
$env:FIHLENS_LOCAL_ASSET_ROOT = 'D:\FihLens-assets'
```

The installer validates source/revision/schema, corpus and retained notice size/SHA256, the exact human report tied to the corpus and frozen query sample, and the complete manifest hash bound by that report before creating target data. Manifest file names are a fixed whitelist; sample evaluation inputs and executable files are never copied. Roots must be fully qualified local drive paths. UNC/device/URI paths, mapped network drives, traversal, and reparse points in selected paths are rejected.

Identical installed files make repeat installation safe. Differing existing assets produce an error before copying; inspect and explicitly move them elsewhere before retrying. Unrelated target files remain untouched. Data and notices are copied and checked before publishing the manifest. No recursive deletion is performed; an interrupted first install can leave partial data without a ready manifest and can be retried after inspecting conflicting files.

Readable retained attribution identifies the converted CC BY-SA 4.0 data and upstream sources. The report covers only the frozen 100-query sample; the remaining generated senses have not received full-corpus human validation. Installer checks use explicit workspace targets; default user-root installation and packaged-app behavior require their own verification.
