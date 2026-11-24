"""Run an isolated October acceptance test and compare it with the saved October coding."""
import argparse
from contextlib import closing
import hashlib
import json
from pathlib import Path

import css_local as css


EXPECTED = {
    926100348: {"6": "Positiv"},
    926100400: {"6": "Positiv", "24": "Neutral"},
    926100617: {"15": "Positiv"},
    926100502: {"15": "Negativ"},
    926100672: {"9": "Positiv"},
    926100623: {"24": "Neutral", "38": "Neutral"},
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, default=Path("D:/AI/Data/CSS"))
    parser.add_argument("--gateway", default="http://127.0.0.1:5000/api")
    parser.add_argument("--prepare-only", action="store_true")
    args = parser.parse_args()
    name = "16.939.26 - Werbetracking 202610.sav"
    sources = [args.data / folder / name for folder in ("Inbox", "_done")]
    source = next((path for path in sources if path.is_file()), None)
    if source is None:
        raise ValueError(f"October original not found in Inbox or _done: {name}")
    rules = args.data / "manual-rules-20261124.json"
    baseline = args.data / "october-results.json"
    if not rules.is_file() or not baseline.is_file():
        raise ValueError("Place manual-rules-20261124.json and october-results.json in the CSS data directory")
    root = args.data / "Tests/October-20261124"
    root.mkdir(parents=True, exist_ok=True)
    with css.locked(root), closing(css.open_db(root)) as db:
        css.remember(db, "test source", css.file_sha256(source))
        css.remember(db, "test rules", css.file_sha256(rules))
        css.import_data(db, [source], None)
        counts = db.execute("SELECT COUNT(*),MIN(month),MAX(month) FROM answers").fetchone()
        if tuple(counts) != (443, 202610, 202610):
            raise ValueError("The test must contain exactly the 443 October answers")
        if args.prepare_only:
            print(f"October test ready: {root}")
            return
        css.classify(db, args.gateway, rules, -1, prompt_key=True,
                     required_feedback_revision="2026-11-24")
        old = {tuple(item["key"]): item for item in json.loads(baseline.read_text(encoding="utf-8"))}
        changes = []
        accepted = []
        no_codes = []
        for row in db.execute("SELECT * FROM answers ORDER BY id"):
            key = (row["intnr"], row["month"], row["source_variable"])
            previous = old[key]
            if (previous["score"], previous["text"]) != (row["score"], row["text"]):
                raise ValueError("Baseline input differs from test input")
            result = json.loads(row["result"])
            if result["codes"] != previous["result"]["codes"]:
                changes.append({"intnr": row["intnr"], "text": row["text"],
                                "before": previous["result"]["codes"], "after": result["codes"]})
            if row["intnr"] in EXPECTED:
                accepted.append({"intnr": row["intnr"], "passed": result["codes"] == EXPECTED[row["intnr"]],
                                 "expected": EXPECTED[row["intnr"]], "actual": result["codes"]})
            if not result["codes"]:
                no_codes.append({"intnr": row["intnr"], "text": row["text"], "review": result["review"]})
        report = {"answers": 443, "acceptance": accepted, "changes": changes, "withoutCode": no_codes,
                  "rulesSha256": hashlib.sha256(rules.read_bytes()).hexdigest()}
        (root / "comparison.json").write_text(css.json_text(report), encoding="utf-8")
        output = root / "Oktober 2026 - CSS Test.sav"
        if not output.exists():
            css.export_data(db, source, output)
        print(f"Acceptance: {sum(item['passed'] for item in accepted)}/6; "
              f"changed: {len(changes)}; without code: {len(no_codes)}")
        print(f"Report: {root / 'comparison.json'}")


if __name__ == "__main__":
    main()
