#!/usr/bin/env python3
"""Build a deterministic, immutable FIH Lens pack from legacy English JMdict XML."""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import re
import sqlite3
import struct
import tempfile
import unicodedata
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

MAGIC = b"FIHJIDX1"
FORMAT_VERSION = 1
SCHEMA_VERSION = 3
NORMALIZATION_VERSION = 1
HEADER = struct.Struct("<8sIIQQ")
ROW = struct.Struct("<QIQIQ")
SOURCE_URL = "https://www.edrdg.org/pub/Nihongo/JMdict_e.gz"
LICENSE_URL = "https://creativecommons.org/licenses/by-sa/4.0/"
ATTRIBUTION = (
    "JMdict (Japanese-English Dictionary) by the Electronic Dictionary Research and Development Group (EDRDG). "
    "This derived data is distributed under Creative Commons Attribution-ShareAlike 4.0 International. "
    "Source and licence: https://www.edrdg.org/edrdg/licence.html"
)
CHANGES = (
    "Changes made: converted legacy English JMdict XML into structured UTF-8 JSONL entries and an exact-match index; "
    "retained source IDs, forms, reading and sense restrictions, inherited parts of speech, English glosses, and source "
    "annotations. No meanings, examples, readings, or synonyms were added."
)
ELEMENTS = {
    "entry": {"ent_seq", "k_ele", "r_ele", "sense"},
    "k_ele": {"keb", "ke_inf", "ke_pri"},
    "r_ele": {"reb", "re_restr", "re_nokanji", "re_inf", "re_pri"},
    "sense": {"pos", "gloss", "stagk", "stagr", "s_inf", "field", "misc", "dial", "xref", "ant", "lsource", "example"},
}
GLOSS_ATTRIBUTES = {"{http://www.w3.org/XML/1998/namespace}lang", "g_type", "pri"}
LSOURCE_ATTRIBUTES = {"{http://www.w3.org/XML/1998/namespace}lang", "ls_type", "ls_wasei"}


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def children(node: ET.Element, tag: str) -> list[ET.Element]:
    return node.findall(tag)


def validate_children(node: ET.Element, parent: str) -> None:
    unknown = {child.tag for child in node} - ELEMENTS[parent]
    if unknown:
        raise ValueError(f"unsupported {parent} element: {sorted(unknown)[0]}")


def text_values(node: ET.Element, tag: str) -> list[str]:
    return [(child.text or "").strip() for child in children(node, tag) if (child.text or "").strip()]


def normalized_key(value: str) -> bytes:
    normalized = unicodedata.normalize("NFC", value)
    if not normalized or len(normalized.encode("utf-16-le", "strict")) // 2 > 2000:
        raise ValueError("key is empty or exceeds 2,000 UTF-16 code units")
    encoded = normalized.encode("utf-8", "strict")
    if len(encoded) > 8000:
        raise ValueError("key exceeds the 8,000-byte UTF-8 index bound")
    return encoded


def parse_entry(node: ET.Element) -> dict:
    validate_children(node, "entry")
    ent_seq = node.findtext("ent_seq")
    if ent_seq is None or not ent_seq.isdecimal() or int(ent_seq) <= 0:
        raise ValueError("entry has invalid ent_seq")
    written = []
    for form in children(node, "k_ele"):
        validate_children(form, "k_ele")
        written.append({"text": (form.findtext("keb") or "").strip(), "information": text_values(form, "ke_inf")})
    written = [form for form in written if form["text"]]
    readings = []
    for relem in children(node, "r_ele"):
        validate_children(relem, "r_ele")
        value = (relem.findtext("reb") or "").strip()
        if not value:
            raise ValueError(f"entry {ent_seq} has an empty reading")
        readings.append({"text": value, "restrictions": text_values(relem, "re_restr"),
                         "noKanji": relem.find("re_nokanji") is not None,
                         "information": text_values(relem, "re_inf")})
    if not readings:
        raise ValueError(f"entry {ent_seq} has no readings")
    senses = []
    inherited_pos: list[str] = []
    for sense in children(node, "sense"):
        validate_children(sense, "sense")
        current_pos = text_values(sense, "pos")
        if current_pos:
            inherited_pos = current_pos
        loanword_sources = []
        for source in children(sense, "lsource"):
            if set(source.attrib) - LSOURCE_ATTRIBUTES or len(source):
                raise ValueError(f"entry {ent_seq} has unsupported loanword source metadata")
            loanword_sources.append({"text": (source.text or "").strip(),
                                     "language": source.get("{http://www.w3.org/XML/1998/namespace}lang") or "eng",
                                     "type": source.get("ls_type") or "full", "wasei": source.get("ls_wasei")})
        antonyms = []
        for ant in children(sense, "ant"):
            if ant.attrib or len(ant):
                raise ValueError(f"entry {ent_seq} has unsupported antonym metadata")
            antonyms.append((ant.text or "").strip())
        glosses = []
        for gloss in children(sense, "gloss"):
            if set(gloss.attrib) - GLOSS_ATTRIBUTES or any(child.tag != "pri" or child.attrib or len(child) for child in gloss):
                raise ValueError(f"entry {ent_seq} has unsupported gloss metadata")
            # Legacy English JMdict uses an omitted xml:lang for English.
            if gloss.get("{http://www.w3.org/XML/1998/namespace}lang") in (None, "eng"):
                value = "".join([gloss.text or "", *(child.tail or "" for child in gloss)]).strip()
                if value:
                    glosses.append({"text": value, "type": gloss.get("g_type")})
        senses.append({"glosses": glosses, "partsOfSpeech": list(inherited_pos),
                       "writtenRestrictions": text_values(sense, "stagk"),
                       "readingRestrictions": text_values(sense, "stagr"),
                       "notes": text_values(sense, "s_inf"), "fields": text_values(sense, "field"),
                       "misc": text_values(sense, "misc"), "dialects": text_values(sense, "dial"),
                       "crossReferences": text_values(sense, "xref"), "loanwordSources": loanword_sources,
                       "antonyms": antonyms})
    return {"id": int(ent_seq), "writtenForms": written, "readings": readings, "senses": senses}


def open_xml(path: Path):
    raw = path.open("rb")
    if path.suffix.lower() == ".gz":
        return gzip.GzipFile(fileobj=raw), raw
    return raw, None


def source_info(path: Path, sha256: str) -> tuple[str, str]:
    stream, raw = open_xml(path)
    try:
        prefix = stream.read(2 * 1024 * 1024).decode("utf-8", "replace")
    finally:
        stream.close()
        if raw:
            raw.close()
    dates = re.findall(r"JMdict\s+created:\s*(\d{4}-\d{2}-\d{2})", prefix)
    if not dates:
        dates = re.findall(r"(?:Date|date|generated|Generation|created)[^\r\n]{0,80}(\d{4}-\d{2}-\d{2})", prefix)
    if not dates:
        raise ValueError("could not find source generation date in the XML header")
    source_date = dates[0]
    converter_hash = digest(Path(__file__))
    license_hash = digest(Path(__file__).parent / "LICENSE-DATA.txt")
    identity = hashlib.sha256(f"schema={SCHEMA_VERSION}\n{sha256}\n{converter_hash}\n{license_hash}".encode("ascii")).hexdigest()
    return source_date, f"{source_date}-s{SCHEMA_VERSION}-{identity[:24]}"


def write_review_sample(output: Path, version: str) -> None:
    query_path = Path(__file__).parent / "review-queries.json"
    queries = json.loads(query_path.read_text(encoding="utf-8"))
    if len(queries) != 30 or len(set(queries)) != 30:
        raise ValueError("review-queries.json must contain exactly 30 unique queries")
    wanted = {unicodedata.normalize("NFC", q).encode("utf-8"): q for q in queries}
    hits = {q: {"matchCount": 0, "rows": []} for q in queries}
    with (output / "lookup.idx").open("rb") as index_file, (output / "keys.utf8").open("rb") as key_file:
        magic, fmt, norm, count, reserved = HEADER.unpack(index_file.read(HEADER.size))
        if (magic, fmt, norm, reserved) != (MAGIC, FORMAT_VERSION, NORMALIZATION_VERSION, 0):
            raise ValueError("cannot review an unsupported index")
        for _ in range(count):
            key_offset, key_length, payload_offset, payload_length, ident = ROW.unpack(index_file.read(ROW.size))
            key_file.seek(key_offset)
            key = key_file.read(key_length)
            query = wanted.get(key)
            if query is not None:
                group = hits[query]
                group["matchCount"] += 1
                if len(group["rows"]) < 5:
                    group["rows"].append((payload_offset, payload_length, ident))
    results = []
    with (output / "entries.jsonl").open("rb") as entries_file:
        for query in queries:
            group = hits[query]
            candidates = []
            for payload_offset, payload_length, ident in group["rows"]:
                entries_file.seek(payload_offset)
                entry = json.loads(entries_file.read(payload_length))
                if entry["id"] != ident:
                    raise ValueError("review sample index row does not identify its payload")
                candidates.append(entry)
            results.append({"query": query, "matchCount": group["matchCount"],
                            "truncated": group["matchCount"] > len(candidates), "candidateEntries": candidates})
    sample = {"sourceVersion": version, "status": "pending-human-review",
              "reviewedBy": None, "reviewedAt": None,
              "reviewInstructions": "Compare each candidate with the exact source and verify meaning/form relations; record verdicts outside this generated file.",
              "queries": results}
    sample_path = output.parent / "sample-review-30.json"
    sample_path.write_text(json.dumps(sample, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n",
                           encoding="utf-8", newline="\n")


def build(source: Path, output: Path) -> dict:
    source_hash = digest(source)
    source_date, version = source_info(source, source_hash)
    output.mkdir(parents=True, exist_ok=True)
    entries_path = output / "entries.jsonl"
    key_path = output / "keys.utf8"
    index_path = output / "lookup.idx"
    seen_ids = 0
    with tempfile.NamedTemporaryFile(prefix=".builder-spool-", suffix=".sqlite", dir=output, delete=False) as temp_spool:
        spool_path = Path(temp_spool.name)
    spool = sqlite3.connect(spool_path)
    spool.execute("CREATE TABLE entries (id INTEGER PRIMARY KEY, payload BLOB NOT NULL, payload_offset INTEGER, payload_length INTEGER)")
    spool.execute("CREATE TABLE aliases (key BLOB NOT NULL, id INTEGER NOT NULL, PRIMARY KEY (key, id)) WITHOUT ROWID")
    stream, raw = open_xml(source)
    try:
        with entries_path.open("wb") as entries_file:
            depth = 0
            for event, node in ET.iterparse(stream, events=("start", "end")):
                if event == "start":
                    if depth == 1 and node.tag != "entry":
                        raise ValueError(f"unsupported JMdict root element: {node.tag}")
                    depth += 1
                    continue
                depth -= 1
                if node.tag == "entry":
                    entry = parse_entry(node)
                    ident = entry["id"]
                    if spool.execute("SELECT 1 FROM entries WHERE id=?", (ident,)).fetchone():
                        raise ValueError(f"duplicate ent_seq {ident}")
                    seen_ids += 1
                    line = (json.dumps(entry, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
                    spool.execute("INSERT INTO entries (id, payload) VALUES (?, ?)", (ident, line))
                    keys = {form["text"] for form in entry["writtenForms"]}
                    keys.update(r["text"] for r in entry["readings"])
                    for key in keys:
                        try:
                            encoded = normalized_key(key)
                        except (UnicodeEncodeError, ValueError) as exc:
                            raise ValueError(f"entry {ident} contains an invalid or oversized key") from exc
                        spool.execute("INSERT OR IGNORE INTO aliases (key, id) VALUES (?, ?)", (encoded, ident))
                    node.clear()
            spool.commit()
            for ident, line in spool.execute("SELECT id, payload FROM entries ORDER BY id"):
                payload_offset = entries_file.tell()
                entries_file.write(line)
                spool.execute("UPDATE entries SET payload_offset=?, payload_length=? WHERE id=?",
                              (payload_offset, len(line), ident))
            spool.commit()
    except BaseException:
        spool.close()
        spool_path.unlink(missing_ok=True)
        raise
    finally:
        stream.close()
        if raw:
            raw.close()
    index_rows = spool.execute("SELECT COUNT(*) FROM aliases").fetchone()[0]
    key_offset = 0
    try:
        with key_path.open("wb") as keys_file, index_path.open("wb") as index_file:
            index_file.write(HEADER.pack(MAGIC, FORMAT_VERSION, NORMALIZATION_VERSION, index_rows, 0))
            for key, ident, payload_offset, payload_length in spool.execute(
                    "SELECT aliases.key, aliases.id, entries.payload_offset, entries.payload_length "
                    "FROM aliases JOIN entries USING (id) ORDER BY aliases.key, aliases.id"):
                keys_file.write(key)
                index_file.write(ROW.pack(key_offset, len(key), payload_offset, payload_length, ident))
                key_offset += len(key)
    finally:
        spool.close()
        spool_path.unlink(missing_ok=True)
    notices = {
        "ATTRIBUTION.txt": ATTRIBUTION + "\n",
        "LICENSE-DATA.txt": (Path(__file__).parent / "LICENSE-DATA.txt").read_text(encoding="utf-8"),
        "CHANGES.txt": CHANGES + "\n",
    }
    for name, content in notices.items():
        (output / name).write_text(content, encoding="utf-8", newline="\n")
    files = []
    for name in ("entries.jsonl", "keys.utf8", "lookup.idx", *notices):
        file = output / name
        files.append({"path": name, "bytes": file.stat().st_size, "sha256": digest(file)})
    license_hash = digest(Path(__file__).parent / "LICENSE-DATA.txt")
    artifact_hash = hashlib.sha256(f"schema={SCHEMA_VERSION}\n{source_hash}\n{digest(Path(__file__))}\n{license_hash}".encode("ascii")).hexdigest()
    manifest = {"schemaVersion": SCHEMA_VERSION, "converterSha256": digest(Path(__file__)), "artifactSha256": artifact_hash, "packId": "jmdict-ja-en", "version": version,
                "sourceId": "EDRDG-JMdict-e-legacy", "sourceRevision": source_hash,
                "sourceLanguage": "ja", "targetLanguage": "en", "name": "JMdict Japanese–English",
                "license": "CC-BY-SA-4.0", "sourceUrl": SOURCE_URL, "attribution": ATTRIBUTION,
                "qualityStatus": "unreviewed", "humanReviewScope": "pending; no semantic sample review performed",
                "sourceArchiveSha256": source_hash, "sourceDate": source_date,
                "entryCount": seen_ids, "indexRows": index_rows, "files": files}
    manifest_bytes = (json.dumps(manifest, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")
    (output / "manifest.json").write_bytes(manifest_bytes)
    write_review_sample(output, version)
    zip_path = output.parent / f"{manifest['packId']}-{version}.zip"
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in sorted([f["path"] for f in files] + ["manifest.json"]):
            info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 0
            info.external_attr = 0
            archive.writestr(info, (output / name).read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    installed_bytes = sum(file["bytes"] for file in files) + len(manifest_bytes)
    descriptor = {"PackId": manifest["packId"], "Version": version, "Name": manifest["name"],
                  "SourceLanguage": "Japanese", "TargetLanguage": "English", "License": manifest["license"],
                  "SourceUrl": SOURCE_URL, "SourceRevision": source_hash, "DownloadBytes": zip_path.stat().st_size,
                  "InstalledBytes": installed_bytes, "ArchiveSha256": digest(zip_path),
                  "ManifestSha256": digest(output / "manifest.json"), "DownloadUri": None}
    (output.parent / "dictionary-pack-definition.json").write_text(
        json.dumps(descriptor, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    result = {"sourceSha256": source_hash, "sourceDate": source_date, "version": version,
              "entryCount": seen_ids, "indexRows": index_rows,
              "catalogDescriptor": descriptor,
              "files": {p.name: {"bytes": p.stat().st_size, "sha256": digest(p)}
                        for p in [output / "entries.jsonl", key_path, index_path, output / "manifest.json", zip_path]}}
    return result


def self_test() -> None:
    assert len(normalized_key("あ" * 2000)) == 6000
    assert len(normalized_key("𠮷" * 1000)) == 4000
    try:
        normalized_key("𠮷" * 1001)
    except ValueError:
        pass
    else:
        raise AssertionError("2,001 UTF-16-unit key was accepted")
    fixture = Path(__file__).parent / "fixtures" / "mini-jmdict.xml"
    verification_root = Path(__file__).parents[2] / ".omx" / "verification" / "japanese-english-dictionary"
    verification_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="self-test-", dir=verification_root) as tmp:
        root = Path(tmp)
        result1 = build(fixture, root / "one")
        result2 = build(fixture, root / "two")
        assert result1 == result2
        manifest = json.loads((root / "one" / "manifest.json").read_text(encoding="utf-8"))
        assert manifest["schemaVersion"] == SCHEMA_VERSION == 3 and manifest["entryCount"] == 3 and manifest["qualityStatus"] == "unreviewed"
        assert manifest["sourceDate"] == "2026-01-02"
        descriptor = json.loads((root / "dictionary-pack-definition.json").read_text(encoding="utf-8"))
        assert descriptor["PackId"] == "jmdict-ja-en" and descriptor["DownloadUri"] is None
        assert descriptor["ManifestSha256"] == digest(root / "one" / "manifest.json")
        fixture_entries = [json.loads(line) for line in (root / "one" / "entries.jsonl").read_text(encoding="utf-8").splitlines()]
        by_id = {entry["id"]: entry for entry in fixture_entries}
        assert [entry["id"] for entry in fixture_entries] == [100, 200, 300]
        assert by_id[100]["readings"] == [
            {"text": "せい", "restrictions": ["生"], "noKanji": False, "information": []},
            {"text": "しょう", "restrictions": ["生"], "noKanji": False, "information": []},
        ]
        assert by_id[100]["writtenForms"] == [{"text": "生", "information": ["usually kana"]}]
        assert by_id[100]["senses"][0]["glosses"] == [{"text": "life", "type": "fig"}]
        assert by_id[100]["senses"][0]["loanwordSources"] == [{"text": "vie", "language": "fre", "type": "part", "wasei": "y"}]
        assert by_id[100]["senses"][1]["loanwordSources"] == [{"text": "", "language": "eng", "type": "full", "wasei": None}]
        assert by_id[100]["senses"][1]["antonyms"] == ["生"]
        assert by_id[100]["senses"][1]["partsOfSpeech"] == ["noun (common) (futsuumeishi)"]
        assert by_id[100]["senses"][1]["writtenRestrictions"] == ["生"]
        assert by_id[100]["senses"][1]["readingRestrictions"] == ["せい"]
        assert by_id[200]["readings"][0]["noKanji"] is True and by_id[200]["writtenForms"] == []
        assert by_id[200]["readings"][0]["information"] == ["word containing irregular kana usage"]
        assert by_id[300]["writtenForms"] == [{"text": "生きる", "information": ["ichidan verb"]}]
        assert by_id[300]["senses"][0]["crossReferences"] == ["生"]
        raw_index = (root / "one" / "lookup.idx").read_bytes()
        magic, fmt, norm, count, reserved = HEADER.unpack_from(raw_index)
        assert (magic, fmt, norm, count, reserved) == (MAGIC, 1, 1, 6, 0)
        assert len(raw_index) == HEADER.size + count * ROW.size
        key_bytes = (root / "one" / "keys.utf8").read_bytes()
        decoded_keys = []
        prev = None
        for offset in range(count):
            key_offset, key_length, payload_offset, payload_length, entry_id = ROW.unpack_from(raw_index, HEADER.size + offset * ROW.size)
            key = key_bytes[key_offset:key_offset + key_length].decode("utf-8", "strict")
            assert prev is None or (prev[0], prev[1]) < (key.encode("utf-8"), entry_id)
            prev = (key.encode("utf-8"), entry_id)
            line = (root / "one" / "entries.jsonl").read_bytes()[payload_offset:payload_offset + payload_length]
            entry = json.loads(line)
            assert entry["id"] == entry_id
            decoded_keys.append(key)
        assert decoded_keys == ["いきる", "かな", "しょう", "せい", "生", "生きる"]
        # ElementTree does not resolve external entities; the undefined entity must fail closed.
        hostile = root / "xxe.xml"
        hostile.write_text('<!DOCTYPE JMdict [<!ENTITY x SYSTEM "file:///C:/Windows/win.ini">]><JMdict><entry><ent_seq>1</ent_seq><r_ele><reb>&x;</reb></r_ele></entry></JMdict>', encoding="utf-8")
        try:
            build(hostile, root / "hostile")
        except (ET.ParseError, ValueError):
            pass
        else:
            raise AssertionError("external XML entity unexpectedly parsed")
        typo = root / "unknown-source-element.xml"
        typo.write_text(fixture.read_text(encoding="utf-8").replace("re_inf", "r_inf"), encoding="utf-8")
        try:
            build(typo, root / "unknown")
        except ValueError as exc:
            assert "unsupported r_ele element: r_inf" in str(exc)
        else:
            raise AssertionError("unknown source element was silently ignored")
        print(json.dumps({"selfTest": "passed", "fixture": result1}, ensure_ascii=False, indent=2))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, help="official JMdict_e.gz or legacy XML source file")
    parser.add_argument("--out", type=Path, help="output pack directory")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if not args.source or not args.out:
        parser.error("--source and --out are required unless --self-test is used")
    result = build(args.source, args.out)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
