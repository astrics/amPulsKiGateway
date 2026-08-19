namespace AiGateway.CSS.Api.Models;

public class SyncAnalyzeRequest
{
    public int StatementId { get; set; }
    public int? MetadatenId { get; set; }
    public int? ProjectId { get; set; }
    public bool IgnoreCache { get; set; }
    public string Dashboard { get; set; } = "";
    public string Text { get; set; } = "";
    public string TextHash { get; set; } = "";
    public string AnalysisType { get; set; } = "service_sentiment";
}