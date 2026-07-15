# ?? Memory-Monitoring erfolgreich implementiert!

## ? Was wurde geändert?

### Neue Dateien erstellt:
1. **`Middleware/MemoryMonitoringMiddleware.cs`**
   - Überwacht Speicher bei jedem Request
   - Warnt bei hoher Auslastung (> 500 MB)
   - Triggert automatisch GC bei kritischen Werten (> 800 MB)
   - Loggt nur bei bedeutenden Änderungen (verhindert Log-Spam)

2. **`Services/MemoryDiagnosticsService.cs`**
   - Erstellt detaillierte Memory-Snapshots
   - Schreibt in `diagnostics/memory-*.txt`
   - Zeigt .NET Managed Memory, Process Memory, ThreadPool, GC-Stats
   - Automatische Cleanup alter Snapshots (> 7 Tage)

3. **`MEMORY-MONITORING.md`**
   - Komplette Dokumentation
   - Verwendungsbeispiele
   - Troubleshooting-Guide

### Bestehende Dateien verbessert:

4. **`Middleware/ApiKeyMiddleware.cs`**
   - ? Mehr Logging (IP, Method, Path)
   - ? Emojis für schnelleres Scannen (?/?)
   - ? Crash-Prevention bei leeren API-Keys
   - ? Key-Preview in Logs

5. **`Services/CacheService.cs`**
   - ?? **BUG-FIX**: `SetSize()` verwendet jetzt Bytes statt Zeichen!
   - ? Cache-Hit/Miss-Counter mit Logging
   - ? Byte-Size wird korrekt berechnet und geloggt

6. **`Controllers/HealthController.cs`**
   - ? Neuer Endpoint: `GET /api/health/memory`
   - ? Live Memory-Info als JSON
   - ? Optional: Snapshot erstellen mit `?snapshot=true`

7. **`Program.cs`**
   - ? `MemoryDiagnosticsService` registriert
   - ? `MemoryMonitoringMiddleware` zur Pipeline hinzugefügt
   - ? Verbesserter Memory-Cache (CompactionPercentage, ExpirationScanFrequency)
   - ? Erweitertes Startup-Logging (GC-Mode, Memory, Prozessoren)
   - ? Initialer Memory-Snapshot beim Start

8. **`appsettings.json`**
   - ? Separates Log-File: `logs/memory-.log` (nur Memory-Logs)
   - ? Haupt-Log: `logs/gateway-.log` (alles andere)
   - ? Bessere Log-Templates mit Timestamps
   - ? Memory-Middleware auf Debug-Level

---

## ?? Sofort loslegen

### 1. Anwendung starten
```bash
dotnet run
```

**Beim Start siehst du jetzt:**
```
???????????????????????????????????????????????????????????
  AI Gateway gestartet
???????????????????????????????????????????????????????????
  LM Studio: http://localhost:1234
  Modell: qwen/qwen3-8b
  Max Concurrency: 1
  Max Tokens/Chunk: 1500
  Cache-Dauer: 60 Minuten
  Cache-Limit: 100,000,000 Bytes (~100MB)
???????????????????????????????????????????????????????????
  .NET Version: 9.0.0
  Prozessoren: 8
  Arbeitsspeicher (Total): 16,384 MB verfügbar
  GC-Modus: Server
???????????????????????????????????????????????????????????
?? Memory-Snapshot geschrieben: memory-20240115-123456.txt
```

### 2. Memory-Info abrufen
```bash
curl http://localhost:5000/api/health/memory | jq .
```

**Ausgabe:**
```json
{
  "managed": {
    "totalAllocatedMB": 156.32,
    "heapSizeMB": 142.18,
    "fragmentedMB": 12.45,
    "memoryLoadPercent": 5.2
  },
  "gc": {
    "gen0Collections": 8,
    "gen1Collections": 2,
    "gen2Collections": 1,
    "isServerGC": true,
    "latencyMode": "Interactive"
  },
  "process": {
    "workingSetMB": 234.56,
    "privateMemoryMB": 198.32,
    "threadCount": 23,
    "handleCount": 456
  },
  "threadPool": {
    "workerThreadsInUse": 4,
    "workerThreadsMax": 32767,
    "ioThreadsInUse": 2,
    "ioThreadsMax": 1000
  },
  "system": {
    "processorCount": 8,
    "is64Bit": true,
    "uptimeSeconds": 123.45
  }
}
```

### 3. Memory-Snapshot erstellen
```bash
curl "http://localhost:5000/api/health/memory?snapshot=true"
```

Dann öffnen:
```bash
cat diagnostics/memory-*.txt
```

### 4. Logs beobachten
```bash
# Memory-spezifische Logs
tail -f logs/memory-*.log

# Alle Logs
tail -f logs/gateway-*.log
```

---

## ?? Log-Beispiele

### Normal (wenig Speicher)
```
[12:34:56 DBG] ? Auth erfolgreich: 5992*** | POST /api/analyze
[12:34:57 DBG] ?? Cache MISS #15: 3f8a2b1c
[12:35:02 DBG] ?? Cache SET: 3f8a2b1c, 2,456 Bytes (589 Zeichen), TTL: 60m
```

### Warnung (hoher Speicher)
```
[12:35:10 INF] ?? Memory: 587.3 MB (+45.2 MB) | GC: Gen0=25 Gen1=8 Gen2=2 | Heap: 542.1 MB | Request: POST /api/analyze (2134ms)
```

### Kritisch (automatischer GC)
```
[12:35:45 WRN] ?? KRITISCHER SPEICHER: 812.5 MB | Triggere GC.Collect() um Speicher freizugeben...
[12:35:46 INF] ? GC abgeschlossen: 812.5 MB ? 387.2 MB (freigegeben: 425.3 MB)
```

---

## ?? Konfiguration anpassen

### Schwellwerte für Memory-Warnungen

In `Middleware/MemoryMonitoringMiddleware.cs`:

```csharp
private const long WarningThresholdBytes = 500_000_000;  // 500 MB
private const long CriticalThresholdBytes = 800_000_000; // 800 MB
private const int LogIntervalSeconds = 30;               // Nur alle 30s
```

**Bei schwachem Server (z.B. 4 GB RAM):**
```csharp
private const long WarningThresholdBytes = 300_000_000;  // 300 MB
private const long CriticalThresholdBytes = 500_000_000; // 500 MB
```

**Bei starkem Server (z.B. 32 GB RAM):**
```csharp
private const long WarningThresholdBytes = 1_000_000_000;  // 1 GB
private const long CriticalThresholdBytes = 2_000_000_000; // 2 GB
```

### Cache-Größe anpassen

In `Program.cs`:

```csharp
options.SizeLimit = 100_000_000; // 100 MB

// Für mehr Caching (wenn genug RAM):
options.SizeLimit = 500_000_000; // 500 MB

// Für weniger RAM:
options.SizeLimit = 50_000_000; // 50 MB
```

---

## ?? Problem beheben: Visual Studio Speicher

Die Fehler die du hattest waren **Visual Studio-intern**, nicht dein Code!

### Sofortlösung:
1. Visual Studio **neu starten**
2. Ordner **`.vs/`** im Solution-Verzeichnis löschen
3. **`obj/`** und **`bin/`** in allen Projekten löschen

### Dauerhafte Lösung:
**Extras ? Optionen ? Texteditor ? C# ? Erweitert**
- ?? "Vollständige Projektmappenanalyse aktivieren" ? **? Deaktivieren**

Das spart 500+ MB RAM in Visual Studio!

---

## ?? Neue Verzeichnisse

Nach dem Start werden erstellt:

```
D:\AmPuls\GitHub\amPulsKiGateway\AIGateway\
??? logs/
?   ??? gateway-20240115.log    ? Haupt-Logs (30 Tage)
?   ??? memory-20240115.log     ? Memory-Logs (7 Tage)
?
??? diagnostics/
    ??? memory-20240115-123456.txt  ? Startup-Snapshot
    ??? memory-20240115-143022.txt  ? Manueller Snapshot
    ??? ...                         ? (automatisch gelöscht nach 7 Tagen)
```

---

## ? Checkliste

- [x] MemoryMonitoringMiddleware implementiert
- [x] MemoryDiagnosticsService implementiert
- [x] Health-Endpoint erweitert (`/api/health/memory`)
- [x] CacheService Bug behoben (Bytes statt Zeichen)
- [x] ApiKeyMiddleware verbessert (mehr Logging)
- [x] Program.cs erweitert (Startup-Info, Memory-Snapshot)
- [x] appsettings.json konfiguriert (separates Memory-Log)
- [x] Dokumentation erstellt (MEMORY-MONITORING.md)
- [x] Build erfolgreich ?

---

## ?? Nächste Schritte

1. **Testen:**
   ```bash
   dotnet run
   curl http://localhost:5000/api/health/memory
   ```

2. **Load-Test** (um zu sehen ob GC triggert):
   ```bash
   # Mehrere Requests parallel
   for i in {1..10}; do
     curl -X POST http://localhost:5000/api/analyze \
       -H "X-Api-Key: test-key-dev-only" \
       -H "Content-Type: application/json" \
       -d '{"analysisType":"sentiment","statements":[{"id":"1","text":"Test"}]}' &
   done
   
   # Memory beobachten
   watch -n 1 'curl -s http://localhost:5000/api/health/memory | jq .managed.totalAllocatedMB'
   ```

3. **Logs überwachen:**
   ```bash
   tail -f logs/memory-*.log
   ```

4. **Bei Speicherproblemen:**
   - Snapshot erstellen: `curl "http://localhost:5000/api/health/memory?snapshot=true"`
   - Datei `diagnostics/memory-*.txt` analysieren
   - Falls nötig: Schwellwerte anpassen (siehe oben)

---

## ?? Support

Bei Fragen oder Problemen:
1. Prüfe `MEMORY-MONITORING.md` für Details
2. Schau in `logs/memory-*.log`
3. Erstelle Snapshot und teile `diagnostics/memory-*.txt`

**Viel Erfolg! ??**
