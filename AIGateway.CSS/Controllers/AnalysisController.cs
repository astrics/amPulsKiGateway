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
    private readonly CssCodeCandidateRetriever _candidateRetriever;

    public AnalysisController(
        LmStudioService lmStudio,
        ResultStore store,
        ILogger<AnalysisController> logger,
        IConfiguration config,
        CssCodeCandidateRetriever candidateRetriever)
    {
        _lmStudio = lmStudio;
        _store = store;
        _logger = logger;
        _config = config;
        _candidateRetriever = candidateRetriever;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] SyncAnalyzeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text darf nicht leer sein" });

        _logger.LogInformation(
            "Analyse: Statement {Id}, ProjectId={ProjectId}, Dashboard={Dash}, IgnoreCache={IgnoreCache}, Kundenzufriedenheit={Score}",
            request.StatementId,
            request.ProjectId,
            request.Dashboard,
            request.IgnoreCache,
            request.CustomerSatisfactionScore);

        try
        {
            var cached = request.IgnoreCache ? null : _store.FindByHash(request.TextHash, request.ProjectId);
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
                    Keywords = CloneKeywords(cached.Keywords),
                    CodeMatches = CloneCodeMatches(cached.CodeMatches),
                    CodeGroupSentiments = CloneCodeGroupSentiments(cached.CodeGroupSentiments),
                    CandidateCodeIds = cached.CandidateCodeIds.ToList(),
                    NeedsReview = cached.NeedsReview,
                    ValidationWarnings = cached.ValidationWarnings.ToList(),
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
                    customerSatisfactionScore = request.CustomerSatisfactionScore,
                    statement = cachedResult.Statement,
                    sentiment = cachedResult.Sentiment,
                    keywords = cachedResult.Keywords,
                    codeMatches = cachedResult.CodeMatches,
                    codeGroupSentiments = cachedResult.CodeGroupSentiments,
                    candidateCodeIds = cachedResult.CandidateCodeIds,
                    needsReview = cachedResult.NeedsReview,
                    validationWarnings = cachedResult.ValidationWarnings,
                    processingMs = 0
                });
            }

            var startTime = DateTime.UtcNow;
            var aiResult = await _lmStudio.AnalyzeSync(request.Text, request.CustomerSatisfactionScore, request.ProjectId, CancellationToken.None);
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
                Keywords = CloneKeywords(aiResult.Keywords),
                CodeMatches = CloneCodeMatches(aiResult.CodeMatches),
                CodeGroupSentiments = CloneCodeGroupSentiments(aiResult.CodeGroupSentiments),
                CandidateCodeIds = aiResult.CandidateCodeIds.ToList(),
                NeedsReview = aiResult.NeedsReview,
                ValidationWarnings = aiResult.ValidationWarnings.ToList(),
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
                customerSatisfactionScore = request.CustomerSatisfactionScore,
                statement = result.Statement,
                sentiment = result.Sentiment,
                keywords = result.Keywords,
                codeMatches = result.CodeMatches,
                codeGroupSentiments = result.CodeGroupSentiments,
                candidateCodeIds = result.CandidateCodeIds,
                needsReview = result.NeedsReview,
                validationWarnings = result.ValidationWarnings,
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
                customerSatisfactionScore = request.CustomerSatisfactionScore,
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
                r.CodeMatches,
                r.CodeGroupSentiments,
                r.CandidateCodeIds,
                r.NeedsReview,
                r.ValidationWarnings,
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

    [HttpPost("candidate-preview")]
    public IActionResult PreviewCandidates([FromBody] SyncAnalyzeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "Text darf nicht leer sein" });
        }

        var candidates = _candidateRetriever.Retrieve(request.Text);
        return Ok(new
        {
            projectId = request.ProjectId,
            classificationMode = _lmStudio.ResolveClassificationMode(request.ProjectId),
            statement = request.Text,
            candidateCodeIds = candidates.Select(candidate => candidate.Id),
            candidates = candidates.Select(candidate => new
            {
                id = candidate.Id,
                codeGroup = candidate.CodeGroup,
                code = candidate.Code,
                score = candidate.Score,
                matchedTerms = candidate.MatchedTerms,
                expandedFromCodeIds = candidate.ExpandedFromCodeIds
            })
        });
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
            return NotFound(new { status = "pending", message = "Ergebnis noch nicht verfuegbar" });

        return Ok(result);
    }

    private static List<AiKeyword> CloneKeywords(IEnumerable<AiKeyword>? source)
    {
        return source?.Select(keyword => new AiKeyword
        {
            Id = keyword.Id,
            ExternalCodeNr = keyword.ExternalCodeNr,
            CodeGroup = keyword.CodeGroup,
            Label = keyword.Label
        }).ToList()
               ?? new List<AiKeyword>();
    }

    private static List<AiCodeMatch> CloneCodeMatches(IEnumerable<AiCodeMatch>? source)
    {
        return source?.Select(match => new AiCodeMatch
        {
            Id = match.Id,
            ExternalCodeNr = match.ExternalCodeNr,
            CodeGroup = match.CodeGroup,
            Code = match.Code,
            Sentiment = match.Sentiment,
            Evidence = match.Evidence
        }).ToList() ?? new List<AiCodeMatch>();
    }

    private static List<AiCodeGroupSentiment> CloneCodeGroupSentiments(IEnumerable<AiCodeGroupSentiment>? source)
    {
        return source?.Select(group => new AiCodeGroupSentiment
        {
            CodeGroup = group.CodeGroup,
            Sentiment = group.Sentiment,
            MatchedCodeIds = group.MatchedCodeIds.ToList()
        }).ToList() ?? new List<AiCodeGroupSentiment>();
    }
}





