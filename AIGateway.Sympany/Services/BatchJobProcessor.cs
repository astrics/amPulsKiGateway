using AiGateway.Sympany.Api.Models;
using System.Diagnostics;

namespace AiGateway.Sympany.Api.Services;

public class BatchJobProcessor
{
    private readonly ILmStudioClient _client;
    private readonly JobStore _jobStore;
    private readonly ResultFileWriter _fileWriter;
    private readonly ILogger<BatchJobProcessor> _logger;

    public BatchJobProcessor(ILmStudioClient client, JobStore jobStore,
        ResultFileWriter fileWriter, ILogger<BatchJobProcessor> logger)
    {
        _client = client;
        _jobStore = jobStore;
        _fileWriter = fileWriter;
        _logger = logger;
    }

    public void StartJob(string jobId, string dashboard, List<StatementInput> statements)
    {
        Task.Run(async () =>
        {
            var job = _jobStore.GetJob(jobId)!;
            job.Status = "processing";
            var sw = Stopwatch.StartNew();

            foreach (var stmt in statements)
            {
                try
                {
                    _logger.LogInformation("Verarbeite Statement {Id}: {Text}",
                        stmt.StatementId, stmt.Text?[..Math.Min(50, stmt.Text.Length)]);

                    var stmtSw = Stopwatch.StartNew();
                    var result = await _client.ClassifyStatementAsync(stmt.Text ?? "");
                    stmtSw.Stop();

                    var entry = new StatementResultEntry
                    {
                        StatementId = stmt.StatementId,
                        MetadatenId = stmt.MetadatenId,
                        Dashboard = dashboard,
                        Text = stmt.Text ?? "",
                        ProcessingMs = stmtSw.ElapsedMilliseconds,
                        ProcessedAt = DateTime.UtcNow
                    };

                    if (result.Success)
                    {
                        entry.Sentiment = result.Sentiment;
                        entry.Keywords = result.Keywords.Select(k => new KeywordEntry
                        {
                            Id = k.Id,
                            Label = k.Label
                        }).ToList();
                    }
                    else
                    {
                        entry.Error = result.Error;
                        entry.RawResponse = result.RawResponse;
                        job.Errors++;
                    }

                    job.Results.Add(entry);
                    job.Processed++;
                    job.ProcessingSec = sw.Elapsed.TotalSeconds;

                    // JSON-Datei nach jedem Statement aktualisieren
                    _fileWriter.WriteProgress(jobId, dashboard, job.Status,
                        job.TotalStatements, job.Processed, job.Errors,
                        job.ProcessingSec, job.Results);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler bei Statement {Id}", stmt.StatementId);
                    job.Results.Add(new StatementResultEntry
                    {
                        StatementId = stmt.StatementId,
                        Text = stmt.Text ?? "",
                        Error = ex.Message
                    });
                    job.Errors++;
                    job.Processed++;
                }
            }

            sw.Stop();
            job.ProcessingSec = sw.Elapsed.TotalSeconds;
            job.Status = "completed";

            _fileWriter.WriteProgress(jobId, dashboard, job.Status,
                job.TotalStatements, job.Processed, job.Errors,
                job.ProcessingSec, job.Results);

            _logger.LogInformation("Job {JobId} abgeschlossen: {Processed}/{Total}, {Errors} Fehler",
                jobId, job.Processed, job.TotalStatements, job.Errors);
        });
    }
}

