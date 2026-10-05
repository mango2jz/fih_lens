# FIH Lens V2 — Document Reading and Language Learning Architecture

> Proposed architecture for the next version. This extends [ARCHITECTURE.md](ARCHITECTURE.md); it is a design proposal, not a description of implemented features.

## 1. Recommendation and assumptions

Build V2 as a **local-first Windows desktop application** using the existing Clean Architecture boundaries. Add document reading, annotations, and learning features within those projects. Keep translation and system-wide text capture usable independently of the document reader.

The central workflow becomes:

```text
Import document → Read → Select text → Translate and analyze
                      │                         │
                      ▼                         ▼
                Highlight / note        Review vocabulary candidates
                                                │
                                                ▼
                                        Save learning notes
                                                │
                                    ┌───────────┴───────────┐
                                    ▼                       ▼
                                Anki export           Local review
                                                     (later milestone)
```

Assumptions for this proposal:

- Continue with C#, .NET, WinUI, and a single desktop executable.
- “Upload” initially means importing a file into the user's local library. It does not mean transmitting the document to a server.
- Imported originals are copied into app-managed storage and remain unchanged. Highlights and notes are stored separately.
- Start with UTF-8 plain text for a complete reading-to-learning workflow. Make PDF support the next format milestone because viewing a PDF and reliably selecting its text are separate problems.
- Anki text export is the first learning integration. A built-in review system can use the same saved learning content later.
- Reading, annotation, and saved learning content work offline. Translation works offline only when a suitable local provider is actually configured.

If remote upload, shared libraries, browser access, or mobile access becomes a requirement, revisit deployment separately. A backend is unnecessary for the local workflow.

## 2. Keep the dependency structure

Project-reference arrows point toward dependencies:

```text
FihLens.Application    → FihLens.Core
FihLens.Infrastructure → FihLens.Application + FihLens.Core
FihLens.Windows        → FihLens.Application + FihLens.Core
FihLens.WinUI          → FihLens.Application
                      + FihLens.Infrastructure
                      + FihLens.Windows
```

WinUI remains the composition root: it constructs or registers the concrete adapters. Application calls those adapters through Application-owned ports, without referencing their projects.

Organize features in folders inside the existing project structure. These are logical modules, not separate executables or a new project per feature:

| Module | Responsibility | Main connections |
|---|---|---|
| Documents | Import, library metadata, immutable revisions, reading position, extracted text. | Reader, annotations, translation. |
| Annotations | Highlights, document notes, selection notes, source navigation. | Document locations. |
| Translation | Existing translation and structured language analysis. | Reader selections and external capture. |
| Learning | User-approved learning notes, decks, export, optional review. | Vocabulary candidates and source references. |

Avoid one large `DocumentService` that imports files, renders pages, calls AI, writes SQL, and schedules reviews. Each use case should coordinate only the operations needed for its user action.

## 3. Responsibilities by project

| Project | V2 additions | Keep out |
|---|---|---|
| `FihLens.Core` | `Document`, `DocumentRevision`, `SourceReference`, `Annotation`, `LearningNote`, `Deck`; later `ReviewCard`, `ReviewState`, `ReviewRecord` and scheduling rules. | SQL, file handles, PDF SDK objects, XAML, provider JSON. |
| `FihLens.Application` | Import, save annotation, translate passage, save learning note, export deck; later load due cards and submit review. Ports for storage, extraction, analysis, and export as needed. | Native pickers, renderer controls, HTTP transport, database transactions implemented as SQL. |
| `FihLens.Infrastructure` | Managed file storage, SQLite, provider adapters, format parsing that is platform-neutral, Anki text serialization. | WinUI controls and Windows-only APIs. |
| `FihLens.Windows` | Native file pickers, WinRT PDF/OCR adapters if chosen, existing hotkeys, clipboard, capture, DPI integration. | Deck ownership and review scheduling policy. |
| `FihLens.WinUI` | Library, reader, annotation panel, translation panel, deck editor, export UI; later review UI. | Direct SQL, provider SDK calls from ViewModels, scheduling rules. |

The reader control and highlight overlay belong in WinUI. A Windows rendering adapter belongs in Windows when it uses Windows-specific APIs. A cross-platform parsing adapter belongs in Infrastructure. Do not make Windows reference Infrastructure to share an implementation; compose their Application ports at startup.

Useful Application ports might include `IDocumentRepository`, `IDocumentContentStore`, `IDocumentTextExtractor`, `IAnnotationRepository`, and `ILearningRepository`. These protect concrete storage or technology boundaries. Introduce them when their first use case establishes an actual contract; do not generate empty interfaces or an interface for every service.

If structured vocabulary analysis already comes from `ITranslationProvider`, reuse that contract where appropriate. Introduce a separate `ITextAnalysisProvider` only if analysis is independently invoked or implemented. Do not perform two provider calls for information already returned by one.

The repository currently contains Core types, a WinUI shell, and a test project. Application, Infrastructure, and Windows are planned boundaries in the original architecture, not completed implementations to assume are available. Existing `VocabularyItem` and `GrammarPoint` are starting points; extend their semantics deliberately as the current project develops.

## 4. Document ownership and import

Distinguish a logical document from its contents:

| Concept | Meaning |
|---|---|
| `Document` | A library entry with a stable ID, title, format, and import date. |
| `DocumentRevision` | One immutable version of its source bytes, identified by revision ID and content hash. |
| Extracted text | A derived representation used for selection, search, analysis, and source locations. |
| Reading position | Where the user stopped, tied to a revision and reader location. |

Start with one revision per imported document. Revisions matter because annotations on the old contents must not silently move when a file is replaced. Add replacement/import-as-new-revision UI only when needed.

Proposed import flow:

1. WinUI receives a file choice from the Windows picker and calls the import use case.
2. Validate the supported format, file signature where applicable, size limits, and readability. A filename extension alone is insufficient.
3. Copy to a staging file under an app-generated name, calculate a content hash, and check for a duplicate.
4. Commit the managed original and metadata using a recoverable import protocol.
5. Extract text on demand or as cancellable background work. A stored document can remain readable while extraction is pending or fails.
6. Return the library entry, with explicit capabilities and any processing status.

SQLite and the filesystem cannot share one ordinary database transaction. Use a small import state such as `Pending → Ready`, staging names, and startup reconciliation for interrupted copies or orphan files. A document becomes `Ready` only after its managed original is present; text extraction has its own status. No workflow engine is required.

Keep user filenames as display metadata, never as unrestricted storage paths. A duplicate should prompt an explicit reuse/import choice rather than silently overwriting existing notes. Import failure must leave the user's original untouched.

Deletion should show what will be removed. Keep learning notes by default, retaining copied source context and showing that the source document is unavailable. Remove the document and its annotations only after confirmation; an explicit “also delete learning content” option is a separate destructive choice.

## 5. Viewing and extracting text are different capabilities

A renderer answers: **What should the page look like?** An extractor answers: **What text and source locations does this page contain?**

They may use one engine, but they are separate contracts. A page image or browser PDF viewer does not by itself provide the selectable, addressable text needed for persistent notes.

| Format | Reading approach | Source-location concern | Proposed stage |
|---|---|---|---|
| UTF-8 text | Native selectable text reader. | Text offsets and unchanged extracted-text version. | First complete slice. |
| PDF with text | Page renderer plus verified text extraction and selection mapping. | Page reading order, glyph geometry, crop/rotation coordinates. | Next milestone after experiment. |
| Scanned PDF | Page renderer plus on-demand OCR. | OCR errors, page regions, extraction version. | After text PDF support. |
| EPUB | Structured chapter reader. | Chapter identity and stable text locations through reflow. | Later, if required. |
| DOCX | Explicit read-only conversion/extraction strategy. | Converted layout differs from the original. | Later, if required. |

The built-in Windows PDF APIs offer document/page loading and page rendering. Their documented surface does not provide the rich text-selection mapping required here. Treat them as a rendering candidate, not a complete PDF reader solution. Verify selection/extraction separately before choosing an engine; see the [Windows PDF reference](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf?view=winrt-26100).

Run a small Windows PDF experiment before production implementation. It must demonstrate page display, Unicode extraction, multi-line selection, highlight restoration after reopening, zoom/rotation mapping, cancellation, and cleanup. Use representative Latin, Vietnamese, Japanese, multi-column, and scanned samples.

Evaluate licensing, redistribution, packaging, maintenance, accessibility, and extraction quality before choosing an additional engine. Do not build a PDF parser for learning value; learning Windows page rendering and coordinate mapping is valuable, recreating a document-format engine is a different project.

Expose capabilities such as `CanSelectText`, `CanSearch`, and `RequiresOcr` so the UI can explain limitations. A view-only PDF is an intermediate milestone, not completion of document translation and annotation support.

## 6. Stable locations for notes and highlights

The most consequential new model is a **source reference**: enough information to find the original passage again. Screen coordinates are temporary presentation state and cannot serve as the persisted location.

For a text selection, store:

- Document ID and revision ID.
- Extracted-text version and block identity, with a defined text range.
- Exact selected text plus short surrounding context.
- A format-specific locator, such as a PDF page and page-space rectangles.

For .NET text, a reasonable initial range contract is a zero-based UTF-16 start offset and length into the stored, versioned text. Keep the range within that text and avoid splitting a surrogate pair or user-perceived character. Do not normalize text and then apply the resulting offsets to the unnormalized original.

For PDF rectangles, define page index, coordinate origin, units, and crop/rotation handling. The renderer converts page-space coordinates into the current viewport; persisted rectangles must survive zoom and DPI changes. A selection can contain multiple ranges/rectangles across lines or pages.

Annotations can be document-wide, page-specific, or attached to a selection. Free notes should not require fake text offsets. A highlight requires a source location; a learning note may originate from external capture and therefore has no library-document location.

Resolve against the exact revision/extraction version first. If text changes, matching a quote and surrounding context is only a recovery heuristic. Ambiguous or missing matches become unresolved annotations requiring user reattachment. Never silently attach a note to a different repeated phrase.

Store annotations in the database, separately from the original file. Embedding annotations into exported PDFs is an optional later feature with its own format adapter.

## 7. Preserve one translation pipeline

This section assumes V1's translation and capture foundation has been completed in milestone 0 below. The named services and capture pipeline are planned V1 components, not features already implemented in the current repository.

```text
External text: hotkey → existing capture → passage text ──────┐
                                                            ▼
Document text: reader selection → text + source → TranslationService
                                                            │
                                                            ▼
                                                 ITranslationProvider
                                                            │
                                                            ▼
                                            Translation + vocabulary + grammar
```

The reader already has selected text, so it must not invoke system-wide UI Automation or clipboard capture to retrieve it again. Both entry points reuse the same translation and analysis behavior.

Keep document provenance alongside the request/result; do not force a document ID into every translation request. External capture still works without a library. If context improves analysis, send the selected passage and bounded nearby context, preserving which text is actually selected.

“Word breaking” needs language-aware semantics. For example, splitting on spaces does not produce useful Japanese vocabulary. Distinguish surface form, dictionary form, reading, part of speech, contextual meaning, sentence, and phrase boundaries. Reuse structured provider results initially; add a dedicated tokenizer/dictionary adapter only after measured quality or latency needs justify it.

AI-derived vocabulary and grammar are suggestions. Validate external response shape, bounds, language, and any returned offsets. If a provider returns only a word string, do not invent a precise source range by taking the first occurrence. Let the user edit and choose candidates before saving; avoid automatically turning every token into a card.

Cancellation is necessary but insufficient to prevent stale UI. Tag each selection request with an identity, cancel superseded work, and apply a result only if the active document, revision, and selection still match. An earlier response must not replace the latest passage's result.

Persist selected translations when useful; do not automatically translate or transmit a whole imported book. On a network failure, reading and annotation continue, and the selected passage can be retried explicitly.

## 8. Shared learning content, two delivery options

Separate **learning content** from **review scheduling**:

| Model | Responsibility |
|---|---|
| `LearningNote` | Stable ID, language, word/phrase, reading, meaning, example, translation, tags, optional source reference and copied source context. |
| `Deck` | Named collection. Initially each learning note belongs to one deck. |
| `ReviewCard` | One prompt generated from a note, such as word → meaning. Needed for local review. |
| `ReviewState` | Current due time, stage, and scheduling parameters for one card. Needed for local review. |
| `ReviewRecord` | One completed review, including card, time, rating, and scheduling version. Needed for local review. |

A note contains information; a card asks a question about that information. One note can eventually generate both recognition and production cards, with different schedules. Start with one recognition template and add another only when requested.

Use the existing `VocabularyItem` as the analysis candidate, rather than turning it into a database record and scheduler object. A saved learning note gains stable identity, deck membership, user edits, and provenance. Initially, it retains one primary source reference and copied context. Additional occurrences remain separate selection annotations, each with its own source location and an optional link to the same learning note; they do not overwrite its primary source. Identical spelling in different languages or senses must remain possible; deduplication should suggest, not erase.

### Option A: Anki export first — recommended

Export user-approved notes as UTF-8 tab-separated text using a documented field mapping. This gives the app a useful learning outcome before it owns a spaced-repetition scheduler.

Anki supports text import with configurable fields and separators. Its normal duplicate handling uses the first field for matching within the applicable note type/import settings. Put a stable FIH Lens note ID first, then word, reading, meaning, example, translation, and source label. Provide instructions for a dedicated note type whose templates hide the ID and show the learning fields. See [Anki text import](https://docs.ankiweb.net/importing/text-files.html).

Keep IDs stable on repeated exports so reimport can update an existing note under the documented settings. Test quoting, embedded tabs/newlines, Unicode, HTML settings, duplicate/update behavior, and template mapping in Anki. This is content export, not bidirectional synchronization or transfer of FIH Lens review history.

Text export is an Anki-importable file, not a packaged `.apkg` deck. If one-click deck-package import is required, implement `.apkg` as a separate adapter after verifying the package contract and library licensing. Media packaging and deck templates become part of that scope. See [Anki packaged decks](https://docs.ankiweb.net/importing/packaged-decks.html).

Write exports to a temporary file and replace the destination only after successful completion. Cancellation or serialization failure must not damage an existing export.

### Option B: In-app review — subsequent milestone

The same saved learning notes feed a local review screen. Application loads due cards and coordinates a review submission. Core calculates the next state using a pure function:

```text
Previous state + user rating + review time + scheduler parameters
                              ↓
                       New review state
```

Use .NET's [TimeProvider](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider?view=net-10.0) at the application boundary or pass a timestamp into the pure function so tests can control time. Store instants in UTC, and define a separate local-day policy for daily limits. Define rating meanings, learning steps, lapse behavior, suspended cards, and session behavior before implementation.

Commit the review record and updated state in one SQLite transaction. Give a submission a stable ID so a double click or retry cannot advance the schedule twice. Keep the review history distinct from diagnostic logs; it can support investigating or changing scheduling behavior without adopting event sourcing.

Evaluate an established spaced-repetition algorithm when this milestone starts. A fixed-delay reminder can be an experiment, but should not be described as a validated learning scheduler. Record the scheduler version with its state/history and define migration behavior if algorithms change.

For the first local version, FIH Lens owns its review state; Anki independently owns its review state. No promise of schedule interoperability follows from exporting the same note fields.

## 9. Persistence, recovery, and privacy

Use one local SQLite database for metadata and one managed file directory for originals and derived assets:

```text
AppData/
├── lens.db
├── documents/<revision-id>/original.<format>
├── cache/                 # Regenerable thumbnails / rendered pages
└── staging/               # Interrupted import/export recovery
```

The path is conceptual; choose the correct writable app-data location for the Windows packaging model. Never store user data in the installed application directory.

Potential tables, introduced only as milestones need them:

| Table | Key purpose |
|---|---|
| `Documents`, `DocumentRevisions` | Library identity and managed source metadata. |
| `DocumentTextBlocks` | Versioned extracted text and locations; OCR corrections invalidate affected mappings. |
| `Annotations`, `ReadingPositions` | User notes, highlights, and navigation state. |
| `TranslationHistory` | V1 selected-passage history established in milestone 0, extended with optional source linkage. |
| `Decks`, `LearningNotes` | Saved, editable learning content and source snapshots. |
| `ReviewCards`, `ReviewStates`, `ReviewRecords` | Local review only, when implemented. |

Enable and verify foreign-key enforcement on each database connection before beginning a transaction, and choose delete behavior deliberately; SQLite documents that enforcement must be enabled rather than assumed. See [SQLite foreign keys](https://www.sqlite.org/foreignkeys.html). Use migrations, parameterized SQL, short transactions, and indexes for measured lookup paths, initially document annotations and due cards.

An annotation is user data, not a cache. An extraction referenced by text offsets must remain available or be migrated explicitly; do not treat it as freely disposable just because it was generated. Thumbnails and unreferenced page images can be regenerated.

Back up the database together with required document files and a manifest. Serialize backup against import, deletion, and annotation/learning writes so the database snapshot and file set refer to the same library state. Use a supported consistent database backup method; copying only a live database file is insufficient, particularly in WAL mode. See [SQLite online backup](https://www.sqlite.org/backup.html). Restore should validate the manifest and offer recovery for missing documents.

Validate imported files and apply format/size/page limits. Treat document contents and provider results as untrusted data: bound extraction work, escape rendered content, disable imported scripts/active content and automatic remote-resource loading, and validate any bridge messages if a web-based reader is chosen. Separate AI instructions from quoted document text; document content must not be allowed to invoke app actions.

Make cloud processing visible in settings and at the point of use. Send only the text needed for the requested operation. Keep secrets in appropriate Windows-protected storage; omit document bodies and credentials from normal logs. Local-first describes where data lives, not an encryption guarantee.

## 10. UI, lifecycle, and performance

Proposed screens:

- **Library:** import, document list, processing status, open, remove.
- **Reader:** document content, highlight overlay, saved reading position, notes panel.
- **Translation panel:** selected text, translation, grammar, editable vocabulary candidates.
- **Learning library:** decks, saved notes, editing, export.
- **Review:** due-card session, answer reveal, rating; introduced with local scheduling.

The V1 hotkey popup, completed in milestone 0, remains an independent entry point to translation and learning-note creation. It should not require opening the library window.

| Resource | Owner and lifetime |
|---|---|
| Reader session / document stream / native page handles | Reader session; close and dispose when the document closes. |
| Selection translation cancellation source | Active selection controller/ViewModel; cancel on replacement, dispose when outstanding work has finished. |
| Import/extraction operation | Application operation; cancel cooperatively and leave recoverable staging state. |
| Database connection/transaction | Persistence operation; keep short and dispose after completion. |
| Hotkey registration | Existing application lifetime; unregister on shutdown. |
| Annotation edit buffer | Reader editor; remain dirty until persistence acknowledges success. |

Debounce note saves if useful, but show save failure and preserve the buffer. Before close/navigation, flush pending edits or offer an explicit retry/discard decision; do not silently lose unsaved notes. Cancellation of translation must not discard annotation edits.

Keep CPU-heavy extraction off the UI thread, while respecting native thread-affinity requirements. Virtualize long text/page lists, render visible pages plus a small nearby cache, and limit concurrent OCR/extraction work. Use on-demand OCR before a whole-library processing queue.

Measure startup, idle CPU/memory, time to first page, selection-to-result latency, save latency, and large-document memory. Avoid eager full-library processing and large permanent page-image caches. Define numerical budgets using actual target hardware and representative files before optimization.

Preserve keyboard selection/navigation, readable zoom, screen-reader access, and focus behavior. A rasterized page alone does not provide accessible document text.

## 11. Delivery sequence and acceptance criteria

| Milestone | Deliverable | Evidence required before moving on |
|---|---|---|
| 0. Finish V1 foundation | Translation contract, provider integration, capture, popup, settings, and applicable history behavior. | Existing translation works independently of document reading. |
| 1. Local text reading | Import UTF-8 text, reopen library, restore position, save document/selection notes. | Restart restores notes/highlights; source-file changes do not alter the managed copy; interrupted import and failed save are recoverable. |
| 2. Passage learning | Reader selection enters existing translation; candidates can be edited and saved to decks. | Latest selection wins; repeated text locations stay distinct; offline annotation still works. |
| 3. Anki integration | Stable-ID TSV export and import guide/template mapping. | Import into Anki produces the expected cards; reimport updates notes without unwanted duplicates. |
| 4. PDF reading and annotation | Verified renderer/extractor, durable PDF locations; add OCR after text PDFs. | Reopened highlights align after zoom/rotation/DPI changes; scanned pages show OCR limitations; native resources close correctly. |
| 5. Optional local review | Defined scheduler, due queue, transactional ratings, review history. | Deterministic schedule tests, restart continuity, time-zone/day policy checks, and duplicate-submission protection. |

If PDF is the primary user need, run its feasibility experiment before milestone 1 and bring PDF implementation forward. Keep the small text reader as a way to validate the end-to-end workflow without PDF-engine complexity. Local review can move ahead of PDF if integrated learning is the higher priority.

Unit-test pure rules and Application orchestration: source-range validation, candidate-to-note mapping, stale-result rejection, import recovery decisions, deletion policy, and scheduling. Use real SQLite integration tests for transaction rollback, foreign keys, migrations, and duplicate review submissions. Use Windows/manual tests for pickers, native renderer/OCR lifecycle, highlighting geometry, accessibility, and actual Anki import.

No new dependencies are selected by this document. Use existing project patterns and .NET/Windows capabilities first; add a parser or scheduling dependency only after its milestone's requirements and tradeoffs have been checked.

## 12. Deferred decisions and learning checkpoints

Confirm these before implementing the affected feature, rather than blocking the entire architecture proposal:

- Which formats matter first, especially PDF versus EPUB, and how common are scanned documents?
- Does “upload” remain local import, or must files be hosted and available across devices?
- Is an Anki-importable text file sufficient, or is a packaged `.apkg` deck required immediately?
- Is local review a product requirement for the first V2 release, or an optional later track?
- Are document notes enough, or is rich text, PDF annotation export, audio, or cloze learning essential?

Defer accounts, cloud sync, collaboration, a custom document editor, whole-document AI processing, automatic card generation for every word, and Anki schedule synchronization until there is a concrete requirement. Preserve stable IDs and explicit source ownership now; a generic sync framework is unnecessary.

The key engineering concepts to practice are immutable source identity, text/geometry mapping, dependency inversion, file/database consistency, cancellation versus stale-result prevention, durable user edits, and the distinction between learning content and scheduling state.

The recommended first target is deliberately concrete: **import one text document, annotate a passage, translate it through the existing pipeline, save an editable learning note, and export it into Anki.** That proves the V2 workflow before adding the two largest technical commitments: a full PDF reader and a spaced-repetition system.
