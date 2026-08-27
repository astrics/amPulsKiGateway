# Projekt 8: Candidate Rerank

## Ziel

Projekt 8 verwendet einen neuen, getrennten Analyseweg. Die bisherigen Projekte und der globale Standard `two_stage` bleiben unveraendert.

Der neue Ablauf ist:

1. Eine deterministische, mehrsprachige Vorauswahl bewertet alle 38 CSS-Codes.
2. Die 14 staerksten Codes inklusive relevanter Verwechslungsfaelle werden als Kandidaten ausgewaehlt.
3. Das vorhandene LM-Studio-Modell bewertet alle Kandidaten in **einem** strukturierten Aufruf.
4. Der Parser akzeptiert nur IDs aus der Kandidatenliste und verlangt Textbelege fuer ausgewaehlte Codes.
5. Unsichere oder formal unvollstaendige Antworten werden mit `needsReview` und `validationWarnings` markiert.

Es ist kein zweites Modell und kein Embedding-Server notwendig. Damit bleibt die bestehende Serverkonfiguration verwendbar.

## Aktivierung

In `appsettings.json` ist der neue Weg nur fuer Projekt 8 aktiviert:

```json
"ClassificationMode": "two_stage",
"ClassificationModeByProject": {
  "8": "candidate_rerank"
}
```

Nach Deployment der Gateway-Dateien muss der Gateway-Prozess neu gestartet werden.

## Projekt 8 vorbereiten

Das idempotente SQL-Skript

`AI/prepare_css_project8_from_project4_candidate_rerank.sql`

legt Projekt 8 bei Bedarf an, kopiert Codes und Aussagen aus Projekt 4 und setzt ausschliesslich die Projekt-8-Aussagen auf `new`. Andere Projekte werden nicht geloescht oder zurueckgesetzt.

Der bestehende Cronjob kann anschliessend mit Projekt 8 verwendet werden, zum Beispiel:

```text
ampuls_ai_cronjob_project7.php?project_id=8&fetch_limit=50&send_limit=1&ignore_cache=1
```

## Diagnose vor einem LLM-Lauf

Der neue Endpunkt zeigt die Kandidaten ohne LM-Studio-Aufruf:

```http
POST /api/analysis/candidate-preview
X-API-Key: <CSS-API-Key>
Content-Type: application/json

{
  "text": "Gute online Loesungen, Preise",
  "projectId": 8
}
```

Die Antwort enthaelt `classificationMode`, `candidateCodeIds`, Scores, Trefferbegriffe und erweiterte Verwechslungsfaelle.

## Neue Diagnosefelder im Ergebnis

- `candidateCodeIds`: Codes, aus denen das Modell waehlen durfte
- `needsReview`: Ergebnis sollte fachlich geprueft werden
- `validationWarnings`: konkrete Parser- oder Evidenzwarnungen

Diese Felder werden im synchronen Ergebnis, im ResultStore und in Ergebnisdateien mitgefuehrt.

## Lokale Referenzmessung

Die Vorauswahl wurde gegen 143 vorhandene, manuell kodierte Aussagen aus Projekt 4 geprueft:

- 250 manuelle Codezuordnungen
- 224 davon in der Top-14-Kandidatenliste
- Kandidaten-Recall: **89,6 %**
- Bei 120 von 143 Aussagen waren alle manuellen Codes enthalten: **83,9 %**
- Keine technischen Fehler im Test

Diese Messung bewertet nur die Vorauswahl. Die eigentliche Trefferquote des Gesamtwegs muss nach dem Projekt-8-Lauf separat gegen die Kundenkodierung ausgewertet werden.

