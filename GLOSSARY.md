# FIH Lens glossary

| Term | Meaning in this app |
|---|---|
| Language pair | A concrete source and target language. Translation supports the six distinct directions among English, Vietnamese and Japanese. Each result retains the pair from its request. |
| Surface word | The word as it appears in the selected text, including its inflection; for example, 食べました. |
| Reading | Optional kana pronunciation of the displayed Japanese surface word; 食べました has reading たべました. |
| Base form | Optional AI-generated dictionary-form hint, such as 食べる for 食べました. It is not verified by a local Japanese dictionary. |
| Source-language synonyms | Near-synonyms of the requested word/sense in its source language. They may differ in nuance; alternate target translations are a separate result section. |
| Pronunciation owner | The window's current speech request and cancellation token. One app-level service plays at most one utterance; another request may supersede it. |
| Local dictionary direction | A sourced offline lookup pair. Current adapters cover English→Vietnamese and Japanese→English; they do not imply reverse or bridged coverage. |
| Dictionary form | A headword as indexed by the dictionary. Exact lookup does not automatically turn an inflected surface word into its dictionary form. |
| Sense applicability | The written forms and readings to which a source definition applies. JMdict restrictions must remain attached to each reading/sense. |
| Curated dictionary pack | A versioned payload, prebuilt index and source notices whose exact hashes are pinned by the app. Data stays outside the app package. |
| Pending restart | A verified installation, update or removal that changes availability on the next app launch; current readers retain their existing snapshot. |
