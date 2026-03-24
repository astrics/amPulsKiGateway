namespace AiGateway.Api.Models.Requests;

public class AnalyzeRequest
{
    public string? Dashboard { get; set; }
    public List<AnalyzeStatementRequest> Statements { get; set; } = new();
}

public class AnalyzeStatementRequest
{
    public string? StatementId { get; set; }
    public string? MetadatenId { get; set; }
    public string? Text { get; set; }
}
