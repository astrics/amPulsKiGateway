namespace AiGateway.Api.Services;

public interface ICacheService
{
    string GenerateKey(string analysisType, string content);
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    int GetCacheHitCount();
    int GetCacheMissCount();
}
