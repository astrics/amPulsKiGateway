using System.Collections.Concurrent;
using System.Threading.Channels;
using AiGateway.Api.Configuration;
using AiGateway.Api.Models.Internal;
using AiGateway.Api.Models.Responses;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Services;

public class QueueService : IQueueService
{
    private readonly Channel<QueueItem> _channel;
    private readonly ConcurrentDictionary<string, QueueItem> _jobs = new();
    private readonly ILogger<QueueService> _logger;
    private readonly GatewayOptions _options;
    private int _completedTotal;

    public QueueService(IOptions<GatewayOptions> options, ILogger<QueueService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Bounded Channel verhindert Memory-Overflow
        _channel = Channel.CreateBounded<QueueItem>(new BoundedChannelOptions(_options.MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>
    /// Wird vom Worker gelesen
    /// </summary>
    public ChannelReader<QueueItem> Reader => _channel.Reader;

    public async Task<QueueItem> EnqueueAsync(QueueItem item)
    {
        _jobs[item.JobId] = item;

        await _channel.Writer.WriteAsync(item);

        _logger.LogInformation(
            "Job {JobId} eingereiht. Quelle: {Source}, Typ: {Type}, Chunks: {Chunks}, Aussagen: {Count}",
            item.JobId, item.Source, item.AnalysisType,
            item.Chunks.Count, item.TotalStatements);

        return item;
    }

    public Task<JobStatusResponse?> GetStatusAsync(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var item))
            return Task.FromResult<JobStatusResponse?>(null);

        return Task.FromResult<JobStatusResponse?>(MapToResponse(item));
    }

    public int GetPendingCount() =>
        _jobs.Values.Count(j => j.Status == "queued");

    public int GetActiveCount() =>
        _jobs.Values.Count(j => j.Status == "processing");

    public int GetCompletedTotal() => _completedTotal;

    public void MarkCompleted(string jobId)
    {
        Interlocked.Increment(ref _completedTotal);
    }

    public List<JobStatusResponse> GetAllJobs()
    {
        return _jobs.Values
            .OrderByDescending(j => j.CreatedAt)
            .Take(50)
            .Select(MapToResponse)
            .ToList();
    }

    /// <summary>
    /// Alte abgeschlossene Jobs aufräumen (Memory sparen)
    /// </summary>
    public void CleanupOldJobs(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var oldJobs = _jobs
            .Where(kv => kv.Value.CompletedAt.HasValue && kv.Value.CompletedAt < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in oldJobs)
        {
            _jobs.TryRemove(key, out _);
        }

        if (oldJobs.Any())
            _logger.LogInformation("{Count} alte Jobs aufgeräumt", oldJobs.Count);
    }

    private JobStatusResponse MapToResponse(QueueItem item) => new()
    {
        JobId = item.JobId,
        Status = item.Status,
        QueuePosition = item.Status == "queued"
            ? _jobs.Values.Count(j => j.Status == "queued" && j.CreatedAt <= item.CreatedAt)
            : null,
        TotalChunks = item.Chunks.Count,
        CompletedChunks = item.CompletedChunks,
        Source = item.Source,
        AnalysisType = item.AnalysisType,
        TotalStatements = item.TotalStatements,
        CreatedAt = item.CreatedAt,
        CompletedAt = item.CompletedAt,
        Result = item.FinalResult,
        Error = item.Error
    };
}
