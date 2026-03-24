namespace AiGateway.Api.Services;

public class StatementInput
{
    public string StatementId { get; set; } = "";
    public string MetadatenId { get; set; } = "";
    public string? Text { get; set; }
}
