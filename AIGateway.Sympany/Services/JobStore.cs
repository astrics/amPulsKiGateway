using AiGateway.Sympany.Api.Models;

namespace AiGateway.Sympany.Api.Services;

public class JobStore
{
    private readonly Dictionary<string, JobInfo> _jobs = new();
    private readonly object _lock = new();

    public void CreateJob(string jobId, string dashboard, int totalStatements)
    {
        lock (_lock)
        {
            _jobs[jobId] = new JobInfo
            {
                JobId = jobId,
                Dashboard = dashboard,
                TotalStatements = totalStatements,
                Status = "processing",
                StartedAt = DateTime.UtcNow,
            };
        }
    }

    public void UpdateProgress(string jobId, int processed, int errorDelta)
    {
        lock (_lock)
        {
            if (_jobs.TryGetValue(jobId, out var job))
            {
                job.Processed = processed;
                job.Errors += errorDelta;
            }
        }
    }

    public void AddResult(string jobId, StatementResultEntry entry)
    {
        lock (_lock)
        {
            if (_jobs.TryGetValue(jobId, out var job))
            {
                job.Results.Add(entry);
            }
        }
    }

    public void CompleteJob(string jobId, double processingSec)
    {
        lock (_lock)
        {
            if (_jobs.TryGetValue(jobId, out var job))
            {
                job.Status = "completed";
                job.ProcessingSec = processingSec;
            }
        }
    }

    public JobInfo? GetJob(string jobId)
    {
        lock (_lock)
        {
            return _jobs.TryGetValue(jobId, out var job) ? job : null;
        }
    }
}

public class JobInfo
{
    public string JobId { get; set; } = "";
    public string Dashboard { get; set; } = "";
    public string Status { get; set; } = "pending";
    public int TotalStatements { get; set; }
    public int Processed { get; set; }
    public int Errors { get; set; }
    public double ProcessingSec { get; set; }
    public DateTime StartedAt { get; set; }
    public List<StatementResultEntry> Results { get; set; } = new();
}

