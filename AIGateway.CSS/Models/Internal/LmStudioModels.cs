using System.Text.Json.Serialization;

namespace AiGateway.CSS.Api.Models.Internal;

/// <summary>
/// OpenAI-kompatible Request/Response Modelle für LM Studio
/// </summary>
public class LmStudioChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "local-model";

    [JsonPropertyName("messages")]
    public List<LmStudioMessage> Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.1;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 1000;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class LmStudioMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class LmStudioChatResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("choices")]
    public List<LmStudioChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public LmStudioUsage? Usage { get; set; }
}

public class LmStudioChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public LmStudioMessage Message { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public class LmStudioUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

// Für den Health-Check: /v1/models
public class LmStudioModelsResponse
{
    [JsonPropertyName("data")]
    public List<LmStudioModelInfo> Data { get; set; } = new();
}

public class LmStudioModelInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}


