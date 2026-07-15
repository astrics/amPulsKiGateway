using AiGateway.CSS.Api.Models.Responses;
using AiGateway.CSS.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.CSS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILmStudioClient _lmStudio;
    private readonly MemoryDiagnosticsService _memoryDiagnostics;

    public HealthController(
        ILmStudioClient lmStudio,
        MemoryDiagnosticsService memoryDiagnostics)
    {
        _lmStudio = lmStudio;
        _memoryDiagnostics = memoryDiagnostics;
    }

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
            }
        };

        return isReachable ? Ok(health) : StatusCode(503, health);
    }

    [HttpGet("memory")]
    public IActionResult GetMemory([FromQuery] bool snapshot = false)
    {
        if (snapshot)
        {
            _memoryDiagnostics.WriteMemorySnapshot("Manual Health Check");
        }

        var memoryInfo = _memoryDiagnostics.GetMemoryInfo();
        return Ok(memoryInfo);
    }
}

