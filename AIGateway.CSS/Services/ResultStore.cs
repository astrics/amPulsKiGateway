using System.Collections.Concurrent;
using System.Text.Json;
using AiGateway.CSS.Api.Models;

namespace AiGateway.CSS.Api.Services;

public class ResultStore
{
    private readonly string _basePath;
    private readonly ILogger<ResultStore> _logger;
    private readonly ConcurrentDictionary<string, AnalysisResult> _resultCache = new();
    private readonly ConcurrentDictionary<string, AnalysisResult> _hashCache = new();

    public ResultStore(IConfiguration config, ILogger<ResultStore> logger)
    {
        _basePath = config["Storage:ResultsPath"] ?? Path.Combine(AppContext.BaseDirectory, "results");
        _logger = logger;

        Directory.CreateDirectory(_basePath);
        LoadExistingResults();
    }

    public AnalysisResult? FindByHash(string textHash, int? projectId = null)
    {
        if (string.IsNullOrEmpty(textHash))
            return null;

        _hashCache.TryGetValue(BuildHashKey(textHash, projectId), out var cached);
        return cached;
    }

    public async Task SaveResult(AnalysisResult result)
    {
        _resultCache[BuildResultKey(result.StatementId, result.ProjectId)] = result;
        if (!string.IsNullOrEmpty(result.TextHash))
            _hashCache[BuildHashKey(result.TextHash, result.ProjectId)] = result;

        var dashPath = Path.Combine(_basePath, SanitizePath(result.Dashboard));
        Directory.CreateDirectory(dashPath);

        var filePath = Path.Combine(dashPath, $"statement_{result.StatementId}.json");
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        await File.WriteAllTextAsync(filePath, json);

        await UpdateDashboardSummary(result.Dashboard, result.ProjectId);

        _logger.LogInformation(
            "Gespeichert: {File} | ProjectId={ProjectId}",
            filePath,
            result.ProjectId?.ToString() ?? "null");
    }

    public AnalysisResult? GetResult(int statementId, int? projectId = null)
    {
        if (projectId.HasValue && _resultCache.TryGetValue(BuildResultKey(statementId, projectId), out var exactResult))
        {
            return exactResult;
        }

        return _resultCache.Values
            .Where(r => r.StatementId == statementId)
            .Where(r => !projectId.HasValue || r.ProjectId == projectId)
            .OrderByDescending(r => r.AnalyzedAt)
            .FirstOrDefault();
    }

    public List<AnalysisResult> GetResults(string dashboard, int? projectId = null, int? sinceId = null)
    {
        var query = _resultCache.Values
            .Where(r => r.Dashboard.Equals(dashboard, StringComparison.OrdinalIgnoreCase));

        if (projectId.HasValue)
            query = query.Where(r => r.ProjectId == projectId);

        if (sinceId.HasValue)
            query = query.Where(r => r.StatementId > sinceId.Value);

        return query.OrderBy(r => r.StatementId).ToList();
    }

    public List<AnalysisResult> GetAllResults(int? projectId = null)
    {
        var query = _resultCache.Values.AsEnumerable();

        if (projectId.HasValue)
            query = query.Where(r => r.ProjectId == projectId);

        return query.OrderBy(r => r.StatementId).ToList();
    }

    public int GetTotalCount(int? projectId = null)
    {
        return projectId.HasValue
            ? _resultCache.Values.Count(r => r.ProjectId == projectId)
            : _resultCache.Count;
    }

    public Dictionary<string, int> GetStats(int? projectId = null)
    {
        var query = _resultCache.Values.AsEnumerable();

        if (projectId.HasValue)
            query = query.Where(r => r.ProjectId == projectId);

        return query
            .GroupBy(r => r.Status ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task UpdateDashboardSummary(string dashboard, int? projectId)
    {
        try
        {
            var results = GetResults(dashboard, projectId);
            var dashPath = Path.Combine(_basePath, SanitizePath(dashboard));
            var summaryFileName = projectId.HasValue
                ? $"_summary_project_{projectId.Value}.json"
                : "_summary.json";
            var summaryPath = Path.Combine(dashPath, summaryFileName);

            var summary = new
            {
                dashboard,
                projectId,
                updatedAt = DateTime.UtcNow,
                totalCount = results.Count,
                sentimentStats = new
                {
                    positiv = results.Count(r => r.Sentiment == "Positiv"),
                    negativ = results.Count(r => r.Sentiment == "Negativ"),
                    neutral = results.Count(r => r.Sentiment == "Neutral")
                },
                topKeywords = results
                    .SelectMany(r => r.Keywords ?? new List<AiKeyword>())
                    .GroupBy(k => k.Label)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => new { label = g.Key, count = g.Count() }),
                avgProcessingMs = results.Any() ? results.Average(r => r.ProcessingMs) : 0,
                results
            };

            var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            await File.WriteAllTextAsync(summaryPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Summary-Update fehlgeschlagen: {Error}", ex.Message);
        }
    }

    private void LoadExistingResults()
    {
        if (!Directory.Exists(_basePath))
            return;

        var files = Directory.GetFiles(_basePath, "statement_*.json", SearchOption.AllDirectories);
        var loaded = 0;

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var result = JsonSerializer.Deserialize<AnalysisResult>(json);
                if (result != null)
                {
                    _resultCache[BuildResultKey(result.StatementId, result.ProjectId)] = result;
                    if (!string.IsNullOrEmpty(result.TextHash))
                        _hashCache[BuildHashKey(result.TextHash, result.ProjectId)] = result;
                    loaded++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Laden fehlgeschlagen: {File}: {Error}", file, ex.Message);
            }
        }

        _logger.LogInformation("{Count} bestehende Ergebnisse aus JSON geladen", loaded);
    }

    private static string BuildResultKey(int statementId, int? projectId)
    {
        return $"{NormalizeProjectKey(projectId)}::{statementId}";
    }

    private static string BuildHashKey(string textHash, int? projectId)
    {
        return $"{NormalizeProjectKey(projectId)}::{textHash}";
    }

    private static string NormalizeProjectKey(int? projectId)
    {
        return projectId.HasValue ? $"project_{projectId.Value}" : "project_none";
    }

    private static string SanitizePath(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "default";

        return string.Join("_", input.Split(Path.GetInvalidFileNameChars()));
    }
}