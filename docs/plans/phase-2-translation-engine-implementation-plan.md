# Phase 2 — Learning HTTP, Structured Responses, and the Translation Engine

## 1. Goal, prerequisites, and boundaries

You will replace the demo-only provider with one real cloud adapter while learning HTTP, asynchronous I/O, JSON contracts, validation, and resource ownership. You write the production code and tests. The mentor explains mechanisms, reviews your decisions, and helps diagnose problems.

Complete and verify the [Core](phase-0-core-implementation-plan.md), [Application](phase-0-application-implementation-plan.md), and [Phase 1](phase-1-winui-implementation-plan.md) assignments first. These documents describe assignments, not components already implemented in the repository. Use a .NET 10 SDK; build and run WinUI on Windows.

You chose a **provider-neutral guide**. Choosing a provider, model, and documented API contract is an explicit student assignment before adapter implementation. This guide does not invent a universal provider endpoint or response envelope.

The milestone is:

```text
ViewModel → TranslationService → ITranslationProvider
                                     ↓
                            Concrete cloud adapter
                                     ↓
                    Application-owned PromptBuilder
                                     ↓
                       Provider request + HTTP call
                                     ↓
                      Provider envelope extraction
                                     ↓
                    TranslationParser → Core result
```

Keep the Phase 1 English → Vietnamese screen for the first live smoke check. At this stage, prove natural translation and reliable transport; Phase 4 will develop the richer English → Vietnamese learning experience. Phase 3 adds CEFR-adapted English explanations.

Stop when one adapter works, failures are observable, tests cover its boundaries, and the UI shows a valid result. No provider router, retries, streaming, history, local model, document-reader work, or new SDK package is required.

## 2. Concepts to understand

### Separate policy, transport, parsing, and presentation

The roadmap names several responsibilities, not four mandatory abstraction frameworks:

| Responsibility | Where it belongs | Why |
|---|---|---|
| Translation request/result and language values | Core | Describe product information. |
| TranslationService and provider interface | Application | Define the use case and required capability. |
| PromptBuilder and neutral instructions | Application | Express what the learning product asks for. |
| HTTP, credentials, provider envelope, schema, DTOs, parser | Infrastructure | Handle external technology and untrusted data. |
| Busy/error state and visible text | WinUI | Present the operation to the user. |

Infrastructure may call a concrete Application PromptBuilder because its dependency already points inward. Application must not reference Infrastructure to perform parsing.

Reuse the existing service and interface. Do not create both PromptBuilder and PromptFactory, or interfaces for a builder and parser with one implementation.

**Reasoning checkpoint:** If the provider changes its response envelope but the translation meaning stays the same, which project should change?

### Prompt construction

A prompt combines product instructions with source material. A deterministic builder lets you test instructions without contacting a model.

Introduce a small Application record, `TranslationInstructions`, with `SystemText` and `UserText`, and a `PromptBuilder.Build(TranslationRequest)` method. These names identify instructional content; the adapter maps them into the selected API's documented roles or fields.

Keep trusted instructions separate from the original input. The source text is material to translate, not authority to change the application's behavior. Preserve it exactly and never execute content returned by the model.

An unrelated syntax example:

```csharp
public sealed record StudyInstructions(string Guidance, string Material);

var instructions = new StudyInstructions(
    "Summarize the supplied material.",
    originalText);
```

The builder specifies desired meaning and sections. Infrastructure defines their JSON representation and the provider's structured-output mechanism. A prompt asking for JSON is not proof that a response will follow the contract.

### HTTP request and response

HTTP exchanges a request containing a method, address, headers, and optional body for a response containing a status, headers, and body.

Authentication is provider-specific. Read the chosen API documentation rather than assuming every provider uses the same header.

A request can fail before a response exists, return an unsuccessful HTTP status, or return HTTP success with unusable content. Those are different failure paths.

`HttpClient` already handles HTTP. `System.Net.Http.Json` and `System.Text.Json` already provide JSON functionality. No SDK or HTTP wrapper library is needed for this learning slice. See [Microsoft's HttpClient overview](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient).

### Lifetime and disposal

Reuse one startup-owned HttpClient. Creating and disposing a new client for each translation discards connection pools and can exhaust network resources. Long-lived clients can use `SocketsHttpHandler.PooledConnectionLifetime` to accommodate DNS changes; choose and document that interval for the selected endpoint rather than treating it as a performance tweak. A client factory is another option, but would add a package unnecessary for one adapter. See [HttpClient lifetime guidance](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines).

Create a fresh HttpRequestMessage for each operation, with credentials assigned to that request. Do not mutate shared authentication headers during a request.

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, exampleUri);
using var response = await client.SendAsync(request, token);
```

`using` disposes the message when the scope exits, including failures. Request and response disposal also releases their content. The injected client belongs to startup, not the provider.

The client lives for the application lifetime. Window.Closed requests ViewModel cancellation; that request does not prove pending work has finished, and this event must not immediately dispose the client. If the process continues running, explicit application shutdown awaits the pending command's ExecutionTask before disposing the startup-owned client. If closing the final window terminates the process, process teardown releases its resources; do not claim an asynchronous Closed handler is guaranteed to finish. Keep request/response disposal inside the operation and do not block the UI waiting for shutdown.

### Cancellation, timeout, and response bounds

Caller cancellation means the user no longer wants the result. Timeout means the operation exceeded its deadline. A network failure is different from both.

Pass the caller's token to sending and reading. Preserve caller cancellation as cancellation; do not wrap it as a provider error.

For the first adapter, use a 30-second HttpClient timeout and a 1 MiB maximum buffered response. Use `ResponseContentRead`, so the request completes after buffering the response content. If those bounds are exceeded, fail explicitly; do not accept truncated JSON.

Do not switch to `ResponseHeadersRead` without revisiting these guarantees: the client timeout then covers receiving headers, and its buffer limit does not bound the subsequent body read. See [HttpCompletionOption](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0).

On modern .NET, an HTTP timeout can surface as an OperationCanceledException with a nested TimeoutException. Check whether the caller requested cancellation before classifying the timeout. Do not classify every cancellation exception as a user action.

### JSON, DTOs, and the trust boundary

The provider normally returns two contracts:

1. Its API envelope: status, output blocks, usage, completion information, or refusal.
2. The generated analysis object inside that envelope.

Infrastructure understands both. The UI receives neither.

A **DTO** represents data crossing a boundary. It can resemble a domain record while serving a different purpose: it represents what arrived, including invalid states that need checking.

```csharp
internal sealed class LessonDto
{
    public required string Title { get; init; }
}

LessonDto? dto = JsonSerializer.Deserialize<LessonDto>(json);
```

`required` can enforce JSON property presence, but presence is not nonnullness or semantic validity. The root can be JSON null, and nested values can still be unusable. Nullable-annotation enforcement also has limitations, including collection elements. Validate explicitly before constructing domain results. See [required JSON properties](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties) and [nullable JSON annotations](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations).

### Provider-neutral failure handling

Add one Application-owned `TranslationProviderException` and a small `TranslationProviderFailureKind` enum. Use only these categories, each with a safe UI interpretation:

| Kind | User feedback |
|---|---|
| Authentication | Check credentials/account access. |
| RateLimited | Wait before trying again. |
| Unavailable | Network/provider unavailable; try later. |
| Timeout | Operation took too long. |
| InvalidResponse | Provider returned unusable data. |
| Refused | Provider declined to produce the requested output. |
| RequestRejected | Request/model settings were rejected. |

Keep HTTP status types, SDK objects, and provider JSON out of this Application contract. Preserve diagnostic causes for debugging, but never display or log raw headers, credentials, request text, or response bodies by default.

This small type enables actionable presentation without coupling the ViewModel to HTTP. It is not a general result framework. Unexpected programming failures still reach the Phase 1 generic-error boundary.

## 3. Your implementation assignments

### Assignment A — Choose and document one provider

Choose a provider you can access, then fill a short contract worksheet using its current official documentation:

- Endpoint, API version, method, authentication, and selected model identifier.
- How product guidance and user text map into request fields.
- Whether that model supports structured output, and which schema features it accepts.
- How to extract the completed analysis from the response envelope.
- How refusal, incomplete output, multiple output blocks, and unsuccessful statuses appear.
- Input limits, output budget, documented costs, and account prerequisites.
- One minimal successful request/response example with credentials removed.

Treat this as a design checkpoint. Have the mentor review the worksheet before writing the adapter. If structured output is unavailable, choose a documented JSON-output path or another suitable model and explicitly retain parser failure handling. Do not claim schema guarantees that the provider does not offer.

Do not copy account secrets into the worksheet. No live call is necessary to write these plans.

### Assignment B — Write the internal analysis contract

Use these provider-independent analysis fields, mapped to the Core result:

| Field | Rule |
|---|---|
| `naturalTranslation` | Required nonblank text for Translate mode. |
| `explanation` | Optional in Phase 2; Phase 3 requires it for Explain mode. |
| `vocabulary` | Required array; empty allowed. |
| `grammar` | Required array; empty allowed. |
| `alternatives` | Required array; empty allowed. |
| `context`, `tone` | Optional string or null. |

Vocabulary items use the Core plan's word, part-of-speech, meaning, optional phonetic, and optional example fields. Grammar and alternative items follow that plan's corresponding descriptions. Required item text must be nonblank. Optional descriptive text may be absent or null; reject wrong types. Reject null arrays and null array elements.

Use exact, documented camel-case names. Ignore unrelated extra fields rather than exposing them to the domain. Do not include `originalText` in generated analysis: set `TranslationResult.OriginalText` from the original request.

Define the chosen provider's schema in Infrastructure. All required collections can be empty; a valid empty collection is different from a missing or null one. Provider-specific schema requirements may require explicitly nullable optional keys.

**Review target:** Explain how the provider schema, DTO checks, and domain model each protect a different boundary.

### Assignment C — Implement the builder and parser offline

Write PromptBuilder for the Phase 2 English → Vietnamese Translate request, with no CEFR adaptation yet. This phase accepts only a null UserLevel; reject non-null levels as unsupported before sending HTTP rather than silently ignoring them. Core may accept a request that this narrower use case does not yet support. Unsupported language/mode combinations also fail before HTTP.

Write `TranslationParser.Parse(string analysisJson, TranslationRequest request)` in Infrastructure. It deserializes internal DTOs, validates values, and maps them into Core records. It does not send HTTP or choose UI messages.

Create small hand-authored JSON fixtures. Test the parser before connecting a real account. Do not extract JSON with regex, strip arbitrary Markdown fences, invent missing required text, or add automatic repair calls.

At the adapter boundary, map malformed or semantically invalid analysis into InvalidResponse. The parser itself may throw JsonException for syntax/shape errors and a documented validation exception for bad values; choose one consistent local convention rather than adding another public hierarchy.

### Assignment D — Implement one concrete adapter

Name it for the provider you selected. Constructor-inject the client, builder, parser, and the small provider-specific settings needed by your worksheet.

The operation should build instructions, serialize a request, attach request-scoped authentication, send/read with cancellation, validate completion/refusal, extract analysis JSON, and parse a result.

Use a fresh request and response with explicit ownership. Map known failures to the Application failure categories. Let caller cancellation propagate. Do not retry automatically, especially after an operation whose server-side outcome is unknown.

Keep `ITranslationProvider` and the service's request/result signature unchanged. Prompt construction and parsing are concrete helpers, not new ports.

### Assignment E — Compose live and demo modes explicitly

At startup, select demo or live mode through a small development setting, such as `FIHLENS_PROVIDER_MODE=demo|live`. Default to demo. Keep the existing fake available for offline work.

Read the selected provider's development credential from a process environment variable and never place it in tracked files. The IDE must inherit that environment. A missing credential in live mode produces a clear configuration message and no request; it must not silently choose the fake.

Label the selected mode in the UI. Live mode tells the user that submitted text goes to the chosen cloud provider. A desktop executable cannot conceal a bundled developer API key; shipping an account/key-management design is a later decision.

Update safe error presentation for the failure categories while retaining Phase 1's one-request, cancellation, and close behavior. Do not move HTTP logic into the ViewModel.

### Assignment F — Verify one complete live workflow

After offline tests pass, use your own configured account to submit a short non-sensitive English input and receive a Vietnamese translation. Inspect the structured result, cancel a pending request, and verify error presentation.

This is a manual integration check, not an automated paid test. Keep model output out of exact-text assertions. Do not claim completion of Phase 4's richer learning behavior merely because the transport returns Vietnamese text.

## 4. Tests and acceptance checks

### Deterministic tests

Add an Infrastructure reference to the existing test project. Use small fixtures and a handwritten HttpMessageHandler test double; override its protected SendAsync method to inspect requests and return canned responses. This tests the real adapter without a network or mocking package. The injection seam follows the [HttpMessageHandler API](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpmessagehandler?view=net-10.0).

Verify:

- Builder preserves original input and keeps it separate from trusted guidance; non-null UserLevel and unsupported language/mode combinations are rejected before HTTP.
- Valid structured analysis maps into Core fields and derives OriginalText from the request.
- Malformed/root-null JSON, missing required collections, wrong types, null items, and blank required text fail.
- Optional fields and empty collections are accepted.
- Actual outgoing method, endpoint, authentication, model, schema, and body match the reviewed worksheet.
- A successful envelope is extracted according to the chosen API, including its output-block rules.
- Authentication, rate-limit, server/network failure, rejection, refusal, and incomplete-output fixtures map correctly.
- Caller cancellation is observed; timeout remains distinguishable.
- Response-bound failures reject the response without repair or a second call.
- Each operation sends once, and disposing its request/response does not dispose the injected client.

Use TaskCompletionSource or a cancellation-aware handler for ordering. Do not use long sleeps. Test timeout classification with a representative timeout exception; the real client deadline can be a focused manual check.

### Build and manual verification

Build Application and Infrastructure, run the test project, inspect warnings, and build/run WinUI on Windows. Verify that Core/Application contain no transport dependencies and that the UI never sees raw provider JSON.

If credentials or account access are unavailable, report offline adapter tests as complete and the live smoke check as outstanding. Demo success is not proof of a working cloud integration.

## 5. Learning sequence and review checkpoints

Budget roughly one 20-hour week: six hours reading/design and fourteen implementing/testing. Provider access problems may extend the schedule without changing the architectural target.

| Stage | Learn | Implement yourself |
|---|---|---|
| 1 | API contracts, HTTP, ownership. | Provider worksheet and sanitized fixtures. |
| 2 | Prompt policy and JSON validation. | Builder, DTOs, parser, offline tests. |
| 3 | Async transport and failures. | Adapter and fake-HTTP tests. |
| 4 | Composition and integration evidence. | Explicit live/demo wiring and manual checks. |

Ask for review after the worksheet, parser, adapter, and final integration. Explain your own design first; the mentor should explain defects before offering a complete patch.

### Architecture and critic review

The plan received independent architecture and critic reviews on 2026-10-02. The architecture reviewer accepted the corrected plan after clarifying application-lifetime client ownership and distinguishing cancellation requests from completed work.

The critic requested an explicit rule for non-null UserLevel in Phase 2. Assignment C now rejects it before HTTP, and the test checklist includes that rule; the primary reviewer verified the correction. The critic's final recheck was interrupted by a subagent usage limit, so no final independent critic acceptance is claimed.

These reviews assess the learning plan, not implemented software. Provider selection still requires your official-documentation worksheet and mentor review. Builds, tests, and a live integration check remain your implementation assignments.

At completion, explain why the UI cannot parse model JSON, why valid JSON can still be invalid data, who owns the client/messages, why timeout differs from cancellation, and how you would debug an HTTP success with unusable analysis.

**Defaults:** Provider-neutral with a student selection gate; one adapter; existing service/port; .NET HTTP/JSON only; 30-second buffered-request timeout; 1 MiB response cap; no automatic retries; basic English → Vietnamese transport smoke check. The V2 architecture proposal is outside these assignments.
