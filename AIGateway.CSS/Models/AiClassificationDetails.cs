namespace AiGateway.CSS.Api.Models;

public class AiCodeMatch
{
    public int Id { get; set; }
    public string ExternalCodeNr { get; set; } = string.Empty;
    public string CodeGroup { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Sentiment { get; set; } = "Neutral";
    public string Evidence { get; set; } = string.Empty;
}

public class AiCodeGroupSentiment
{
    public string CodeGroup { get; set; } = string.Empty;
    public string Sentiment { get; set; } = "Neutral";
    public List<int> MatchedCodeIds { get; set; } = new();
}
