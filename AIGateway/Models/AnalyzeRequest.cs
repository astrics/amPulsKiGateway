using AiGateway.Api.Models.Requests;
using AiGateway.Api.Services;


namespace AiGateway.Api.Models
{
    public class AnalyzeRequest
    {
        public string? Dashboard { get; set; }
        public string? Source { get; set; }
        public string? AnalysisType { get; set; }
        public string? Priority { get; set; }
        public string? Prompt { get; set; }
        public List<StatementInput> Statements { get; set; } = new();
    
    }
}
