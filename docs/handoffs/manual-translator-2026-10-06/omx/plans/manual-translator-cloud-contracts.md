# Provider contract worksheet — Gemini and DeepSeek

Native official-doc researcher: /root/cloud_contracts. Retrieved 2026-10-06; REST/API evidence only, not account/model-access or live verification. This worksheet is a planning input. Before adapter implementation, recheck these chosen contract fields against the linked API references and record any change; never substitute a different envelope/API/model silently.

## Explicit selection and credential ownership

User selected both Gemini and DeepSeek. `FIHLENS_PROVIDER_MODE=demo|gemini|deepseek`, default demo; selection once at startup. Read only the selected development key from `GEMINI_API_KEY` or `DEEPSEEK_API_KEY`, no keys in source/fixtures/worksheet/logs/UI. A missing selected key or unknown mode is a visible configuration failure with zero HTTP calls and no fake fallback. The unselected provider's key is not required. No runtime provider switch, router, failover or general plugin mechanism.

## Gemini

- Model: stable `gemini-3.5-flash-lite`; documented limits 1,048,576 input /65,536 output tokens, not app targets. [Model roster](https://ai.google.dev/gemini-api/docs/models), [model details](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite). Document request output budget 2,048 tokens for this small object, increase only after a justified later contract change.
- POST `https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent` with per-request `x-goog-api-key` and JSON content.
- Trusted instructions map to `systemInstruction.parts[].text`; original material maps to `contents` role `user`, text parts. Keep original input separate and unchanged.
- Current [GenerateContent reference](https://ai.google.dev/api/generate-content) describes `generationConfig.responseFormat.text` with `mimeType: "APPLICATION_JSON"` and `schema`. Older `responseMimeType`/`responseJsonSchema` are marked deprecated. Do not copy Interactions API `response_format` or its response envelope. Verify wire format against current reference before implementing.
- Use supported JSON schema subset for neutral analysis fields, required arrays and nonblank validation in the parser; schema is not a replacement for local semantic validation. Optional provider schema fields may need required nullable keys. [Structured output](https://ai.google.dev/gemini-api/docs/structured-output).
- `thinkingLevel: "minimal"` is default for this model; explicit minimal is allowed, not a guarantee of no reasoning. [Thinking](https://ai.google.dev/gemini-api/docs/generate-content/thinking).
- Request one candidate/default count. Reject blocked `promptFeedback.blockReason`; require exactly one candidate, `finishReason: "STOP"`, and usable ordered non-thought text parts; concatenate text in order, reject unsupported nontext output. Parse resulting analysis JSON.
- Prompt/candidate safety, blocklist, prohibited-content or recitation blocks → Refused. MAX_TOKENS, missing candidate without a block reason, missing/unknown finish reason, empty/nontext/unusable output → InvalidResponse. Do not fabricate missing analysis or retry.

Sanitized request-shape guide (model key supplied only at runtime, schema abbreviated here; implementation must supply full schema):

```json
{
  "systemInstruction": {"parts": [{"text": "Return the required translation analysis as JSON."}]},
  "contents": [{"role": "user", "parts": [{"text": "Hello world"}]}],
  "generationConfig": {
    "maxOutputTokens": 2048,
    "responseFormat": {"text": {"mimeType": "APPLICATION_JSON", "schema": {"type": "object"}}}
  }
}
```

Sanitized completed envelope guide: `{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"<valid analysis JSON>"}]}}]}`. This is a shape guide, not a passing fixture: offline tests must use fully valid hand-authored analysis plus the real provider fields; test prompt blocks, truncated/unknown completion and ordered multiple text parts separately.

## DeepSeek

- Model `deepseek-flash` currently maps to V4.1 Flash. Documented 1M context/384K maximum output are platform limits; use `max_tokens: 2048` for this operation. [Model/pricing table](https://api-docs.deepseek.com/quick_start/pricing/).
- POST `https://api.deepseek.com/chat/completions`, per-request `Authorization: Bearer <runtime key>`, JSON. [Chat completion reference](https://api-docs.deepseek.com/api/create-chat-completion/).
- Separate `system`/`user` messages map neutral trusted instructions/original text. Explicit `thinking: {"type":"disabled"}` because provider defaults thinking on; `stream: false`.
- `response_format: {"type":"json_object"}` guarantees JSON syntax, not a schema or meaningful values. The system prompt must explicitly mention JSON and show the desired analysis shape. Empty content is a documented caveat. [JSON mode](https://api-docs.deepseek.com/guides/json_mode/).
- Require exactly one choice, `finish_reason: "stop"`, nonblank `message.content`; ignore `reasoning_content`, reject tool/function output. Parse the content locally. `length` → InvalidResponse; `content_filter` → Refused; `insufficient_system_resource`/`aborted` → Unavailable; unknown/missing finish reason and empty/null/wrong-type content → InvalidResponse.

Sanitized request-shape guide:

```json
{
  "model": "deepseek-flash",
  "thinking": {"type": "disabled"},
  "messages": [
    {"role": "system", "content": "Translate to Vietnamese. Return JSON shaped as {\"naturalTranslation\":\"...\",\"vocabulary\":[],\"grammar\":[],\"alternatives\":[]}."},
    {"role": "user", "content": "Hello world"}
  ],
  "response_format": {"type": "json_object"},
  "max_tokens": 2048,
  "stream": false
}
```

Completed envelope shape: `{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"<valid analysis JSON>"}}]}`. As above, tests must replace placeholders with valid complete analysis and independently test invalid envelopes.

## Shared error mapping, resource bounds and verification

Application categories: Authentication (including 401/403/account-access or insufficient balance 402), RateLimited (429), RequestRejected (400/404/422), Unavailable (transport/server failures), Timeout (client deadline/504), InvalidResponse (HTTP success but unusable envelope/JSON/semantics or body bound exceeded), Refused (documented content blocks). Caller cancellation takes precedence over exception-based timeout classification and propagates as cancellation. Unsupported request capability is rejected before HTTP with NotSupportedException and never converted into a fake success.

[Gemini errors](https://ai.google.dev/gemini-api/docs/api-errors), [Gemini troubleshooting](https://ai.google.dev/gemini-api/docs/troubleshooting), [DeepSeek error table](https://api-docs.deepseek.com/quick_start/error_codes/). Preserve diagnostic causes internally but do not display/log raw headers, keys, source/response text or bodies by default. No automatic retries, streaming, repair calls or fallback.

Use app-owned selected-adapter HttpClient: 30-second timeout, 1 MiB ResponseContentRead buffer cap, cancellation passed to send/read, per-call request-scoped credentials and message disposal. Startup chooses/document DNS pooling lifetime before implementation. Account costs/access prerequisites are runtime/user-account facts: do not promise free use, infer access or make calls during planning. Link the model/pricing pages above for future live evaluation.

Offline verification: assert exact method/URL/auth/model/schema/message fields, completion-envelope extraction, all failure cases, send-once and cancellation/disposal using handwritten handlers for each real adapter. Credentialed smoke checks use short nonsensitive English→Vietnamese input, verify meaningful nonblank result and original text/shape, not exact model wording. Gemini and DeepSeek live verification are independent outstanding gates; missing one key never certifies that adapter through demo/other-provider success.
