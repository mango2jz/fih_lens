# Phase 0 — Understanding and Implementing the Application Layer

## 1. Goal and relationship to the Core plan

Your assignment is to build a small Application layer that accepts a valid translation request, delegates to a provider, and returns its result.

```text
Caller
    ↓
TranslationService           Application
    ↓
ITranslationProvider         Application contract
    ↓
FakeTranslationProvider      Infrastructure implementation
    ↓
TranslationResult            Core model
```

You write the production code and tests. The mentor explains the mechanisms, reviews your reasoning, and helps you diagnose problems. Examples in this guide illustrate syntax without providing finished FIH Lens implementations.

This guide expands the Application work already mentioned in the [Core plan](phase-0-core-implementation-plan.md). It does not require a second service or a different provider interface.

Prerequisites:

- Implement the Core request/result models from the previous plan.
- Understand their validation and optional fields.
- Use a .NET 10 SDK. The environment inspected while preparing this plan has only .NET 8.
- Keep the existing test project and xUnit packages.

The milestone is a translation use case that can be tested without launching WinUI, contacting a network service, or using Windows APIs.

## 2. Concepts you need before writing the code

### Application layer and use cases

A **use case** is an action the application performs for a caller. “Translate this request” is a use case. “Represent a language” is a domain concept.

Application connects product intentions to the capabilities needed to carry them out.

| Layer | Question it answers | Example |
|---|---|---|
| Core | What does this information mean? | A request has a concrete target language. |
| Application | What action should happen? | Submit the request for translation. |
| Infrastructure | How does an external capability work? | Send HTTP, parse provider JSON. |
| Windows | How does the app use the operating system? | Capture selected text. |
| WinUI | How does the user interact with the result? | Display the translation. |

For Phase 0, `TranslationService` will be small. It need not invent orchestration just to look like a service. Later, genuine workflow requirements can make it more substantial.

**Common mistake:** Putting HTTP or JSON parsing in Application because translation passes through it. Passing through a layer does not make every technical detail that layer’s responsibility.

### Orchestration versus implementation

**Orchestration** decides which operations happen and how their results connect.

**Implementation** performs an individual capability.

For example, a later capture-and-translate use case might:

1. Ask a capture capability for text.
2. Construct a request.
3. Ask a translation capability for a result.
4. Return that result.

It should not itself call clipboard APIs or construct an HTTP request.

In Phase 0 there is only one provider operation. Keep the service correspondingly small.

**Reasoning checkpoint:** If the cloud provider changes, which part of this flow should need editing? Explain your answer before writing an interface.

### Interfaces, ports, and adapters

An **interface** specifies a capability. A **port** is an interface used as an architectural boundary. An **adapter** implements that port using a particular technology.

For FIH Lens:

- `ITranslationProvider` is the port.
- `FakeTranslationProvider` is the first adapter.
- A future cloud provider is another adapter.

Here is an unrelated syntax example:

```csharp
public interface IBookLookup
{
    Task<string> FindTitleAsync(
        int bookId,
        CancellationToken cancellationToken = default);
}
```

Read this declaration as:

> An implementation can find a title and report its eventual result, failure, or cancellation.

`public` makes the contract accessible to other projects. The method has no implementation body. `default` lets callers omit cancellation when they do not need it.

The interface belongs in Application because that layer defines the capability it requires. Core contains translation concepts, not the mechanism for obtaining a translation.

An interface is justified at this external boundary. It does not follow that every class needs an interface.

### Dependency inversion

Distinguish **runtime calls** from **project references**.

At runtime:

```text
Application service → Infrastructure provider object
```

At compile time:

```text
Application → Core
Infrastructure → Application + Core
```

Application calls an object through an interface that Application owns. Therefore, Application does not need a project reference to Infrastructure.

This is **dependency inversion**: the outer implementation depends on the inner contract.

Microsoft’s [architectural principles](https://learn.microsoft.com/en-us/dotnet/standard/modern-web-apps-azure-architecture/architectural-principles) explain the general principle. FIH Lens’s exact project boundaries come from `AGENTS.md`.

**Common mistake:** Adding an Infrastructure reference to Application to construct the fake provider. Construction belongs at startup or in a test.

### Constructor injection and the composition root

**Dependency injection** means receiving collaborators from outside instead of constructing them internally.

```csharp
public sealed class BookReader
{
    private readonly IBookLookup _lookup;

    public BookReader(IBookLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        _lookup = lookup;
    }
}
```

Syntax explained:

- `private` hides the field from callers.
- `readonly` prevents reassignment of the field after construction.
- It does not make the referenced object immutable.
- The constructor accepts the dependency.
- The null guard rejects invalid construction.

This makes dependencies visible and lets tests supply controlled implementations.

The **composition root** is where the executable assembles the object graph. For FIH Lens, that is near WinUI startup. Tests have their own small composition roots.

Manual constructor calls are enough for Phase 0. Do not add a DI container or hosting package.

Avoid **service locator**: passing a container into a service and making that service look up its dependencies. It hides what the service needs. See [Microsoft’s DI guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines).

### Contracts and validation ownership

A contract describes what callers and implementations may expect.

The Phase 0 contracts are:

| Boundary | Responsibility |
|---|---|
| Core request constructor | Validate text, languages, mode, and level compatibility. |
| Application service constructor | Reject a null provider. |
| Application service method | Reject a null request and delegate the valid object. |
| Provider | Perform the operation, observe cancellation, and produce a valid result. |
| Caller | Await completion and decide how to present success or failure. |

Do not repeat all request validation inside the service. The Core constructor already establishes those invariants.

Do not silently trim text, change languages, or replace the caller’s level.

Later, Infrastructure must validate untrusted external responses before producing domain results. That work is not part of this assignment.

### Asynchronous contracts and direct task forwarding

`Task<TranslationResult>` represents completion, not a background thread.

When a method only delegates, it can return the provider’s task directly. This unrelated example shows the syntax:

```csharp
public Task<string> ReadAsync(
    int bookId,
    CancellationToken token = default)
{
    return _lookup.FindTitleAsync(bookId, token);
}
```

No `async` keyword is needed here.

Use `async` and `await` when you need to work with the completed value, coordinate multiple asynchronous steps, or perform cleanup around the operation.

There is an observable difference:

- A non-async method can throw before returning a task.
- An `async Task` method reports exceptions from its body through its task.
- A returned provider task can already be complete, pending, faulted, or canceled.

For this assignment, direct forwarding is the chosen behavior. Do not add an async wrapper merely to change exception timing. See the [Task-based Asynchronous Pattern](https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap).

### Cancellation and resource ownership

Cancellation is cooperative. Passing a token does not stop anything unless the receiving operation observes it.

Application must forward the caller’s token unchanged. It must not create a replacement token, swallow cancellation, or dispose resources owned by the caller.

For Phase 0:

- The caller owns any `CancellationTokenSource`.
- The service forwards its token.
- The provider implements cancellation behavior.
- The service creates no cancellation source and owns no disposable resource.

A canceled request is not a successful empty translation.

The service should not catch cancellation and return an empty result. It should let the caller observe cancellation. See [task cancellation](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-cancellation).

### Stateless services and overlapping requests

A **stateless** service does not store information about the currently executing request between calls.

Keep these values local or in method parameters:

- Request.
- Token.
- Result.
- Error.

The provider reference can be a field because it is a dependency, not per-request state.

Do not add fields such as `CurrentRequest`, `LastResult`, or `IsBusy` to `TranslationService`. Two callers could otherwise overwrite each other’s state.

Statelessness does not automatically make every provider thread-safe. It prevents this service from introducing shared request state.

Later, the presentation layer must decide whether an older response may replace a newer one. That policy is outside this Phase 0 service.

## 3. Your implementation assignments

### Assignment A — Create the Application project

Create a .NET 10 class library under:

```text
src/FihLens.Application
```

Add it to the existing solution and give it a project reference to Core.

Use this initial structure:

```text
FihLens.Application/
└── Translation/
    ├── ITranslationProvider.cs
    └── TranslationService.cs
```

Do not create empty Capture, History, Settings, or OCR folders yet.

**Explain before review:** Why is a project reference stronger than a folder convention as an architectural boundary?

**Review target:** Application references Core only and builds without Windows-specific dependencies or added NuGet packages.

### Assignment B — Define the provider contract

Write `ITranslationProvider` yourself.

Its single operation must have:

| Part | Contract |
|---|---|
| Name | `TranslateAsync` |
| Request | `TranslationRequest` |
| Cancellation | `CancellationToken cancellationToken = default` |
| Return | `Task<TranslationResult>` |

A successful operation returns a nonnull domain result. Unsupported operations fail explicitly. Cancellation remains observable.

Document the contract briefly. Do not promise that the operation runs on another thread or that every provider supports every language.

Keep SDK objects, HTTP responses, raw JSON, UI controls, and native handles out of the signature.

**Explain before review:** What would happen if this interface returned an SDK-specific response type?

**Review target:** Another project can implement the interface using only Application and Core.

### Assignment C — Implement the translation use case

Write a sealed `TranslationService` with:

- One constructor dependency: `ITranslationProvider`.
- A constructor null guard.
- A `TranslateAsync` method with the same request, token, and result types.
- A request null guard.
- One provider call using the same request and token.
- Direct return of the provider task.

Do not introduce `ITranslationService`, a base service, a mediator, or a request-handler framework. Callers can use the concrete service.

Do not add retries, caching, logging packages, timeouts, provider selection, or response transformations.

Those features require explicit behavior and failure policies. They should not appear accidentally during this exercise.

**Explain before review:** Why can a small method still provide a useful application boundary? What would justify expanding it later?

**Review target:** The service preserves request identity, cancellation, result, and failure behavior without storing per-request state.

### Assignment D — Write test-controlled providers

Use small handwritten implementations inside the test project.

Start with:

- A **recording provider** that records its request, token, and invocation count.
- A **failing provider** or configurable provider that returns a faulted task.
- A **canceling provider** that returns a canceled task for a canceled token.

A test double is a controlled substitute used to isolate the behavior under test. It is not the same responsibility as the Infrastructure fake used to demonstrate the application.

Keep test doubles local to these tests. Do not create a reusable mocking framework or add a mocking package.

**Review target:** Tests verify the service contract rather than an external provider’s behavior.

### Assignment E — Verify delayed completion

An immediate fake can hide asynchronous mistakes. Add one test that uses a pending task and completes it deliberately.

This example teaches manual completion:

```csharp
var completion = new TaskCompletionSource<string>(
    TaskCreationOptions.RunContinuationsAsynchronously);

Task<string> pending = completion.Task;

completion.SetResult("Ready");
string value = await pending;
```

`TaskCompletionSource<T>` gives its owner control over a task’s completion. A test provider can return its task while the test decides when to complete it.

`RunContinuationsAsynchronously` prevents continuations from running inline inside `SetResult`. Explicit completion gives the test its ordering; this option alone does not make a test deterministic.

Your test should establish that:

1. The provider has been called.
2. The service operation is still pending.
3. Completing the provider operation produces the expected result.

Do not use sleeps or arbitrary delays to prove ordering. See [TaskCompletionSource](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1?view=net-10.0).

### Assignment F — Connect the existing fake-provider exercise

Implement the Infrastructure fake described in the Core plan if it is not already written. Do not create a second version.

Compose:

```text
FakeTranslationProvider → TranslationService → Caller
```

The fake handles the agreed `"Hello world"` fixture and returns structured data. It does not contact a real service.

At startup, construct the provider and service explicitly. The caller uses the service rather than constructing providers during each operation.

A minimal WinUI smoke check can display the result. Building a complete screen or ViewModel remains Phase 1.

**Review target:** The same Application service works with both test-controlled providers and the demonstration fake.

## 4. Map the future Application layer without implementing it

| Capability | Application responsibility | Implementation elsewhere |
|---|---|---|
| Selected-text capture | Coordinate a capture use case. | Windows UI Automation and clipboard. |
| OCR | Request recognition and use recognized text. | Capture/OCR APIs. |
| History | Decide when to save and expose history operations. | SQLite storage. |
| Settings | Apply user settings to workflows. | Persistence and UI editing. |
| Provider routing | Select among genuinely available providers. | Individual provider adapters. |
| Translation prompting | Define learning instructions and adaptation policy. | Provider-specific payload encoding and transport. |

These are future responsibilities, not Phase 0 assignments.

Do not declare speculative interfaces whose inputs, outputs, ownership, or failure behavior are not yet known.

When a later feature begins, work through:

```text
User goal
    ↓
Use-case behavior
    ↓
Domain inputs and outputs
    ↓
Required external capability
    ↓
Interface and implementation
```

For example, clipboard capture needs decisions about unavailable text, clipboard restoration, thread affinity, and cancellation. An empty interface added today does not resolve those decisions.

The Core plan permits a level with automatic source detection. Checking that level against a detected language remains future work until detection is implemented and represented in the contract.

## 5. Verification and mentor checkpoints

### Tests to write

Use the existing xUnit project, retaining its Core reference and adding an Application reference.

Verify:

- A null provider is rejected during service construction.
- A null request is rejected before provider invocation.
- A valid call invokes the provider exactly once.
- The provider receives the original request object.
- The provider receives the same cancellation token.
- Omitting cancellation passes the default token.
- The successful result is returned unchanged.
- A provider fault remains observable.
- Cancellation remains observable and is not converted to success.
- A pending provider operation remains pending until deliberately completed.

For canceled tasks, remember that `TaskCanceledException` derives from `OperationCanceledException`; use an assertion that accepts the appropriate derived exception.

The delayed-completion test proves asynchronous forwarding without depending on elapsed time. Testing failures and cancellation proves the service does not hide them.

Do not duplicate all Core validation tests here.

### Build and inspect

Run:

```text
dotnet build src/FihLens.Application/FihLens.Application.csproj
dotnet test tests/FihLens.Tests/FihLens.Tests.csproj
```

Once the demonstration fake is implemented, build Infrastructure and run its applicable tests too. On Windows, perform the minimal WinUI display check.

Inspect compiler warnings and Rider analysis. Verify:

- Application references Core only.
- Core has no reference back to Application.
- No provider construction occurs inside `TranslationService`.
- No Windows, HTTP, database, or UI code enters Application.
- No blocking waits or per-request fields were added.

Successful unit tests prove the Application contract; they do not prove real Windows integration or a real translation provider.

### Suggested learning sequence

| Session | Learn | Implement yourself |
|---|---|---|
| 1 | Use cases and dependency direction. | Project setup and provider contract. |
| 2 | Constructor injection and validation ownership. | Translation service. |
| 3 | Tasks, failures, and cancellation. | Recording, failure, and cancellation tests. |
| 4 | Controlled asynchronous completion. | Pending-task test and fake-provider integration. |

After each session, explain your design before asking for review. The mentor should identify defects, explain their causes, and let you make corrections.

At completion, you should be able to answer:

- How can Application call Infrastructure without referencing it?
- Why does the provider interface belong in Application?
- What does constructor injection make visible?
- Why is a DI container unnecessary here?
- When should a method return a task directly?
- Who observes cancellation, and who owns its source?
- Why should request-specific data stay out of service fields?
- Which tests would still run if WinUI and the real provider were removed?

**Assumptions:** This is the Phase 0 Application guide with a future-responsibility map. You implement all assignments. The Core plan remains the source of domain rules. Manual composition and existing xUnit dependencies are sufficient; no new packages or later-phase workflows are included.
