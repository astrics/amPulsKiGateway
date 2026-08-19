using System.Text;
using System.Text.Json;
using AiGateway.CSS.Api.Models;

namespace AiGateway.CSS.Api.Services;

public class LmStudioClient : ILmStudioClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly double _temperature;
    private readonly LmStudioConcurrencyGate _concurrencyGate;
    private readonly CssCodebookPromptService _promptService;

    public LmStudioClient(
        HttpClient http,
        IConfiguration config,
        LmStudioConcurrencyGate concurrencyGate,
        CssCodebookPromptService promptService)
    {
        _http = http;
        _baseUrl = config["Gateway:LmStudioBaseUrl"] ?? "http://localhost:1234";
        _model = config["Gateway:ModelName"] ?? "qwen2.5-7b-instruct";
        _temperature = config.GetValue<double?>("Gateway:Temperature") ?? 0.1;
        _concurrencyGate = concurrencyGate;
        _promptService = promptService;
    }

    public async Task<LmStudioResponse> ClassifyStatementAsync(string statementText, CancellationToken ct = default)
    {
        var responseJson = await SendChatCompletionAsync(
            _promptService.GetSystemPrompt(),
            _promptService.BuildUserPrompt(statementText),
            ct);

        return ParseResponse(responseJson);
    }

    public async Task<string> ChatCompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var responseJson = await SendChatCompletionAsync(systemPrompt, userPrompt, ct);

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        return root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    public async Task<(bool isReachable, string? modelName, int? responseTimeMs)> HealthCheckAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var response = await _http.GetAsync($"{_baseUrl}/v1/models");
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseJson);
            var modelName = doc.RootElement
                .GetProperty("data")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetString())
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

            sw.Stop();
            return (true, modelName, (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            sw.Stop();
            return (false, null, (int)sw.ElapsedMilliseconds);
        }
    }

    private LmStudioResponse ParseResponse(string responseJson)
    {
        var parsed = CssAiResponseParser.ParseCompletionResponse(responseJson);
        _promptService.NormalizeResult(parsed);

        return new LmStudioResponse
        {
            Success = string.IsNullOrWhiteSpace(parsed.ParseError),
            Sentiment = parsed.Sentiment,
            Keywords = parsed.Keywords
                .Select(keyword => new KeywordResult { Id = keyword.Id, Label = keyword.Label })
                .ToList(),
            CodeMatches = parsed.CodeMatches
                .Select(match => new AiCodeMatch
                {
                    Id = match.Id,
                    CodeGroup = match.CodeGroup,
                    Code = match.Code,
                    Sentiment = match.Sentiment
                })
                .ToList(),
            CodeGroupSentiments = parsed.CodeGroupSentiments
                .Select(group => new AiCodeGroupSentiment
                {
                    CodeGroup = group.CodeGroup,
                    Sentiment = group.Sentiment,
                    MatchedCodeIds = group.MatchedCodeIds.ToList()
                })
                .ToList(),
            RawResponse = parsed.RawResponse,
            Error = parsed.ParseError
        };
    }

    private async Task<string> SendChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        using var lease = await _concurrencyGate.EnterAsync(ct);
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = _temperature,
            response_format = new { type = "json_object" },
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_baseUrl}/v1/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct);
    }
}

public class LmStudioResponse
{
    public bool Success { get; set; }
    public string Sentiment { get; set; } = string.Empty;
    public List<KeywordResult> Keywords { get; set; } = new();
    public List<AiCodeMatch> CodeMatches { get; set; } = new();
    public List<AiCodeGroupSentiment> CodeGroupSentiments { get; set; } = new();
    public string RawResponse { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class KeywordResult
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}