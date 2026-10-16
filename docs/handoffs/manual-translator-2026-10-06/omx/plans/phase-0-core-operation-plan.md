# Phase 0 — Core operation plan

**Status: DRAFT — awaiting developer approval. No implementation is authorized by this document alone.**

Prepared 2026-10-04. Duplicated from [the original Core plan](../../docs/plan/phase-0-core-implementation-plan.md) before refinement. The original text is retained verbatim in the appendix. This operational section governs execution of the copy; the appendix remains teaching/reference material, including its historical environment notes.

## 1. Target result and execution boundary

Produce the complete Phase 0 domain model and meaningful Core tests, then hand off once to [the Application operation plan](phase-0-application-operation-plan.md). The combined milestone is the fixed `Hello world` translation flow through one service and one fake provider, displayed in the existing Windows window. Core completion alone does not complete Phase 0.

Only planning is allowed before approval. After explicit approval to begin implementation, the agent implements the approved steps in small reviewable batches, explains significant choices and records verification. Preserve developer work already staged or unstaged; re-read affected files before each batch. Approval does not authorize new packages, later-phase features, SDK installation, or broad refactoring.

Retain the learning goals through short explanations before each meaningful batch and reasoning checkpoints afterward. The teaching appendix supplies the longer concept introductions. Do not pause after every routine step for another permission request once implementation is approved.

## 2. Current evidence and corrections to the source plan

| Evidence | Operational consequence |
|---|---|
| `dotnet --list-sdks` on 2026-10-04 reports 8.0.417 and 10.0.401. | Recheck selected SDK before implementation; the original SDK 8-only blocker is historical. Keep `net10.0`. |
| [Core project](../../src/FihLens.Core/FihLens.Core.csproj), line 4; [test project](../../tests/FihLens.Tests/FihLens.Tests.csproj), lines 4, 11–22. | Reuse .NET 10 and existing xUnit dependencies; tests currently reference Core only. |
| [Language](../../src/FihLens.Core/Enums/Language.cs), lines 1–8; [JLPT](../../src/FihLens.Core/Enums/JlptLevel.cs), lines 1–11. | `Vietnamese` and these namespaces are already corrected. Preserve completed work. |
| [CEFR](../../src/FihLens.Core/Enums/CefrLevel.cs), lines 5–16. | File is in `Enums`, namespace remains `ValueObjects`; align it and update affected imports only. |
| [VocabularyItem](../../src/FihLens.Core/Entities/VocabularyItem.cs), lines 3–10; [GrammarPoint](../../src/FihLens.Core/Entities/GrammarPoint.cs), lines 3–8. | Vocabulary is already a record but has mutable properties and overlapping positional/explicit members. Refine existing types rather than adding duplicates. |
| [UnitTest1](../../tests/FihLens.Tests/UnitTest1.cs), lines 3–10. | Empty test proves no behavior; replace it with focused domain tests. |
| [Solution](../../FihLens.slnx), lines 3–10. | Application and Infrastructure are not yet present. They are owned by the companion plan. |
| `git status --short` at preparation shows existing Core edits/renames and WinUI project edits. | Preserve their staged/unstaged state; do not reset, stage, commit, or rewrite unrelated changes. |

All paths below are repository-relative. New files are explicitly marked as planned. Domain requirements derive from the original Core plan, lines 189–314; testing requirements from lines 494–513.

## 3. Domain decisions to approve

| Topic | Required behavior |
|---|---|
| Languages and modes | Keep `Auto`, `English`, `Vietnamese`, `Japanese`; add `Translate`, `Explain`. Reject undefined enum values. Source may be `Auto`; target must be concrete. Equal source and target are valid. |
| Learning levels | Abstract `LearningLevel` with sealed `CefrLearningLevel` and `JlptLearningLevel`. Explicit constructors/get-only properties reject `Unknown`, `None`, and undefined values. No numeric proficiency comparisons. |
| Source compatibility | English accepts CEFR/null; Japanese accepts JLPT/null; Vietnamese accepts null only; Auto accepts CEFR/JLPT/null. Reject unsupported learning-level subclasses as incompatible. Detection and subsequent compatibility checks remain future work. |
| Request | Sealed record, explicit constructor, get-only properties. Require text/source/target; default level to null and mode to Translate. Preserve accepted text exactly. No mutation path that bypasses construction rules. |
| Validation failures | Use `ArgumentException` for null/empty/blank text and incompatible levels (`ArgumentNullException` for null text is acceptable as its subclass). Use `ArgumentOutOfRangeException` for undefined enums or Auto target. Test one invalid input at a time; avoid promising validation order for multiple invalid inputs. |
| Result descriptions | Sealed records using required/init members exactly as listed in the source plan. `required` expresses initialization requirements, not runtime validation of external payloads. Optional text defaults to null. |
| Collections | `IReadOnlyList<T>`, empty by default. Reject explicit null initialization, including via `with`, using small property guards. Do not claim deep immutability or structural list equality. Provider owns its returned collection data and never mutates it afterward. |

Result shapes: `VocabularyItem` requires Word/PartOfSpeech/Meaning and permits Phonetic/ExampleSentence; `GrammarPoint` requires Pattern and permits Explanation/ExampleSentence; `TranslationAlternative` requires Text and permits Explanation; `TranslationResult` requires OriginalText and permits NaturalTranslation/Explanation/Context/Tone plus Vocabulary/Grammar/Alternatives lists.

## 4. Ordered implementation batches

### C0 — Establish the approved baseline

- Re-read the worktree diff, selected SDK, shared build properties, solution and references. Use the current developer edits as the starting point.
- Build existing Core and tests separately from WinUI. Record errors/warnings; an empty passing test is not domain coverage.
- If SDK selection or restore blocks checks, diagnose without lowering targets or changing package versions. Record the blocker and preserve the approved scope.
- Exit: usable .NET 10 toolchain and recorded baseline, or explicit validation blocker before dependent implementation.

### C1 — Align existing enums and add translation mode

- Existing: `src/FihLens.Core/Enums/{Language,CefrLevel,JlptLevel}.cs`.
- Planned: `src/FihLens.Core/Enums/TranslationMode.cs`.
- Align CEFR namespace to `FihLens.Core.Enums`; retain existing enum values/sentinels. Correct only unresolved naming/import issues.
- Teach enum defaults and unnamed cast values. Exit: Core builds and all affected imports resolve.

### C2 — Implement validated learning levels and request

- Planned: `src/FihLens.Core/ValueObjects/{LearningLevel,CefrLearningLevel,JlptLearningLevel}.cs` and `src/FihLens.Core/Models/TranslationRequest.cs`.
- Implement the rules in section 3 at constructor boundaries; use standard exceptions and simple conditions/pattern matching.
- Planned tests: `tests/FihLens.Tests/Core/{LearningLevelTests,TranslationRequestTests}.cs`.
- Teach constructor invariants, record equality, and source proficiency. Exit: all C2 acceptance cases pass and nullable warnings in these files are resolved.

### C3 — Refine descriptions and complete result shape

- Move existing `Entities/VocabularyItem.cs` and `Entities/GrammarPoint.cs` to `Models/`, updating namespaces and references; retain one definition of each type.
- Planned: `src/FihLens.Core/Models/{TranslationAlternative,TranslationResult}.cs`.
- Preserve meaningful fields, remove duplicate mutable/positional representation, implement empty collection defaults and null guards.
- Planned tests: `tests/FihLens.Tests/Core/TranslationResultTests.cs`; remove the empty `UnitTest1.cs` once real coverage exists.
- Teach shallow immutability, optional fields, collection ownership, and why list equality needs explicit assertions.
- Exit: C3 acceptance cases pass; Core still has no project/package dependency.

### C4 — Verify and hand off

- Run the checks in section 6, review the diff for unintended edits, and explain domain choices with examples.
- Hand off the models once to the Application operation plan; do not implement a provider/interface/service in this Core lane.
- Exit: Core acceptance criteria proven; remaining platform/integration work recorded in the companion plan.

## 5. Testable acceptance criteria

- [ ] Null, empty, and whitespace-only text rejected; valid text including surrounding whitespace preserved byte-for-byte as a string.
- [ ] Undefined source, target and mode rejected; Auto target rejected; Auto source and equal concrete source/target accepted.
- [ ] Every named non-sentinel CEFR/JLPT value accepted; sentinel and undefined values rejected.
- [ ] Parameterized compatibility tests cover every row in section 3, including Auto and incompatible/unsupported levels; null level accepted for all valid sources.
- [ ] Omitted level is null and omitted mode is Translate; request properties cannot be assigned through an initializer or `with` (review public API, no compile-failure test harness).
- [ ] Equivalent levels compare equal; different level values compare unequal.
- [ ] Result optional text defaults to null; Vocabulary/Grammar/Alternatives default to nonnull empty lists; explicitly assigning null to each list is rejected.
- [ ] Description fields retain supplied values; results are asserted by fields/list elements rather than whole-record collection equality.
- [ ] Core builds with no new warnings attributable to these changes and no new dependencies/platform APIs.

## 6. Verification, risks and stop conditions

After implementation approval, run:

```text
dotnet --version
dotnet build src/FihLens.Core/FihLens.Core.csproj
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj --filter FullyQualifiedName~FihLens.Tests.Core
```

Use `FihLens.Tests.Core` namespaces for planned Core tests so the filter selects them. Require a nonzero executed-test count. Review compiler output, applicable Rider inspections when available, project references and `git diff`; shared settings currently allow warnings, so a successful build alone is insufficient.

Risks: preserving developer edits requires checking each affected file before modification; nullable/init syntax can be mistaken for runtime validation, so tests cover the explicit collection guards; an open abstract level base can be extended, so the request rejects unsupported variants. SDK/restore failure is a verification blocker, not permission to retarget. No Core test can prove Windows behavior.

Core stop condition: all acceptance criteria are proven or a concrete blocker is reported, and the handoff is ready. Combined Phase 0 stops only after the companion plan's Windows gate. Learning checkpoint: explain domain invariants, record limitations, enum validation, nullable versus required, and dependency direction.

## 7. Approval gate

**Await explicit developer approval before creating or modifying code, project files, tests, or running implementation batches.** Review/approval of this plan is the next action. Approval should cover both operation plans as one ordered scope; any requested changes are applied to these copies only.

---

## Appendix — original teaching plan (verbatim snapshot)

# Phase 0 — Modern C# and the FIH Lens Core Layer

## 1. Goal, boundaries, and starting point

Build and understand this small flow:

```text
“Hello world”
    ↓
TranslationRequest
    ↓
ITranslationProvider → FakeTranslationProvider
    ↓
TranslationResult
    ↓
Automated test and a minimal Windows display
```

You will write the implementation. The agent’s role is to explain concepts, give small examples, review your work, and help verify it. Examples below teach individual mechanisms; they are not finished implementations to copy into FIH Lens.

Phase 0 is complete when:

- The full translation domain described below exists in Core.
- A fake provider returns a predictable structured result.
- Tests verify meaningful rules and cancellation behavior.
- The existing WinUI window can display the fake translation on Windows.
- You can explain the model choices and dependency direction.

Real AI, HTTP, persistence, hotkeys, capture, OCR, and a complete MVVM screen belong to later phases.

### What already exists

The repository already contains Core, WinUI, and an xUnit test project. Nullable reference types and implicit usings are enabled.

Core currently has:

- `Language`, `CefrLevel`, and `JlptLevel` enums.
- A mutable `VocabularyItem` class using a primary constructor.
- A mutable `GrammarPoint` class with a `Pattern` property.

The enums live under `ValueObjects` but declare an `Enums` namespace. `Language` contains the spelling `Vientnamese`. The existing test has no assertions.

Reuse this work. Correct the spelling and align enum locations with their namespace as a small, separate change. Do not reorganize the entire solution.

### Environment prerequisite

The projects target .NET 10, but the environment inspected while preparing this plan has SDK 8.0.417. A Core build fails with `NETSDK1045`.

Use a .NET 10 SDK before validating the implementation. Keep the project targets unchanged. Core and provider tests can run without Windows; WinUI build and display verification require Windows.

## 2. Understand Core before designing its types

### Domain, model, and invariant

The **domain** is the subject your application works with: translation and language learning.

A **domain model** expresses that subject in code. `Language` describes a product concept; an HTTP response describes how an external service communicates. Those are different responsibilities.

An **invariant** is a rule that must hold for a valid domain object. For example:

> A translation request must contain nonblank text and a concrete target language.

Core needs these concepts so every caller uses the same vocabulary and rules. Otherwise, different screens and providers can interpret the same request differently.

Core should contain ordinary C# types and .NET functionality. It should not know about WinUI, provider SDKs, HTTP, JSON payloads, SQLite, or Windows APIs.

### Entity versus value object

An **entity** has an identity that continues through changes. A saved history entry might remain the same entry even after its notes change.

A **value object** is described by its contents. Two language-level values representing CEFR B1 mean the same thing.

For Phase 0, a vocabulary item inside a translation result is a description, not a saved vocabulary entry with an identity. The same applies to grammar points and alternatives. Model these as records without inventing IDs.

Use `Enums` for enums, `ValueObjects` for learning-level records, and a simple `Models` folder for translation request/result data and their nested descriptions. Moving the existing vocabulary and grammar files there is a bounded change, not a broad architecture refactor.

**Your reasoning checkpoint:** What would change if a vocabulary item became a saved flashcard with review history? Explain the identity requirement before adding an ID.

### Class, record, and immutability

An ordinary class uses reference equality unless you implement another equality policy. A record supplies equality based on its members and supports useful data-model syntax.

This is useful when comparing small domain values:

```csharp
public sealed record StudyTopic(string Name);

var first = new StudyTopic("Cancellation");
var second = new StudyTopic("Cancellation");

Console.WriteLine(first == second); // True
```

Here:

- `record` requests record behavior, including synthesized equality.
- `sealed` prevents inheritance from this concrete type.
- The positional parameter generates an initialization-only property.

A record is still a reference type unless declared `record struct`. It is not automatically deeply immutable. A record containing a mutable list can still observe changes to that list. Record equality also does not automatically compare list contents element by element. See [Microsoft’s record documentation](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/records).

Use ordinary classes for services and records for the Phase 0 data models. Do not create base entities, generic model frameworks, or interfaces for individual values.

### Primary constructors

Your existing class uses this syntax:

```csharp
public class ExampleItem(string word)
{
    public string Word { get; set; } = word;
}
```

In an ordinary class, `word` is a constructor parameter; it does not automatically become a public property. The explicit `Word` declaration creates that property.

A positional record generates properties from its parameters. These similar-looking declarations have different behavior.

Start with explicit constructors where validation matters. Use shorter syntax only after you can explain what it generates. See [primary constructors](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/primary-constructors).

### `get`, `set`, `init`, and `required`

These keywords solve different problems:

| Syntax | Purpose |
|---|---|
| `get; set;` | Allow reading and later reassignment. |
| `get; init;` | Allow assignment during initialization. |
| `get;` with constructor assignment | Require construction through the constructor’s rules. |
| `required` | Require callers to supply a member during initialization. |

For example:

```csharp
public sealed record Lesson
{
    public required string Title { get; init; }
    public string? Notes { get; init; }
}

var lesson = new Lesson { Title = "Records" };
var updated = lesson with { Notes = "Compare two values." };
```

The object initializer supplies properties. `with` creates a shallow copy with selected changes.

`required` does not reject `""`, whitespace, or runtime `null`. `init` does not validate values either. Neither keyword replaces a domain rule. See [`required`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/required) and [`init`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/init).

**Implementation choice:** Give `TranslationRequest` and concrete learning-level values explicit validating constructors and get-only properties. Use `required` and `init` for result descriptions, which are constructed by the provider. Do not add `with`-based mutation to invariant-bearing request properties.

### Nullable reference types

Nullable reference types help the compiler identify possible missing values:

```csharp
string? explanation = null;
int length = explanation?.Length ?? 0;
```

- `string?` means a reference may be absent.
- `?.` accesses a member only when the receiver is nonnull.
- `??` supplies a fallback.

`string?` is a compiler annotation. `CefrLevel?`, because an enum is a value type, uses `Nullable<CefrLevel>`.

Use null to represent optional information, not to disguise an error. Do not silence warnings with `!` unless you can explain the guarantee the compiler cannot see. See [nullable reference types](https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references).

### Enums

An enum names a small set of choices. It prevents repeated prompt strings such as `"B1"` from becoming your internal model.

However, casting an integer can create an unnamed value:

```csharp
var level = (CefrLevel)999;
bool recognized = Enum.IsDefined(level);
```

An enum’s default value is zero, even if zero has no declared member. Validate values at construction boundaries; named values alone do not guarantee validity. See [enum behavior](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum).

Keep the existing CEFR and JLPT members. Their sentinel values mean “unspecified” and must not become concrete learning levels. Use a null `UserLevel` for an unspecified request level.

Do not compare numeric enum values to infer proficiency. JLPT naming and CEFR naming have different conventions.

## 3. Your Core implementation assignments

Complete these assignments incrementally. After each, explain your choices and request a review before moving to the next significant step.

### Assignment A — Define languages and translation modes

Correct `Vietnamese` and retain:

```text
Auto, English, Vietnamese, Japanese
```

Define `TranslationMode` with:

```text
Translate, Explain
```

`Translate` requests a natural translation. `Explain` requests learning-oriented explanation. These are Phase 0 model choices; their real provider behavior comes later.

Rules:

- `Auto` is valid as a source.
- `Auto` is invalid as a target.
- Source and target may match, supporting English-to-English learning.
- Undefined enum values are invalid.

**Review focus:** Enum defaults, naming, and the difference between domain choices and display labels.

### Assignment B — Model one learning level

A request needs one optional learning level that can represent either CEFR or JLPT.

Use an abstract `LearningLevel` record with two concrete records:

- `CefrLearningLevel`, containing a valid `CefrLevel`.
- `JlptLearningLevel`, containing a valid `JlptLevel`.

`abstract` means the base type cannot be instantiated directly. It lets a property hold either supported specialization while preventing meaningless construction of a generic level.

This unrelated example illustrates the syntax:

```csharp
public abstract record DeliveryOption;

public sealed record Pickup(string Location) : DeliveryOption;
public sealed record Shipping(string Address) : DeliveryOption;
```

The colon means “inherits from.” This hierarchy is justified because the requested unified level represents two distinct alternatives. A pair of nullable properties would allow contradictory states unless separately checked.

Concrete level constructors must reject sentinel and undefined values.

`UserLevel` describes the **source language**, as agreed:

| Source | Accepted level |
|---|---|
| English | CEFR or null |
| Japanese | JLPT or null |
| Vietnamese | Null |
| Auto | CEFR, JLPT, or null |

With `Auto`, compatibility cannot be established until detection occurs. Phase 0 only permits the supplied level; later translation orchestration must compare it against the detected language.

**Review focus:** Why inheritance is useful here, what invalid states the design prevents, and why each leaf validates its enum.

### Assignment C — Implement `TranslationRequest`

Use a sealed record with an explicit constructor and get-only properties:

| Member | Type and purpose |
|---|---|
| `Text` | Nonblank input text. |
| `SourceLanguage` | `Language`; may be `Auto`. |
| `TargetLanguage` | `Language`; must be concrete. |
| `UserLevel` | `LearningLevel?`; optional source proficiency. |
| `Mode` | `TranslationMode`; defaults to `Translate`. |

Require text and both languages as constructor inputs. Default the level to null.

Validate the rules from Assignments A and B in the constructor. Reject null, empty, and whitespace-only text. Preserve accepted text exactly; do not silently trim or normalize it.

Use standard exceptions:

- `ArgumentException` for invalid text or incompatible level.
- `ArgumentOutOfRangeException` for undefined enums or `Auto` as target.

A small validation example:

```csharp
public ExampleName(string value)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(value);
    Value = value;
}
```

A constructor prevents invalid initial state. Get-only properties prevent a later assignment from bypassing that constructor.

**Review focus:** Whether every successfully constructed request satisfies its rules, and whether copying or property assignment can bypass them.

### Assignment D — Complete result descriptions

Implement the full requested roadmap model:

| Type | Members |
|---|---|
| `VocabularyItem` | Required `Word`, `PartOfSpeech`, `Meaning`; optional `Phonetic`, `ExampleSentence`. |
| `GrammarPoint` | Required `Pattern`; optional `Explanation`, `ExampleSentence`. |
| `TranslationAlternative` | Required `Text`; optional `Explanation`. |
| `TranslationResult` | Required `OriginalText`; optional `NaturalTranslation`, `Explanation`, `Context`, `Tone`; vocabulary, grammar, and alternative collections. |

Use sealed records with initialization-only properties. Preserve the existing vocabulary concepts instead of replacing them with an unrelated model.

Collections must default to empty, never null:

```csharp
public IReadOnlyList<string> Examples { get; init; }
    = Array.Empty<string>();
```

`IReadOnlyList<T>` exposes indexed reading without collection mutation methods. `<T>` is a generic type parameter: the same interface works for strings, vocabulary items, and grammar points.

This interface does not freeze the underlying collection. For Phase 0, the fake provider must construct its collections and never mutate them after returning the result. Do not add an immutable-collection dependency.

Optional text means “not supplied.” The fake’s successful translation fixture must supply `NaturalTranslation`; an explanation-only result need not.

`TranslationResult` is a domain result, not a provider JSON schema. JSON attributes, provider field names, parsing, and malformed external-response handling belong to later Infrastructure work.

**Review focus:** Optionality, empty collection defaults, shallow immutability, and meaningful equality tests.

## 4. Connect the models while learning modern C#

### Project boundaries and dependency inversion

Follow `AGENTS.md` where the older roadmap diagram differs:

```text
Application → Core
Infrastructure → Application and Core
WinUI → Application and Infrastructure
Tests → projects under test
```

Add small .NET 10 Application and Infrastructure projects. Do not create Windows integration projects yet.

Place:

- `ITranslationProvider` in Application.
- `TranslationService` in Application.
- `FakeTranslationProvider` in Infrastructure.
- Manual object construction in the WinUI startup/composition area.

An interface describes a capability without specifying its mechanism:

```csharp
public interface IExampleLookup
{
    Task<string> FindAsync(
        string key,
        CancellationToken cancellationToken = default);
}
```

For FIH Lens, implement the roadmap contract returning `Task<TranslationResult>` from a `TranslationRequest`.

This boundary is useful even with one fake implementation: Application needs translation without depending on provider technology. It does not justify interfaces for every model.

### Dependency injection

Dependency injection means supplying an object’s dependencies from outside it. A container is optional.

```csharp
public sealed class ExampleReader(IExampleLookup lookup)
{
    public Task<string> ReadAsync(CancellationToken token)
        => lookup.FindAsync("example", token);
}
```

`TranslationService` should receive `ITranslationProvider` through its constructor and expose `TranslateAsync`, forwarding the request and cancellation token. Reject a null provider or null request using standard argument guards.

Wire these objects manually for Phase 0. No new DI or hosting package is needed. The startup code owns the service and provider; neither needs disposal if it owns no disposable resources.

This manual wiring applies the constructor-injection mechanism described in [Microsoft’s DI documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection).

### `Task`, `async`, and `await`

`Task<T>` represents eventual completion with a value, failure, or cancellation. It does not imply that a new thread is created.

```csharp
static async Task<string> ReadExampleAsync()
{
    await Task.Delay(100);
    return "Ready";
}
```

`await` allows the method to suspend while the operation is incomplete. Exceptions are observed by awaiting the task.

A method that simply forwards a task does not need `async`:

```csharp
public Task<string> ReadAsync(CancellationToken token)
    => lookup.FindAsync("example", token);
```

Avoid `.Result`, `.Wait()`, and using `Task.Run` to disguise naturally asynchronous work. Keep `async void` limited to UI event handlers that require it. See [asynchronous programming](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/).

### Cancellation and the fake provider

Cancellation is cooperative. A token communicates a request to stop; the operation must observe it.

```csharp
using var source = new CancellationTokenSource();
source.Cancel();

await Task.Delay(100, source.Token);
```

The creator owns the `CancellationTokenSource` and disposes it. A receiver uses the token without taking ownership of the source.

For the fake provider:

- Support the exact fixture `"Hello world"`, English → Vietnamese, `Translate`, with no level or a CEFR level.
- Return `"Xin chào thế giới"` and deterministic learning data.
- Populate the full result shape with a small vocabulary example, grammar example, and alternative.
- Reject other fixtures with `NotSupportedException`; do not present fake data as a real translation.
- Return a canceled task immediately when the token is already canceled.
- Use `Task.FromResult` for the immediate result. No artificial delay is needed.

`Task.FromResult` creates an already-completed task; it performs no asynchronous work. A canceled task can be created with `Task.FromCanceled<T>` when its token is canceled. See [Task.FromResult](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.fromresult?view=net-10.0).

For this immediate fake, cancellation after completion cannot undo the result. Study cancellation during an operation with the separate `Task.Delay` example.

### Pattern matching and LINQ

Pattern matching helps inspect the concrete learning-level variant:

```csharp
string Describe(DeliveryOption option) => option switch
{
    Pickup pickup => $"Collect at {pickup.Location}",
    Shipping shipping => $"Send to {shipping.Address}",
    _ => throw new ArgumentException("Unsupported option.")
};
```

Use this idea where checking CEFR versus JLPT makes request rules clearer. Avoid clever patterns that obscure a simple condition.

LINQ queries collections:

```csharp
var longerWords = words
    .Where(word => word.Length > 5)
    .ToArray();
```

`Where` filters. `ToArray` materializes the result. Without materialization, many LINQ queries run when enumerated, potentially more than once.

Practice LINQ with a small vocabulary exercise. Do not add a query pipeline where a direct property access suffices.

### Awareness topics: understand, then defer

These are listed in Phase 0 but do not need production use yet:

| Concept | What it does | FIH Lens use later |
|---|---|---|
| `IAsyncEnumerable<T>` | Produces values incrementally and asynchronously. | Streaming translation output. |
| `IDisposable` | Defines synchronous resource cleanup. | Native registrations, streams, cancellation sources. |
| `IAsyncDisposable` | Defines cleanup that must be awaited. | Resources with asynchronous shutdown. |
| `Span<T>` | Provides a view over contiguous memory without copying it. | Measured parsing or buffer-processing work. |

Small syntax examples:

```csharp
await foreach (var item in stream.WithCancellation(token))
{
    Console.WriteLine(item);
}

using var source = new CancellationTokenSource();
await using var resource = new ExampleAsyncResource();
```

`stream` must be an `IAsyncEnumerable<T>` whose producer observes cancellation. `ExampleAsyncResource` represents an object implementing `IAsyncDisposable`; it is illustrative, not a requested project class.

`using` ensures cleanup when its scope exits, including exceptional exits. `await using` awaits asynchronous cleanup. Neither belongs on Core records with no owned resources. See [async streams](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream) and [using statements](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/using).

```csharp
ReadOnlySpan<char> firstWord = "Hello world".AsSpan(0, 5);
```

A span refers to existing memory rather than making a substring copy. It has lifetime restrictions; it cannot survive across an `await` boundary. Learn the restriction, but do not optimize request strings with spans in Phase 0. See [ref struct restrictions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct).

## 5. Implementation order, verification, and learning checkpoints

### Suggested 20-hour schedule

| Session | Study and explanation | Your implementation |
|---|---|---|
| 1 — 3 hours | Domain boundaries, enums, SDK/project references. | Verify .NET 10; correct existing enum naming and locations. |
| 2 — 4 hours | Records, constructors, invariants, learning-level alternatives. | Implement learning levels and request rules. |
| 3 — 4 hours | Nullability, generics, collection ownership. | Complete result models and focused Core tests. |
| 4 — 5 hours | Interfaces, DI, tasks, cancellation. | Add Application/Infrastructure, service, fake, and tests. |
| 5 — 4 hours | Brief awareness exercises and review. | Display the fixture on Windows; run checks and reflect. |

Aim for approximately six hours of study and fourteen hours of implementation. Adjust the schedule if understanding a concept takes longer.

### Tests you should write

Replace the empty test with tests that prove behavior:

- Null, empty, and whitespace-only request text is rejected.
- Accepted text is preserved exactly.
- Undefined languages/modes and an automatic target are rejected.
- Matching source and target are permitted.
- CEFR/JLPT sentinel and undefined values are rejected.
- Source/level compatibility follows the agreed table.
- Two equivalent concrete learning levels compare equal.
- Optional result text defaults to null and result collections to empty.
- The fake returns the expected translation and structured learning data.
- Unsupported fake fixtures fail explicitly.
- An already-canceled token produces an observable cancellation.
- The service forwards the same request and token, using a small handwritten recording provider in the tests.

Do not assert whole-result record equality to compare collections. Assert the relevant fields and collection contents.

These are unit tests because they check domain rules and application contracts without Windows or a network. The WinUI display is a manual integration check.

### Build and review

After installing a compatible SDK, run:

```text
dotnet build src/FihLens.Core/FihLens.Core.csproj
dotnet build src/FihLens.Application/FihLens.Application.csproj
dotnet build src/FihLens.Infrastructure/FihLens.Infrastructure.csproj
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj
```

Inspect compiler warnings and Rider inspections. Verify project references point inward and Core has no added packages or platform dependencies.

On Windows, build the existing WinUI project for x64 and run it. Display the fixed request’s translation in a simple text element. Keep this a small smoke check; complete data binding, commands, and MVVM belong to Phase 1.

### How to use the mentor

After each assignment, share your code and a short explanation:

> “Here is my request model. I chose constructor validation because … Please review correctness, invalid states, and architecture, and explain issues before suggesting a patch.”

The review should distinguish blockers from improvements, explain the mechanism behind each issue, and let you make the correction first.

At the milestone, you should be able to explain:

- Why request/result types belong in Core but provider interfaces belong in Application.
- Why `required` differs from runtime validation.
- Why a record containing a list is not deeply immutable.
- Why `Task<T>` does not mean “background thread.”
- How cancellation reaches a provider and who owns its source.
- How manual construction qualifies as dependency injection.
- Which responsibilities remain for later phases.

**Defaults and assumptions:** Full roadmap models are included as requested. `UserLevel` describes the source language. Translation modes are `Translate` and `Explain`. Existing projects and xUnit dependencies are reused. No new NuGet packages, real provider integration, or broad refactors are part of Phase 0.
