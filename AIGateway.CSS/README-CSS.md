# AI Gateway CSS

Dieses Projekt ist als eigenstaendiges Schwesterprojekt zum bestehenden Sympany-Gateway angelegt.

## Ziel

- separate Ausfuehrung auf derselben Maschine oder spaeter auf einer zweiten Maschine
- eigene Konfiguration, eigene API-Keys, eigener Port und eigene Ergebnisdateien
- Startpunkt fuer das CSS-Codeprojekt mit 38 Codes

## Aktueller Stand

- Codebasis aus dem bestehenden Gateway kopiert
- Namespace und Projektname auf `AiGateway.CSS.Api` umgestellt
- eigener Port `5001`
- eigene Output-Pfade fuer Jobs und Resultate
- eigenes Beispiel-Codebook unter `Codebooks/css-codes.sample.json`
- importiertes Word-Codebook unter `Codebooks/css-codes.imported.json`

## Wichtig

Die CSS-Codegruppen, Codes, Beispiele und Codierregeln werden aus `Codebooks/css-codes.imported.json` geladen und von Prompt und Kandidatensuche verwendet. Nach einer Aktualisierung muss der Gateway neu gestartet werden, da das Codebook beim Start geladen wird.

## Codeliste Stand 16. Oktober 2026

Quelle: `Codierleitfaden_für WT_mit Beispielen ergänzt.docx`. Weiterhin 38 Codes; aktualisierte Beispiele bei 2, 3, 4, 5, 6, 11, 14, 15, 19, 32, 33, 34 und 38 sowie eine neue Bezeichnung für Code 24 (Apps / Portal inklusive allgemeinem Feedback). Die Bezeichnung wird unverändert aus der Vorlage übernommen. Codierregeln und Nummern bleiben unverändert. Obwohl bei Code 33 das Beispiel „keine Probleme“ entfernt wurde, verweist die unveränderte Regel von Code 35 weiterhin ausdrücklich auf Code 33; diese Zuordnung bleibt deshalb erhalten.

Datenbankskript: `C:/ASTRICS/ampuls-dashboards/_KI-Tool/AI/update_css_codebook_20261016.sql`. Es aktualisiert die vollständigen Codebücher der Projekte 4 bis 8 anhand von `external_code_nr`, erhält Keyword-IDs, Aktivstatus und bestehende Codierungen und ergänzt nur fehlende Codes. Grundlage ist der Dump `db12599328-ampulsaistatements_20261016.sql`. Die älteren Projekte 2 und 3 haben abweichende Teillisten ohne externe Nummern und werden nicht auf das neue Schema migriert. Das Skript wurde erstellt, nicht gegen eine Datenbank ausgeführt. Bestehende Analyseergebnisse werden nicht neu codiert.

Auch `AI/import_css_keywords_projects_4_5_6.sql` wurde auf den aktuellen Stand und den ID-erhaltenden Import aktualisiert. Der Importer unterstützt weitere Projekt-IDs mit `--projects`; Standard sind 4 bis 8. Bei Umbenennungen bestehender Codes muss eine externe Codenummer hinterlegt sein, damit die Zuordnung stabil bleibt.
