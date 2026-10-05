# Phase 3 — Learning CEFR Policy and Building English Explanations

## 1. Goal, prerequisites, and boundaries

You will add English → English explanations tailored to a selected CEFR level while learning how product policy differs from provider transport, how to test generated instructions, and how to evaluate nondeterministic output.

You write the implementation and evaluate the explanations. The mentor teaches concepts, reviews your reasoning, and helps improve your tests and design.

Complete the [Phase 2 engine](phase-2-translation-engine-implementation-plan.md) and the earlier [Phase 1 UI](phase-1-winui-implementation-plan.md) first. Phase 2 must have a reviewed provider contract, verified parser/adapter, and a real integration check before this phase can be declared complete. You can develop policy tests offline while provider access is pending.

The new workflow is:

```text
English input + selected CEFR level
                ↓
English → English, Explain request
                ↓
Existing TranslationService and provider adapter
                ↓
PromptBuilder adds English explanation guidance
                ↓
Validated explanation + learning information
```

Use the same provider, builder, parser, ViewModel, and one-request lifecycle. Do not introduce a second language-analysis API, a level-classification model, or another screen.

The selected level describes the learner's proficiency in the source language. It is an audience target, not an estimated difficulty score for the input and not a certificate that generated text satisfies CEFR.

## 2. Concepts to understand

### What CEFR describes

CEFR describes communicative ability using A1, A2, B1, B2, C1, and C2 descriptors. It is not a universal prescribed word list or a sentence-length algorithm. Language-specific reference descriptions are separate work. See the Council of Europe's [level descriptions](https://www.coe.int/en/web/common-european-framework-reference-languages/level-descriptions) and [reference-level descriptions](https://www.coe.int/en/web/common-european-framework-reference-languages/reference-level-descriptions).

For this product, the learner selects a target level, and the model receives guidance intended to make an explanation understandable at that level.

Do not equate lower level with less accurate meaning. Difficult source material may require defining an advanced word, explaining an idiom, or acknowledging ambiguity.

### Policy versus mechanism

**Policy** expresses the behavior the product wants. **Mechanism** performs the technical operation.

| Responsibility | Location |
|---|---|
| Represent CEFR and valid learning levels | Existing Core types. |
| Choose effective default and explanation guidance | Application PromptBuilder. |
| Encode provider request/schema and send it | Existing Infrastructure adapter. |
| Validate Explain-mode analysis | Infrastructure parser. |
| Select a level and display the result | WinUI ViewModel and view. |

The UI sends a typed level. It should not build its own prompt paragraph or contain six provider-specific branches. The provider transports Application guidance without becoming the owner of CEFR policy.

**Reasoning checkpoint:** Why would placing the level-to-guidance table inside the ViewModel make future capture or document-reading callers harder to support?

### Default selection versus unspecified request

Keep the Core plan's distinction:

- Null UserLevel means the caller did not specify a level.
- The English-explanation policy uses B1 as its effective target when the request level is null.
- The UI initially selects B1 and therefore sends an explicit CEFR value.

Resolving an effective target does not mutate the original request or redefine null globally. JLPT and other future workflows are unaffected.

Unknown is not a selectable CEFR level. Undefined enum values remain invalid under the existing Core constructors. Do not compare numeric enum values to create policy.

### Pattern matching and guidance

A switch expression makes a small, explicit policy easy to read:

```csharp
string GetStudyAdvice(StudyStage stage) => stage switch
{
    StudyStage.Beginner => "Start with one concrete example.",
    StudyStage.Practiced => "Compare examples and exceptions.",
    _ => throw new ArgumentOutOfRangeException(nameof(stage))
};
```

The arrow maps one case to its result. The final case rejects unsupported values. This unrelated example illustrates the syntax; you must write the CEFR mapping yourself.

Keep the mapping in named Application helper methods inside the existing builder. No generic policy engine, strategy hierarchy, configurable prompt repository, or IPromptBuilder is needed.

### Practical explanation guidance

Use this as the initial product policy, then refine it from human evaluation. It is not an official CEFR grammar specification.

| Target | Intended explanation style |
|---|---|
| A1 | Common words, short direct statements, concrete example; define unavoidable difficult terms. |
| A2 | Simple paraphrase, a familiar example, and direct explanation of an idiom's meaning. |
| B1 | Connected plain-English explanation, useful context, and brief clarification of unfamiliar language. |
| B2 | Explain nuance and tone with moderate detail; define less familiar terminology. |
| C1 | Explore register, figurative meaning, ambiguity, and distinctions precisely. |
| C2 | Offer precise, nuanced interpretation where useful, without making the prose ornate or unnecessarily difficult. |

All targets must preserve meaning, keep explanations in English, avoid unsupported context, and label uncertainty. More advanced does not mean longer by default.

### Translation versus explanation

For English → English Explain mode, the main result is an explanation, not a foreign-language translation.

Require a nonblank `Explanation`. Leave `NaturalTranslation` null for this operation. Ask the provider to use null there, and normalize that field to null when mapping Explain results so the UI never mislabels a same-language rewrite as a translation.

Vocabulary meanings should be explained in English for the selected audience. Grammar, context, tone, and alternatives remain optional. Do not fabricate a grammar point just to fill a section.

OriginalText still comes from the request. No new domain result or second provider call is required.

### Snapshotting presentation input

Snapshot both text and selected level before awaiting. Construct one immutable Core request from that snapshot.

Changing a UI selection is not supposed to change an already-running operation. Keep input and level selection disabled while busy, and retain the Phase 1 cancellation and close protections.

Bind the level selector two-way with live changes. Its choices are A1–C2 only. A nullable result remains a complete snapshot; there is no need to mutate its vocabulary collection item by item.

### Deterministic tests versus quality evaluation

You can deterministically test the chosen level, instructions, preserved input, JSON requirements, and UI state. You cannot prove comprehension by asserting that a model emits one exact paragraph.

Human evaluation checks whether the explanation is accurate, usable, and understandable. Automated readability metrics may be supporting evidence later, but word length or sentence count does not establish CEFR compliance. See the Council of Europe's [classroom assessment guidance](https://www.coe.int/en/web/common-european-framework-reference-languages/classroom-assessment).

## 3. Your implementation assignments

### Assignment A — Write a policy note before code

Write your interpretation of each level's explanation style, with one short illustrative example. Include the null → effective B1 default, English-language output, and meaning-preservation requirements.

Use the roadmap sentence, `It doesn't add up.`, to compare A2 and C1:

- A2 might explain that something does not seem correct or logical, with a concrete example.
- C1 might explain inconsistency between available facts and an expected conclusion, including figurative usage.

These are examples of the intended difference, not required model output or proof of either level. Do not infer a specific speaker's intent without context.

**Review target:** Could the guidance simplify language while accidentally removing the original meaning?

### Assignment B — Extend the existing builder

Add the supported English → English Explain branch. Use the existing `CefrLearningLevel`; do not create another level enum or generic profile.

Resolve null to effective B1, then build guidance using the table. Preserve original user text separately from trusted instructions. Keep the existing Phase 2 Translate branch working.

Reject unsupported level/language/mode combinations before HTTP. Core still owns basic request validity; the builder owns which application workflows it can instruct.

Do not spread conditionals through the provider or ViewModel. Have the mentor review the policy mapping before testing it against a live model.

### Assignment C — Extend the analysis contract and parser

Update Infrastructure's schema instructions for Explain mode. The common collections and optional fields remain the Phase 2 contract; nonblank Explanation becomes mandatory, and NaturalTranslation maps to null.

Keep mode-specific required output checks near parsing/mapping, not in XAML. Reject invalid responses instead of inventing explanations or making an automatic second request.

Preserve parser support for English → Vietnamese Translate, including its required NaturalTranslation. The same adapter should handle the two explicitly supported workflows without a provider router.

### Assignment D — Update the existing screen

Make English → English explanation the screen's primary workflow for this milestone. Update its direction label, action label, and result emphasis to match. The existing command/service can keep its translation-oriented name; avoid a rename-only refactor.

Add a CEFR selector with A1–C2 and default B1. Send source English, target English, mode Explain, and the selected typed level. Disable the selector while busy and clear the previous result when a new operation starts.

Show original text, explanation, vocabulary, and any supplied grammar/context/tone/alternatives. Do not show an empty heading as if a translation were produced.

Retain Phase 2 live/demo labels and safe failure messages. Keep the English → Vietnamese adapter path under tests; a full direction selector and its enriched experience can wait for Phase 4.

### Assignment E — Extend the same demo provider

Add one exact English → English Explain fixture, such as `It doesn't add up.`, to the existing fake provider. Provide simple deterministic explanation variants for A1–C2, and treat null as the effective B1 target for this fixture.

Retain its original English → Vietnamese fixture and optional simulated latency. Clearly identify fake output. This fixture exercises request wiring and display; it does not simulate a general language model.

### Assignment F — Evaluate the real feature

Choose six short English inputs: a literal sentence, an idiom, an ambiguous phrase, a technical statement, informal language, and a sentence with important grammatical nuance. Record why each was chosen.

First evaluate two inputs at A2 and C1. Fix clear policy problems before spending time on the full six-input × six-level matrix.

For each completed comparison, record:

- Input, chosen level, provider/model, and date.
- Whether meaning and uncertainty were preserved.
- Which words or explanations would confuse the target learner.
- Whether examples illuminate the input without inventing facts.
- Whether the more advanced explanation adds useful nuance.
- A short next action: accept, adjust instructions, or identify a provider limitation.

Run these live checks yourself using your configured account. Use non-sensitive samples and account for their cost. Save sanitized evaluation notes under docs; do not commit credentials or incidental private text.

## 4. Tests and acceptance checks

### Automated checks

Use the existing test project and test seams. Verify:

- Every A1–C2 value selects its intended Application guidance.
- Null level uses effective B1 without changing the request.
- Source text is preserved, including quotes, Unicode, line breaks, and instruction-like text.
- Unsupported workflow combinations are rejected before HTTP sending. The provider adapter invokes the Application builder, so do not assert that the adapter itself is never called.
- Existing English → Vietnamese instructions/parser behavior remain valid.
- Explain-mode JSON requires nonblank Explanation; NaturalTranslation maps to null.
- Empty optional learning collections are accepted, while invalid required item content is rejected.
- The ViewModel snapshots the chosen level and sends English → English Explain.
- The selector is disabled during pending work; cancellation and close still suppress late publication.
- Failure clears busy state and gives safe feedback.

Assert policy behavior and required components, not an entire frozen prompt string. Do not make live model prose a unit-test golden file. Use controlled providers for pending/canceled outcomes.

### Manual Windows checks

Build the affected projects, run tests, and inspect warnings. On Windows, verify level selection, explanation-first presentation, keyboard usability, long output, and live/demo behavior. Run an A2/C1 comparison, cancel a request, and close during a pending request.

Completion requires both verified software behavior and the documented initial quality evaluation. Passing tests does not prove CEFR adaptation; a good sample explanation does not prove cancellation or resource correctness.

## 5. Learning sequence and review checkpoints

Budget roughly one 20-hour week, split between policy study, implementation, tests, and focused output evaluation. Expand evaluation when the first comparisons reveal a concrete problem.

| Stage | Learn | Implement yourself |
|---|---|---|
| 1 | CEFR descriptors and product interpretation. | Policy note and comparison examples. |
| 2 | Application policy and deterministic tests. | Builder guidance and defaults. |
| 3 | Mode-specific validation and input snapshots. | Parser, selector, and demo fixture. |
| 4 | Nondeterministic quality evaluation. | Live comparisons and documented findings. |

Ask for review after policy design, builder/parser changes, and the first output comparisons. When results are weak, explain your hypothesis before changing the prompt. Change one relevant instruction at a time and compare again.

### Architecture and critic review

The plan received independent architecture and critic reviews on 2026-10-02. The critic accepted the plan. The architecture reviewer accepted it after correcting the unsupported-workflow test to assert rejection before HTTP sending, rather than before adapter invocation.

The reviewed boundaries keep CEFR policy in Application, preserve the meaning of an unspecified domain level, reuse the provider contract, and separate deterministic software checks from human language-quality evaluation. These verdicts assess the plan; your implementation still requires code review, builds, tests, and the initial documented output evaluation.

At completion, explain what CEFR can and cannot tell you, why a selected level is not an input-difficulty estimate, why policy does not belong in the ViewModel, how you preserve ambiguity, and which evidence would convince you that an explanation is useful to its intended learner.

**Defaults:** English → English Explain; explicit UI B1 selection; unspecified request level resolves to effective B1 in Application policy; all A1–C2 supported; same provider and lifecycle; no proficiency classifier, second AI call, automatic prompt tuning, readability certification, or new dependencies. Phase 4 retains responsibility for the richer English → Vietnamese learning experience.
