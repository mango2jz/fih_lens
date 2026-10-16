# Japanese word details and local pronunciation

Status: Accepted · 2026-10-15

The first Japanese release extends the existing explicit Gemini/DeepSeek translation flow to all six EN/VI/JA directions. Word cards show generated kana readings, optional base-form hints and source-language synonyms. They retain AI provenance; local Japanese dictionary import, morphology and browser-style term scanning are deferred by the user's scope choice. The existing local corpus remains English→Vietnamese only.

Pronunciation is an Application capability implemented by the existing Windows project with installed SpeechSynthesizer voices and MediaPlayer. Each window owns its cancellation token; one app service serializes utterances and retires old native work before a replacement plays. Missing matching-language voices are explicit unavailable states. No cloud audio service, package dependency, language-pack installation or wrong-language fallback is introduced.

This keeps platform APIs outside Core/Application and makes window cancellation testable. It trades dictionary-backed linguistic certainty for the requested provider reuse, and requires native lifecycle checks in addition to portable tests. Device voice/OCR availability and generated Japanese semantic quality remain separate from contract correctness.
