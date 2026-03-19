namespace AiGateway.Api.Models.Responses;

public class JobStatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public int? QueuePosition { get; set; }
    public int TotalChunks { get; set; }
    public int CompletedChunks { get; set; }
    public double ProgressPercent => TotalChunks > 0
        ? Math.Round((double)CompletedChunks / TotalChunks * 100, 1)
        : 0;
    public string Source { get; set; } = string.Empty;
    public string AnalysisType { get; set; } = string.Empty;
    public int TotalStatements { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? ProcessingTimeSeconds => CompletedAt.HasValue
        ? (CompletedAt.Value - CreatedAt).TotalSeconds
        : null;
    public object? Result { get; set; }
    public string? Error { get; set; }
}
