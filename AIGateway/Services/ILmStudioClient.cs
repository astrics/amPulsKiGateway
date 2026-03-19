namespace AiGateway.Api.Services;

public interface ILmStudioClient
{
    Task<string> ChatCompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
    Task<(bool isReachable, string? modelName, int? responseTimeMs)> HealthCheckAsync();
}
