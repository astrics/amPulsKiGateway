using System.Text;
using System.Text.Json;

namespace AiGateway.Api.Services;

public class LmStudioClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    public LmStudioClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _baseUrl = config["LmStudio:BaseUrl"] ?? "http://localhost:1234";
        _model = config["LmStudio:Model"] ?? "qwen2.5-7b-instruct";
    }

    public async Task<LmStudioResponse> ClassifyStatementAsync(string statementText, CancellationToken ct = default)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = PromptBuilder.GetSystemPrompt() },
                new { role = "user",   content = PromptBuilder.GetUserPrompt(statementText) }
            },
            temperature = 0.1,
            //max_tokens = 512,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _http.PostAsync($"{_baseUrl}/v1/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return ParseResponse(responseJson);
    }

    private LmStudioResponse ParseResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var messageContent = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        // JSON aus der Antwort extrahieren (evtl. in Markdown eingebettet)
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
                        Label = item.GetProperty("label").GetString() ?? ""
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
        // Markdown-Code-Block entfernen
        text = text.Trim();
        if (text.StartsWith("```json"))
            text = text[7..];
        else if (text.StartsWith("```"))
            text = text[3..];
        if (text.EndsWith("```"))
            text = text[..^3];

        // Erstes { bis letztes } extrahieren
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];

        return text.Trim();
    }
}

public class LmStudioResponse
{
    public bool Success { get; set; }
    public string Sentiment { get; set; } = "";
    public List<KeywordResult> Keywords { get; set; } = new();
    public string RawResponse { get; set; } = "";
    public string? Error { get; set; }
}

public class KeywordResult
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
}
