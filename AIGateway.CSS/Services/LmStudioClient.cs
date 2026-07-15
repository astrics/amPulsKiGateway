using System.Text;
using System.Text.Json;

namespace AiGateway.CSS.Api.Services;

public class LmStudioClient : ILmStudioClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly double _temperature;

    public LmStudioClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _baseUrl = config["Gateway:LmStudioBaseUrl"] ?? "http://localhost:1234";
        _model = config["Gateway:ModelName"] ?? "qwen2.5-7b-instruct";
        _temperature = config.GetValue<double?>("Gateway:Temperature") ?? 0.1;
    }

    public async Task<LmStudioResponse> ClassifyStatementAsync(string statementText, CancellationToken ct = default)
    {
        var responseJson = await SendChatCompletionAsync(
            PromptBuilder.GetSystemPrompt(),
            PromptBuilder.GetUserPrompt(statementText),
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
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var messageContent = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        var cleanJson = ExtractJson(messageContent);

        try
        {
            using var resultDoc = JsonDocument.Parse(cleanJson);
            var result = resultDoc.RootElement;

            var sentiment = result.GetProperty("sentiment").GetString() ?? "Neutral";

            var keywords = new List<KeywordResult>();
            if (result.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in kw.EnumerateArray())
                {
                    keywords.Add(new KeywordResult
                    {
                        Id = item.GetProperty("id").GetInt32(),
                        Label = item.GetProperty("label").GetString() ?? string.Empty
                    });
                }
            }

            return new LmStudioResponse
            {
                Success = true,
                Sentiment = sentiment,
                Keywords = keywords,
                RawResponse = messageContent
            };
        }
        catch (Exception ex)
        {
            return new LmStudioResponse
            {
                Success = false,
                Error = $"JSON-Parse-Fehler: {ex.Message}",
                RawResponse = messageContent
            };
        }
    }

    private string ExtractJson(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```json"))
            text = text[7..];
        else if (text.StartsWith("```"))
            text = text[3..];

        if (text.EndsWith("```"))
            text = text[..^3];

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];

        return text.Trim();
    }

    private async Task<string> SendChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = _temperature,
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
    public string RawResponse { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class KeywordResult
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

