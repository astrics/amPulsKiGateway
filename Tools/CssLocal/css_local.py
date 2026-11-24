"""Import CSS SAV files, classify new answers on the local gateway, and export SAV copies."""
import argparse
from contextlib import closing, contextmanager
from getpass import getpass
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import unicodedata
from urllib.error import HTTPError
from urllib.request import ProxyHandler, Request, build_opener
import uuid

import pandas as pd
import pyreadstat

FIELDS = ["INTNR", "Monat", "F05_2025", "F06_2025"]
VARIABLE = FIELDS[-1]
PLACEHOLDERS = {"", "keine angabe", "k.a.", "-"}
CODES = [str(number) for number in range(1, 39)]
SENTIMENT = {"Negativ": -1, "Neutral": 0, "Positiv": 1}
PROFILE = {
    "projectId": 13, "modelName": "gemma-4-12b-it-qat", "classificationMode": "candidate_rerank",
    "candidateLimit": 20, "allCandidates": False, "customerFeedbackEnabled": True,
    "responseTokenLimit": 1536,
}
GATEWAY_ID_BASE = 2100000000


def json_text(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)


def open_db(root):
    db = sqlite3.connect(root / "css.sqlite3")
    db.row_factory = sqlite3.Row
    db.executescript("""
        CREATE TABLE IF NOT EXISTS settings (name TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS deliveries (
            filename TEXT PRIMARY KEY,
            source_sha256 TEXT NOT NULL,
            output_sha256 TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS answers (
            id INTEGER PRIMARY KEY,
            intnr INTEGER NOT NULL,
            month INTEGER NOT NULL,
            source_variable TEXT NOT NULL,
            score INTEGER NOT NULL CHECK(score BETWEEN 1 AND 10),
            text TEXT NOT NULL,
            result TEXT,
            UNIQUE(intnr, month, source_variable)
        );
    """)
    with db:
        db.execute("INSERT OR IGNORE INTO settings VALUES ('dashboard', ?)", ("CSS-Local-" + uuid.uuid4().hex,))
    return db


@contextmanager
def locked(root):
    # A Windows file lock is released automatically if the runner exits or crashes.
    import msvcrt
    with (root / "run.lock").open("a+b") as lock:
        lock.seek(0)
        try:
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as exc:
            raise ValueError("Another CSS run is using this data directory") from exc
        yield


def answer_rows(columns):
    absent = [name for name in FIELDS if name not in columns]
    if absent:
        raise ValueError("Missing SAV variables: " + ", ".join(absent))
    for index, (intnr, month, score, text) in enumerate(zip(*(columns[n] for n in FIELDS), strict=True)):
        text = " ".join((text or "").split())
        if text.casefold() in PLACEHOLDERS:
            continue
        if (intnr is None or not 0 < intnr < 2**63 or intnr != int(intnr)
                or month is None or not 190001 <= month <= 999912 or month != int(month)
                or not 1 <= int(month) % 100 <= 12 or score not in range(1, 11)):
            raise ValueError(f"Invalid INTNR, Monat or F05_2025 on SAV row {index + 1}")
        yield index, (int(intnr), int(month), VARIABLE), int(score), text


def check_result(result):
    pairs = result["codes"]
    if (not isinstance(result["review"], bool) or not isinstance(pairs, dict)
            or any(code not in CODES or value not in SENTIMENT for code, value in pairs.items())):
        raise ValueError("Invalid code or sentiment in CSS result")
    return json_text(result)


def add_answer(db, key, score, text, result=None):
    if key[2] != VARIABLE:
        raise ValueError("Unexpected answer variable")
    existing = db.execute(
        "SELECT score, text, result FROM answers WHERE intnr=? AND month=? AND source_variable=?", key
    ).fetchone()
    if existing is None:
        db.execute(
            "INSERT INTO answers (intnr,month,source_variable,score,text,result) VALUES (?,?,?,?,?,?)",
            (*key, score, text, result),
        )
        return True
    if (existing["score"], existing["text"]) != (score, text):
        raise ValueError(f"Conflicting text or score for {key}; import rolled back")
    if result is not None:
        if existing["result"] is None:
            db.execute("UPDATE answers SET result=? WHERE intnr=? AND month=? AND source_variable=?", (result, *key))
        elif json.loads(existing["result"]) != json.loads(result):
            raise ValueError(f"Conflicting saved coding for {key}; import rolled back")
    return False


def import_data(db, sources, seed):
    inserted = duplicates = 0
    with db:
        db.execute("BEGIN IMMEDIATE")
        if seed:
            entries = json.loads(seed.read_text(encoding="utf-8"))
            for item in entries:
                key = tuple(item["key"])
                add_answer(db, key, item["score"], item["text"], check_result(item["result"]))
            print(f"Existing codings checked: {len(entries)}")
        for path in sources:
            columns, _ = pyreadstat.read_sav(path, usecols=FIELDS, output_format="dict", user_missing=True)
            new = same = 0
            for _, key, score, text in answer_rows(columns):
                if add_answer(db, key, score, text):
                    new += 1
                else:
                    same += 1
            inserted += new
            duplicates += same
            print(f"{path.name}: {new} new, {same} already present")
    print(f"Import committed: {inserted} new answers, {duplicates} repeated answers")


def remember(db, name, value):
    with db:
        db.execute("INSERT OR IGNORE INTO settings VALUES (?,?)", (name, value))
        previous = db.execute("SELECT value FROM settings WHERE name=?", (name,)).fetchone()[0]
        if previous != value:
            raise ValueError(f"The saved {name} differs; use the original configuration to resume")


def code_number(item):
    return str(item.get("externalCodeNr") or item["id"])


def compact_result(saved):
    matches = saved["codeMatches"]
    codes = {code_number(match): match["sentiment"] for match in matches}
    keywords = {code_number(keyword) for keyword in saved["keywords"]}
    if len(codes) != len(matches) or set(codes) != keywords:
        raise ValueError("Gateway returned mismatched or duplicate codes")
    return {
        "codes": codes,
        "review": bool(saved.get("needsReview") or saved.get("validationWarnings")),
    }


def manual_result(rules, row):
    rule = rules.get(unicodedata.normalize("NFC", row["text"]).lower())
    if rule is None:
        return None
    codes = {}
    for code, sentiment in rule["codes"].items():
        if sentiment == "kuzu_price":
            score = row["score"]
            sentiment = "Negativ" if score <= 4 else "Positiv" if score >= 8 else "Neutral"
        codes[code] = sentiment
    return {"codes": codes, "review": False, "rule": [rule["id"], rule["revision"]]}


def gateway_client(endpoint, prompt_key=False):
    key = os.environ.get("CSS_GATEWAY_API_KEY")
    if not key and prompt_key:
        key = getpass("API-Key des CSS-Gateways: ")
    if not key:
        raise ValueError("Set CSS_GATEWAY_API_KEY for the active CSS profile")
    opener = build_opener(ProxyHandler({}))

    def call(path, body=None):
        request = Request(endpoint.rstrip("/") + path, data=json_text(body).encode() if body is not None else None,
                          headers={"X-Api-Key": key, "Content-Type": "application/json"})
        with opener.open(request, timeout=240) as response:
            return json.load(response)

    return call


class UnusableGatewayResult(ValueError):
    """The gateway replied, but the individual answer has no usable coding."""


def gateway_result(call, row, dashboard):
    gateway_id = GATEWAY_ID_BASE + row["id"]
    if gateway_id > 2147483647:
        raise ValueError("Local gateway ID range exhausted")
    path = f"/analysis/result/{gateway_id}?dashboard={dashboard}&projectId=13"

    def read_saved():
        try:
            saved = call(path)
        except HTTPError as error:
            if error.code != 404:
                raise
            return None, "Gateway-Ergebnis nicht gefunden (HTTP 404)"
        saved = lower_keys(saved)
        if (saved.get("statementId"), saved.get("projectId"), saved.get("dashboard"), saved.get("text")) != (
            gateway_id, 13, dashboard, row["text"]
        ):
            # The endpoint can fall back to the shared store and return another dashboard's ID.
            if saved.get("dashboard") != dashboard:
                return None, "Gateway-Ergebnis gehört zu einem anderen Lauf"
            raise ValueError(f"Gateway result does not match local row {row['id']}")
        if saved.get("parseError"):
            return None, "Gateway meldet einen Parserfehler"
        if saved.get("status") != "completed" or saved.get("errorMessage"):
            return None, "Gateway-Ergebnis ist im Fehlerstatus oder unvollständig"
        if saved.get("cachedFrom"):
            return None, "Gateway hat ein Cache-Ergebnis geliefert"
        return compact_result(saved), ""

    saved, _ = read_saved()
    if saved is not None:
        return saved
    request = {
        "statementId": gateway_id, "projectId": 13, "dashboard": dashboard,
        "text": row["text"], "customerSatisfactionScore": row["score"], "ignoreCache": True,
    }
    label = f"Zeile {row['id']} (INTNR {row['intnr']}, Monat {row['month']}, Gateway-ID {gateway_id})"
    for attempt in (1, 2):
        response = call("/analysis/analyze", request)
        if response.get("status") != "completed" or response.get("cached") is not False:
            reason = "Gateway meldet keine neue abgeschlossene Kodierung"
        else:
            saved, reason = read_saved()
            if saved is not None:
                return saved
        if attempt == 1:
            print(f"{label}: {reason}. Ein neuer Versuch folgt.", flush=True)
    raise UnusableGatewayResult(f"{label}: {reason}")


def lower_keys(value):
    if isinstance(value, dict):
        return {key[:1].lower() + key[1:]: lower_keys(item) for key, item in value.items()}
    if isinstance(value, list):
        return [lower_keys(item) for item in value]
    return value


def check_preflight(call, required_feedback_revision):
    print("LM-Studio-Vorgaben: Gemma 4 12B QAT, Kontext 16384, Max Concurrent Predictions 1, "
          "Thinking AUS (Template: enable_thinking = false), GPU-Offload 48, CPU-Threads 6, "
          "Eval Batch 2048, Physical Batch 512. Nach Änderung Modell entladen und neu laden.",
          flush=True)
    preview = call("/analysis/candidate-preview", {
        "projectId": 13, "text": "Der Kundenservice war freundlich."
    })
    if any(preview.get(key) != value for key, value in PROFILE.items()):
        raise ValueError("Active gateway is not the expected Gemma4_20Pilot configuration")
    if required_feedback_revision is not None and preview.get("customerFeedbackRevision") != required_feedback_revision:
        raise ValueError("Transfer the updated gateway sources and restart Gemma4_20Pilot before this test")
    try:
        report = call("/analysis/preflight", {})
    except HTTPError as error:
        if error.code == 404:
            raise ValueError("Gateway-Update mit PreflightController.cs fehlt. Gateway neu bauen und starten.") from error
        raise
    for warning in report.get("warnings", []):
        print("Manuell prüfen: " + warning, flush=True)
    for name, value in report.get("settings", {}).items():
        print(f"LM Studio: {name} = {value}", flush=True)
    if report.get("passed") is not True:
        raise ValueError("Vorprüfung fehlgeschlagen. Kodierung gestoppt. " + " ".join(report.get("problems", [])))
    print("Vorprüfung bestanden: Modell antwortet mit JSON ohne Thinking.", flush=True)
    return preview


def classify(db, endpoint, rule_path, limit, prompt_key=False, required_feedback_revision=None):
    pending = db.execute("SELECT * FROM answers WHERE result IS NULL ORDER BY id LIMIT ?", (limit,)).fetchall()
    if not pending:
        print("All imported answers already have results")
        return
    rule_bytes = rule_path.read_bytes()
    rules = json.loads(rule_bytes)
    if not isinstance(rules, dict):
        raise ValueError("The manual rule snapshot must be a JSON object")
    for text, rule in rules.items():
        if (not text or not isinstance(rule, dict) or not isinstance(rule.get("codes"), dict)
                or not rule["codes"] or type(rule.get("id")) is not int or rule["id"] < 1
                or type(rule.get("revision")) is not int or rule["revision"] < 1
                or any(code not in CODES or not (sentiment in SENTIMENT
                       or (code == "6" and sentiment == "kuzu_price"))
                       for code, sentiment in rule["codes"].items())):
            raise ValueError("Invalid entry in manual rule snapshot")
    rule_hash = hashlib.sha256(rule_bytes).hexdigest()
    with db:
        db.execute("INSERT OR REPLACE INTO settings VALUES ('manual rules', ?)", (rule_hash,))
    print(f"Manual rules: {len(rules)} active from {rule_path.name} (SHA-256 {rule_hash})", flush=True)
    dashboard = db.execute("SELECT value FROM settings WHERE name='dashboard'").fetchone()[0]
    call = gateway_client(endpoint, prompt_key)
    preview = check_preflight(call, required_feedback_revision)
    remember(db, "gateway prompt", preview["systemPromptSha256"])
    total = len(pending)
    saved_count = 0
    failures = []
    for index, row in enumerate(pending, 1):
        result = manual_result(rules, row)
        if result is None:
            try:
                result = gateway_result(call, row, dashboard)
            except UnusableGatewayResult as error:
                check_preflight(call, required_feedback_revision)
                failures.append(str(error))
                print(f"Fall bleibt offen: {error}", flush=True)
                continue
        with db:
            db.execute("UPDATE answers SET result=? WHERE id=? AND result IS NULL", (check_result(result), row["id"]))
        saved_count += 1
        print(f"{index}/{total} geprüft · {saved_count} gespeichert", flush=True)
    if failures:
        raise SystemExit("Kodierung beendet, offene Fehlerfälle. SAV-Ausgabe und Archivierung warten:\n"
                         + "\n".join(failures))


def export_data(db, source, target):
    if source.resolve() == target.resolve() or target.exists():
        raise ValueError("Choose a new target SAV filename")
    frame, metadata = pyreadstat.read_sav(source, user_missing=True, disable_datetime_conversion=True)
    if metadata.mr_sets:
        raise ValueError("The source defines multiple-response sets that this writer cannot retain")
    n = len(frame)
    names = {"CSS_STATUS": "CSS Verarbeitungsstatus"}
    codebook = Path(__file__).resolve().parents[2] / "AIGateway.CSS/Codebooks/css-codes.imported.json"
    labels_by_code = {str(item["number"]): item["code"]
                      for item in json.loads(codebook.read_text(encoding="utf-8"))["labels"]}
    for code in CODES:
        names[f"CSS_C{int(code):02}"] = f"CSS {code}: {labels_by_code[code]}"
        names[f"CSS_S{int(code):02}"] = f"CSS {code} Sentiment: {labels_by_code[code]}"
    if {name.casefold() for name in names} & {name.casefold() for name in frame.columns}:
        raise ValueError("CSS output columns already exist in this SAV")
    extra = pd.DataFrame(float("nan"), index=range(n), columns=list(names))
    extra["CSS_STATUS"] = 0.0
    code_columns = list(extra.columns[1::2])
    cases = list(answer_rows(frame[FIELDS].to_dict("list")))
    extra.loc[[index for index, *_ in cases], code_columns] = 0.0
    for index, key, score, text in cases:
        row = db.execute(
            "SELECT score,text,result FROM answers WHERE intnr=? AND month=? AND source_variable=?", key
        ).fetchone()
        if row is None or (row["score"], row["text"]) != (score, text) or row["result"] is None:
            raise ValueError(f"No matching completed result for {key}; no export written")
        result = json.loads(row["result"])
        check_result(result)
        extra.at[index, "CSS_STATUS"] = 2 if result["review"] else (1 if result["codes"] else 3)
        for code, sentiment in result["codes"].items():
            extra.at[index, f"CSS_C{int(code):02}"] = 1
            extra.at[index, f"CSS_S{int(code):02}"] = SENTIMENT[sentiment]
    output = pd.concat([frame, extra], axis=1)
    # ReadStat infers string storage widths from the data. SPSS discards trailing padding.
    if n:
        for name, width in metadata.variable_storage_width.items():
            if not metadata.original_variable_types[name].startswith("A"):
                continue
            value = output.at[0, name]
            output.at[0, name] = value + " " * max(0, width - len(value.encode("utf-8")))
    labels = {**metadata.column_names_to_labels, **names}
    values = dict(metadata.variable_value_labels)
    values["CSS_STATUS"] = {0: "Ausgeschlossen", 1: "Kodiert mit Code", 2: "Prüfen",
                            3: "Verarbeitet – ohne Code"}
    for code in CODES:
        values[f"CSS_C{int(code):02}"] = {0: "Nicht vergeben", 1: "Vergeben"}
        values[f"CSS_S{int(code):02}"] = {value: label for label, value in SENTIMENT.items()}
    # Write beside the target so the verified file can be renamed atomically on Windows.
    temporary = target.with_name(f".{target.stem}-{uuid.uuid4().hex}.sav")
    try:
        pyreadstat.write_sav(
            output, temporary, row_compress=True, file_label=metadata.file_label or "",
            column_labels=labels, variable_value_labels=values, missing_ranges=metadata.missing_ranges,
            variable_display_width=metadata.variable_display_width, variable_measure=metadata.variable_measure,
            variable_format={**metadata.original_variable_types, **dict.fromkeys(names, "F2.0")},
            note=metadata.notes or None,
        )
        _, written = pyreadstat.read_sav(temporary, metadataonly=True, user_missing=True)
        if written.number_rows != n or written.column_names != list(output.columns):
            raise ValueError("SPSS round trip changed the number of rows or the order of variables")
        for attribute in ("column_names_to_labels", "variable_value_labels", "missing_ranges",
                          "original_variable_types", "variable_storage_width",
                          "variable_display_width", "variable_measure"):
            original = getattr(metadata, attribute)
            exported = getattr(written, attribute)
            if any(exported.get(name) != value for name, value in original.items()):
                raise ValueError(f"SPSS round trip changed existing {attribute}")
        del output
        block_size = 10000
        for start in range(0, n, block_size):
            block, _ = pyreadstat.read_sav(temporary, user_missing=True, disable_datetime_conversion=True,
                                          row_offset=start, row_limit=block_size)
            pd.testing.assert_frame_equal(frame.iloc[start:start + block_size].reset_index(drop=True),
                                          block[frame.columns], check_dtype=False)
            pd.testing.assert_frame_equal(extra.iloc[start:start + block_size].reset_index(drop=True),
                                          block[extra.columns], check_dtype=False)
        temporary.rename(target)
    finally:
        temporary.unlink(missing_ok=True)
    print(f"Verified {n} SPSS rows with {len(names)} additional variables: {target}")


def file_sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def process_inbox(db, root, endpoint, rules, seed, prompt_key=False):
    inbox, done, outbox = (root / name for name in ("Inbox", "_done", "Outbox"))
    staging = root / ".export-staging"
    for directory in (inbox, done, outbox, staging):
        directory.mkdir(exist_ok=True)
    pending = []
    for source in sorted(inbox.glob("*.sav")):
        digest = file_sha256(source)
        archive = done / source.name
        output = outbox / f"{source.stem} - CSS kodiert.sav"
        receipt = db.execute("SELECT * FROM deliveries WHERE filename=?", (source.name,)).fetchone()
        if receipt is not None and receipt["source_sha256"] != digest:
            raise ValueError(f"Different delivery already recorded as {source.name}")
        if archive.exists():
            if receipt is None or file_sha256(archive) != digest:
                raise ValueError(f"Archive filename already in use: {archive}")
            print(f"Already archived: {source.name}; repeated copy left in Inbox")
            continue
        if output.exists() and (receipt is None or file_sha256(output) != receipt["output_sha256"]):
            raise ValueError(f"Output filename already in use: {output}")
        pending.append((source, digest, archive, output, receipt))
    if not pending:
        print("No new SAV deliveries in Inbox")
        return

    import_data(db, [source for source, *_ in pending], seed)
    classify(db, endpoint, rules, -1, prompt_key)
    for source, digest, archive, output, receipt in pending:
        if file_sha256(source) != digest:
            raise ValueError(f"Source changed during processing: {source}")
        stage = staging / f"{source.stem}-{digest}.sav"
        if not output.exists():
            if not stage.exists():
                if receipt is not None:
                    raise ValueError(f"Restore the previously verified output before archiving: {output}")
                export_data(db, source, stage)
            output_digest = file_sha256(stage)
            if file_sha256(source) != digest:
                raise ValueError(f"Source changed during export: {source}")
            if receipt is None:
                with db:
                    db.execute("INSERT INTO deliveries VALUES (?,?,?)", (source.name, digest, output_digest))
            elif receipt["output_sha256"] != output_digest:
                raise ValueError(f"Previously verified output is missing or changed: {output}")
            stage.rename(output)
        source.rename(archive)
        print(f"Archived: {archive} | Export: {output}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("D:/AI/Data/CSS"))
    actions = parser.add_subparsers(dest="action", required=True)
    importer = actions.add_parser("import")
    importer.add_argument("sources", type=Path, nargs="*")
    importer.add_argument("--seed", type=Path)
    runner = actions.add_parser("classify")
    runner.add_argument("--rules", type=Path, required=True)
    runner.add_argument("--gateway", default="http://127.0.0.1:5000/api")
    runner.add_argument("--limit", type=int, default=-1)
    runner.add_argument("--export", action="append", nargs=2, type=Path, default=[],
                        metavar=("SOURCE", "TARGET"), help="Write a SAV copy after all pending answers are coded")
    exporter = actions.add_parser("export")
    exporter.add_argument("source", type=Path)
    exporter.add_argument("target", type=Path)
    inbox_runner = actions.add_parser("process-inbox", help="Import, classify, export and archive SAV deliveries")
    inbox_runner.add_argument("--seed", type=Path)
    inbox_runner.add_argument("--rules", type=Path, required=True)
    inbox_runner.add_argument("--gateway", default="http://127.0.0.1:5000/api")
    inbox_runner.add_argument("--prompt-key", action="store_true", help="Ask for the gateway key when needed")
    actions.add_parser("status")
    args = parser.parse_args()
    if args.action == "classify" and args.export:
        if args.limit != -1:
            parser.error("--export requires a complete classification run without --limit")
        targets = set()
        for source, target in args.export:
            resolved = target.resolve()
            if not source.is_file() or target.exists() or not target.parent.is_dir() or resolved in targets:
                parser.error(f"Invalid source or output path: {source} -> {target}")
            targets.add(resolved)
    args.root.mkdir(parents=True, exist_ok=True)
    with locked(args.root), closing(open_db(args.root)) as db:
        if args.action == "import":
            paths = args.sources or sorted((args.root / "Inbox").glob("*.sav"))
            if not paths and args.seed is None:
                parser.error("No SAV files found in Inbox")
            import_data(db, paths, args.seed)
        elif args.action == "classify":
            classify(db, args.gateway, args.rules, args.limit)
            for source, target in args.export:
                export_data(db, source, target)
        elif args.action == "export":
            export_data(db, args.source, args.target)
        elif args.action == "process-inbox":
            process_inbox(db, args.root, args.gateway, args.rules, args.seed, args.prompt_key)
        else:
            for row in db.execute("SELECT month,COUNT(*) total,COUNT(result) completed FROM answers GROUP BY month ORDER BY month"):
                print(dict(row))


if __name__ == "__main__":
    main()
