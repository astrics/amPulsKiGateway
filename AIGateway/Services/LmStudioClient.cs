using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiGateway.Api.Configuration;
using AiGateway.Api.Models.Internal;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Services;

public class LmStudioClient : ILmStudioClient
{
    private readonly HttpClient _httpClient;
    private readonly GatewayOptions _options;
    private readonly ILogger<LmStudioClient> _logger;

    public LmStudioClient(
        HttpClient httpClient,
        IOptions<GatewayOptions> options,
        ILogger<LmStudioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.LmStudioBaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
    }

    public async Task<string> ChatCompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        // Request-Body als Dictionary damit wir chat_template_kwargs hinzufuegen koennen
        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _options.ModelName,
            ["temperature"] = _options.Temperature,
            ["max_tokens"] = _options.MaxResponseTokens,
            ["stream"] = false,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            // Qwen3: Thinking-Modus deaktivieren
            ["chat_template_kwargs"] = new { enable_thinking = false }
        };

        _logger.LogDebug(
            "LM Studio Request: ~{PromptLength} Zeichen Prompt, Max {MaxTokens} Response-Tokens",
            systemPrompt.Length + userPrompt.Length,
            _options.MaxResponseTokens);

        var sw = Stopwatch.StartNew();

        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/v1/chat/completions", requestBody, jsonOptions, ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<LmStudioChatResponse>(
                cancellationToken: ct);

            sw.Stop();

            if (result?.Choices == null || !result.Choices.Any())
            {
                throw new InvalidOperationException(
                    "LM Studio hat keine Antwort geliefert");
            }

            var content = result.Choices[0].Message.Content;

            _logger.LogInformation(
                "LM Studio Antwort in {Duration:F1}s. " +
                "Tokens: {PromptTokens} prompt + {CompletionTokens} completion = {TotalTokens} total",
                sw.Elapsed.TotalSeconds,
                result.Usage?.PromptTokens ?? 0,
                result.Usage?.CompletionTokens ?? 0,
                result.Usage?.TotalTokens ?? 0);

            // Schritt 1: Think-Bloecke entfernen (Safety-Net)
            content = StripThinkingBlocks(content);

            // Schritt 2: JSON extrahieren
            return ExtractJson(content);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogError(
                "LM Studio Timeout nach {Duration:F1}s (Limit: {Timeout}s)",
                sw.Elapsed.TotalSeconds, _options.RequestTimeoutSeconds);
            throw new TimeoutException(
                $"LM Studio hat nicht innerhalb von " +
                $"{_options.RequestTimeoutSeconds}s geantwortet. " +
                $"Bei CPU-Inferenz ggf. RequestTimeoutSeconds erhoehen.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "LM Studio nicht erreichbar unter {Url}",
                _options.LmStudioBaseUrl);
            throw new InvalidOperationException(
                $"LM Studio nicht erreichbar: {ex.Message}. " +
                $"Laeuft LM Studio auf {_options.LmStudioBaseUrl}?", ex);
        }
    }

    public async Task<(bool isReachable, string? modelName, int? responseTimeMs)>
        HealthCheckAsync()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var response = await _httpClient.GetAsync("/v1/models");
            sw.Stop();

            if (!response.IsSuccessStatusCode)
                return (false, null, (int)sw.ElapsedMilliseconds);

            var models = await response.Content
                .ReadFromJsonAsync<LmStudioModelsResponse>();

            var modelName = models?.Data?.FirstOrDefault()?.Id;

            return (true, modelName, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "LM Studio Health Check fehlgeschlagen: {Error}",
                ex.Message);
            return (false, null, null);
        }
    }

    /// <summary>
    /// Entfernt Think-Bloecke die manche Modelle (Qwen3, DeepSeek)
    /// vor der eigentlichen Antwort generieren.
    /// </summary>
    private string StripThinkingBlocks(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        // <think>...</think> komplett entfernen (auch mehrzeilig)
        var stripped = Regex.Replace(
            content,
            @"<think>[\s\S]*?</think>",
            "",
            RegexOptions.IgnoreCase);

        // Falls nur ein oeffnender <think> ohne schliessendes Tag
        var thinkStart = stripped.IndexOf(
            "<think>", StringComparison.OrdinalIgnoreCase);
        if (thinkStart >= 0)
        {
            stripped = stripped[..thinkStart];
        }

        stripped = stripped.Trim();

        if (string.IsNullOrWhiteSpace(stripped))
        {
            _logger.LogWarning(
                "LM Studio Antwort enthielt nur Think-Block ohne " +
                "eigentlichen Content. Laenge Original: {Length} Zeichen",
                content.Length);
            return content;
        }

        if (stripped.Length < content.Length)
        {
            _logger.LogDebug(
                "Think-Block entfernt: {OriginalLength} -> " +
                "{StrippedLength} Zeichen",
                content.Length, stripped.Length);
        }

        return stripped;
    }

    /// <summary>
    /// Extrahiert JSON aus einer Antwort die moeglicherweise
    /// zusaetzlichen Text enthaelt
    /// </summary>
    private string ExtractJson(string content)
    {
        content = content.Trim();

        // Schon valides JSON?
        if ((content.StartsWith('{') && content.EndsWith('}')) ||
            (content.StartsWith('[') && content.EndsWith(']')))
        {
            return content;
        }

        // JSON-Block in Markdown Code-Bloecken?
        var jsonMatch = Regex.Match(
            content,
            @"```(?:json)?\s*(\{[\s\S]*\})\s*```");

        if (jsonMatch.Success)
        {
            return jsonMatch.Groups[1].Value;
        }

        // Erstes { bis letztes } extrahieren
        int firstBrace = content.IndexOf('{');
        int lastBrace = content.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            var extracted = content[firstBrace..(lastBrace + 1)];
            try
            {
                JsonDocument.Parse(extracted);
                return extracted;
            }
            catch
            {
                // Kein valides JSON
            }
        }

        _logger.LogWarning(
            "Konnte kein JSON aus LM Studio Antwort extrahieren. " +
            "Raw-Content wird verwendet.");
        return content;
    }
}