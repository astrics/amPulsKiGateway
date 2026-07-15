using Microsoft.AspNetCore.Mvc;
using AiGateway.Sympany.Api.Services;
using AiGateway.Sympany.Api.Models.Requests;

namespace AiGateway.Sympany.Api.Controllers;

[ApiController]
[Route("api/analyze")]
public class AnalyzeController : ControllerBase
{
    private readonly BatchJobProcessor _processor;
    private readonly JobStore _jobStore;
    private readonly ILogger<AnalyzeController> _logger;

    public AnalyzeController(BatchJobProcessor processor, JobStore jobStore, ILogger<AnalyzeController> logger)
    {
        _processor = processor;
        _jobStore = jobStore;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult StartAnalysis([FromBody] AnalyzeRequest request)
    {
        if (request.Statements == null || request.Statements.Count == 0)
            return BadRequest(new { status = "error", message = "Keine Statements übergeben" });

        var jobId = $"job_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
        var dashboard = request.Dashboard ?? "Unbekannt";

        var statements = request.Statements.Select(s => new StatementInput
        {
            StatementId = s.StatementId ?? "",
            MetadatenId = s.MetadatenId ?? "",
            Text = s.Text
        }).ToList();

        // Job registrieren
        _jobStore.CreateJob(jobId, dashboard, statements.Count);

        // Verarbeitung starten
        _processor.StartJob(jobId, dashboard, statements);

        _logger.LogInformation("Job {JobId} gestartet: {Count} Statements für {Dashboard}",
            jobId, statements.Count, dashboard);

        return Accepted(new
        {
            job_id = jobId,
            status = "accepted",
            total_statements = statements.Count,
            dashboard
        });
    }
}

