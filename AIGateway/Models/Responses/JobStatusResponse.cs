namespace AiGateway.Api.Models.Responses;

public class JobStatusResponse
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Dashboard { get; set; } = "";
    public int TotalStatements { get; set; }
    public int Processed { get; set; }
    public int Errors { get; set; }
    public double Percent { get; set; }
    public double ProcessingSec { get; set; }
}
