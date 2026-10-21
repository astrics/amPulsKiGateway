using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Css = AiGateway.CSS.Api.Services;
using Sympany = AiGateway.Sympany.Api.Services;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var checks = 0;

foreach (var custom in new[] { false, true })
{
    var values = new Dictionary<string, string?>
    {
        ["Gateway:ModelName"] = "test-model",
        ["CssCodebook:Path"] = Path.Combine(root, "AIGateway.CSS/Codebooks/css-codes.sample.json"),
        ["CssCodebook:ClassificationMode"] = "single_pass"
    };
    if (custom)
    {
        values["Gateway:Temperature"] = "0";
        values["Gateway:MaxResponseTokens"] = "768";
        values["Gateway:ResponseTokenLimits:single_pass"] = "1536";
    }

    var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    using var handler = new RecordingHandler();
    using var http = new HttpClient(handler);
    var cssGate = new Css.LmStudioConcurrencyGate(Options.Create(new AiGateway.CSS.Api.Configuration.GatewayOptions()));
    var sympanyGate = new Sympany.LmStudioConcurrencyGate(Options.Create(new AiGateway.Sympany.Api.Configuration.GatewayOptions()));
    var prompts = new Css.CssCodebookPromptService(config, NullLogger<Css.CssCodebookPromptService>.Instance);
    var candidates = new Css.CssCodeCandidateRetriever(prompts, config, NullLogger<Css.CssCodeCandidateRetriever>.Instance);
    var cssClient = new Css.LmStudioClient(http, config, cssGate, prompts, candidates);
    var cssService = new Css.LmStudioService(http, config, NullLogger<Css.LmStudioService>.Instance, cssGate, prompts, candidates);
    var sympanyClient = new Sympany.LmStudioRuntimeClient(http, config, sympanyGate);
    var sympanyService = new Sympany.LmStudioService(http, config, NullLogger<Sympany.LmStudioService>.Instance, sympanyGate);

    await cssClient.ClassifyStatementAsync("Schnell bearbeitet.");
    Check(custom ? 0 : 0.1, custom ? 1536 : 1200);
    await cssService.AnalyzeSync("Schnell bearbeitet.");
    Check(custom ? 0 : 0.1, custom ? 1536 : 1200);
    await sympanyClient.ClassifyStatementAsync("Schnell bearbeitet.");
    Check(custom ? 0 : 0.1, custom ? 768 : null);
    await sympanyService.AnalyzeSync("Schnell bearbeitet.");
    Check(custom ? 0 : 0.1, custom ? 768 : 500);

    // An explicit zero must survive configuration binding; candidate temperature
    // must continue to override the general temperature on BOTH request paths.
    config["CssCodebook:ClassificationMode"] = "candidate_rerank";
    config["Gateway:Temperature"] = "0.7";
    config["CssCodebook:CandidateTemperature"] = "0";
    config["Gateway:ResponseTokenLimits:candidate_rerank"] = "1400";
    prompts = new Css.CssCodebookPromptService(config, NullLogger<Css.CssCodebookPromptService>.Instance);
    candidates = new Css.CssCodeCandidateRetriever(prompts, config, NullLogger<Css.CssCodeCandidateRetriever>.Instance);
    cssClient = new Css.LmStudioClient(http, config, cssGate, prompts, candidates);
    cssService = new Css.LmStudioService(http, config, NullLogger<Css.LmStudioService>.Instance, cssGate, prompts, candidates);
    await cssClient.ClassifyStatementAsync("Schnell bearbeitet.");
    Check(0, 1400);
    await cssService.AnalyzeSync("Schnell bearbeitet.");
    Check(0, 1400);

    config["Gateway:MaxResponseTokens"] = "0";
    try
    {
        await sympanyService.AnalyzeSync("Schnell bearbeitet.");
        throw new Exception("Invalid token limit was accepted.");
    }
    catch (InvalidOperationException) { checks++; }

    void Check(double expectedTemperature, int? expectedTokens)
    {
        using var json = JsonDocument.Parse(handler.LastBody!);
        var payload = json.RootElement;
        if (payload.GetProperty("model").GetString() != "test-model"
            || payload.GetProperty("temperature").GetDouble() != expectedTemperature
            || payload.GetProperty("stream").GetBoolean()
            || payload.GetProperty("messages").GetArrayLength() != 2)
            throw new Exception("Unexpected request payload.");
        var actualTokens = payload.TryGetProperty("max_tokens", out var tokenValue) ? tokenValue.GetInt32() : (int?)null;
        if (actualTokens != expectedTokens)
            throw new Exception($"Expected token limit {expectedTokens}, got {actualTokens}.");
        checks++;
    }
}

Console.WriteLine($"PASS: {checks} checks across CSS/Sympany synchronous and batch HTTP request paths.");

sealed class RecordingHandler : HttpMessageHandler
{
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastBody = await request.Content!.ReadAsStringAsync(ct);
        var content = JsonSerializer.Serialize(new
        {
            statement = "Schnell bearbeitet.", sentiment = "Positiv", keywords = Array.Empty<object>(),
            codeMatches = Array.Empty<object>(), codeGroupSentiments = Array.Empty<object>()
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content }, finish_reason = "stop" } }
            }), Encoding.UTF8, "application/json")
        };
    }
}
