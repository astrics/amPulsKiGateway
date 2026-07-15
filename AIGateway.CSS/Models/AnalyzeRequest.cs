using AiGateway.CSS.Api.Models.Requests;
using AiGateway.CSS.Api.Services;


namespace AiGateway.CSS.Api.Models
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


