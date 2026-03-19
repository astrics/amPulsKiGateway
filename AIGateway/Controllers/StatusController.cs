using AiGateway.Api.Models.Responses;
using AiGateway.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly IQueueService _queue;
    private readonly ICacheService _cache;

    public StatusController(IQueueService queue, ICacheService cache)
    {
        _queue = queue;
        _cache = cache;
    }

    /// <summary>
    /// Dashboard-Info: Aktuelle Queue- und Cache-Statistiken
    /// GET /api/status
    /// </summary>
    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            queue = new
            {
                pending = _queue.GetPendingCount(),
                active = _queue.GetActiveCount(),
                completedTotal = _queue.GetCompletedTotal()
            },
            cache = new
            {
                hits = _cache.GetCacheHitCount(),
                misses = _cache.GetCacheMissCount(),
                hitRate = (_cache.GetCacheHitCount() + _cache.GetCacheMissCount()) > 0
                    ? Math.Round(
                        (double)_cache.GetCacheHitCount() /
                        (_cache.GetCacheHitCount() + _cache.GetCacheMissCount()) * 100, 1)
                    : 0
            },
            timestamp = DateTime.UtcNow
        });
    }
}
