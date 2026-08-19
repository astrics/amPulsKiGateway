namespace AiGateway.CSS.Api.Models;

public class AiResult
{
    public string Statement { get; set; } = "";
    public string Sentiment { get; set; } = "Neutral";
    public List<AiKeyword> Keywords { get; set; } = new();
    public List<AiCodeMatch> CodeMatches { get; set; } = new();
    public List<AiCodeGroupSentiment> CodeGroupSentiments { get; set; } = new();
    public string RawResponse { get; set; } = "";
    public string? ParseError { get; set; }
}

public class AiKeyword
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
}