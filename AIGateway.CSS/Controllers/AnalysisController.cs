using Microsoft.AspNetCore.Mvc;
using AiGateway.CSS.Api.Models;
using AiGateway.CSS.Api.Services;

namespace AiGateway.CSS.Api.Controllers;

[ApiController]
[Route("api/analysis")]
public class AnalysisController : ControllerBase
{
    private readonly LmStudioService _lmStudio;
    private readonly ResultStore _store;
    private readonly ILogger<AnalysisController> _logger;
    private readonly IConfiguration _config;

    public AnalysisController(
        LmStudioService lmStudio,
        ResultStore store,
        ILogger<AnalysisController> logger,
        IConfiguration config)
    {
        _lmStudio = lmStudio;
        _store = store;
        _logger = logger;
        _config = config;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] SyncAnalyzeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text darf nicht leer sein" });

        _logger.LogInformation(
            "Analyse: Statement {Id}, ProjectId={ProjectId}, Dashboard={Dash}",
            request.StatementId,
            request.ProjectId,
            request.Dashboard);

        try
        {
            var cached = _store.FindByHash(request.TextHash, request.ProjectId);
            if (cached != null && cached.Status == "completed")
            {
                _logger.LogInformation(
                    "Cache-Hit: ProjectId={ProjectId}, Hash {Hash} (Statement {From} -> {To})",
                    request.ProjectId,
                    request.TextHash,
                    cached.StatementId,
                    request.StatementId);

                var cachedResult = new AnalysisResult
                {
                    StatementId = request.StatementId,
                    MetadatenId = request.MetadatenId,
                    ProjectId = request.ProjectId,
                    Dashboard = request.Dashboard,
                    Text = request.Text,
                    TextHash = request.TextHash,
                    Statement = cached.Statement,
                    Sentiment = cached.Sentiment,
                    Keywords = cached.Keywords,
                    RawResponse = cached.RawResponse,
                    ProcessingMs = 0,
                    Status = "completed",
                    CachedFrom = cached.StatementId,
                    AnalyzedAt = DateTime.UtcNow
                };

                await _store.SaveResult(cachedResult);

                return Ok(new
                {
                    statementId = cachedResult.StatementId,
                    projectId = cachedResult.ProjectId,
                    status = "completed",
                    cached = true,
                    cachedFrom = cached.StatementId,
                    statement = cachedResult.Statement,
                    sentiment = cachedResult.Sentiment,
                    keywords = cachedResult.Keywords,
                    processingMs = 0
                });
            }

            var startTime = DateTime.UtcNow;
            var aiResult = await _lmStudio.AnalyzeSync(request.Text, cancellationToken);
            var processingMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

            var result = new AnalysisResult
            {
                StatementId = request.StatementId,
                MetadatenId = request.MetadatenId,
                ProjectId = request.ProjectId,
                Dashboard = request.Dashboard,
                Text = request.Text,
                TextHash = request.TextHash,
                Statement = aiResult.Statement,
                Sentiment = aiResult.Sentiment,
                Keywords = aiResult.Keywords,
                RawResponse = aiResult.RawResponse,
                ParseError = aiResult.ParseError,
                ProcessingMs = processingMs,
                Status = "completed",
                AnalyzedAt = DateTime.UtcNow
            };

            await _store.SaveResult(result);

            _logger.LogInformation(
                "Fertig: Statement {Id}, ProjectId={ProjectId}, Sentiment={Sentiment}, {Ms}ms",
                request.StatementId,
                request.ProjectId,
                result.Sentiment,
                processingMs);

            return Ok(new
            {
                statementId = result.StatementId,
                projectId = result.ProjectId,
                status = "completed",
                cached = false,
                statement = result.Statement,
                sentiment = result.Sentiment,
                keywords = result.Keywords,
                rawResponse = result.RawResponse,
                processingMs
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Analyse abgebrochen, weil der Client die Verbindung geschlossen hat | Statement {Id}",
                request.StatementId);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Statement {Id}", request.StatementId);

            var errorResult = new AnalysisResult
            {
                StatementId = request.StatementId,
                MetadatenId = request.MetadatenId,
                ProjectId = request.ProjectId,
                Dashboard = request.Dashboard,
                Text = request.Text,
                TextHash = request.TextHash,
                Status = "error",
                ErrorMessage = ex.Message,
                AnalyzedAt = DateTime.UtcNow
            };

            await _store.SaveResult(errorResult);

            return StatusCode(500, new
            {
                statementId = request.StatementId,
                projectId = request.ProjectId,
                status = "error",
                error = ex.Message
            });
        }
    }

    [HttpGet("results/{dashboard}")]
    public IActionResult GetResults(string dashboard, [FromQuery] int? since = null, [FromQuery] int? projectId = null)
    {
        var results = _store.GetResults(dashboard, projectId, since);
        return Ok(new
        {
            dashboard,
            projectId,
            count = results.Count,
            results = results.Select(r => new
            {
                r.StatementId,
                r.MetadatenId,
                r.ProjectId,
                r.Status,
                r.Statement,
                r.Sentiment,
                r.Keywords,
                r.ProcessingMs,
                r.CachedFrom,
                r.AnalyzedAt
            })
        });
    }

    [HttpGet("status")]
    public IActionResult GetStatus([FromQuery] int? projectId = null)
    {
        return Ok(new
        {
            status = "ready",
            projectId,
            totalResults = _store.GetTotalCount(projectId),
            stats = _store.GetStats(projectId),
            timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("results-all")]
    public IActionResult GetAllResults([FromQuery] string? status = null, [FromQuery] int? projectId = null)
    {
        var results = _store.GetAllResults(projectId);
        if (!string.IsNullOrEmpty(status))
            results = results.Where(r => r.Status == status).ToList();

        return Ok(new { projectId, count = results.Count, results });
    }

    [HttpGet("result/{statementId}")]
    public IActionResult GetResult(int statementId, [FromQuery] string dashboard = "Kundendienst", [FromQuery] int? projectId = null)
    {
        var resultsPath = _config["Storage:ResultsPath"] ?? @"D:\AI-Gateway\results-css";
        var filePath = Path.Combine(resultsPath, dashboard, $"statement_{statementId}.json");

        if (System.IO.File.Exists(filePath))
        {
            var json = System.IO.File.ReadAllText(filePath);
            return Content(json, "application/json");
        }

        var result = _store.GetResult(statementId, projectId);
        if (result == null)
            return NotFound(new { status = "pending", message = "Ergebnis noch nicht verfügbar" });

        return Ok(result);
    }
}