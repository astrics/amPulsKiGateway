using System.Diagnostics;

namespace AiGateway.Api.Middleware;

/// <summary>
/// Überwacht Speichernutzung bei jedem Request und schreibt Warnungen
/// bei hoher Auslastung
/// </summary>
public class MemoryMonitoringMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<MemoryMonitoringMiddleware> _logger;
    private static readonly object _lockObj = new();
    private static DateTime _lastMemoryLog = DateTime.MinValue;
    private static DateTime _lastGcLog = DateTime.MinValue;

    // Schwellwerte für Warnungen
    private const long WarningThresholdBytes = 500_000_000; // 500 MB
    private const long CriticalThresholdBytes = 800_000_000; // 800 MB
    private const int LogIntervalSeconds = 30; // Nur alle 30s loggen um Spam zu vermeiden

    public MemoryMonitoringMiddleware(
        RequestDelegate next,
        ILogger<MemoryMonitoringMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Speicher VOR dem Request messen
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: false);
        var sw = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();

            // Speicher NACH dem Request messen
            var memoryAfter = GC.GetTotalMemory(forceFullCollection: false);
            var memoryDelta = memoryAfter - memoryBefore;

            // GC-Statistiken
            var gcInfo = GC.GetGCMemoryInfo();
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);

            // Nur bei größeren Speicheränderungen oder hoher Auslastung loggen
            if (Math.Abs(memoryDelta) > 10_000_000 || // > 10 MB Änderung
                memoryAfter > WarningThresholdBytes)
            {
                var shouldLog = false;
                lock (_lockObj)
                {
                    if ((DateTime.UtcNow - _lastMemoryLog).TotalSeconds >= LogIntervalSeconds)
                    {
                        _lastMemoryLog = DateTime.UtcNow;
                        shouldLog = true;
                    }
                }

                if (shouldLog)
                {
                    var logLevel = memoryAfter > CriticalThresholdBytes
                        ? LogLevel.Warning
                        : LogLevel.Information;

                    _logger.Log(logLevel,
                        "?? Memory: {MemoryMB:F1} MB ({Delta:+#;-#;0} MB) | " +
                        "GC: Gen0={Gen0} Gen1={Gen1} Gen2={Gen2} | " +
                        "Heap: {HeapMB:F1} MB | " +
                        "Request: {Method} {Path} ({Duration}ms)",
                        memoryAfter / 1024.0 / 1024.0,
                        memoryDelta / 1024.0 / 1024.0,
                        gen0, gen1, gen2,
                        gcInfo.HeapSizeBytes / 1024.0 / 1024.0,
                        context.Request.Method,
                        context.Request.Path,
                        sw.ElapsedMilliseconds);
                }
            }

            // Kritische Speicherschwelle erreicht?
            if (memoryAfter > CriticalThresholdBytes)
            {
                var shouldGcLog = false;
                lock (_lockObj)
                {
                    if ((DateTime.UtcNow - _lastGcLog).TotalSeconds >= LogIntervalSeconds)
                    {
                        _lastGcLog = DateTime.UtcNow;
                        shouldGcLog = true;
                    }
                }

                if (shouldGcLog)
                {
                    _logger.LogWarning(
                        "?? KRITISCHER SPEICHER: {MemoryMB:F1} MB | " +
                        "Triggere GC.Collect() um Speicher freizugeben...",
                        memoryAfter / 1024.0 / 1024.0);

                    // Garbage Collection erzwingen
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

                    var memoryAfterGc = GC.GetTotalMemory(forceFullCollection: false);
                    _logger.LogInformation(
                        "? GC abgeschlossen: {BeforeMB:F1} MB ? {AfterMB:F1} MB (freigegeben: {FreedMB:F1} MB)",
                        memoryAfter / 1024.0 / 1024.0,
                        memoryAfterGc / 1024.0 / 1024.0,
                        (memoryAfter - memoryAfterGc) / 1024.0 / 1024.0);
                }
            }
        }
    }
}
