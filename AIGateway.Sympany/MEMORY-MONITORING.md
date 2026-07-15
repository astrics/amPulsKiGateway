# Memory-Monitoring & Diagnostics

## ?? Übersicht

Das AI Gateway wurde mit umfassendem Memory-Monitoring und Diagnostics erweitert, um Speicherprobleme frühzeitig zu erkennen und zu protokollieren.

## ?? Neue Features

### 1. **MemoryMonitoringMiddleware**
Überwacht jede HTTP-Request auf Speicherverbrauch:

- ? Misst Speicher vor und nach jedem Request
- ? Loggt große Speicheränderungen (> 10 MB)
- ? Warnt bei hoher Auslastung (> 500 MB)
- ? Triggert automatisch Garbage Collection bei kritischen Werten (> 800 MB)
- ? Zeigt GC-Statistiken (Gen0/Gen1/Gen2 Collections)

**Beispiel-Log:**
```
[12:34:56 INF] ?? Memory: 456.3 MB (+12.4 MB) | GC: Gen0=15 Gen1=3 Gen2=1 | Heap: 428.1 MB | Request: POST /api/analyze (1234ms)
[12:35:10 WRN] ?? KRITISCHER SPEICHER: 812.5 MB | Triggere GC.Collect() um Speicher freizugeben...
[12:35:11 INF] ? GC abgeschlossen: 812.5 MB ? 387.2 MB (freigegeben: 425.3 MB)
```

### 2. **MemoryDiagnosticsService**
Erstellt detaillierte Memory-Snapshots:

- ? Erfasst .NET Managed Memory (Heap, Fragmentation)
- ? Erfasst Process Memory (Working Set, Private Memory)
- ? Erfasst ThreadPool-Status
- ? Erfasst System-Informationen
- ? Schreibt Snapshots in `diagnostics/memory-*.txt`
- ? Automatische Cleanup alter Snapshots (> 7 Tage)

**Snapshot-Beispiel:**
```
???????????????????????????????????????????????????????????
  AI GATEWAY - MEMORY DIAGNOSTICS
  Zeitstempel: 2024-01-15 12:34:56
  Kontext: Application Startup
???????????????????????????????????????????????????????????

???????????????????????????????????????????????????????????
  .NET MANAGED MEMORY
???????????????????????????????????????????????????????????
  Total Allocated:       156.32 MB
  Heap Size:             142.18 MB
  Fragmented:            12.45 MB
  Memory Load:           5.2%

???????????????????????????????????????????????????????????
  GARBAGE COLLECTION
???????????????????????????????????????????????????????????
  Gen 0 Collections:     8
  Gen 1 Collections:     2
  Gen 2 Collections:     1
  GC Mode:               Server
  Latency Mode:          Interactive
```

### 3. **Erweiterte Health-Endpoints**

#### `/api/health` - Standard Health Check
```json
{
  "status": "healthy",
  "lmStudio": { ... },
  "queue": { ... }
}
```

#### `/api/health/memory` - Memory-Informationen
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
  }
}
```

#### `/api/health/memory?snapshot=true` - Mit Datei-Snapshot
Erstellt zusätzlich eine Snapshot-Datei in `diagnostics/`.

### 4. **Verbessertes CacheService**
- ? **Bug-Fix**: `SetSize()` verwendet jetzt Bytes statt Zeichen
- ? Mehr Logging: Cache-Hits/Misses mit Statistiken
- ? Byte-Size wird korrekt protokolliert

**Vorher (FEHLER):**
```csharp
.SetSize(value.Length) // Falsch! Length = Zeichen, nicht Bytes
```

**Nachher (KORREKT):**
```csharp
var sizeInBytes = Encoding.UTF8.GetByteCount(value);
.SetSize(sizeInBytes) // Korrekt! Bytes für Memory-Cache
```

### 5. **Verbesserte Logging-Konfiguration**

#### Drei Log-Dateien:
1. **`logs/gateway-YYYYMMDD.log`** - Haupt-Log (30 Tage Retention)
2. **`logs/memory-YYYYMMDD.log`** - Nur Memory-Logs (7 Tage Retention)
3. **`diagnostics/memory-YYYYMMDD-HHmmss.txt`** - Detaillierte Snapshots (7 Tage)

## ?? Verwendung

### Memory-Überwachung im Betrieb

Die MemoryMonitoringMiddleware läuft automatisch bei jedem Request. Keine Konfiguration nötig.

### Manueller Memory-Snapshot

```bash
# Snapshot erstellen via API
curl http://localhost:5000/api/health/memory?snapshot=true

# Snapshot-Datei wird in diagnostics/ geschrieben
# Datei: diagnostics/memory-20240115-123456.txt
```

### Memory-Probleme debuggen

1. **Logs prüfen:**
   ```bash
   # Memory-spezifische Logs
   tail -f logs/memory-20240115.log
   
   # Haupt-Logs
   tail -f logs/gateway-20240115.log
   ```

2. **Snapshot analysieren:**
   ```bash
   # Neuesten Snapshot öffnen
   cat diagnostics/memory-*.txt | tail -100
   ```

3. **Live-Monitoring:**
   ```bash
   # Memory-Info abrufen
   curl http://localhost:5000/api/health/memory | jq .
   ```

## ?? Konfiguration

### Schwellwerte anpassen

In `Middleware/MemoryMonitoringMiddleware.cs`:

```csharp
private const long WarningThresholdBytes = 500_000_000;  // 500 MB ? Warnung
private const long CriticalThresholdBytes = 800_000_000; // 800 MB ? GC triggern
private const int LogIntervalSeconds = 30;               // Nur alle 30s loggen
```

### Cache-Limit anpassen

In `Program.cs`:

```csharp
options.SizeLimit = 100_000_000; // 100MB in Bytes
options.CompactionPercentage = 0.25; // Bei Überlauf 25% entfernen
```

### Log-Retention anpassen

In `appsettings.json`:

```json
{
  "Name": "File",
  "Args": {
    "path": "logs/memory-.log",
    "rollingInterval": "Day",
    "retainedFileCountLimit": 7  // ? Anzahl Tage
  }
}
```

## ?? Troubleshooting

### Speicher steigt kontinuierlich

1. **Prüfe Memory-Logs:**
   ```bash
   grep "Memory:" logs/memory-*.log
   ```

2. **Erstelle Snapshot vor/nach kritischer Operation:**
   ```bash
   curl "http://localhost:5000/api/health/memory?snapshot=true"
   # ... kritische Operation durchführen ...
   curl "http://localhost:5000/api/health/memory?snapshot=true"
   # Vergleiche die beiden Snapshots
   ```

3. **Prüfe GC-Collections:**
   ```bash
   grep "GC:" logs/memory-*.log
   ```
   
   Wenn Gen2-Collections häufig sind ? Memory-Leak!

### Cache zu groß

Wenn der Cache das Limit überschreitet:

```bash
grep "Cache SET" logs/gateway-*.log | wc -l  # Anzahl Cache-Einträge
```

Reduziere `CacheDurationMinutes` in `appsettings.json`.

### Visual Studio Speicherprobleme

Die sind **nicht** von deinem Code:

1. Visual Studio neu starten
2. `.vs/` Ordner löschen
3. `obj/` und `bin/` löschen
4. **Extras ? Optionen ? Texteditor ? C# ? Erweitert**
   - "Vollständige Projektmappenanalyse aktivieren" ? **Deaktivieren**

## ?? Performance-Impact

- **MemoryMonitoringMiddleware:** ~1-2ms pro Request
- **Memory-Logging:** Nur bei großen Änderungen (> 10 MB)
- **Automatischer GC:** Nur bei kritischen Werten (> 800 MB)
- **Snapshots:** Nur auf Anfrage via `/api/health/memory?snapshot=true`

## ? Änderungen-Zusammenfassung

| Datei | Änderung |
|-------|----------|
| `Middleware/MemoryMonitoringMiddleware.cs` | ? **NEU** - Kontinuierliches Memory-Monitoring |
| `Services/MemoryDiagnosticsService.cs` | ? **NEU** - Detaillierte Diagnostics |
| `Middleware/ApiKeyMiddleware.cs` | ? Mehr Logging, Crash-Prevention |
| `Services/CacheService.cs` | ? **BUG-FIX** - Bytes statt Zeichen |
| `Controllers/HealthController.cs` | ? Memory-Endpoint hinzugefügt |
| `Program.cs` | ? Services registriert, Startup-Info erweitert |
| `appsettings.json` | ? Memory-Log-File, bessere Templates |

## ?? Nächste Schritte

1. **Testen:**
   ```bash
   dotnet run
   curl http://localhost:5000/api/health/memory
   ```

2. **Logs beobachten:**
   ```bash
   tail -f logs/memory-*.log
   ```

3. **Snapshot erstellen:**
   ```bash
   curl "http://localhost:5000/api/health/memory?snapshot=true"
   cat diagnostics/memory-*.txt
   ```

4. **Bei Problemen:** Snapshots an mich schicken für Analyse!
