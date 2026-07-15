namespace AiGateway.CSS.Api.Services;

public interface ILmStudioClient
{
    Task<LmStudioResponse> ClassifyStatementAsync(string statementText, CancellationToken ct = default);
    Task<string> ChatCompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
    Task<(bool isReachable, string? modelName, int? responseTimeMs)> HealthCheckAsync();
}

