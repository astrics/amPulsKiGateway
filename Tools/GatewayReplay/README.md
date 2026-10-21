# Wiederholbare Gateway- und Gesamtablauf-Tests

## Lokaler Dump und echter Cronjob

Die PHP-/DB-Simulation liegt im zweiten Workspace unter
`C:\ASTRICS\ampuls-dashboards\_KI-Tool\AI\LocalTests\README.md`.
Sie importiert den bereitgestellten SQL-Dump in eine getrennte lokale MySQL-DB
und fuehrt den bestehenden Projekt-8-Cronjob mit eingefrorener Stichprobe aus.
`Invoke-LocalBenchmark.ps1 -Target New|Old` waehlt den Gateway ohne Umstellung
des produktiven Cronjobs. Dieser PowerShell-Replay-Runner bleibt fuer den
isolierten Gateway-Test und Warm-up sinnvoll.

Fuer ASTRICSAI gibt es jetzt `appsettings.VpnBenchmark.json` (LAN-IP
192.168.178.60:5001, Modell qwen3.5-9b, Concurrency 1) und den Starter
`Start-CssGateway.ps1 -Profile VpnBenchmark -ModelName <API-Identifier>`.
`-Profile Production` startet mit der bestehenden Produktivkonfiguration.
Die beiden Starter variieren keine Prompts oder Codebuecher. Portfreigabe,
VPN-Route und exakter LM-Studio-Identifier muessen am Server geprueft werden.

Stand: 21. Oktober 2026. Lokaler Test-Runner fuer PowerShell 7; die verwendete
Syntax ist auch fuer Windows PowerShell 5.1 ausgelegt. Keine zusaetzlichen Pakete.
Der Runner ruft echte Analysen auf
und das Gateway speichert dabei Ergebnisse. Zuerst eine separate Pilotinstanz mit
eigenen Ergebnisordnern verwenden.

## Drei Teststufen

| Stufe | Aufrufweg | Was sie nachweist |
| --- | --- | --- |
| A | PowerShell auf ASTRICSAI -> Gateway -> LM Studio | Kodierung, GPU-Inferenz, Parser und lokale Ergebnisablage |
| B | derselbe Runner im LAN bzw. auf einem VPN-Client -> Gateway | HTTP-Erreichbarkeit und Antwortzeit ueber diesen konkreten Netzweg |
| C | externer PHP-Test-Runner -> neuer Gateway -> LM Studio -> PHP | realer Server-Netzweg, PHP-Timeouts, optional DB und Ergebnisanzeige |

Ein funktionierender VPN-Tunnel am eigenen Laptop stellt KEINE Route vom
externen PHP-Host ins Buero bereit. Der PHP-Host muss selbst eine passende
VPN-/Netzroute oder einen gezielt eingerichteten Vermittlungsweg besitzen.
`127.0.0.1` auf dem PHP-Host bedeutet den PHP-Host, nicht ASTRICSAI.
Die bisher hinterlegte URL `https://ki.ampuls.ch/api` darf nicht ohne Pruefung
als neuer Server angenommen werden. Fuer den Pilot eine eigene erreichbare
Testadresse festlegen; Produktiv-Cronjobs waehrend des Vergleichs nicht umstellen.

Die vorbereiteten `GpuPilot`-Profile binden absichtlich auf Loopback:
CSS `127.0.0.1:5001`, Sympany `127.0.0.1:5000`. Fuer Stufe B/C muss der Gateway
an die LAN-IP oder die passenden Schnittstellen gebunden und seine Windows-
Firewallregel auf die benoetigten LAN-/VPN-Quellen begrenzt werden. Die LM-Studio-
API kann weiterhin lokal auf Port 1234 bleiben. RDP-Erreichbarkeit prueft nicht
den Gateway-Port; RDP und LM Studio nicht oeffentlich fuer diesen Test freigeben.

## Vorbereitung auf ASTRICSAI

1. Jeweils nur einen Gateway mit Last und genau ein dokumentiertes Modell
   betreiben. Im ersten Vergleich Prompt, Codebuch, Projekt-ID und Verfahren
   unveraendert lassen. Den Modellnamen nicht allein aus einem Alias ableiten.
2. `GET http://127.0.0.1:5001/api/health` pruefen. Ein gruener Healthcheck allein
   belegt noch keinen erfolgreichen Modellaufruf.
3. Testdatenordner pro Modelllauf setzen und Gateway neu starten, z.B.:

   ```powershell
   $env:Storage__ResultsPath = 'D:/AI/Tests/Data/qwen35-run01/results'
   $env:ResultFiles__OutputDir = 'D:/AI/Tests/Data/qwen35-run01/output'
   ```

   Diese Variablen gehoeren in das Terminal bzw. Startkonto DES GATEWAYS,
   nicht nur in das Terminal des Test-Runners. Bestehende Instanzen erst nach
   Abschluss laufender Jobs neu starten.
4. `settings.example.json` kopieren und mit den realen Werten fuellen.
   Keine appsettings-Datei als SettingsPath uebergeben: sie kann API-Schluessel
   enthalten. Der Runner kopiert die angegebene Settingsdatei unveraendert.
5. API-Schluessel im Runner-Terminal als Umgebungsvariable setzen. Er wird
   weder als Kommandozeilenargument benoetigt noch in das Manifest geschrieben.
   Die Ausgabedateien enthalten allerdings die angefragten Texte und Ergebnisse.

## Lokaler Smoke-Test

Aus dem Repository-Hauptordner, mit dem real konfigurierten Gateway-Schluessel:

```powershell
$env:AI_GATEWAY_API_KEY = '<Gateway-Schluessel>'
& .\Tools\GatewayReplay\Invoke-GatewayReplay.ps1 `
  -GatewayBaseUrl 'http://127.0.0.1:5001/api' `
  -ModelLabel 'qwen3.5-9b-q6-no-thinking' `
  -Target Css -ProjectId 8 `
  -Repeats 3 `
  -OutputRoot 'D:\AI\Tests\Runs'
```

Dies sendet vier feste synthetische DE-/FR-/IT-/EN-Aussagen dreimal plus eine
separate Warm-up-Anfrage. Sie eignen sich fuer einen Funktionscheck, nicht fuer
eine belastbare fachliche Bewertung. `ModelLabel` ist eine eigene Beschriftung;
es wechselt NICHT das Modell in LM Studio. Projekt 8 verwendet im aktuellen
CSS-Stand `candidate_rerank`. Die effektive Projektkonfiguration dokumentieren.

Fuer Sympany `-Target Sympany` und dessen Gateway-URL verwenden. `ProjectId`
wird fuer Sympany nicht uebertragen. Der gleiche Runner laeuft auf einem
LAN-/VPN-Rechner; dafuer nur `GatewayBaseUrl` auf die erreichbare Adresse von
ASTRICSAI aendern. Zeitpunkt und Netzwerkpfad im Laufprotokoll dokumentieren.

## Echter Modellvergleich

100-150 feste manuell kodierte Aussagen exportieren und als eingefrorene
JSON-Datei ablegen. Pro Fall koennen korrekte Referenzen hinterlegt werden:

```json
{
  "cases": [
    {
      "caseId": "eigene-stabile-id",
      "text": "Originaltext",
      "customerSatisfactionScore": 8,
      "expectedSentiment": "Positiv",
      "expectedCodeIds": [1, 2]
    }
  ]
}
```

`[1,2]` ist nur ein Formatbeispiel, keine Kodierempfehlung. Hier werden die
numerischen `keywords[].id` des passenden Gateway-Codebuchs verglichen, nicht
beliebige externe Codenummern. Eine fehlende `expectedCodeIds`-Eigenschaft
bedeutet keine Bewertung; `[]` bedeutet fachlich erwartete KEINE Codes.
Referenzen fuer Sympany und CSS nicht vermischen. Kundenzufriedenheitswert und
Projekt-ID beeinflussen den Prompt und muessen ebenfalls stabil bleiben.

```powershell
& .\Tools\GatewayReplay\Invoke-GatewayReplay.ps1 `
  -GatewayBaseUrl 'http://127.0.0.1:5001/api' `
  -ModelLabel 'qwen3.5-9b-q6-no-thinking' `
  -Target Css -ProjectId 8 `
  -CasesPath 'D:\AI\Tests\Cases\css-golden.json' `
  -SettingsPath 'D:\AI\Tests\Settings\qwen35-run01.json' `
  -Repeats 3 -OutputRoot 'D:\AI\Tests\Runs'
```

Pro Lauf entstehen:

- `manifest.json`: Zieladresse, Dataset-/Script-Hash, Parameter, Modellbeschriftung.
- `cases.json`: exakte Kopie der Eingaben.
- optional `settings.json`: dokumentierter Modell-/Runtime-/Promptstand.
- `responses.jsonl`: Anfragen, rohe Antworten und gespeicherte Parserdiagnosen.
- `results.csv`: ein Datensatz je Aufruf inkl. Warm-up-Markierung, Gesamtdauer,
  Gatewaydauer, Codeauswahl, Sentimenttreffer und technischen Fehlern.
- `summary.json`: Median, P95, Fehler-/Cache-/Reviewzahlen, Sentimenttreffer,
  Code-Micro-F1 und Zahl der Faelle mit unterschiedlicher Auswahl in Wiederholungen.

Warm-up wird nicht in die Messwerte eingerechnet. Das ist ein warmer, sequenzieller
Vergleich, kein Kaltstart- oder Parallelitaetsbenchmark. P95 verwendet nearest rank;
Median mittelt bei gerader Anzahl die mittleren Werte. Drei Wiederholungen messen
Streuung, erzeugen aber keine zusaetzlichen unabhaengigen fachlichen Beispiele.
Zeitstatistiken und Qualitaetswerte beziehen sich auf technisch erfolgreiche
Antworten; Fehlerzahl und Bewertungsnenner immer mitlesen. `needsReview` ist eine
eigene Diagnose und wird nicht automatisch als technischer Fehler gezaehlt.

`wallMs` misst den vollstaendigen POST bis zum Antworttext. `processingMs` ist die
vom Gateway gemeldete Zeit, keine reine GPU-Rechenzeit. Ihre Differenz umfasst
u.a. Transport, Proxy und weitere Gatewayarbeit; sie ist kein reiner VPN-Ping.
Der zusaetzliche GET der Parserdiagnose zaehlt NICHT zur POST-Zeit. Fuer GPU-/RAM-
Temperatur und Tokenrate parallel LM-Studio-Protokoll und `nvidia-smi` nutzen.

Cachebehandlung: `textHash=""` verhindert Hashwiederverwendung in beiden
Gateway-ResultStores, CSS erhaelt ausserdem `ignoreCache=true`. Unerwartete
Cachetreffer werden als Fehler markiert. Der Lauf benutzt einen eigenen
Dashboardnamen und neue Statement-IDs. Trotzdem eine Pilotinstanz mit eigenen
Datenordnern verwenden; es entstehen echte Gateway-Ergebnisdateien.

Der Runner prueft zusaetzlich das gespeicherte `ParseError`: Der aktuelle
synchrone Gateway kann nach einem Modell-Parserfehler HTTP 200 und `completed`
mit neutralem Ersatzwert liefern. HTTP 200 plus irgendein Sentiment reicht daher
nicht als Erfolgskriterium. Kann der Ergebnis-GET nicht gelesen werden, gilt die
Pruefung als unvollstaendig. Bei Transportfehler/401/403 wird der Lauf abgebrochen,
ohne automatische Retries, weil ein abgebrochener POST serverseitig weiterlaufen
kann. Exitcode 1 bedeutet technischen Fehler oder unvollstaendigen Lauf; fachliche
Abweichungen werden separat ausgewertet und setzen den Exitcode nicht auf 1.

## Externen PHP-Pfad gezielt pruefen

Vorhandene Datei: `AI/ampuls_ai_test_runner.php` im PHP-Projekt. Sie kann feste
Statements anfragen und muss fuer den Ersttest nicht neu geschrieben werden.
Schluessel serverseitig konfigurieren, nicht in Browser-URLs eintragen.

```text
/AI/ampuls_ai_test_runner.php?action=single&statement_id=<feste-ID>&project_id=<Projekt-ID>&write_db=0&ignore_cache=1&gateway_url=<URL-ENCODIERTE-NEUE-TESTADRESSE-MIT-/api>
```

`action=single` liest den Datensatz anhand der Statement-ID. Die wirksame
Projekt-ID stammt bei vorhandener DB-Spalte aus dem Datensatz und kann den
URL-Parameter uebersteuern. Deshalb den richtigen TESTDATENSATZ/-PROJEKT verwenden.
`write_db=0` verhindert PHP-DB-Schreibvorgaenge; das Gateway speichert trotzdem.
Der PHP-Runner ueberspringt mit `ignore_cache=1` seinen DB-Cache und uebertraegt
`ignoreCache` weiter. Das funktioniert im CSS-Weg; Sympany hat dieses Requestfeld
aktuell nicht. Fuer einen identischen direkten Vergleich denselben Text,
Zufriedenheitswert und dieselbe effektive Projekt-ID benutzen.

Der normale Cronjob mit `status=new` verarbeitet wechselnde Mengen. Fuer
reproduzierbare Tests feste IDs oder besser eine exportierte Eingabedatei
verwenden. `send_limit=3` allein garantiert keine identischen drei Aussagen.
Auch der bestehende PHP-Test-Runner bewertet HTTP 200 + Sentiment bereits als
Erfolg; fuer die Abnahme `ParseError`, Codes und Reviewwarnungen aus dem Gateway
zusaetzlich pruefen. Seine Gesamtdauer enthaelt DB-Zugriffe und bei `full` weitere
Phasen; sie ist nicht direkt mit der reinen Gatewaydauer vergleichbar.

Erst wenn der externe Aufruf ohne DB-Schreiben klappt, 3-5 dedizierte
Teststatements mit `write_db=1` durchlaufen lassen und pruefen:

1. Richtiger neuer Gateway und richtiges Modell wurden angesprochen.
2. DB-Status, Codeverknuepfungen und Ergebnisdateien sind konsistent.
3. Dashboard zeigt exakt diese Ergebnisse und keine alten Cachewerte.
4. Danach 20-30 Minuten Lasttest, Temperaturen und Fehler beobachten.
5. RDP-Verbindung trennen und weiter testen; anschliessend kontrollierter
   Neustart und pruefen, ob LM Studio und Gateway wie vorgesehen starten.

Identische Inputs und Einstellungen machen den Ablauf wiederholbar. Bitgenau
gleiche Modellantworten sind auch mit Temperature 0 nicht garantiert, besonders
nach Runtime-/GPU-/Parallelitaetswechseln. Deshalb pro Fall drei Wiederholungen
und normalisierte Code-/Sentimentvergleiche statt Vergleich der JSON-Formatierung.

Letzter Punkt der Installationsroutine bleibt die Umstellung auf .NET 10 LTS
mit Build-, Anfrage- und Neustarttest vor dem dauerhaften Betrieb.

## Offline-Test des Runners

```powershell
pwsh -NoProfile -File .\Tools\GatewayReplay\Test-GatewayReplay.ps1
```

Fuenf Szenarien gegen einen synthetischen Loopback-Server: gueltige Antwort,
HTTP-200-Parserfehler, unerwarteter Cachetreffer, ungueltiges JSON, HTTP 500.
Keine echte Inferenz und keine echte Datenbank. Der eigentliche Runner ist
fuer PowerShell 5.1 ausgelegt; dieser automatisierte Prueflauf benoetigt PowerShell 7.
Die Ausfuehrung unter 5.1 konnte auf der Entwicklungsmaschine wegen ihrer
Signaturpflicht nicht verifiziert werden. Falls Skriptausfuehrung auf ASTRICSAI
gesperrt ist, die betriebliche Signatur-/Freigaberegel verwenden; Richtlinien nicht
pauschal deaktivieren.
