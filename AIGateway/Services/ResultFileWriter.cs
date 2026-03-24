using AiGateway.Api.Models;
using System.Text.Json;

namespace AiGateway.Api.Services;

public class ResultFileWriter
{
    private readonly string _outputDir;
    private readonly object _lock = new();

    public ResultFileWriter(IConfiguration config)
    {
        _outputDir = config["ResultFiles:OutputDir"] ?? Path.Combine(AppContext.BaseDirectory, "results");
        Directory.CreateDirectory(_outputDir);
    }

    public void WriteProgress(string jobId, string dashboard, string status,
        int total, int processed, int errors, double processingSec,
        List<StatementResultEntry> results)
    {
        lock (_lock)
        {
            var payload = new
            {
                job_id = jobId,
                dashboard,
                status,
                total,
                processed,
                errors,
                percent = total > 0 ? Math.Round((double)processed / total * 100, 1) : 0,
                processing_sec = Math.Round(processingSec, 2),
                updated_at = DateTime.UtcNow.ToString("o"),
                results = results.Select(r => new
                {
                    statement_id = r.StatementId,
                    metadaten_id = r.MetadatenId,
                    text = r.Text,
                    sentiment = r.Sentiment,
                    keywords = r.Keywords.Select(k => new { k.Id, k.Label }),
                    processing_ms = r.ProcessingMs,
                    processed_at = r.ProcessedAt.ToString("o"),
                    error = r.Error
                })
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            var filePath = Path.Combine(_outputDir, $"{jobId}.json");
            File.WriteAllText(filePath, json);
        }
    }
}
