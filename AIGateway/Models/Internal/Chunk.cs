using AiGateway.Api.Models.Requests;

namespace AiGateway.Api.Models.Internal;

public class Chunk
{
    public int Index { get; set; }
    public List<CustomerStatement> Statements { get; set; } = new();
    public int EstimatedTokens { get; set; }
}
