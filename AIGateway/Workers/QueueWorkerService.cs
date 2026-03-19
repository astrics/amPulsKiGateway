using System.Text.Json;
using AiGateway.Api.Configuration;
using AiGateway.Api.Models.Internal;
using AiGateway.Api.Services;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Workers;

/// <summary>
/// Hintergrund-Dienst der die Queue abarbeitet.
/// Läuft solange die Applikation läuft.
/// </summary>
public class QueueWorkerService : BackgroundService
{
    private readonly QueueService _queue;
    private readonly ILmStudioClient _lmStudio;
    private readonly IPromptBuilder _promptBuilder;
    private readonly ICacheService _cache;
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<QueueWorkerService> _logger;
    private readonly GatewayOptions _options;

    public QueueWorkerService(
        QueueService queue, // Konkrete Klasse für Channel-Zugriff
        ILmStudioClient lmStudio,
        IPromptBuilder promptBuilder,
        ICacheService cache,
        IOptions<GatewayOptions> options,
        ILogger<QueueWorkerService> logger)
    {
        _queue = queue;
        _lmStudio = lmStudio;
        _promptBuilder = promptBuilder;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _semaphore = new SemaphoreSlim(_options.MaxConcurrency);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Queue Worker gestartet. Max Concurrency: {Concurrency}",
            _options.MaxConcurrency);

        // Cleanup-Timer: alle 30 Minuten alte Jobs entfernen
        _ = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                _queue.CleanupOldJobs(TimeSpan.FromHours(2));
            }
        }, stoppingToken);

        // Hauptschleife: Items aus dem Channel lesen
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await _semaphore.WaitAsync(stoppingToken);

            // Verarbeitung in eigenem Task (für Concurrency > 1)
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessItemAsync(item, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler bei Job {JobId}", item.JobId);
                    item.Status = "failed";
                    item.Error = ex.Message;
                    item.CompletedAt = DateTime.UtcNow;
                }
                finally
                {
                    _semaphore.Release();
                }
            }, stoppingToken);
        }
    }

    private async Task ProcessItemAsync(QueueItem item, CancellationToken ct)
    {
        item.Status = "processing";
        _logger.LogInformation("Job {JobId} wird verarbeitet. {Chunks} Chunks.",
            item.JobId, item.Chunks.Count);

        var systemPrompt = _promptBuilder.BuildSystemPrompt(item.AnalysisType);

        foreach (var chunk in item.Chunks)
        {
            ct.ThrowIfCancellationRequested();

            var userPrompt = _promptBuilder.BuildUserPrompt(
                item.AnalysisType, chunk, item.CustomPrompt);

            // Cache prüfen
            var cacheKey = _cache.GenerateKey(item.AnalysisType, userPrompt);
            var cached = await _cache.GetAsync(cacheKey);

            string result;
            if (cached != null)
            {
                result = cached;
                _logger.LogInformation("Chunk {Index} von Job {JobId}: Cache-Hit!",
                    chunk.Index, item.JobId);
            }
            else
            {
                // LM Studio aufrufen
                result = await _lmStudio.ChatCompleteAsync(systemPrompt, userPrompt, ct);

                // Ergebnis cachen
                await _cache.SetAsync(cacheKey, result);
            }

            item.ChunkResults.Add(result);
            item.CompletedChunks++;

            _logger.LogInformation(
                "Job {JobId}: Chunk {Completed}/{Total} fertig",
                item.JobId, item.CompletedChunks, item.Chunks.Count);
        }

        // Alle Chunks fertig → Ergebnisse aggregieren
        item.FinalResult = AggregateResults(item.ChunkResults, item.AnalysisType);
        item.Status = "completed";
        item.CompletedAt = DateTime.UtcNow;
        _queue.MarkCompleted(item.JobId);

        _logger.LogInformation(
            "Job {JobId} abgeschlossen in {Duration:F1}s",
            item.JobId, (item.CompletedAt.Value - item.CreatedAt).TotalSeconds);

        // Webhook senden falls konfiguriert
        if (!string.IsNullOrEmpty(item.CallbackUrl))
        {
            await SendWebhookAsync(item);
        }
    }

    /// <summary>
    /// Fasst die Ergebnisse aller Chunks zusammen.
    /// Bei "results"-Arrays werden sie zusammengeführt.
    /// </summary>
    private object AggregateResults(List<string> chunkResults, string analysisType)
    {
        if (chunkResults.Count == 1)
        {
            // Nur ein Chunk → direkt zurückgeben
            try
            {
                return JsonSerializer.Deserialize<object>(chunkResults[0])!;
            }
            catch
            {
                return new { raw = chunkResults[0] };
            }
        }

        // Mehrere Chunks → "results"-Arrays zusammenführen
        var allResults = new List<JsonElement>();

        foreach (var chunkResult in chunkResults)
        {
            try
            {
                using var doc = JsonDocument.Parse(chunkResult);

                if (doc.RootElement.TryGetProperty("results", out var resultsArray)
                    && resultsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in resultsArray.EnumerateArray())
                    {
                        allResults.Add(element.Clone());
                    }
                }
                else
                {
                    // Kein "results"-Array → ganzes Objekt hinzufügen
                    allResults.Add(doc.RootElement.Clone());
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Chunk-Ergebnis ist kein valides JSON: {Error}", ex.Message);
                // Raw-Text als Fallback
            }
        }

        return new
        {
            results = allResults,
            metadata = new
            {
                total_chunks = chunkResults.Count,
                total_results = allResults.Count,
                aggregated = true
            }
        };
    }

    private async Task SendWebhookAsync(QueueItem item)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);

            var payload = new
            {
                jobId = item.JobId,
                status = item.Status,
                result = item.FinalResult,
                completedAt = item.CompletedAt
            };

            await http.PostAsJsonAsync(item.CallbackUrl, payload);
            _logger.LogInformation("Webhook gesendet für Job {JobId}", item.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Webhook fehlgeschlagen für Job {JobId}: {Error}",
                item.JobId, ex.Message);
        }
    }
}
