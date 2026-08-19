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
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<AiResult> AnalyzeSync(string text, CancellationToken cancellationToken = default)
    {
        var baseUrl = _config["Gateway:LmStudioBaseUrl"] ?? "http://localhost:1234";
        var model = _config["Gateway:ModelName"] ?? "default";

        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = _promptService.GetSystemPrompt() },
                new { role = "user", content = _promptService.BuildUserPrompt(text) }
            },
            temperature = 0.1,
            max_tokens = 1200,
            response_format = new { type = "json_object" }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "LM Studio Request | Url: {Url} | Model: {Model} | Text-Laenge: {Len} | Payload: {Payload}",
            $"{baseUrl}/v1/chat/completions",
            model,
            text.Length,
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
                "LM Studio Response | Status: {Status} | Dauer: {Ms}ms | Body-Laenge: {Len} | Body: {Body}",
                (int)response.StatusCode,
                sw.ElapsedMilliseconds,
                responseBody.Length,
                responseBody[..Math.Min(3000, responseBody.Length)]);
        }
        catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                ex,
                "LM Studio Request abgebrochen, weil der Client die Verbindung geschlossen hat | Url: {Url}",
                $"{baseUrl}/v1/chat/completions");
            throw new OperationCanceledException("Request aborted by client.", ex, cancellationToken);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(
                ex,
                "LM Studio Timeout | Url: {Url} | Timeout: {Timeout}",
                $"{baseUrl}/v1/chat/completions",
                _http.Timeout);
            throw new Exception($"LM Studio Timeout nach {_http.Timeout.TotalSeconds}s", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "LM Studio Connection Error | Url: {Url}",
                $"{baseUrl}/v1/chat/completions");
            throw new Exception($"LM Studio nicht erreichbar: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "LM Studio HTTP Error | Status: {Code} | Body: {Body}",
                (int)response.StatusCode,
                responseBody[..Math.Min(1000, responseBody.Length)]);
            throw new Exception(
                $"LM Studio HTTP {(int)response.StatusCode}: {responseBody[..Math.Min(200, responseBody.Length)]}");
        }

        var result = CssAiResponseParser.ParseCompletionResponse(responseBody, _logger);
        _promptService.NormalizeResult(result);
        return result;
    }
}