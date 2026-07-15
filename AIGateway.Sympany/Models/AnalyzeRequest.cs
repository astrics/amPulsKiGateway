namespace AiGateway.Sympany.Api.Models;

public class AnalyzeRequest
{
    public int StatementId { get; set; }
    public int? MetadatenId { get; set; }
    public string Dashboard { get; set; } = "";
    public string Text { get; set; } = "";
    public string TextHash { get; set; } = "";
    public string AnalysisType { get; set; } = "service_sentiment";
}
