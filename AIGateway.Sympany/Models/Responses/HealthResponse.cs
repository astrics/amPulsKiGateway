namespace AiGateway.Sympany.Api.Models.Responses;

public class HealthResponse
{
    public string Status { get; set; } = "healthy";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0.0";
    public LmStudioHealth LmStudio { get; set; } = new();
    public QueueHealth Queue { get; set; } = new();
}

public class LmStudioHealth
{
    public bool IsReachable { get; set; }
    public string? ModelLoaded { get; set; }
    public int? ResponseTimeMs { get; set; }
}

public class QueueHealth
{
    public int PendingJobs { get; set; }
    public int ActiveJobs { get; set; }
    public int CompletedJobsTotal { get; set; }
}

