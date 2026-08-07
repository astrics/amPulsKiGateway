using Microsoft.AspNetCore.Mvc;
using AiGateway.Sympany.Api.Models;
using AiGateway.Sympany.Api.Services;
using AiGateway.Sympany.Api.Configuration;
using Microsoft.Extensions.Options;

namespace AiGateway.Sympany.Api.Controllers;

[ApiController]
[Route("api/analysis")]
public class AnalysisController : ControllerBase
{
    private readonly LmStudioService _lmStudio;
    private readonly ResultStore _store;
    private readonly ILogger<AnalysisController> _logger;
    private readonly IConfiguration _config;
    private readonly TimeSpan _analysisTimeout;

    public AnalysisController(
        LmStudioService lmStudio,
        ResultStore store,
        ILogger<AnalysisController> logger,
        IConfiguration config,
        IOptions<GatewayOptions> options)
    {
        _lmStudio = lmStudio;
        _store = store;
        _logger = logger;
        _config = config;
        _analysisTimeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.RequestTimeoutSeconds));
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] SyncAnalyzeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text darf nicht leer sein" });

        _logger.LogInformation("Analyse: Statement {Id}, Dashboard={Dash}", request.StatementId, request.Dashboard);

        using var analysisCts = new CancellationTokenSource(_analysisTimeout);

        try
        {
            var cached = _store.FindByHash(request.TextHash);
            if (cached != null && cached.Status == "completed")
            {
                _logger.LogInformation(
                    "Cache-Hit: Hash {Hash} (Statement {From} -> {To})",
                    request.TextHash,
                    cached.StatementId,
                    request.StatementId);

                var cachedResult = new AnalysisResult
                {
                    StatementId = request.StatementId,
                    MetadatenId = request.MetadatenId,
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
            var aiResult = await _lmStudio.AnalyzeSync(request.Text, analysisCts.Token);
            var processingMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

            var result = new AnalysisResult
            {
                StatementId = request.StatementId,
                MetadatenId = request.MetadatenId,
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
                "Fertig: Statement {Id}, Sentiment={Sentiment}, {Ms}ms",
                request.StatementId,
                result.Sentiment,
                processingMs);

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Analyse trotz geschlossener Client-Verbindung abgeschlossen und gespeichert | Statement {Id}",
                    request.StatementId);

                Response.StatusCode = 499;
                return new EmptyResult();
            }

            return Ok(new
            {
                statementId = result.StatementId,
                status = "completed",
                cached = false,
                statement = result.Statement,
                sentiment = result.Sentiment,
                keywords = result.Keywords,
                rawResponse = result.RawResponse,
                processingMs
            });
        }
        catch (OperationCanceledException ex) when (analysisCts.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Analyse nach {TimeoutSeconds}s durch Gateway-Timeout beendet | Statement {Id}",
                _analysisTimeout.TotalSeconds,
                request.StatementId);

            var errorResult = new AnalysisResult
            {
                StatementId = request.StatementId,
                MetadatenId = request.MetadatenId,
                Dashboard = request.Dashboard,
                Text = request.Text,
                TextHash = request.TextHash,
                Status = "error",
                ErrorMessage = $"Gateway-Timeout nach {_analysisTimeout.TotalSeconds:0}s",
                AnalyzedAt = DateTime.UtcNow
            };

            await _store.SaveResult(errorResult);

            if (cancellationToken.IsCancellationRequested)
            {
                Response.StatusCode = 499;
                return new EmptyResult();
            }

            return StatusCode(504, new
            {
                statementId = request.StatementId,
                status = "error",
                error = errorResult.ErrorMessage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei Statement {Id}", request.StatementId);

            var errorResult = new AnalysisResult
            {
                StatementId = request.StatementId,
                MetadatenId = request.MetadatenId,
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
                status = "error",
                error = ex.Message
            });
        }
    }

    [HttpGet("results/{dashboard}")]
    public IActionResult GetResults(string dashboard, [FromQuery] int? since = null)
    {
        var results = _store.GetResults(dashboard, since);
        return Ok(new
        {
            dashboard,
            count = results.Count,
            results = results.Select(r => new
            {
                r.StatementId,
                r.MetadatenId,
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
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            status = "ready",
            totalResults = _store.GetTotalCount(),
            stats = _store.GetStats(),
            timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("results-all")]
    public IActionResult GetAllResults([FromQuery] string? status = null)
    {
        var results = _store.GetAllResults();
        if (!string.IsNullOrEmpty(status))
            results = results.Where(r => r.Status == status).ToList();

        return Ok(new { count = results.Count, results });
    }

    [HttpGet("result/{statementId}")]
    public IActionResult GetResult(int statementId, [FromQuery] string dashboard = "Kundendienst")
    {
        var resultsPath = _config["Storage:ResultsPath"] ?? @"D:\AI-Gateway\results-sympany";
        var filePath = Path.Combine(resultsPath, dashboard, $"statement_{statementId}.json");

        if (!System.IO.File.Exists(filePath))
            return NotFound(new { status = "pending", message = "Ergebnis noch nicht verfügbar" });

        var json = System.IO.File.ReadAllText(filePath);
        return Content(json, "application/json");
    }
}
