using System.Text.Json;
using AiGateway.CSS.Api.Models;
using Microsoft.Extensions.Logging;

namespace AiGateway.CSS.Api.Services;

public static class CssAiResponseParser
{
    public static AiResult ParseCompletionResponse(string responseBody, ILogger? logger = null)
    {
        try
        {
            using var rootDoc = JsonDocument.Parse(responseBody);
            var contentStr = rootDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";

            return ParseMessageContent(contentStr, logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Antwort von LM Studio konnte nicht geparst werden");
            return new AiResult
            {
                Sentiment = "Neutral",
                Keywords = new List<AiKeyword>(),
                CodeMatches = new List<AiCodeMatch>(),
                CodeGroupSentiments = new List<AiCodeGroupSentiment>(),
                RawResponse = responseBody[..Math.Min(5000, responseBody.Length)],
                ParseError = ex.Message
            };
        }
    }

    private static AiResult ParseMessageContent(string messageContent, ILogger? logger)
    {
        try
        {
            var cleanJson = ExtractJson(messageContent);
            using var contentDoc = JsonDocument.Parse(cleanJson);
            var root = contentDoc.RootElement;

            return new AiResult
            {
                Statement = ReadString(root, "statement"),
                Sentiment = ReadString(root, "sentiment", "overallSentiment", "overall_sentiment"),
                Keywords = ParseKeywords(root),
                CodeMatches = ParseCodeMatches(root),
                CodeGroupSentiments = ParseCodeGroupSentiments(root),
                RawResponse = cleanJson
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Inhalt von LM Studio konnte nicht geparst werden");
            return new AiResult
            {
                Sentiment = "Neutral",
                Keywords = new List<AiKeyword>(),
                CodeMatches = new List<AiCodeMatch>(),
                CodeGroupSentiments = new List<AiCodeGroupSentiment>(),
                RawResponse = messageContent[..Math.Min(5000, messageContent.Length)],
                ParseError = ex.Message
            };
        }
    }

    private static List<AiKeyword> ParseKeywords(JsonElement root)
    {
        var keywords = new List<AiKeyword>();
        if (!TryGetProperty(root, out var keywordElement, "keywords") || keywordElement.ValueKind != JsonValueKind.Array)
        {
            return keywords;
        }

        foreach (var item in keywordElement.EnumerateArray())
        {
            var id = ReadInt(item, "id", "Id", "number");
            var label = ReadString(item, "label", "Label", "code", "name");
            if (id > 0)
            {
                keywords.Add(new AiKeyword { Id = id, Label = label });
            }
        }

        return keywords;
    }

    private static List<AiCodeMatch> ParseCodeMatches(JsonElement root)
    {
        var matches = new List<AiCodeMatch>();
        if (!TryGetProperty(root, out var matchElement, "codeMatches", "code_matches", "codes", "matches") || matchElement.ValueKind != JsonValueKind.Array)
        {
            return matches;
        }

        foreach (var item in matchElement.EnumerateArray())
        {
            var id = ReadInt(item, "id", "Id", "number");
            var codeGroup = ReadString(item, "codeGroup", "code_group", "group");
            var code = ReadString(item, "code", "label", "name");
            var sentiment = ReadString(item, "sentiment");

            if (id > 0 || !string.IsNullOrWhiteSpace(code))
            {
                matches.Add(new AiCodeMatch
                {
                    Id = id,
                    CodeGroup = codeGroup,
                    Code = code,
                    Sentiment = sentiment
                });
            }
        }

        return matches;
    }

    private static List<AiCodeGroupSentiment> ParseCodeGroupSentiments(JsonElement root)
    {
        var groups = new List<AiCodeGroupSentiment>();
        if (!TryGetProperty(root, out var groupElement, "codeGroupSentiments", "code_group_sentiments", "groupSentiments", "group_sentiments") || groupElement.ValueKind != JsonValueKind.Array)
        {
            return groups;
        }

        foreach (var item in groupElement.EnumerateArray())
        {
            var matchedCodeIds = new List<int>();
            if (TryGetProperty(item, out var idsElement, "matchedCodeIds", "matched_code_ids", "codeIds", "code_ids") && idsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var idElement in idsElement.EnumerateArray())
                {
                    if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out var parsedId) && parsedId > 0)
                    {
                        matchedCodeIds.Add(parsedId);
                    }
                }
            }

            var codeGroup = ReadString(item, "codeGroup", "code_group", "group");
            if (!string.IsNullOrWhiteSpace(codeGroup))
            {
                groups.Add(new AiCodeGroupSentiment
                {
                    CodeGroup = codeGroup,
                    Sentiment = ReadString(item, "sentiment"),
                    MatchedCodeIds = matchedCodeIds
                });
            }
        }

        return groups;
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static int ReadInt(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ExtractJson(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
            {
                text = text[(firstNewline + 1)..];
            }

            if (text.TrimEnd().EndsWith("```"))
            {
                text = text.TrimEnd()[..^3];
            }
        }

        text = text.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return text[start..(end + 1)];
        }

        return text;
    }
}