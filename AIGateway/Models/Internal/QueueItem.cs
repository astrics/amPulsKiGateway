using AiGateway.Api.Models.Requests;
using AiGateway.Api.Models.Internal;

namespace AiGateway.Api.Models.Internal;

public class QueueItem
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Source { get; set; } = string.Empty;
    public string AnalysisType { get; set; } = string.Empty;
    public int Priority { get; set; } = 5;
    public string? CustomPrompt { get; set; }
    public string? CallbackUrl { get; set; }

    // Status
    public string Status { get; set; } = "queued";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }

    // Chunks
    public List<Chunk> Chunks { get; set; } = new();
    public List<string> ChunkResults { get; set; } = new();
    public int CompletedChunks { get; set; }

    // Original-Daten für Response
    public int TotalStatements { get; set; }

    // Aggregiertes Ergebnis
    public object? FinalResult { get; set; }
}
