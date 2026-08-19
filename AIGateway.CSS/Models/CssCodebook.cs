using System.Text.Json.Serialization;

namespace AiGateway.CSS.Api.Models;

public sealed class CssCodebook
{
    [JsonPropertyName("project")]
    public string Project { get; set; } = string.Empty;

    [JsonPropertyName("sourceDocument")]
    public string SourceDocument { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("labelCount")]
    public int LabelCount { get; set; }

    [JsonPropertyName("labels")]
    public List<CssCodebookLabel> Labels { get; set; } = new();
}

public sealed class CssCodebookLabel
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("codeGroup")]
    public string CodeGroup { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("exampleText")]
    public string ExampleText { get; set; } = string.Empty;

    [JsonPropertyName("codingRule")]
    public string CodingRule { get; set; } = string.Empty;
}