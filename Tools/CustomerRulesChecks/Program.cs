using System.Net;
using System.Text;
using System.Text.Json;
using AiGateway.CSS.Api.Configuration;
using AiGateway.CSS.Api.Models;
using AiGateway.CSS.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var values = new Dictionary<string, string?>
{
    ["CssCodebook:Path"] = Path.Combine(root, "AIGateway.CSS/Codebooks/css-codes.imported.json"),
    ["CssCodebook:PromptVariant"] = "quality_v2",
    ["CssCodebook:ClassificationModeByProject:9"] = "candidate_rerank",
    ["CssCodebook:ClassificationModeByProject:10"] = "candidate_rerank",
    ["Gateway:ModelName"] = "test-model"
};
var baselineConfig = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
var baseline = new CssCodebookPromptService(baselineConfig, NullLogger<CssCodebookPromptService>.Instance);
values["CssCodebook:CustomerRules20261030Projects:0"] = "10";
var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
var prompts = new CssCodebookPromptService(config, NullLogger<CssCodebookPromptService>.Instance);
var candidates = prompts.GetCodebookLabels().Select(label => new CssCodeCandidate
{
    Id = label.Number, Code = label.Code, CodeGroup = label.CodeGroup
}).ToList();
var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
Check(prompts.BuildCandidateRerankSystemPrompt(candidates, 9) == baseline.BuildCandidateRerankSystemPrompt(candidates, 9), "Project 9 baseline changed.");
Check(prompts.BuildCandidateRerankSystemPrompt(candidates) == baseline.BuildCandidateRerankSystemPrompt(candidates), "Default prompt changed.");
var updated = prompts.BuildCandidateRerankSystemPrompt(candidates, 10);
Check(updated.Contains("Kundenabstimmung 2026-10-30"), "Project 10 rules missing.");
Check(updated.Contains("ohne Dauerbewertung keine 15"), "Missing-reply boundary missing.");
Check(updated.Contains("32/Positiv und 16/Negativ"), "Per-code mixed sentiment example missing.");

using var handler = new FixtureHandler();
using var http = new HttpClient(handler);
var gate = new LmStudioConcurrencyGate(Options.Create(new GatewayOptions()));
var retriever = new CssCodeCandidateRetriever(prompts, config, NullLogger<CssCodeCandidateRetriever>.Instance);
var service = new LmStudioService(http, config, NullLogger<LmStudioService>.Instance, gate, prompts, retriever);
var result = await service.AnalyzeSync("Soweit alles in Ordnung. Der persönliche Kontakt fehlt.", projectId: 10);
using var body = JsonDocument.Parse(handler.LastBody!);
Check(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("Kundenabstimmung 2026-10-30"), "Sync route lost project-specific rules.");
Check(result.CodeMatches.Single(match => match.Id == 32).Sentiment == "Positiv", "General positive statement lost its code sentiment.");
Check(result.CodeMatches.Single(match => match.Id == 16).Sentiment == "Negativ", "Missing contact lost its code sentiment.");
Check(result.Sentiment == "Neutral", "Existing technical aggregation compatibility changed.");
Console.WriteLine($"PASS: {checks} checks; baseline isolation, sync routing, code sentiments and compatibility aggregation. No real model called.");

sealed class FixtureHandler : HttpMessageHandler
{
    public string? LastBody { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastBody = await request.Content!.ReadAsStringAsync(ct);
        var content = JsonSerializer.Serialize(new
        {
            decisions = new[]
            {
                new { id = 16, match = true, sentiment = "Negativ", evidence = "Der persönliche Kontakt fehlt" },
                new { id = 32, match = true, sentiment = "Positiv", evidence = "Soweit alles in Ordnung" }
            },
            needsReview = false
        });
        var envelope = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") };
    }
}
