# GPU-Pilot: RTX 5060 Ti 16 GB, Ryzen 7 8700G, 32 GB RAM

Stand: 16. Oktober 2026. Empfehlungen fuer die lokale Klassifikation von
Versicherungskommentaren (CSS/Sympany), keine gemessenen GPU-Ergebnisse.

## Modellentscheidung

Erster Kandidat: **Qwen3.5-9B, GGUF Q6_K, ohne Thinking**. Neuer Vergleichskandidat:
**Gemma 4 12B IT, GGUF Q4_K_M**. **Gemma 3 12B IT, GGUF Q6_K** dient als
konservativer Vergleich innerhalb der bisherigen Modellgeneration.
Das bisher konfigurierte Gemma 3 4B auf derselben
GPU bildet die Referenz, um Hardwaregewinn und Modellwechsel getrennt zu messen.
Der Gewinner wird anhand eurer manuell kodierten Aussagen bestimmt; allgemeine
Benchmarks belegen keine bessere CSS-/Sympany-Kodierung.

| Modell / Datei | Downloadgroesse der Sprachgewichte | Rolle |
| --- | --- | --- |
| Gemma 3 4B, bisherige Quantisierung | bestehende Datei dokumentieren | Referenz mit unveraendertem Prompt |
| Qwen3.5-9B-Q6_K.gguf | 7,36 GB | erster Qualitaets-/Geschwindigkeitskandidat |
| Qwen3.5-9B-Q8_0.gguf | 9,53 GB | spaeterer Vergleich, falls Q6-Grenzfaelle auftreten |
| gemma-4-12B-it-Q4_K_M.gguf | 7,38 GB | aktueller mehrsprachiger Vergleichskandidat |
| Ministral-3-14B-Instruct-2512-Q5_K_M.gguf | 9,62 GB | optionaler dritter Anbieter; DE/FR/IT/EN unterstuetzt |
| gemma-3-12b-it-Q6_K.gguf | 9,66 GB | Vergleich innerhalb der bisherigen Modellfamilie |
| gemma-3-12b-it-Q4_K_M.gguf | 7,30 GB | Alternative bei Speicherdruck |

Dateigroesse ist nicht der gesamte VRAM-Bedarf. Kontext, Rechenpuffer,
gegebenenfalls Vision-Projektor, Windows-Anzeige und parallele Anfragen kommen
hinzu. Jeweils nur EIN Modell laden. Keine Vision-/Bildverarbeitung fuer diese
Textaufgabe aktivieren; einen von der Runtime erforderlichen Projektor nicht
manuell aus dem Modell entfernen. Zusaetzliche Downloads benoetigen SSD-Platz,
auch wenn sie nicht gleichzeitig geladen werden.

Qwen3.5 denkt standardmaessig nach. In LM Studio im Modell-Preset bzw. den
unterstuetzten Prompt-Template-Einstellungen Thinking deaktivieren und einen
API-Aufruf kontrollieren. Die offizielle Qwen-Referenz verwendet
`enable_thinking=false`; das ist keine Zusage, dass jede LM-Studio-Version diesen
Parameter auf `/v1/chat/completions` uebernimmt. `/no_think` oder `/nothink` ist fuer
Qwen3.5 kein offiziell unterstuetzter Ersatz. Wenn die installierte Runtime keinen
zuverlaessigen Nicht-Thinking-Modus liefert, zuerst Gemma 3 12B testen. Der Gateway
entfernt keine Denktexte und steuert Thinking mit diesem Profil nicht automatisch.

Aktuellere groessere Modelle wie Qwen3.8-27B sind auf dieser 16-GB-Karte kein
guter Einstieg: Eine nutzbare Quantisierung benoetigt zusaetzlich Platz fuer
Kontext und Puffer. CPU-/SSD-Offloading erschwert den Geschwindigkeitsvergleich.

## LM Studio: Startwerte auf dem neuen PC

Aktuelle stabile LM-Studio-Version, aktuelle kompatible NVIDIA-CUDA-Runtime in
LM Studio und einen passenden NVIDIA-Treiber installieren. Die RTX 5060 Ti als
Rechengeraet waehlen, nicht die integrierte Radeon-Grafik des 8700G.
LM Studio mit seinem nativen Windows-CUDA-Backend verwenden; WSL ist fuer diesen
Aufbau nicht erforderlich.

| Einstellung | Startwert | Begruendung / Anpassung |
| --- | --- | --- |
| GPU-Offload | alle moeglichen Layer auf RTX 5060 Ti | Nach Laden im Log kontrollieren; kein stiller Rueckfall auf CPU |
| Kontext | 8192 Tokens | enthaelt Systemprompt, Codebuch, Eingabe UND Antwort; bei laengstem echten Prompt messen, dann gegebenenfalls 16384 |
| Flash Attention | ein, soweit vom Modell/Backend unterstuetzt | weniger Kontextspeicher; bei Runtimefehlern erst Kompatibilitaet pruefen |
| KV-Cache auf GPU | ein, soweit unterstuetzt | Modell und Cache sollen in dedizierten VRAM passen |
| K-/V-Cache-Quantisierung | zunaechst Standard/F16; Q8_0 bei Speicherdruck und unterstuetzter Architektur | ein weiterer Qualitaets-/Kompatibilitaetsfaktor; kein Q4-Cache im ersten Vergleich |
| Evaluation Batch Size | 256, danach optional 512 vergleichen | Input-Verarbeitung; NICHT 256 Kundenkommentare in einer Anfrage |
| CPU-Threadpool | Auto; bei manueller Einstellung mit 8 beginnen | keine proportionalen Geschwindigkeitsversprechen aus Threadzahlen |
| Thinking / Reasoning | aus | kurze Klassifikationsausgabe, kein verstecktes Aufbrauchen des Antwortbudgets |
| Temperature | 0,1 im Gateway; spaeter kontrolliert 0 vergleichen | niedrige Streuung; auch 0 garantiert keine vollstaendige Reproduzierbarkeit |
| Top-P / Top-K / Repeat-Penalty | Runtime-/Modellstandard dokumentieren | im ersten Lauf keine weiteren Stellschrauben gleichzeitig veraendern |
| Speculative Decoding / Draft-Modell | aus fuer die erste Messung | zusaetzlicher VRAM-/Abstimmungsbedarf |
| parallele Generierungen | 1 insgesamt | erst nach stabiler Einzelmessung 2 testen |

8K ist ein Startwert, keine nachgewiesene Obergrenze eurer Prompts. Bei
Kontextueberlauf nicht Systemprompt oder Codebuch still abschneiden: erforderliche
Prompttokens plus Antwortreserve ermitteln und Kontext oder Prompt gezielt anpassen.
GPU-Speicher im Betrieb kontrollieren (`nvidia-smi` und LM-Studio-Logs); fuer
Anzeige und Lastspitzen Reserve lassen. Shared GPU Memory zaehlt nicht als
zusaetzlicher schneller VRAM.

Das Modell unter einem stabilen API-Identifier laden. Nach Download zuerst den
genauen Modellschluessel mit `lms ls` ermitteln:

```powershell
lms ls
lms load '<Modellschluessel aus lms ls>' --gpu max --context-length 8192 --estimate-only
lms load '<Modellschluessel aus lms ls>' --gpu max --context-length 8192 --identifier survey-classifier
lms server start
Invoke-RestMethod http://127.0.0.1:1234/v1/models
```

Flash Attention, Thinking und Cache-Einstellungen im Modell-Preset pruefen; die
obige CLI-Zeile setzt diese nicht. Beim Wechsel das vorherige Modell gezielt
entladen. Datei, Quantisierung, Revision und Runtimeversion pro Lauf protokollieren:
der Alias `survey-classifier` allein identifiziert den Modellstand nicht.
Mit `lms ps` pruefen, was tatsaechlich geladen ist. `/v1/models` und der aktuelle
Gateway-Healthcheck allein belegen weder erfolgreichen CUDA-Offload noch die
vollstaendige Funktionsfaehigkeit des gewaehlten Modells.

Fuer Gemma 4 ebenfalls einen Antworttest ohne Thinking durchfuehren: die
Modellkarte nennt leere Thought-Tags auch im deaktivierten Modus. Die Runtime muss
diese Kanalmarker korrekt verarbeiten, damit `message.content` sauberes JSON
enthaelt. Offizielle allgemeine Samplingempfehlungen unterscheiden sich von
unserem Klassifikationsprofil: Qwen3.5 Nicht-Thinking nennt Temperature 0,7,
Gemma 4 nennt 1,0. Die hier gewaehlten 0,1 sind ein aufgabenspezifischer
Vergleichswert und keine Herstellerempfehlung; bei Qualitaetsproblemen auch mit
dem jeweiligen Herstellerpreset gegenpruefen. Fuer Windows/NVIDIA GGUF waehlen,
keine MLX-Dateien fuer Apple Silicon.

## Vorbereitete Gateway-Profile

Die Dateien `AIGateway.CSS/appsettings.GpuPilot.json` und
`AIGateway.Sympany/appsettings.GpuPilot.json` werden erst mit der expliziten
Umgebung **GpuPilot** geladen. Die normalen appsettings-Dateien bleiben bestehen.

| Wert | CSS | Sympany |
| --- | --- | --- |
| LM-Studio-URL | http://127.0.0.1:1234 | http://127.0.0.1:1234 |
| Modell-ID | survey-classifier | survey-classifier |
| Gateway-Port | 5001 (Loopback) | 5000 (Loopback) |
| MaxConcurrency | 1 | 1 |
| Temperature | 0,1; CandidateTemperature weiter separat | 0,1 |
| Antwortbudget | 1536; Gruppen-Vorauswahl 512 | 768 |
| RequestTimeoutSeconds | 300 | 300 |
| Pilotdaten | C:/AI-Gateway/gpu-pilot/css | C:/AI-Gateway/gpu-pilot/sympany |

Antwortbudgets sind Obergrenzen, keine vorab belegten Ausgabe-Tokens. Bei
`finish_reason=length`, abgeschnittenem JSON oder besonders langen Originaltexten
das Budget gezielt erhoehen. Timeouts erst nach Messung senken, z.B. auf 120 s;
ein kuerzerer Timeout beschleunigt keine Inferenz.

Beide Gateways besitzen ihre eigene Parallelitaetssperre. Beide mit
`MaxConcurrency=1` gleichzeitig ergeben bis zu ZWEI Anfragen an LM Studio.
Fuer den ersten Benchmark jeweils nur einen Gateway mit Last betreiben.
Auch ein einzelner Batchjob arbeitet momentan sequenziell; blosses Erhoehen von
`MaxConcurrency` macht dessen Schleife nicht parallel.

Temperatur und Tokenlimits werden jetzt fuer alle vier aktiven Wege zentral
auf den HTTP-Payload angewandt: CSS synchron/Batch und Sympany synchron/Batch.
Prioritaet beim Tokenlimit:

1. `Gateway:ResponseTokenLimits:<Phase>`
2. explizites `Gateway:MaxResponseTokens`
3. bisheriger Standard des jeweiligen Aufrufs

CSS-Phasen: `single_pass`, `candidate_rerank`, `codegroup_preselection`,
`code_selection`, `chat`; Sympany: `classification`.
`CssCodebook:CandidateTemperature` hat weiterhin Vorrang vor
`Gateway:Temperature`. Ohne neue Einstellungen gelten die bisherigen Tokenbudgets;
der bislang unbegrenzte Sympany-Batchaufruf bleibt ohne explizite Konfiguration
unbegrenzt. Sync-Aufrufe beachten nun ebenfalls die konfigurierte Temperatur.
Die HTTP-Timeouts beider Sympany-Clients folgen jetzt `RequestTimeoutSeconds`.

`MaxTokensPerChunk` und `CharsPerToken` begrenzen in den hier betrachteten
Statement-Aufrufen nicht den Prompt und stellen NICHT das LM-Studio-Kontextfenster
ein. Sie werden deshalb im GPU-Profil nicht als Leistungsschalter verwendet.

### Start aus dem Repository

In je einem Terminal, nach Installation des .NET-9-SDK:

```powershell
$env:DOTNET_ENVIRONMENT = 'GpuPilot'
dotnet run --no-launch-profile --project .\AIGateway.CSS\AiGateway.CSS.Api.csproj
```

```powershell
$env:DOTNET_ENVIRONMENT = 'GpuPilot'
dotnet run --no-launch-profile --project .\AIGateway.Sympany\AiGateway.Sympany.Api.csproj
```

Die Profile enthalten keine neuen API-Schluessel und erben die bestehenden
Basiseinstellungen. Vor Fernzugriff einen eigenen Gateway-Schluessel verwenden.
Pilotserver sind zunaechst nur lokal erreichbar. Fuer Zugriff ueber VPN spaeter
die Gateway-Bindung und die Windows-Firewall gezielt fuer das interne Netz
konfigurieren; LM Studio kann auf Loopback bleiben, wenn Gateway und Inferenz
auf derselben Maschine laufen. Fuer den alten Gateway auf der VM waere stattdessen
die LM-Studio-URL plus dessen Netzwerkfreigabe anzupassen.

Die Pilotpfade nutzen C:, weil der gekaufte Rechner nicht zwingend ein D:-Laufwerk
hat. Vor Deployment Speicherorte und Schreibrechte des ausfuehrenden Kontos pruefen.

## Vergleich ohne alte Ergebnis-Caches

Der persistente ResultStore benutzt Texthashes (CSS zusaetzlich Projekt-ID), aber
keinen Modell-/Promptfingerprint. `CacheDurationMinutes=0` macht alte Ergebnisse
hier nicht automatisch ungueltig. Das neue Profil trennt Pilot und alte Ergebnisse,
trennt aber nicht automatisch jeden folgenden Modelllauf.

Pro Modelllauf vor dem Gateway-Start einen eigenen Ergebnisordner setzen, z.B.:

```powershell
$env:Storage__ResultsPath = 'C:/AI-Gateway/gpu-pilot/css/qwen35-q6-run01/results'
$env:ResultFiles__OutputDir = 'C:/AI-Gateway/gpu-pilot/css/qwen35-q6-run01/output'
```

Beim Modellwechsel Gateway neu starten, damit sein In-Memory-Hashcache leer ist.
CSS synchron bietet ausserdem `ignoreCache=true`; Sympany hat dieses Feld aktuell
nicht. Fuer Benchmark-Aufrufe ist dort `textHash=""` eine weitere Moeglichkeit,
die Hashwiederverwendung zu umgehen. Auch Dashboard-/Cronjob-Caches und alte
Fertig-Markierungen beruecksichtigen; nur Testprojekte auf die Pilot-URL umstellen.

## Fachlicher Versuchsplan

Fuer wiederholbare HTTP-Tests steht jetzt `Tools/GatewayReplay/Invoke-GatewayReplay.ps1`
bereit. Einrichtung, fester Testdatensatz, Cachebehandlung und externe PHP-Abnahme:
[GatewayReplay-Anleitung](Tools/GatewayReplay/README.md).

1. Auf der neuen GPU zuerst das bisherige Gemma 3 4B mit bisherigem Prompt und
   Kodierverfahren laufen lassen. Dies isoliert den Hardwareeffekt.
2. Dieselben Aussagen mit Qwen3.5-9B Q6_K ohne Thinking und Gemma 4 12B Q4_K_M
   auswerten; Gemma 3 12B Q6_K als Rueckfall-/Generationsvergleich und optional
   Ministral 3 14B Instruct Q5_K_M hinzunehmen. Jeweils ein Warm-up separat,
   danach mindestens 100 repraesentative
   manuell kodierte Aussagen; DE/FR/IT/EN, Negationen, Mischsentiment,
   kurze unklare Antworten und Verwechslungsfaelle abdecken.
3. Code-Precision, Code-Recall und F1, vollstaendige Statement-Uebereinstimmung,
   Sentimenttreffer, Parsefehler, `needsReview`, Median und P95 der Gesamtdauer
   pro Statement sowie VRAM-/RAM-Verbrauch vergleichen. Keine erfundenen tok/s-
   oder Laufzeitversprechen vor einer Messung auf dieser Maschine.
4. Erst mit dem besten Modell CSS-Verfahren vergleichen: `two_stage`, danach
   `candidate_rerank` und `single_pass` mit allen 38 Codes als kontrollierten
   separaten Versuch. Nicht gleichzeitig Modell, Codebuch und Verfahren wechseln.
5. Danach optional zwei gleichzeitige Anfragen testen. Hoeherer Gesamtdurchsatz
   darf nicht zu unbrauchbarer P95-Wartezeit, VRAM-Ueberlauf oder Fehlern fuehren.

Die vorhandene Projekt-8-Dokumentation nennt 89,6 % Kandidaten-Recall bei 250
manuellen Codezuordnungen (143 Aussagen), nicht 89,6 % Modellgenauigkeit. Das ist
ein dokumentierter Stand und nach Aenderungen an Codebuch/Vorauswahl erneut zu
messen. Ein nachgeschaltetes Modell kann fehlende Kandidaten nicht zurueckholen.
`CandidateLimit` wird im Retriever auf maximal 20 begrenzt; `38` einzutragen
uebergibt daher nicht alle Codes. `single_pass` ist der passende Vergleich mit
vollstaendigem Codebuch. Bisherige Projekt-8-Zuordnung bleibt im Pilot geerbt.

## Naechster getrennter Qualitaetsschritt

Aktuell wird `response_format: { "type": "text" }` gesendet. LM Studio kann
ueber `json_schema` ein Ausgabeformat erzwingen. Das ist fuer feste
Code-/Sentimentausgaben sinnvoll und sollte als eigener Versuch mit
phasenspezifischen Schemas und erlaubten IDs umgesetzt werden. Das Schema
verhindert keine fachlich falsche Kodierung. In dieser Vorbereitung wird es
noch nicht aktiviert, um Modellwechsel und Ausgabezwang separat zu bewerten.
Thinking muss vor dem Test sicher deaktiviert sein; ein Schema allein ersetzt
diese Einstellung nicht.

## Lokale technische Pruefung

```powershell
dotnet build Tools/GenerationSettingsChecks/GenerationSettingsChecks.csproj -m:1 -p:BuildInParallel=false
dotnet run --no-build --project Tools/GenerationSettingsChecks/GenerationSettingsChecks.csproj -- .
```

Der kleine Prueflauf simuliert LM Studio und kontrolliert die tatsaechlichen
HTTP-Payloads aller vier aktiven Wege, Standardwerte, globale und phasenspezifische
Tokenlimits, explizite Temperatur 0, CandidateTemperature-Vorrang und ungueltige
Tokenlimits. Er bewertet weder GPU-Kompatibilitaet noch Modellqualitaet.

## Quellen

- [Qwen3.5-9B: Modellkarte und Nicht-Thinking-Modus](https://huggingface.co/Qwen/Qwen3.5-9B)
- [Qwen3.5-9B: GGUF-Dateien und Groessen](https://huggingface.co/lmstudio-community/Qwen3.5-9B-GGUF/tree/main)
- [Gemma 3 12B: GGUF-Dateien und Groessen](https://huggingface.co/lmstudio-community/gemma-3-12b-it-GGUF/tree/main)
- [Gemma 4 12B: Modellkarte und Thinking](https://huggingface.co/google/gemma-4-12B-it)
- [Gemma 4 12B: GGUF-Dateien und Groessen](https://huggingface.co/lmstudio-community/gemma-4-12B-it-GGUF/tree/main)
- [Ministral 3 14B Instruct: Modellkarte](https://huggingface.co/mistralai/Ministral-3-14B-Instruct-2512)
- [Ministral 3 14B Instruct: offizielle GGUF-Dateien](https://huggingface.co/mistralai/Ministral-3-14B-Instruct-2512-GGUF/tree/main)
- [LM Studio: Laden, GPU-Offload und Speicherabschaetzung](https://lmstudio.ai/docs/cli/local-models/load)
- [LM Studio: Modell-Ladeparameter](https://lmstudio.ai/docs/typescript/api-reference/llm-load-model-config)
- [LM Studio: Structured Output](https://lmstudio.ai/docs/developer/openai-compat/structured-output)
