using System.Collections.Concurrent;
using System.Text.Json;
using AiGateway.Api.Models.Internal;

namespace AiGateway.Api.Services;

public interface IJobPersistenceService
{
    Task SaveJobAsync(string jobId, QueueItem item);
    Task<QueueItem?> LoadJobAsync(string jobId);
    Task<List<string>> LoadPendingJobIdsAsync();
    string GetJobsDirectory();
}

public class JobPersistenceService : IJobPersistenceService
{
    private readonly string _jobsDirectory;
    private readonly ILogger<JobPersistenceService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JobPersistenceService(IConfiguration config, ILogger<JobPersistenceService> logger)
    {
        _logger = logger;
        _jobsDirectory = config.GetValue<string>("Jobs:StoragePath")
                         ?? Path.Combine(AppContext.BaseDirectory, "jobs");

        foreach (var sub in new[] { "pending", "processing", "completed", "failed" })
            Directory.CreateDirectory(Path.Combine(_jobsDirectory, sub));

        _logger.LogInformation("💾 Job-Persistenz aktiv: {Path}", _jobsDirectory);
    }

    public async Task SaveJobAsync(string jobId, QueueItem item)
    {
        var semaphore = _locks.GetOrAdd(jobId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();

        try
        {
            var subDir = item.Status switch
            {
                "queued" => "pending",
                "processing" => "processing",
                "completed" => "completed",
                "failed" => "failed",
                _ => "pending"
            };

            // Alte Dateien in anderen Ordnern löschen
            foreach (var dir in new[] { "pending", "processing", "completed", "failed" })
            {
                var oldFile = Path.Combine(_jobsDirectory, dir, $"{jobId}.json");
                if (File.Exists(oldFile) && dir != subDir)
                {
                    try { File.Delete(oldFile); } catch { /* ignore */ }
                }
            }

            var snapshot = new JobSnapshot
            {
                JobId = item.JobId,
                Status = item.Status,
                Source = item.Source,
                AnalysisType = item.AnalysisType,
                Priority = item.Priority,
                CustomPrompt = item.CustomPrompt,
                CallbackUrl = item.CallbackUrl,
                TotalStatements = item.TotalStatements,
                TotalChunks = item.Chunks?.Count ?? 0,
                CompletedChunks = item.CompletedChunks,
                CreatedAt = item.CreatedAt,
                CompletedAt = item.CompletedAt,
                UpdatedAt = DateTime.UtcNow,
                Error = item.Error,
                ChunkResults = item.ChunkResults?.ToList(),
                FinalResult = item.FinalResult
            };

            var filePath = Path.Combine(_jobsDirectory, subDir, $"{jobId}.json");
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogDebug("💾 Job {JobId} → {SubDir} ({Chunks}/{Total} Chunks)",
                jobId, subDir, item.CompletedChunks, item.Chunks?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Speichern von Job {JobId}", jobId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<QueueItem?> LoadJobAsync(string jobId)
    {
        foreach (var dir in new[] { "processing", "completed", "pending", "failed" })
        {
            var filePath = Path.Combine(_jobsDirectory, dir, $"{jobId}.json");
            if (!File.Exists(filePath)) continue;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var snapshot = JsonSerializer.Deserialize<JobSnapshot>(json, JsonOpts);
                if (snapshot == null) continue;

                return SnapshotToQueueItem(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fehler beim Laden von Job {JobId}", jobId);
            }
        }
        return null;
    }

    public Task<List<string>> LoadPendingJobIdsAsync()
    {
        var pendingDir = Path.Combine(_jobsDirectory, "pending");
        var ids = Directory.Exists(pendingDir)
            ? Directory.GetFiles(pendingDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList()
            : new List<string>();

        return Task.FromResult(ids);
    }

    public string GetJobsDirectory() => _jobsDirectory;

    private static QueueItem SnapshotToQueueItem(JobSnapshot s) => new()
    {
        JobId = s.JobId,
        Status = s.Status,
        Source = s.Source,
        AnalysisType = s.AnalysisType,
        Priority = s.Priority,
        CustomPrompt = s.CustomPrompt,
        CallbackUrl = s.CallbackUrl,
        TotalStatements = s.TotalStatements,
        CompletedChunks = s.CompletedChunks,
        CreatedAt = s.CreatedAt,
        CompletedAt = s.CompletedAt,
        Error = s.Error,
        ChunkResults = s.ChunkResults ?? new(),
        FinalResult = s.FinalResult
    };
}

/// <summary>
/// Serialisierbarer Snapshot eines Jobs (ohne Chunks-Rohdaten)
/// </summary>
public class JobSnapshot
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string Source { get; set; } = "";
    public string AnalysisType { get; set; } = "";
    public int Priority { get; set; } = 5;
    public string? CustomPrompt { get; set; }
    public string? CallbackUrl { get; set; }
    public int TotalStatements { get; set; }
    public int TotalChunks { get; set; }
    public int CompletedChunks { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Error { get; set; }
    public List<string>? ChunkResults { get; set; }
    public object? FinalResult { get; set; }
}
