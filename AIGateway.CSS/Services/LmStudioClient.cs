using System.Text;
using System.Text.Json;
using AiGateway.CSS.Api.Models;
using AiGateway.Shared;

namespace AiGateway.CSS.Api.Services;

public class LmStudioClient : ILmStudioClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly IConfiguration _config;
    private readonly double _candidateTemperature;
    private readonly LmStudioConcurrencyGate _concurrencyGate;
    private readonly CssCodebookPromptService _promptService;
    private readonly CssCodeCandidateRetriever _candidateRetriever;

    public LmStudioClient(
        HttpClient http,
        IConfiguration config,
        LmStudioConcurrencyGate concurrencyGate,
        CssCodebookPromptService promptService,
        CssCodeCandidateRetriever candidateRetriever)
    {
        _http = http;
        _baseUrl = config["Gateway:LmStudioBaseUrl"] ?? "http://localhost:1234";
        _model = config["Gateway:ModelName"] ?? "qwen2.5-7b-instruct";
        _config = config;
        _candidateTemperature = config.GetValue<double?>("CssCodebook:CandidateTemperature") ?? 0.0;
        _concurrencyGate = concurrencyGate;
        _promptService = promptService;
        _candidateRetriever = candidateRetriever;
    }

    public async Task<LmStudioResponse> ClassifyStatementAsync(string statementText, CancellationToken ct = default)
    {
        var parsed = _promptService.UsesCandidateRerankClassification
            ? await ClassifyCandidateRerankAsync(statementText, ct)
            : _promptService.UsesTwoStageClassification
            ? await ClassifyTwoStageAsync(statementText, ct)
            : await ClassifySinglePassAsync(statementText, ct);

        return ToResponse(parsed);
    }

    public async Task<string> ChatCompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var responseJson = await SendChatCompletionAsync(systemPrompt, userPrompt, 1200, ct, phase: "chat");

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

    private async Task<AiResult> ClassifyCandidateRerankAsync(string statementText, CancellationToken ct)
    {
        var candidates = _candidateRetriever.Retrieve(statementText);
        var responseJson = await SendChatCompletionAsync(
            _promptService.BuildCandidateRerankSystemPrompt(candidates),
            _promptService.BuildCandidateRerankUserPrompt(statementText, candidates),
            1200,
            ct,
            _candidateTemperature,
            "candidate_rerank");

        var parsed = CssCandidateResponseParser.ParseCompletionResponse(
            responseJson,
            statementText,
            candidates);
        _promptService.NormalizeResult(parsed);
        return parsed;
    }

    private async Task<AiResult> ClassifySinglePassAsync(string statementText, CancellationToken ct)
    {
        var responseJson = await SendChatCompletionAsync(
            _promptService.GetSystemPrompt(),
            _promptService.BuildUserPrompt(statementText),
            1200,
            ct);

        var parsed = CssAiResponseParser.ParseCompletionResponse(responseJson);
        parsed.Statement = string.IsNullOrWhiteSpace(parsed.Statement) ? statementText : parsed.Statement;
        _promptService.NormalizeResult(parsed);
        return parsed;
    }

    private async Task<AiResult> ClassifyTwoStageAsync(string statementText, CancellationToken ct)
    {
        var preselectionJson = await SendChatCompletionAsync(
            _promptService.GetCodeGroupSystemPrompt(),
            _promptService.BuildCodeGroupUserPrompt(statementText),
            450,
            ct,
            phase: "codegroup_preselection");

        var preselection = CssAiResponseParser.ParseCompletionResponse(preselectionJson);
        preselection.Statement = string.IsNullOrWhiteSpace(preselection.Statement) ? statementText : preselection.Statement;
        _promptService.NormalizeResult(preselection);

        var selectedGroups = _promptService.SelectKnownCodeGroups(
            preselection.CodeGroupSentiments.Select(group => group.CodeGroup));

        if (selectedGroups.Count == 0)
        {
            return _promptService.BuildPreselectionFallback(statementText, preselection);
        }

        var finalJson = await SendChatCompletionAsync(
            _promptService.BuildCodeSelectionSystemPrompt(selectedGroups),
            _promptService.BuildCodeSelectionUserPrompt(statementText, preselection.CodeGroupSentiments),
            900,
            ct,
            phase: "code_selection");

        var finalResult = CssAiResponseParser.ParseCompletionResponse(finalJson);
        finalResult.Statement = string.IsNullOrWhiteSpace(finalResult.Statement) ? statementText : finalResult.Statement;
        _promptService.NormalizeResult(finalResult);
        _promptService.RestrictResultToAllowedGroups(finalResult, selectedGroups);
        _promptService.NormalizeResult(finalResult);
        return finalResult;
    }

    private static LmStudioResponse ToResponse(AiResult parsed)
    {
        return new LmStudioResponse
        {
            Success = string.IsNullOrWhiteSpace(parsed.ParseError),
            Sentiment = parsed.Sentiment,
            Keywords = parsed.Keywords
                .Select(keyword => new KeywordResult
                {
                    Id = keyword.Id,
                    ExternalCodeNr = keyword.ExternalCodeNr,
                    CodeGroup = keyword.CodeGroup,
                    Label = keyword.Label
                })
                .ToList(),
            CodeMatches = parsed.CodeMatches
                .Select(match => new AiCodeMatch
                {
                    Id = match.Id,
                    ExternalCodeNr = match.ExternalCodeNr,
                    CodeGroup = match.CodeGroup,
                    Code = match.Code,
                    Sentiment = match.Sentiment,
                    Evidence = match.Evidence
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
            CandidateCodeIds = parsed.CandidateCodeIds.ToList(),
            NeedsReview = parsed.NeedsReview,
            ValidationWarnings = parsed.ValidationWarnings.ToList(),
            RawResponse = parsed.RawResponse,
            Error = parsed.ParseError
        };
    }

    private async Task<string> SendChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        CancellationToken ct,
        double? temperatureOverride = null,
        string phase = "single_pass")
    {
        using var lease = await _concurrencyGate.EnterAsync(ct);
        var requestBody = LmStudioGenerationSettings.CreatePayload(
            _config, _model, systemPrompt, userPrompt, phase, maxTokens, temperatureOverride);

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
    public List<int> CandidateCodeIds { get; set; } = new();
    public bool NeedsReview { get; set; }
    public List<string> ValidationWarnings { get; set; } = new();
    public string RawResponse { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class KeywordResult
{
    public int Id { get; set; }
    public string ExternalCodeNr { get; set; } = string.Empty;
    public string CodeGroup { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
