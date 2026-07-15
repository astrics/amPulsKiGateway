using Microsoft.AspNetCore.Mvc;
using AiGateway.Sympany.Api.Services;
using AiGateway.Sympany.Api.Models;

namespace AiGateway.Sympany.Api.Controllers;

[ApiController]
[Route("api/results")]
public class ResultsController : ControllerBase
{
    private readonly JobStore _jobStore;

    public ResultsController(JobStore jobStore)
    {
        _jobStore = jobStore;
    }

    [HttpGet("{jobId}")]
    public IActionResult GetResults(string jobId)
    {
        var job = _jobStore.GetJob(jobId);
        if (job == null)
            return NotFound(new { status = "error", message = "Job nicht gefunden" });

        return Ok(new
        {
            job_id = job.JobId,
            status = job.Status,
            total = job.TotalStatements,
            processed = job.Processed,
            errors = job.Errors,
            results = job.Results.Select(r => new
            {
                statement_id = r.StatementId,
                metadaten_id = r.MetadatenId,
                dashboard = r.Dashboard,
                text = r.Text,
                sentiment = r.Sentiment,
                keywords = r.Keywords.Select(k => new { k.Id, k.Label }),
                processing_ms = r.ProcessingMs,
                processed_at = r.ProcessedAt,
                error = r.Error,
                raw_response = r.RawResponse
            })
        });
    }
}

