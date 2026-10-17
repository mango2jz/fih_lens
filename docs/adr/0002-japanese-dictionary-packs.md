# 0002 — Curated Japanese dictionary packs and restart activation

Date: 2026-10-16. Status: implemented and verified by automated tests; downloads disabled by user decision.

## Decision

Use the legacy English JMdict distribution for Japanese→English exact whole-headword/kana lookup. Preserve entry identity, written/reading/sense restrictions, per-form orthography and reading labels, per-gloss type, POS inheritance, loanword-source text/metadata, source antonym references and notices; no deinflection, sentence tokenization, reverse direction or inferred synonyms. Existing EN→VI source pinning and `FIHLENS_LOCAL_ASSET_ROOT` remain intact.

Store structured UTF-8 JSONL plus a prebuilt sorted byte-key offset index. Build offline with standard Python libraries; the app uses .NET files/JSON only and has no Python or SQLite runtime dependency. Temporary index/key buffers speed full validation; published readers retain read-only handles and fetch only matching ranges with explicit offsets. Concurrent lookups use independent buffers and cancellation; disposal rejects new leases and drains existing work.

Application selects only a provider registered for the requested pair. Core retains source relationships. Infrastructure owns catalog hashes, files, HTTP, installation and registry. App owns shared readers and publishes availability on the UI dispatcher. Main/popup render sourced dictionary cards separately from generated vocabulary.

Install only explicitly selected curated artifacts. Validate bounded archive/member bytes, hashes, schema, paths and index correspondence before committing a pending version. Immutable version directories and an atomic identifier-only registry preserve old active data through failure. Startup opens retained handles under the mutation lock, validates outside it, and rechecks registry generation before promotion. Failed pending updates retain a visible retry state and the authenticated active version. Explicit reinstall can quarantine a corrupt registry after candidate validation; it never infers an active version from folders. Startup cleans owned abandoned staging and superseded supported versions while preserving active/pending/rollback/failed and unknown data. Activation/update/removal takes effect after restart; busy readers defer cleanup. Keep supported released version pins through skipped app updates.

## Alternatives and tradeoffs

The existing eager EN→VI loader cannot be repurposed without losing source pinning or retaining the entire Japanese corpus. SQLite provides mature indexing and may reduce custom-format maintenance, but adds an unapproved package/native bundle and different cancellation/handle semantics. The selected file format avoids that dependency at the cost of explicit validation and corruption tests. A measured resource failure reopens this decision.

Restart activation adds one user step and avoids hot provider replacement. Dictionary setup is skippable; missing packs, unsupported pairs and local misses never invoke AI or download. Windows speech remains an explicit action and missing voices are nonblocking.

## Evidence and open gates

The 2026-10-16 official source contains 218,920 converted entries and 499,479 alias/entry rows. The schema-3 candidate `2026-10-16-s3-992be02f3a404e9cd761bdbb` is 19,182,109 ZIP bytes and 146,083,524 installed bytes including manifest/notices. Version identity binds the source, converter, schema and licence text. Two fresh conversions produced identical member and ZIP hashes. Upstream SHA-256: `3846524394e7b149f88f1cf09d9d6b10aa0b0e05267cd180bfb5eea31873952b`; ZIP: `d0bb8b6b589b32a48710d6854d8fb494683d669b24cb14bb596b943e5cda0ee0`; manifest: `c8dcb6726d636fccb015215f25e0be09ea4c8c2860c42a38300808ccf059d156`.

Five fresh x64 .NET 10 processes on Windows machine `TOMATO2JZ` measured validation at 9.90–11.83 s, first exact hit after availability at 16.96–24.74 ms, and warm p95 at 1.04–1.54 ms over 100 fixed hits per process. The OS high-water counter recorded peak incremental process working set of 60,964,864 bytes; after diagnostic full GC, idle working set increment was 18,198,528–22,753,280 bytes and managed increment 166,512–181,984 bytes. This demonstrates bounded retained data; OS caches were not flushed, and diagnostic GC is not normal app-idle acceptance. Two real processes starting against the same pending full pack both became available (11.09 s and 22.27 s), exercising generation retry without lock timeout. Python 3.14.7 conversions took 19.13–21.57 s with sampled peak process working set below 194 MB.

An independent agent compared all 30 frozen queries and returned relations with the exact upstream XML: 30 passed, including two expected misses. It also independently reconstructed all 218,920 converted entries and confirmed whole-payload equality, including 6,339 loanword sources (1,991 empty) and 1,122 antonym references. The user accepted this source review as satisfying the semantic gate. It does not claim bilingual human approval.

Release x64 publishing with ReadyToRun and trimming produced 245 files totaling 208,571,866 bytes, versus 208,167,750 bytes from isolated baseline commit `f493a63635f36763d435c3eaf7c1f0b4469c5778` (+404,116 bytes). Neither published inventory includes dictionary corpus files. The full 522-test suite and x64 WinUI build pass with zero warnings.

The user directed downloads to remain disabled and verification to use tests with no Computer Use. The candidate therefore retains **no download URL**. Its frozen manifest still says `unreviewed`; subsequent user acceptance of the agent source review is recorded separately without altering authenticated bytes. Normal app memory/startup interaction, real UI/DPI/accessibility/offline lifecycle and peak-install/low-space checks remain unverified. A maintainer-owned host and regular refresh process are prerequisites for any later authorized distribution. No paid provider or public release was invoked. Reproduction, fresh test/build results and local evidence paths are recorded in [the implementation handoff](../handoffs/2026-10-16-japanese-dictionary.md).

Data terms and source format: [EDRDG licence](https://www.edrdg.org/edrdg/licence.html), [source archive](https://www.edrdg.org/pub/Nihongo/JMdict_e.gz), [JMdict semantics](https://www.edrdg.org/jmdict/jmdict_dtd_h.html). Converted data retains CC BY-SA 4.0 attribution, publisher notice and change notices independently of application-code licensing. Build instructions: [dictionary experiment](../../experiments/japanese-dictionary/README.md).
