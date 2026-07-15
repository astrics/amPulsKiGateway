using System.Diagnostics;
using System.Runtime;
using System.Text;

namespace AiGateway.Sympany.Api.Services;

/// <summary>
/// Schreibt detaillierte Memory-Diagnostics in eine lokale Datei
/// </summary>
public class MemoryDiagnosticsService
{
    private readonly ILogger<MemoryDiagnosticsService> _logger;
    private readonly string _diagnosticsPath;
    private static readonly object _fileLock = new();

    public MemoryDiagnosticsService(ILogger<MemoryDiagnosticsService> logger)
    {
        _logger = logger;
        _diagnosticsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diagnostics");
        
        // Verzeichnis erstellen falls nicht vorhanden
        Directory.CreateDirectory(_diagnosticsPath);
    }

    /// <summary>
    /// Schreibt einen Memory-Snapshot in eine Datei
    /// </summary>
    public void WriteMemorySnapshot(string context)
    {
        try
        {
            var snapshot = CaptureMemorySnapshot(context);
            var fileName = $"memory-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            var fullPath = Path.Combine(_diagnosticsPath, fileName);

            lock (_fileLock)
            {
                File.WriteAllText(fullPath, snapshot, Encoding.UTF8);
            }

            _logger.LogInformation("?? Memory-Snapshot geschrieben: {FileName}", fileName);

            // Alte Snapshots löschen (älter als 7 Tage)
            CleanupOldSnapshots(TimeSpan.FromDays(7));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Schreiben des Memory-Snapshots");
        }
    }

    /// <summary>
    /// Erstellt einen detaillierten Memory-Snapshot als String
    /// </summary>
    public string CaptureMemorySnapshot(string context)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;
        var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();

        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine($"  AI GATEWAY - MEMORY DIAGNOSTICS");
        sb.AppendLine($"  Zeitstempel: {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Kontext: {context}");
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine();

        // .NET Managed Memory
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine("  .NET MANAGED MEMORY");
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine($"  Total Allocated:       {GC.GetTotalMemory(false) / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"  Heap Size:             {gcInfo.HeapSizeBytes / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"  Fragmented:            {gcInfo.FragmentedBytes / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"  High Memory Load:      {gcInfo.MemoryLoadBytes / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"  Total Available:       {gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"  Memory Load:           {(gcInfo.MemoryLoadBytes * 100.0 / gcInfo.TotalAvailableMemoryBytes):F1}%");
        sb.AppendLine();

        // GC Collections
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine("  GARBAGE COLLECTION");
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine($"  Gen 0 Collections:     {GC.CollectionCount(0)}");
        sb.AppendLine($"  Gen 1 Collections:     {GC.CollectionCount(1)}");
        sb.AppendLine($"  Gen 2 Collections:     {GC.CollectionCount(2)}");
        sb.AppendLine($"  GC Mode:               {(GCSettings.IsServerGC ? "Server" : "Workstation")}");
        sb.AppendLine($"  Latency Mode:          {GCSettings.LatencyMode}");
        sb.AppendLine($"  Concurrent:            {gcInfo.Concurrent}");
        sb.AppendLine($"  Compacted:             {gcInfo.Compacted}");
        sb.AppendLine();

        // Process Memory (native)
        process.Refresh();
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine("  PROCESS MEMORY (inkl. Native)");
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine($"  Working Set:           {process.WorkingSet64 / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"  Private Memory:        {process.PrivateMemorySize64 / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"  Virtual Memory:        {process.VirtualMemorySize64 / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"  Paged Memory:          {process.PagedMemorySize64 / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine($"  Peak Working Set:      {process.PeakWorkingSet64 / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine();

        // Thread-Informationen
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine("  THREADS");
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine($"  Thread Count:          {process.Threads.Count}");
        sb.AppendLine($"  Handle Count:          {process.HandleCount}");
        
        ThreadPool.GetAvailableThreads(out int availableWorker, out int availableIo);
        ThreadPool.GetMaxThreads(out int maxWorker, out int maxIo);
        ThreadPool.GetMinThreads(out int minWorker, out int minIo);
        
        sb.AppendLine($"  ThreadPool Worker:     {maxWorker - availableWorker}/{maxWorker} (Min: {minWorker})");
        sb.AppendLine($"  ThreadPool I/O:        {maxIo - availableIo}/{maxIo} (Min: {minIo})");
        sb.AppendLine();

        // System-Informationen
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine("  SYSTEM");
        sb.AppendLine("???????????????????????????????????????????????????????????");
        sb.AppendLine($"  OS:                    {Environment.OSVersion}");
        sb.AppendLine($"  .NET Version:          {Environment.Version}");
        sb.AppendLine($"  Processor Count:       {Environment.ProcessorCount}");
        sb.AppendLine($"  64-Bit Process:        {Environment.Is64BitProcess}");
        sb.AppendLine($"  Uptime:                {DateTime.Now - Process.GetCurrentProcess().StartTime:hh\\:mm\\:ss}");
        sb.AppendLine();

        sb.AppendLine("???????????????????????????????????????????????????????????");

        return sb.ToString();
    }

    /// <summary>
    /// Löscht alte Snapshot-Dateien
    /// </summary>
    private void CleanupOldSnapshots(TimeSpan maxAge)
    {
        try
        {
            var files = Directory.GetFiles(_diagnosticsPath, "memory-*.txt");
            var cutoffDate = DateTime.Now - maxAge;

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffDate)
                {
                    File.Delete(file);
                    _logger.LogDebug("Alte Snapshot-Datei gelöscht: {FileName}", fileInfo.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Löschen alter Snapshots");
        }
    }

    /// <summary>
    /// Gibt Memory-Info als strukturiertes Objekt zurück
    /// </summary>
    public object GetMemoryInfo()
    {
        var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();
        
        ThreadPool.GetAvailableThreads(out int availableWorker, out int availableIo);
        ThreadPool.GetMaxThreads(out int maxWorker, out int maxIo);

        return new
        {
            managed = new
            {
                totalAllocatedMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
                heapSizeMB = gcInfo.HeapSizeBytes / 1024.0 / 1024.0,
                fragmentedMB = gcInfo.FragmentedBytes / 1024.0 / 1024.0,
                memoryLoadPercent = gcInfo.MemoryLoadBytes * 100.0 / gcInfo.TotalAvailableMemoryBytes
            },
            gc = new
            {
                gen0Collections = GC.CollectionCount(0),
                gen1Collections = GC.CollectionCount(1),
                gen2Collections = GC.CollectionCount(2),
                isServerGC = GCSettings.IsServerGC,
                latencyMode = GCSettings.LatencyMode.ToString()
            },
            process = new
            {
                workingSetMB = process.WorkingSet64 / 1024.0 / 1024.0,
                privateMemoryMB = process.PrivateMemorySize64 / 1024.0 / 1024.0,
                threadCount = process.Threads.Count,
                handleCount = process.HandleCount
            },
            threadPool = new
            {
                workerThreadsInUse = maxWorker - availableWorker,
                workerThreadsMax = maxWorker,
                ioThreadsInUse = maxIo - availableIo,
                ioThreadsMax = maxIo
            },
            system = new
            {
                processorCount = Environment.ProcessorCount,
                is64Bit = Environment.Is64BitProcess,
                uptimeSeconds = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds
            }
        };
    }
}

