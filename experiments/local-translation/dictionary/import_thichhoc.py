"""Offline one-corpus conversion. No upstream code, HTML or dictionary programs run."""
import argparse
import hashlib
import html
import json
import time
import unicodedata
import zipfile
from pathlib import Path

REVISION = "ec3fa199aebdc4da4ebcaa7a11ac4b9c44046f75"
SOURCE_SHA256 = "f65cab4dad91ac4bcc8cdd588dabb7d718eb9e50588d3ae221e468e433b6eaf1"
ATTRIBUTION_SHA256 = "d8476eecb2703a0fe9424de2bed178b04619da4c59f061638d11aba1a6870605"
SOURCE_ID = "thichhoc-en-vi-v0.9.0"
MAX_FILE = 500 * 1024 * 1024
MAX_LINE = 1024 * 1024


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def normalized(text):
    return unicodedata.normalize("NFC", text.strip()).casefold()


def convert_record(raw, location):
    if len(raw) > MAX_LINE:
        raise ValueError(f"{location}: record exceeds 1 MiB")
    try:
        record = json.loads(raw.decode("utf-8"))
        if not isinstance(record, dict) or record.get("lang") != "en-vi":
            raise ValueError("expected en-vi object")
        for key in ("id", "headword", "pos", "source"):
            if not isinstance(record.get(key), str) or not record[key].strip():
                raise ValueError(f"invalid {key}")
        senses = record.get("senses_vi")
        if not isinstance(senses, list) or not senses or any(
            not isinstance(sense, str) or not sense.strip() for sense in senses
        ):
            raise ValueError("missing Vietnamese senses")
        return {
            "headword": record["headword"], "senses": senses,
            "partOfSpeech": record["pos"], "sourceId": SOURCE_ID,
            "sourceArticleId": record["id"], "sourceOrigin": record["source"],
        }
    except (ValueError, KeyError, UnicodeError) as error:
        raise ValueError(f"{location}: {error}") from error


def self_check():
    valid = {"id": "en:test:n", "lang": "en-vi", "headword": "test",
             "pos": "n", "source": "fixture", "senses_vi": ["thử nghiệm"]}
    actual = convert_record(json.dumps(valid).encode(), "fixture:1")
    assert actual["senses"] == ["thử nghiệm"]
    assert actual["sourceArticleId"] == "en:test:n"
    assert normalized("  CAFÉ ") == normalized("cafe\u0301")
    for raw in (b"{", b"[]", b"\xff", b" " * (MAX_LINE + 1),
                json.dumps({**valid, "senses_vi": []}).encode(),
                json.dumps({**valid, "lang": "vi-en"}).encode()):
        try:
            convert_record(raw, "fixture:2")
        except ValueError as error:
            assert "fixture:2" in str(error)
        else:
            raise AssertionError("invalid record accepted")
    print("parser self-check: passed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--attribution", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-check", action="store_true")
    args = parser.parse_args()
    if args.self_check:
        self_check()
        return
    if not all((args.archive, args.attribution, args.output)):
        parser.error("--archive, --attribution and --output are required")
    if args.archive.stat().st_size > MAX_FILE:
        raise ValueError("source archive exceeds 500 MiB")
    if digest(args.archive) != SOURCE_SHA256:
        raise ValueError("source archive SHA256 does not match inspected corpus")
    if args.attribution.stat().st_size > MAX_LINE or digest(args.attribution) != ATTRIBUTION_SHA256:
        raise ValueError("attribution SHA256 does not match inspected notice")
    args.output.mkdir(parents=True, exist_ok=True)
    corpus = args.output / "dictionary.jsonl"
    pending = args.output / "dictionary.jsonl.pending"
    records, index, source_files, origins = 0, {}, [], {}
    start = time.perf_counter()
    with zipfile.ZipFile(args.archive) as archive:
        prefix = f"thichhoc-dict-{REVISION}/"
        files = sorted(item for item in archive.namelist()
                       if item.startswith(prefix + "dict-en-vi/data/entries/")
                       and item.endswith(".jsonl"))
        if not files or sum(archive.getinfo(item).file_size for item in files) > MAX_FILE:
            raise ValueError("missing or oversized source corpus")
        notices = {
            "LICENSE-DATA": prefix + "dict-en-vi/LICENSE-DATA",
            "UPSTREAM-CODE-LICENSE": prefix + "LICENSE",
            "UPSTREAM-README.md": prefix + "README.md",
        }
        for filename, member in notices.items():
            data = archive.read(member)
            if len(data) > MAX_LINE:
                raise ValueError("oversized notice")
            (args.output / filename).write_bytes(data)
        (args.output / "ATTRIBUTION.txt").write_bytes(args.attribution.read_bytes())
        with pending.open("wb") as output:
            for member in files:
                file_hash = hashlib.sha256()
                with archive.open(member) as source:
                    row = 0
                    while raw := source.readline(MAX_LINE + 1):
                        row += 1
                        file_hash.update(raw)
                        record = convert_record(raw, f"{member}:{row}")
                        encoded = (json.dumps(record, ensure_ascii=False,
                                              separators=(",", ":")) + "\n").encode("utf-8")
                        if len(encoded) > MAX_LINE or output.tell() + len(encoded) > MAX_FILE:
                            raise ValueError(f"{member}:{row}: output limit exceeded")
                        output.write(encoded)
                        index.setdefault(normalized(record["headword"]), []).append(record)
                        origins[record["sourceOrigin"]] = origins.get(record["sourceOrigin"], 0) + 1
                        records += 1
                source_files.append({"path": member.removeprefix(prefix), "rows": row,
                                     "sha256": file_hash.hexdigest(),
                                     "bytes": archive.getinfo(member).file_size})
    if len(index) < 10000:
        raise ValueError("corpus has fewer than 10,000 distinct normalized headwords")
    pending.replace(corpus)
    import_seconds = time.perf_counter() - start
    queries_path = Path(__file__).with_name("queries.json")
    queries = json.loads(queries_path.read_text(encoding="utf-8"))
    assert {key: len(value) for key, value in queries.items()} == {
        "common": 70, "polysemous": 15, "phrases": 15}
    assert len({query for values in queries.values() for query in values}) == 100
    sample, coverage, timings = [], {}, []
    for category, values in queries.items():
        found = 0
        for query in values:
            matches = index.get(normalized(query), [])
            found += bool(matches)
            for _ in range(30):
                before = time.perf_counter_ns()
                index.get(normalized(query), [])
                timings.append((time.perf_counter_ns() - before) / 1e6)
            sample.append({"category": category, "query": query, "entries": matches,
                           "review": {"acceptable": None, "notes": None}})
        coverage[category] = {"hits": found, "queries": len(values)}
    (args.output / "review-sample.json").write_text(
        json.dumps(sample, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    def cell(text):
        return html.escape(text).replace("|", "\\|").replace("\n", " ").replace("\r", " ")
    review = ["# Frozen dictionary meaning review", "",
              "**Pending bilingual human review. Generated/unreviewed source; coverage is not meaning quality.**",
              "", "Source: thichhoc v0.9.0, revision `" + REVISION + "`. Converted data CC BY-SA 4.0.",
              "See complete attribution/notices beside this file. Conversion retains original VI senses and article IDs.",
              "", "Mark each query acceptable only if at least one displayed VI sense is appropriate; record critical misleading meanings.",
              "A05 requires at least 90/100 appropriate senses and zero critical misleading reviewed meanings.",
              "", "| # | Category | Query | Vietnamese senses (part of speech; article ID; origin) | Human verdict / notes |",
              "|---|---|---|---|---|" ]
    for number, item in enumerate(sample, 1):
        details = "<br>".join(cell("; ".join(entry["senses"]) + " (" + entry["partOfSpeech"]
                                  + "; " + entry["sourceArticleId"] + "; " + entry["sourceOrigin"] + ")")
                              for entry in item["entries"]) or "MISS"
        review.append(f"| {number} | {item['category']} | {cell(item['query'])} | {details} | pending |")
    (args.output / "review-sample.md").write_text("\n".join(review) + "\n", encoding="utf-8")
    files = [corpus, *(args.output / name for name in notices),
             args.output / "ATTRIBUTION.txt"]
    manifest = {
        "schemaVersion": 1, "sourceId": SOURCE_ID, "name": "thichhoc English–Vietnamese",
        "revision": REVISION, "release": "v0.9.0", "license": "CC-BY-SA-4.0",
        "licenseUrl": "https://creativecommons.org/licenses/by-sa/4.0/",
        "sourceUrl": "https://github.com/thichhoc-org/thichhoc-dict",
        "sourceArchiveUrl": f"https://codeload.github.com/thichhoc-org/thichhoc-dict/zip/{REVISION}",
        "sourceArchiveSha256": digest(args.archive), "originalRecords": records,
        "entryCount": records, "uniqueNormalizedHeadwords": len(index),
        "dictionaryFile": corpus.name, "dictionarySha256": digest(corpus),
        "dictionaryBytes": corpus.stat().st_size, "sourceFiles": source_files,
        "files": [{"path": path.name, "sha256": digest(path), "bytes": path.stat().st_size}
                  for path in files],
        "attribution": "Dữ liệu từ điển: Từ điển Anh–Việt thichhoc.com (thichhoc-dict), "
                       "giấy phép CC BY-SA 4.0. https://github.com/thichhoc-org/thichhoc-dict "
                       "Nguồn gốc: WordNet 3.1 (Princeton), CMUdict (CMU), Wiktionary. "
                       "Đã chỉnh sửa dữ liệu: chuyển lược đồ sang JSONL văn bản thuần; "
                       "giữ nguyên nghĩa tiếng Việt và mã bài gốc.",
        "qualityStatus": "pending-bilingual-human-review; generated Vietnamese senses",
        "originCounts": origins, "coverage": coverage, "querySampleSha256": digest(queries_path),
        "importSeconds": import_seconds,
        "pythonWarmLookupP95Milliseconds": sorted(timings)[int(len(timings) * .95)],
        "performanceScope": "Python prototype only, not .NET/UI proof",
    }
    (args.output / "dictionary-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"records": records, "uniqueHeadwords": len(index), "coverage": coverage,
                      "importSeconds": import_seconds, "bytes": corpus.stat().st_size}))


if __name__ == "__main__":
    main()
