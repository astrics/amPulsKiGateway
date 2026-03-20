using AiGateway.Api.Models.Internal;
using AiGateway.Api.Models.Requests;
using AiGateway.Api.Models.Responses;
using AiGateway.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyzeController : ControllerBase
{
    private readonly IQueueService _queue;
    private readonly IChunkService _chunker;
    private readonly IJobPersistenceService _persistence;
    private readonly ILogger<AnalyzeController> _logger;

    public AnalyzeController(
        IQueueService queue,
        IChunkService chunker,
        IJobPersistenceService persistence,
        ILogger<AnalyzeController> logger)
    {
        _queue = queue;
        _chunker = chunker;
        _persistence = persistence;
        _logger = logger;
    }

    /// <summary>
    /// Neue Analyse einreichen
    /// POST /api/analyze
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] AnalysisRequest request)
    {
        if (request.Statements == null || !request.Statements.Any())
            return BadRequest(new { error = "Keine Aussagen übergeben ('statements' ist leer)" });

        if (request.Statements.Count > 1000)
            return BadRequest(new { error = "Maximal 1000 Aussagen pro Request" });

        var chunks = _chunker.Split(request.Statements);

        var item = new QueueItem
        {
            Source = request.Source,
            AnalysisType = request.AnalysisType,
            Priority = Math.Clamp(request.Priority, 1, 10),
            CustomPrompt = request.CustomPrompt,
            CallbackUrl = request.CallbackUrl,
            Chunks = chunks,
            TotalStatements = request.Statements.Count
        };

        var queued = await _queue.EnqueueAsync(item);

        // ─── Sofort auf Disk persistieren ───
        await _persistence.SaveJobAsync(queued.JobId, queued);

        _logger.LogInformation("💾 Job {JobId} persisted (queued, {Chunks} Chunks, {Statements} Aussagen)",
            queued.JobId, chunks.Count, request.Statements.Count);

        return Accepted(new JobStatusResponse
        {
            JobId = queued.JobId,
            Status = "queued",
            QueuePosition = _queue.GetPendingCount(),
            TotalChunks = chunks.Count,
            CompletedChunks = 0,
            Source = request.Source,
            AnalysisType = request.AnalysisType,
            TotalStatements = request.Statements.Count,
            CreatedAt = queued.CreatedAt
        });
    }

    /// <summary>
    /// Job-Status abfragen
    /// GET /api/analyze/status/{jobId}
    /// </summary>
    [HttpGet("status/{jobId}")]
    public async Task<IActionResult> GetStatus(string jobId)
    {
        // 1. RAM (schnell)
        var status = await _queue.GetStatusAsync(jobId);
        if (status != null)
            return Ok(status);

        // 2. Disk-Fallback (nach Neustart)
        var diskJob = await _persistence.LoadJobAsync(jobId);
        if (diskJob != null)
        {
            _logger.LogInformation("📂 Job {JobId} von Disk geladen (Status: {Status})",
                jobId, diskJob.Status);

            return Ok(new JobStatusResponse
            {
                JobId = diskJob.JobId,
                Status = diskJob.Status,
                TotalChunks = diskJob.Chunks?.Count ?? 0,
                CompletedChunks = diskJob.CompletedChunks,
                Source = diskJob.Source,
                AnalysisType = diskJob.AnalysisType,
                TotalStatements = diskJob.TotalStatements,
                CreatedAt = diskJob.CreatedAt,
                CompletedAt = diskJob.CompletedAt,
                Result = diskJob.FinalResult,
                Error = diskJob.Error
            });
        }

        return NotFound(new { error = $"Job '{jobId}' nicht gefunden" });
    }

    /// <summary>
    /// Alle Jobs auflisten
    /// GET /api/analyze/jobs
    /// </summary>
    [HttpGet("jobs")]
    public IActionResult GetAllJobs()
    {
        if (_queue is QueueService qs)
            return Ok(qs.GetAllJobs());

        return Ok(new List<JobStatusResponse>());
    }
}
