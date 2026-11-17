# CSS-SAV-Verarbeitung auf ASTRICSAI

Das Kommandozeilentool verarbeitet SAV-Dateien auf dem KI-Server. Es speichert
Import und Kodierungen in `D:/AI/Data/CSS/css.sqlite3` und verwendet den
CSS-Gateway auf demselben Rechner. Das Profil `Gemma4_20Pilot` lauscht nach
dem Neustart zusätzlich auf `127.0.0.1:5000`. LM Studio verwendet weiter
Gemma 4, Kontext 16384 und eine gleichzeitige Generierung.

## Einrichten und importieren

1. Gateway-Quellstand mit `Tools/CssLocal` übertragen und `Gemma4_20Pilot`
   neu starten.
2. `kont WT.sav` und optional die separate Oktober-SAV nach
   `D:/AI/Data/CSS/Inbox` kopieren. `october-results.json` und
   `manual-rules-20261106.json` nach `D:/AI/Data/CSS` kopieren. Der
   Ergebnisbestand enthält die 443 bereits übernommenen Oktober-Kodierungen,
   die Regeldatei 474 aktive Kundenregeln aus dem Stand vom 6. November.
3. PowerShell im Gateway-Quellordner öffnen und ausführen.

```powershell
py -3.12 -m pip install pandas pyreadstat==1.3.8
$cssData = 'D:/AI/Data/CSS'
$cssRunner = './Tools/CssLocal/css_local.py'
py -3.12 $cssRunner --root $cssData import --seed "$cssData/october-results.json"
py -3.12 $cssRunner --root $cssData status
```

Der Schlüssel besteht aus `INTNR`, `Monat` als `JJJJMM` und der
Quellvariablen `F06_2025`. Beide SAV-Dateien liefern die Erhebungsperiode
auf Monatsebene. Der Import vergleicht zusätzlich den normalisierten Text
und den Wert aus `F05_2025`. Ein Konflikt bricht den gesamten Import ab.
Identische Antworten aus mehreren Dateien und wiederholte Importe behalten
die gespeicherte Kodierung. Leere Texte und die CSS-Platzhalter `-`, `k.a.`
und `keine Angabe` werden ausgespart. Bei den zwei vorliegenden Dateien
führt der erste Import mit Oktober-Ergebnissen zu 9’859 eindeutigen
Aussagen, davon 443 mit bereits gespeichertem Ergebnis.
Von den 9’416 offenen Aussagen treffen 1’393 exakt auf eine Kundenregel.
Damit bleiben 8’023 Modellanfragen. Bei rund 13 Sekunden pro Anfrage
entspricht das etwa 29 Stunden reiner Modelllaufzeit.

## Kodieren

```powershell
$env:CSS_GATEWAY_API_KEY = Read-Host 'API-Key des CSS-Gateways' -MaskInput
py -3.12 $cssRunner --root $cssData classify --rules "$cssData/manual-rules-20261106.json" --limit 10
py -3.12 $cssRunner --root $cssData classify --rules "$cssData/manual-rules-20261106.json"
Remove-Item Env:CSS_GATEWAY_API_KEY
```

Der erste Aufruf kodiert bis zu zehn noch offene Aussagen zur Kontrolle.
Der zweite verarbeitet den Rest. Exakte Kundenregeln greifen vor dem
Modellaufruf. Jeder abgeschlossene Fall wird sofort gespeichert. Bei
Unterbrechung denselben Befehl mit derselben Datenbank, demselben Gateway-
Profil und Regelstand wiederholen. Während eines Laufs den Gateway mit
seinem Ergebnisverzeichnis und die lokale SQLite-Datei beibehalten.

## SPSS-Datei erzeugen

Für die Produktionsablage ist `process-inbox` der vollständige Ablauf.
Lege fertig kopierte Original-SAVs unter `Inbox` ab und starte auf ASTRICSAI:

```powershell
$env:CSS_GATEWAY_API_KEY = Read-Host 'API-Key des CSS-Gateways' -MaskInput
py -3.12 $cssRunner --root $cssData process-inbox --seed "$cssData/october-results.json" --rules "$cssData/manual-rules-20261106.json"
Remove-Item Env:CSS_GATEWAY_API_KEY
```

Der Befehl importiert alle SAV-Dateien aus `Inbox`. Überschneidungen werden
je Aussage anhand des Importschlüssels geprüft. Nach der Kodierung
entsteht pro Original eine geprüfte Datei `<Name> - CSS kodiert.sav` in
`Outbox`. Danach wird das Original mit seinem Namen nach `_done`
verschoben. Die SQLite-Datenbank hält die Prüfsummen beider Dateien fest.
Nach einer Unterbrechung denselben Befehl erneut ausführen. Bereits
archivierte, identisch erneut abgelegte Dateien werden gemeldet und bleiben
zur manuellen Klärung in `Inbox`. Namenskonflikte mit abweichendem Inhalt
halten den Lauf an.

Mit `--export` schreibt der vollständige Kodierlauf die angereicherte SAV
direkt nach Abschluss aller offenen Aussagen. Bei mehreren gewünschten
Ausgaben kann `--export QUELLE ZIEL` wiederholt werden.

```powershell
py -3.12 $cssRunner --root $cssData classify --rules "$cssData/manual-rules-20261106.json" --export "$cssData/Inbox/kont WT.sav" "$cssData/kont WT - CSS kodiert.sav"
```

Für einen späteren Einzel-Export aus gespeicherten Ergebnissen:

```powershell
py -3.12 $cssRunner --root $cssData export "$cssData/Inbox/kont WT.sav" "$cssData/kont WT - CSS kodiert.sav"
```

Die neue Datei behält Zeilenfolge, Originalwerte und die geprüften
Variablenattribute. Der Export verweigert die Ausgabe, wenn für eine
auswertbare Antwort ein Ergebnis fehlt oder Text oder Score abweichen.

| Variable | Belegung |
| --- | --- |
| `CSS_C01` bis `CSS_C38` | 0 = nicht vergeben, 1 = vergeben |
| `CSS_S01` bis `CSS_S38` | -1 = negativ, 0 = neutral, 1 = positiv, System-Missing bei unvergebenem Code |
| `CSS_STATUS` | 0 = keine auswertbare Antwort, 1 = kodiert, 2 = zur Prüfung markiert |

Bei nicht auswertbaren Antworten bleiben alle Code- und Sentimentfelder
System-Missing. Die Werte werden nach dem Schreiben vollständig
zurückgelesen und verglichen. Dieser Ablauf schreibt in die lokale SQLite-
Datenbank und erzeugt SAV-Ausgaben für die Übergabe.
