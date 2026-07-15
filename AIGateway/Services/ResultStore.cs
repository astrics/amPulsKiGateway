using System.Collections.Concurrent;
using System.Text.Json;
using AiGateway.Api.Models;

namespace AiGateway.Api.Services;

public class ResultStore
{
    private readonly string _basePath;
    private readonly ILogger<ResultStore> _logger;
    private readonly ConcurrentDictionary<int, AnalysisResult> _resultCache = new();
    private readonly ConcurrentDictionary<string, AnalysisResult> _hashCache = new();

    public ResultStore(IConfiguration config, ILogger<ResultStore> logger)
    {
        _basePath = config["Storage:ResultsPath"] ?? Path.Combine(AppContext.BaseDirectory, "results");
        _logger = logger;

        Directory.CreateDirectory(_basePath);
        LoadExistingResults();
    }

    /// <summary>
    /// Prüft ob ein Text-Hash bereits analysiert wurde (Cache-Hit)
    /// </summary>
    public AnalysisResult? FindByHash(string textHash)
    {
        if (string.IsNullOrEmpty(textHash)) return null;
        _hashCache.TryGetValue(textHash, out var cached);
        return cached;
    }

    /// <summary>
    /// Ergebnis speichern: lokal als JSON + In-Memory-Cache
    /// </summary>
    public async Task SaveResult(AnalysisResult result)
    {
        // In-Memory-Cache
        _resultCache[result.StatementId] = result;
        if (!string.IsNullOrEmpty(result.TextHash))
            _hashCache[result.TextHash] = result;

        // Dashboard-Ordner
        var dashPath = Path.Combine(_basePath, SanitizePath(result.Dashboard));
        Directory.CreateDirectory(dashPath);

        // Einzeldatei
        var filePath = Path.Combine(dashPath, $"statement_{result.StatementId}.json");
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        await File.WriteAllTextAsync(filePath, json);

        // Gesamtdatei aktualisieren
        await UpdateDashboardSummary(result.Dashboard);

        _logger.LogInformation("Gespeichert: {File}", filePath);
    }

    public AnalysisResult? GetResult(int statementId)
    {
        _resultCache.TryGetValue(statementId, out var result);
        return result;
    }

    public List<AnalysisResult> GetResults(string dashboard, int? sinceId = null)
    {
        var query = _resultCache.Values
            .Where(r => r.Dashboard.Equals(dashboard, StringComparison.OrdinalIgnoreCase));

        if (sinceId.HasValue)
            query = query.Where(r => r.StatementId > sinceId.Value);

        return query.OrderBy(r => r.StatementId).ToList();
    }

    public List<AnalysisResult> GetAllResults()
    {
        return _resultCache.Values.OrderBy(r => r.StatementId).ToList();
    }

    public int GetTotalCount() => _resultCache.Count;

    public Dictionary<string, int> GetStats()
    {
        return _resultCache.Values
            .GroupBy(r => r.Status ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task UpdateDashboardSummary(string dashboard)
    {
        try
        {
            var results = GetResults(dashboard);
            var dashPath = Path.Combine(_basePath, SanitizePath(dashboard));
            var summaryPath = Path.Combine(dashPath, "_summary.json");

            var summary = new
            {
                dashboard,
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
        if (!Directory.Exists(_basePath)) return;

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
                    _resultCache[result.StatementId] = result;
                    if (!string.IsNullOrEmpty(result.TextHash))
                        _hashCache[result.TextHash] = result;
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

    private static string SanitizePath(string input)
    {
        if (string.IsNullOrEmpty(input)) return "default";
        return string.Join("_", input.Split(Path.GetInvalidFileNameChars()));
    }
}
