using System.Text;
using System.Text.Json;
using AiGateway.Api.Models;

namespace AiGateway.Api.Services;

public class LmStudioService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<LmStudioService> _logger;

    public LmStudioService(HttpClient http, IConfiguration config, ILogger<LmStudioService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<AiResult> AnalyzeSync(string text)
    {
        var baseUrl = _config["LmStudio:BaseUrl"] ?? "http://localhost:1234";
        var model = _config["LmStudio:Model"] ?? "default";

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
            //response_format = new { type = "json_object" }
            response_format = new { type = "text" }

        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // ===== VOLLES LOGGING =====
        _logger.LogInformation(
            "═══ LM STUDIO REQUEST ═══\n" +
            "URL: {Url}\n" +
            "Model: {Model}\n" +
            "Text-Länge: {Len}\n" +
            "Payload:\n{Payload}",
            $"{baseUrl}/v1/chat/completions", model, text.Length,
            json[..Math.Min(2000, json.Length)]);

        HttpResponseMessage response;
        string responseBody;

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            response = await _http.PostAsync($"{baseUrl}/v1/chat/completions", content);
            sw.Stop();
            responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation(
                "═══ LM STUDIO RESPONSE ═══\n" +
                "Status: {Status}\n" +
                "Dauer: {Ms}ms\n" +
                "Body-Länge: {Len}\n" +
                "Body:\n{Body}",
                (int)response.StatusCode, sw.ElapsedMilliseconds,
                responseBody.Length,
                responseBody[..Math.Min(3000, responseBody.Length)]);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(
                "═══ LM STUDIO TIMEOUT ═══\n" +
                "URL: {Url}\n" +
                "Timeout: {Timeout}\n" +
                "Error: {Error}",
                $"{baseUrl}/v1/chat/completions", _http.Timeout, ex.Message);
            throw new Exception($"LM Studio Timeout nach {_http.Timeout.TotalSeconds}s", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                "═══ LM STUDIO CONNECTION ERROR ═══\n" +
                "URL: {Url}\n" +
                "Error: {Error}\n" +
                "Inner: {Inner}",
                $"{baseUrl}/v1/chat/completions", ex.Message, ex.InnerException?.Message);
            throw new Exception($"LM Studio nicht erreichbar: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "═══ LM STUDIO HTTP ERROR ═══\n" +
                "Status: {Code}\n" +
                "Body: {Body}",
                (int)response.StatusCode, responseBody[..Math.Min(1000, responseBody.Length)]);
            throw new Exception($"LM Studio HTTP {(int)response.StatusCode}: {responseBody[..Math.Min(200, responseBody.Length)]}");
        }

        return ParseLmStudioResponse(responseBody);
    }

    private static string StripMarkdownCodeBlock(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            // Erste Zeile entfernen (```json oder ```)
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
                text = text[(firstNewline + 1)..];
            // Letztes ``` entfernen
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

            // ===== Markdown-Wrapper entfernen =====
            contentStr = StripMarkdownCodeBlock(contentStr);

            var content = JsonSerializer.Deserialize<JsonElement>(contentStr);

            // Sentiment auslesen
            var sentiment = content.TryGetProperty("sentiment", out var s)
                ? NormalizeSentiment(s.GetString() ?? "") : "Neutral";

            // Keywords auslesen (Array von {id, label})
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

            // Statement (Original-Text aus Antwort)
            var statement = content.TryGetProperty("statement", out var stmt)
                ? stmt.GetString() ?? "" : "";

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
            _logger.LogWarning("Parse-Fehler: {Error}. Raw: {Raw}", ex.Message,
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
