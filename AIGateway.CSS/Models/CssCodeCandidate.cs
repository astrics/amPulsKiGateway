namespace AiGateway.CSS.Api.Models;

public sealed class CssCodeCandidate
{
    public int Id { get; init; }
    public string CodeGroup { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string CodingRule { get; init; } = string.Empty;
    public string ExampleText { get; init; } = string.Empty;
    public double Score { get; init; }
    public List<string> MatchedTerms { get; init; } = new();
    public List<int> ExpandedFromCodeIds { get; init; } = new();
}
