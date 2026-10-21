using Microsoft.Extensions.Configuration;

namespace AiGateway.Shared;

/// <summary>Applies the same generation settings to synchronous and batch requests.</summary>
public static class LmStudioGenerationSettings
{
    public static Dictionary<string, object> CreatePayload(
        IConfiguration configuration,
        string model,
        string systemPrompt,
        string userPrompt,
        string phase,
        int? defaultMaxTokens,
        double? temperatureOverride = null)
    {
        var temperature = temperatureOverride
            ?? configuration.GetValue<double?>("Gateway:Temperature")
            ?? 0.1;
        if (!double.IsFinite(temperature) || temperature < 0 || temperature > 2)
            throw new InvalidOperationException("Gateway:Temperature must be between 0 and 2.");

        var maxTokens = configuration.GetValue<int?>($"Gateway:ResponseTokenLimits:{phase}")
            ?? configuration.GetValue<int?>("Gateway:MaxResponseTokens")
            ?? defaultMaxTokens;
        if (maxTokens is <= 0)
            throw new InvalidOperationException("Response token limits must be greater than zero.");

        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            ["temperature"] = temperature,
            ["response_format"] = new { type = "text" },
            ["stream"] = false
        };

        if (maxTokens.HasValue)
            payload["max_tokens"] = maxTokens.Value;

        return payload;
    }
}
