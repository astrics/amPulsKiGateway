namespace AiGateway.Api.Models;

public class AnalysisResult
{
    public int StatementId { get; set; }
    public int? MetadatenId { get; set; }
    public string Dashboard { get; set; } = "";
    public string Text { get; set; } = "";
    public string TextHash { get; set; } = "";
    public string Status { get; set; } = "completed";

    // KI-Ergebnis passend zum PromptBuilder-Format
    public string? Statement { get; set; }
    public string? Sentiment { get; set; }
    public List<AiKeyword>? Keywords { get; set; }

    public string? RawResponse { get; set; }
    public string? ParseError { get; set; }
    public string? ErrorMessage { get; set; }
    public int ProcessingMs { get; set; }
    public int? CachedFrom { get; set; }
    public DateTime AnalyzedAt { get; set; }
}
