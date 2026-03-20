using System.Security.Cryptography;
using System.Text;
using AiGateway.Api.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Services;

public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly GatewayOptions _options;
    private readonly ILogger<CacheService> _logger;
    private int _hits;
    private int _misses;

    public CacheService(
        IMemoryCache cache,
        IOptions<GatewayOptions> options,
        ILogger<CacheService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public string GenerateKey(string analysisType, string content)
    {
        var raw = $"{analysisType}:{_options.ModelName}:{_options.Temperature}:{content}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var key = Convert.ToHexString(hash)[..32];
        
        _logger.LogTrace("Cache-Key generiert: {Key} für AnalysisType: {Type}", 
            key, analysisType);
        
        return key;
    }

    public Task<string?> GetAsync(string key)
    {
        if (_cache.TryGetValue(key, out string? value))
        {
            var hits = Interlocked.Increment(ref _hits);
            _logger.LogDebug("📦 Cache HIT #{Hits}: {Key}, Länge: {Length} Zeichen", 
                hits, key[..8], value?.Length ?? 0);
            return Task.FromResult(value);
        }

        var misses = Interlocked.Increment(ref _misses);
        _logger.LogDebug("❌ Cache MISS #{Misses}: {Key}", misses, key[..8]);
        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(string key, string value)
    {
        // WICHTIG: SetSize braucht Bytes, nicht Zeichen!
        var sizeInBytes = Encoding.UTF8.GetByteCount(value);
        
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(_options.CacheDurationMinutes))
            .SetSize(sizeInBytes);

        _cache.Set(key, value, cacheOptions);
        
        _logger.LogDebug("📥 Cache SET: {Key}, {Size:N0} Bytes ({Chars} Zeichen), TTL: {Minutes}m", 
            key[..8], sizeInBytes, value.Length, _options.CacheDurationMinutes);

        return Task.CompletedTask;
    }

    public int GetCacheHitCount() => _hits;
    public int GetCacheMissCount() => _misses;
}
