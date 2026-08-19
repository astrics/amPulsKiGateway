using System.Text;
using System.Text.Json;
using AiGateway.CSS.Api.Models;

namespace AiGateway.CSS.Api.Services;

public class LmStudioService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<LmStudioService> _logger;
    private readonly LmStudioConcurrencyGate _concurrencyGate;
    private readonly CssCodebookPromptService _promptService;

    public LmStudioService(
        HttpClient http,
        IConfiguration config,
        ILogger<LmStudioService> logger,
        LmStudioConcurrencyGate concurrencyGate,
        CssCodebookPromptService promptService)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _concurrencyGate = concurrencyGate;
        _promptService = promptService;
    }

    public async Task<AiResult> AnalyzeSync(string text, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("CSS-Klassifizierungsstrategie: {Mode}", _promptService.ClassificationMode);

        return _promptService.UsesTwoStageClassification
            ? await AnalyzeTwoStageAsync(text, cancellationToken)
            : await AnalyzeSinglePassAsync(text, cancellationToken);
    }

    private async Task<AiResult> AnalyzeSinglePassAsync(string text, CancellationToken cancellationToken)
    {
        var responseBody = await SendChatCompletionAsync(
            _promptService.GetSystemPrompt(),
            _promptService.BuildUserPrompt(text),
            1200,
            "single_pass",
            cancellationToken);

        var result = CssAiResponseParser.ParseCompletionResponse(responseBody, _logger);
        result.Statement = string.IsNullOrWhiteSpace(result.Statement) ? text : result.Statement;
        _promptService.NormalizeResult(result);
        return result;
    }

    private async Task<AiResult> AnalyzeTwoStageAsync(string text, CancellationToken cancellationToken)
    {
        var preselectionResponse = await SendChatCompletionAsync(
            _promptService.GetCodeGroupSystemPrompt(),
            _promptService.BuildCodeGroupUserPrompt(text),
            450,
            "codegroup_preselection",
            cancellationToken);

        var preselection = CssAiResponseParser.ParseCompletionResponse(preselectionResponse, _logger);
        preselection.Statement = string.IsNullOrWhiteSpace(preselection.Statement) ? text : preselection.Statement;
        _promptService.NormalizeResult(preselection);

        var selectedGroups = _promptService.SelectKnownCodeGroups(
            preselection.CodeGroupSentiments.Select(group => group.CodeGroup));

        _logger.LogInformation(
            "CSS-Codegruppen-Vorselektion: {Count} Gruppen -> {Groups}",
            selectedGroups.Count,
            selectedGroups.Count == 0 ? "<keine>" : string.Join(", ", selectedGroups));

        if (selectedGroups.Count == 0)
        {
            _logger.LogWarning(
                "CSS-Codegruppen-Vorselektion leer. Fallback auf single_pass fuer die Aussage: {Preview}",
                text[..Math.Min(160, text.Length)]);
            return await AnalyzeSinglePassAsync(text, cancellationToken);
        }

        var finalResponse = await SendChatCompletionAsync(
            _promptService.BuildCodeSelectionSystemPrompt(selectedGroups),
            _promptService.BuildCodeSelectionUserPrompt(text, preselection.CodeGroupSentiments),
            900,
            "code_selection",
            cancellationToken);

        var finalResult = CssAiResponseParser.ParseCompletionResponse(finalResponse, _logger);
        finalResult.Statement = string.IsNullOrWhiteSpace(finalResult.Statement) ? text : finalResult.Statement;
        _promptService.NormalizeResult(finalResult);
        _promptService.RestrictResultToAllowedGroups(finalResult, selectedGroups);
        _promptService.NormalizeResult(finalResult);
        return finalResult;
    }

    private async Task<string> SendChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        string phase,
        CancellationToken cancellationToken)
    {
        var baseUrl = _config["Gateway:LmStudioBaseUrl"] ?? "http://localhost:1234";
        var model = _config["Gateway:ModelName"] ?? "default";

        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1,
            max_tokens = maxTokens,
            response_format = new { type = "text" }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "LM Studio Request | Phase: {Phase} | Url: {Url} | Model: {Model} | Text-Laenge: {Len} | Payload: {Payload}",
            phase,
            $"{baseUrl}/v1/chat/completions",
            model,
            userPrompt.Length,
            json[..Math.Min(2000, json.Length)]);

        HttpResponseMessage response;
        string responseBody;

        try
        {
            using var lease = await _concurrencyGate.EnterAsync(cancellationToken);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            response = await _http.PostAsync($"{baseUrl}/v1/chat/completions", content, cancellationToken);
            sw.Stop();
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation(
                "LM Studio Response | Phase: {Phase} | Status: {Status} | Dauer: {Ms}ms | Body-Laenge: {Len} | Body: {Body}",
                phase,
                (int)response.StatusCode,
                sw.ElapsedMilliseconds,
                responseBody.Length,
                responseBody[..Math.Min(3000, responseBody.Length)]);
        }
        catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                ex,
                "LM Studio Request abgebrochen, weil der Client die Verbindung geschlossen hat | Phase: {Phase} | Url: {Url}",
                phase,
                $"{baseUrl}/v1/chat/completions");
            throw new OperationCanceledException("Request aborted by client.", ex, cancellationToken);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(
                ex,
                "LM Studio Timeout | Phase: {Phase} | Url: {Url} | Timeout: {Timeout}",
                phase,
                $"{baseUrl}/v1/chat/completions",
                _http.Timeout);
            throw new Exception($"LM Studio Timeout nach {_http.Timeout.TotalSeconds}s", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "LM Studio Connection Error | Phase: {Phase} | Url: {Url}",
                phase,
                $"{baseUrl}/v1/chat/completions");
            throw new Exception($"LM Studio nicht erreichbar: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "LM Studio HTTP Error | Phase: {Phase} | Status: {Code} | Body: {Body}",
                phase,
                (int)response.StatusCode,
                responseBody[..Math.Min(1000, responseBody.Length)]);
            throw new Exception(
                $"LM Studio HTTP {(int)response.StatusCode}: {responseBody[..Math.Min(200, responseBody.Length)]}");
        }

        return responseBody;
    }
}