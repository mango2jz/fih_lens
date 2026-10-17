# Japanese dictionary pack experiment

This standard-library-only Python tool converts the official legacy English JMdict XML (`JMdict_e.gz`) to the proposed immutable FIH Lens pack. It writes UTF-8 JSONL entries in numeric `ent_seq` order, NFC written/reading aliases in unsigned UTF-8 byte order, a little-endian fixed-row `FIHJIDX1` index, a manifest, notices and a reproducible ZIP. It uses a temporary SQLite spool so nonmonotonic source entry IDs do not require an in-memory corpus.

```powershell
python experiments/japanese-dictionary/build_pack.py --self-test
python experiments/japanese-dictionary/build_pack.py `
  --source .omx/verification/japanese-english-dictionary/JMdict_e.gz `
  --out .omx/verification/japanese-english-dictionary/pack
```

The input may be a gzip archive or plain XML. Output files are overwritten by a rebuild; keep them in the ignored verification folder unless a future release process intentionally stages an approved pack elsewhere. Do not distribute a pack based on `qualityStatus: unreviewed`; semantic review, trusted catalog pinning, release ownership and refresh procedure are separate gates.

The schema-3 converter preserves entry IDs, each written form's `ke_inf`, each reading's `re_inf` and restrictions, reading-only markers, English gloss text and `g_type`, inherited POS, sense restrictions, loanword-source text with language/type/wasei metadata, antonym reference strings, usage labels and cross-reference strings. Empty loanword source bodies remain represented; absent DTD-defaulted attributes become `eng` and `full`. Pack versions identify a digest over the source, converter, and license text; a change to any of them creates a new immutable version. The manifest records the exact converter SHA-256. It keeps loanword and antonym references as source annotations and does not resolve links or infer relationships. It also does not infer synonyms, tokenize sentences, or deinflect inputs. ElementTree does not resolve external XML entities; the self-test includes an external-entity rejection case. The actual legacy input carries an internal DTD used for POS entity names.
