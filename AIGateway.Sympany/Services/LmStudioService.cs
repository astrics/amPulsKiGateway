using System.Text;
using System.Text.Json;
using AiGateway.Sympany.Api.Models;

namespace AiGateway.Sympany.Api.Services;

public class LmStudioService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<LmStudioService> _logger;
    private readonly LmStudioConcurrencyGate _concurrencyGate;

    public LmStudioService(
        HttpClient http,
        IConfiguration config,
        ILogger<LmStudioService> logger,
        LmStudioConcurrencyGate concurrencyGate)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _concurrencyGate = concurrencyGate;
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
                new { role = "system", content = PromptBuilder.GetSystemPrompt() },
                new { role = "user", content = PromptBuilder.GetUserPrompt(text) }
            },
            temperature = 0.1,
            max_tokens = 500,
            response_format = new { type = "text" }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "LM Studio Request | Url: {Url} | Model: {Model} | Text-Länge: {Len} | Payload: {Payload}",
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
                "LM Studio Response | Status: {Status} | Dauer: {Ms}ms | Body-Länge: {Len} | Body: {Body}",
                (int)response.StatusCode,
                sw.ElapsedMilliseconds,
                responseBody.Length,
                responseBody[..Math.Min(3000, responseBody.Length)]);
        }
        catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                ex,
                "LM Studio Request durch Gateway-Timeout abgebrochen | Url: {Url}",
                $"{baseUrl}/v1/chat/completions");
            throw new OperationCanceledException("LM Studio request canceled by gateway timeout.", ex, cancellationToken);
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

        return ParseLmStudioResponse(responseBody);
    }

    private static string StripMarkdownCodeBlock(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
                text = text[(firstNewline + 1)..];

            if (text.TrimEnd().EndsWith("```"))
                text = text.TrimEnd()[..^3];
        }

        return text.Trim();
    }

    private AiResult ParseLmStudioResponse(string responseBody)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var contentStr = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";

            _logger.LogInformation("LM Studio Content: {Content}", contentStr[..Math.Min(500, contentStr.Length)]);

            contentStr = StripMarkdownCodeBlock(contentStr);
            var content = JsonSerializer.Deserialize<JsonElement>(contentStr);

            var sentiment = content.TryGetProperty("sentiment", out var s)
                ? NormalizeSentiment(s.GetString() ?? "")
                : "Neutral";

            var keywords = new List<AiKeyword>();
            if (content.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in kw.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                    var label = item.TryGetProperty("label", out var lblProp) ? lblProp.GetString() ?? "" : "";
                    if (id > 0 && !string.IsNullOrEmpty(label))
                    {
                        keywords.Add(new AiKeyword { Id = id, Label = label });
                    }
                }
            }

            var statement = content.TryGetProperty("statement", out var stmt)
                ? stmt.GetString() ?? ""
                : "";

            return new AiResult
            {
                Statement = statement,
                Sentiment = sentiment,
                Keywords = keywords,
                RawResponse = contentStr
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Parse-Fehler: {Error}. Raw: {Raw}",
                ex.Message,
                responseBody[..Math.Min(500, responseBody.Length)]);

            return new AiResult
            {
                Sentiment = "Neutral",
                Keywords = new List<AiKeyword>(),
                RawResponse = responseBody[..Math.Min(5000, responseBody.Length)],
                ParseError = ex.Message
            };
        }
    }

    private static string NormalizeSentiment(string raw)
    {
        var lower = raw.ToLower().Trim();
        return lower switch
        {
            "positiv" or "positive" or "pos" => "Positiv",
            "negativ" or "negative" or "neg" => "Negativ",
            "neutral" => "Neutral",
            _ => "Neutral"
        };
    }
}
