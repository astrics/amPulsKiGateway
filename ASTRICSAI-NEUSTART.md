# ASTRICSAI: LM Studio und Gateway nach einem Neustart

Naechster Modellvergleich: **Projekt 12 / Gemma4Pilot** mit Gemma 4 12B QAT Q4_0
und Limit 14. Hierfuer die separate Anleitung im PHP-Repository
`AI/PROJECT12_GEMMA4_TEST.md` verwenden. Das unten beschriebene Qwen-Chat-Template
nicht auf Gemma uebertragen; GPU-Layerzahl und nativer Thinking-Modus sind
modellspezifisch. Projekt 11 ist inzwischen abgeschlossen.

Abgeschlossener Kandidatenvergleich: **Projekt 11**, Visual-Studio-Profil
**CustomerRules20Pilot**. Es verwendet bis zu 20 Kandidaten ausschliesslich
fuer Projekt 11, bei gleichen Modell-/Load-Einstellungen wie unten.
`CustomerRulesPilot` bleibt die Variante fuer Projekt 10 mit Limit 14.
Die Schrittfolge fuer Projekt 11 steht im PHP-Repository unter
`AI/PROJECT11_CANDIDATES20_TEST.md`.

Stand: 05.11.2026. Vorlage fuer den lokalen Projekt-10-Test mit Qwen3.5-9B
auf RTX 5060 Ti mit 16 GB VRAM. Werte aus dem bereitgestellten Screenshot
und dem Gateway-Testprofil. Das ist die wiederherzustellende Konfiguration;
ein erfolgreicher vollstaendiger Projekt-10-Lauf steht noch aus.

## Kurzablauf

1. Mit dem eingerichteten Windows-Benutzer an ASTRICSAI anmelden.
2. LM Studio oeffnen und Qwen3.5-9B auswaehlen.
3. Vor dem Laden die Einstellungen und das Chat-Template unten pruefen.
4. Modell laden; bei Aenderungen ein bereits geladenes Modell entladen und neu laden.
5. Den lokalen LM-Studio-API-Server auf Port 1234 starten bzw. seinen Zustand pruefen.
6. In Visual Studio den CSS-Gateway mit Profil `CustomerRulesPilot` starten.
7. Health pruefen und vor einem grossen Lauf eine echte Klassifikationsantwort validieren.

Nicht davon ausgehen, dass nach einem Windows-/LM-Studio-Neustart automatisch
das richtige Modell, das angepasste Template und der API-Server aktiv sind.
Nach Modellwechseln oder Updates diese Liste ebenfalls durchgehen.

## LM Studio: Modell und Load-Einstellungen

Modell/API-Identifier: **`qwen3.5-9b`**. Nur das benoetigte Modell laden.
Die genaue GGUF-Datei und Quantisierung sind anhand des Screenshots nicht
feststellbar: vor Vergleichslaeufen in den Modellinformationen nachsehen und
zusammen mit LM-Studio- und Runtime-Version dokumentieren. Modellgroesse
„9B“ allein identifiziert die heruntergeladene Datei nicht eindeutig.

| Einstellung | Wiederherzustellender Wert |
| --- | --- |
| Context Length | 8192 |
| GPU Offload | 32, alle angezeigten Layer |
| CPU Thread Pool Size | 6 |
| Evaluation Batch Size | 2048 |
| Physical Batch Size | 512 |
| Max Concurrent Predictions | 1 |
| Unified KV Cache | Ein |
| Context Checkpoints | 32 |
| Reasoning Budget Message | Leer |
| RoPE Frequency Base | Auto, kein eigener Wert |
| RoPE Frequency Scale | Auto, kein eigener Wert |
| Offload KV Cache to GPU Memory | Ein |
| Keep Model in Memory | Ein |
| Try mmap() | Ein |
| Seed | Kein fester Wert; Random Seed wie im Screenshot |
| Speculative Decoding | Off |
| Flash Attention | Ein |
| K Cache Quantization Type | Kein eigener Wert aktiviert |
| V Cache Quantization Type | Kein eigener Wert aktiviert |

Diese Werte fuer den Vergleich beibehalten. Sie sind kein Nachweis, dass jede
Einstellung fuer jedes spaetere Modell optimal ist. Concurrency erst in einem
separaten Vergleichstest erhoehen.

## Wichtig: Thinking im aktiven Chat-Template ausschalten

Im Bereich **Load → Advanced → Chat Template** muss vor dem restlichen
Qwen-Template diese Zeile stehen:

```jinja
{%- set enable_thinking = false %}
```

Der Anfang sieht so aus:

```jinja
{%- set enable_thinking = false %}
{%- set image_count = namespace(value=0) %}
{%- set video_count = namespace(value=0) %}
```

Den restlichen Inhalt des vorhandenen Templates beibehalten. Eine vollstaendige
vorbereitete Kopie liegt unter
[Tools/GatewayReplay/qwen3.5-9b-no-thinking.jinja](Tools/GatewayReplay/qwen3.5-9b-no-thinking.jinja).
Diese Kopie gehoert zum bisherigen Modell-Template; bei einem anderen Modell
nicht ungeprueft uebernehmen.

Aenderung speichern. Ein bereits geladenes Modell anschliessend entladen und
neu laden. Das leere Feld „Reasoning Budget Message“ schaltet Thinking nicht ab.
Auch die Aufforderung „nur JSON“ im Prompt ersetzt diese Einstellung nicht.

Fehlerbild beim bisherigen Test: HTTP 200, aber leerer Antworttext,
`candidate_content_invalid` und JSON-Parserfehler. Im frueheren LM-Studio-Log
wurde das gesamte Antwortbudget fuer `reasoning_content` verbraucht,
mit `finish_reason: length`. Bei diesem Fehler erst das aktive Template und
den Modell-Reload pruefen; das Tokenlimit nicht als erste Massnahme erhoehen.

## LM-Studio-API-Server

- API-Server im Entwickler-/Serverbereich aktivieren, Port **1234**.
- Gateway und LM Studio laufen auf derselben Maschine. Die Gateway-Verbindung
  verwendet `http://127.0.0.1:1234`; der Chat-Endpunkt ist `/v1/chat/completions`.
- Modell-Identifier muss zum Gateway passen: `qwen3.5-9b`.
- LAN-/VPN-Clients greifen auf den Gateway-Port **5000** zu. Fuer diesen Aufbau
  braucht LM Studio keine zusaetzliche Freigabe ins LAN oder Internet.

Optionaler Erreichbarkeitstest auf ASTRICSAI, falls LM-Studio-Authentifizierung
nicht zusaetzlich aktiviert ist:

```powershell
Invoke-RestMethod http://127.0.0.1:1234/v1/models
```

## Gateway aus Visual Studio starten

1. Den aktuellen Quellstand auf ASTRICSAI oeffnen, bisher
   `D:\GitHub\amPulsKiGateway`.
2. **AiGateway.CSS.Api** als Startprojekt auswaehlen.
3. Neben dem Startpfeil das Profil **CustomerRulesPilot** auswaehlen.
4. Fuer Messlaeufe mit **Strg+F5** ohne angehaengten Debugger starten.

Das Profil ist in `AIGateway.CSS/Properties/launchSettings.json` definiert.
Beide Umgebungsvariablen sind auf `CustomerRulesPilot` gesetzt.
Die Einstellungen stammen aus `AIGateway.CSS/appsettings.CustomerRulesPilot.json`:

| Einstellung | Wert |
| --- | --- |
| Modell | qwen3.5-9b |
| Gateway MaxConcurrency | 1 |
| Projekt 10 | candidate_rerank mit abgestimmten Kundenregeln |
| CandidateLimit aus der Basiskonfiguration | 14 |
| CandidateTemperature aus der Basiskonfiguration | 0 |
| Antwortlimit fuer candidate_rerank | 1536 Tokens |
| Gateway-Adresse | http://192.168.178.60:5000 |
| Jobs | D:/AI/Tests/CustomerRules20261030/jobs |
| Output | D:/AI/Tests/CustomerRules20261030/output |
| Gespeicherte Ergebnisse | D:/AI/Tests/CustomerRules20261030/results |

Den freigegebenen lokalen Testschluessel `css-dev-key` in `Gateway:ApiKeys`
der aktiven Testkonfiguration bereithalten. Die Serverkopie wurde dafuer vom
Nutzer angepasst; beim Aktualisieren nicht versehentlich ueberschreiben.
Das ist ein lokaler Testschluessel, kein vorgesehener Kundenzugang.

Alternative aus PowerShell im Repository:

```powershell
.\Tools\GatewayReplay\Start-CssGateway.ps1 -Profile CustomerRulesPilot -RunLabel project10-customer-rules
```

Der PowerShell-Starter verwendet fuer diesen Aufruf stattdessen getrennte
Ordner unter `D:/AI/Tests/Gateway/project10-customer-rules/`.
Visual-Studio- und PowerShell-Variante nicht gleichzeitig auf Port 5000 starten.
Beide Starts schalten nur den Listener ein, sie starten keinen Benchmark.

## Pruefen, bevor ein Testlauf beginnt

Health im Browser aufrufen:

[http://192.168.178.60:5000/api/health](http://192.168.178.60:5000/api/health)

Erwartet: `status: healthy`, `lmStudio.isReachable: true`,
`lmStudio.modelLoaded: qwen3.5-9b`.
Das prueft die Erreichbarkeit, noch keine gueltige Kodierung.

Vor 143 Aussagen deshalb einen kontrollierten Klassifikationsaufruf pruefen:
gueltiger JSON-Antworttext, kein `ParseError`, keine Warnung
`candidate_content_invalid`, passende Projekt-ID und Code-Sentiments.
Der lokale Benchmark-Runner prueft die Projektzuordnung vor einem Reset und
bricht jetzt bei einer ungueltigen gespeicherten Antwort nach dem Tick ab.
Ein einzelner erfolgreicher Aufruf ist noch kein vollstaendiger Qualitaetstest.

## Bereits aufgetretene Startprobleme

- **Visual-Studio-Fehler `WebHotReloadAgent.TryStartSession` / doppelter Key:**
  Unter Extras → Optionen nach „Hot Reload“ suchen. Hot Reload sowohl beim
  Debuggen als auch beim Start ohne Debugging deaktivieren; Visual Studio neu starten.
- **`0x800711C7`, Anwendungssteuerungsrichtlinie blockiert DLL:**
  Dies wurde als Smart-App-Control-Sperre identifiziert. Fuer die lokale
  unsignierte Anwendung wurde das Deaktivieren mit dem Nutzer abgestimmt.
  Den aktuellen Status bei erneutem Auftreten pruefen; auf diesem Windows
  warnt die UI vor einer Neuinstallation zur spaeteren Reaktivierung.
  Vor Kundendeployment den fertigen Build inklusive eigener DLLs signieren.
- **HTTP 403 / ungueltiger API-Key:** Aktives Profil und dessen `Gateway:ApiKeys`
  pruefen; nach Konfigurationsaenderungen Gateway neu starten.
- **Health healthy, aber leere Klassifikation:** Thinking/Template pruefen;
  gueltige gespeicherte Ergebnisse kontrollieren, nicht nur HTTP 200.

## Offener Installationspunkt

Erinnerung aus der Servereinrichtung: **.NET 10 LTS** am Ende der
Installationsroutine beruecksichtigen. Die aktuellen Projekte zielen noch auf
`net9.0`; ein SDK-Installationsschritt ersetzt keine geplante Projektmigration.
