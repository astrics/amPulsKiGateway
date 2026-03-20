using AiGateway.Api.Models.Responses;
using AiGateway.Api.Models.Responses;
using AiGateway.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILmStudioClient _lmStudio;
    private readonly IQueueService _queue;
    private readonly MemoryDiagnosticsService _memoryDiagnostics;

    public HealthController(
        ILmStudioClient lmStudio, 
        IQueueService queue,
        MemoryDiagnosticsService memoryDiagnostics)
    {
        _lmStudio = lmStudio;
        _queue = queue;
        _memoryDiagnostics = memoryDiagnostics;
    }

    /// <summary>
    /// Health Check - auch für Monitoring
    /// GET /api/health
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (isReachable, modelName, responseTimeMs) = await _lmStudio.HealthCheckAsync();

        var health = new HealthResponse
        {
            Status = isReachable ? "healthy" : "degraded",
            LmStudio = new LmStudioHealth
            {
                IsReachable = isReachable,
                ModelLoaded = modelName,
                ResponseTimeMs = responseTimeMs
            },
            Queue = new QueueHealth
            {
                PendingJobs = _queue.GetPendingCount(),
                ActiveJobs = _queue.GetActiveCount(),
                CompletedJobsTotal = _queue.GetCompletedTotal()
            }
        };

        return isReachable ? Ok(health) : StatusCode(503, health);
    }

    /// <summary>
    /// Memory Diagnostics - Detaillierte Speicher-Informationen
    /// GET /api/health/memory
    /// </summary>
    [HttpGet("memory")]
    public IActionResult GetMemory([FromQuery] bool snapshot = false)
    {
        if (snapshot)
        {
            // Schreibe Snapshot in Datei
            _memoryDiagnostics.WriteMemorySnapshot("Manual Health Check");
        }

        var memoryInfo = _memoryDiagnostics.GetMemoryInfo();
        return Ok(memoryInfo);
    }
}

