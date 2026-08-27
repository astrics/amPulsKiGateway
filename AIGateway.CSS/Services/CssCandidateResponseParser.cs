using System.Text.Json;
using AiGateway.CSS.Api.Models;

namespace AiGateway.CSS.Api.Services;

public static class CssCandidateResponseParser
{
    public static AiResult ParseCompletionResponse(
        string responseBody,
        string statementText,
        IReadOnlyList<CssCodeCandidate> candidates,
        ILogger? logger = null)
    {
        var candidateIds = candidates.Select(candidate => candidate.Id).Distinct().OrderBy(id => id).ToList();

        try
        {
            using var rootDoc = JsonDocument.Parse(responseBody);
            var messageContent = rootDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";

            return ParseMessageContent(messageContent, statementText, candidateIds, logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Candidate-Rerank-Antwort von LM Studio konnte nicht geparst werden");
            return new AiResult
            {
                Statement = statementText,
                Sentiment = "Neutral",
                CandidateCodeIds = candidateIds,
                NeedsReview = true,
                ValidationWarnings = ["completion_response_invalid"],
                RawResponse = responseBody[..Math.Min(5000, responseBody.Length)],
                ParseError = ex.Message
            };
        }
    }

    private static AiResult ParseMessageContent(
        string messageContent,
        string statementText,
        IReadOnlyCollection<int> candidateIds,
        ILogger? logger)
    {
        try
        {
            var cleanJson = ExtractJson(messageContent);
            using var contentDoc = JsonDocument.Parse(cleanJson);
            var root = contentDoc.RootElement;
            var matches = new List<AiCodeMatch>();
            var warnings = new List<string>();
            var seenIds = new HashSet<int>();
            var decisionIds = new HashSet<int>();

            if (!TryGetProperty(root, out var decisionsElement, "decisions") ||
                decisionsElement.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Das Feld 'decisions' fehlt oder ist kein Array.");
            }

            foreach (var decision in decisionsElement.EnumerateArray())
            {
                if (decision.ValueKind != JsonValueKind.Object)
                {
                    warnings.Add("decision_not_an_object");
                    continue;
                }

                var id = ReadInt(decision, "id", "codeId", "code_id");
                if (id <= 0 || !candidateIds.Contains(id))
                {
                    warnings.Add($"decision_outside_candidates:{id}");
                    continue;
                }

                if (!decisionIds.Add(id))
                {
                    warnings.Add($"duplicate_decision:{id}");
                    continue;
                }

                var isMatch = ReadBoolean(decision, "match", "selected", "isMatch", "is_match");
                if (!isMatch)
                {
                    continue;
                }

                if (!seenIds.Add(id))
                {
                    continue;
                }

                var evidence = ReadString(decision, "evidence", "quote", "excerpt").Trim();
                if (string.IsNullOrWhiteSpace(evidence))
                {
                    warnings.Add($"missing_evidence:{id}");
                }
                else if (!statementText.Contains(evidence, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"evidence_not_in_statement:{id}");
                    evidence = string.Empty;
                }

                matches.Add(new AiCodeMatch
                {
                    Id = id,
                    Sentiment = NormalizeSentiment(ReadString(decision, "sentiment")),
                    Evidence = evidence
                });
            }

            foreach (var missingId in candidateIds.Where(id => !decisionIds.Contains(id)))
            {
                warnings.Add($"candidate_without_decision:{missingId}");
            }

            var modelNeedsReview = ReadBoolean(root, "needsReview", "needs_review", "reviewRequired");
            return new AiResult
            {
                Statement = statementText,
                Sentiment = DeriveOverallSentiment(matches),
                CodeMatches = matches.OrderBy(match => match.Id).ToList(),
                CandidateCodeIds = candidateIds.OrderBy(id => id).ToList(),
                NeedsReview = modelNeedsReview || warnings.Count > 0,
                ValidationWarnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
                RawResponse = cleanJson
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Candidate-Rerank-Inhalt von LM Studio konnte nicht geparst werden");
            return new AiResult
            {
                Statement = statementText,
                Sentiment = "Neutral",
                CandidateCodeIds = candidateIds.OrderBy(id => id).ToList(),
                NeedsReview = true,
                ValidationWarnings = ["candidate_content_invalid"],
                RawResponse = messageContent[..Math.Min(5000, messageContent.Length)],
                ParseError = ex.Message
            };
        }
    }

    private static string DeriveOverallSentiment(IEnumerable<AiCodeMatch> matches)
    {
        var sentiments = matches
            .Select(match => NormalizeSentiment(match.Sentiment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return sentiments.Count == 1 ? sentiments[0] : "Neutral";
    }

    private static string NormalizeSentiment(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.StartsWith("pos"))
        {
            return "Positiv";
        }

        if (value.StartsWith("neg"))
        {
            return "Negativ";
        }

        return "Neutral";
    }

    private static bool ReadBoolean(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString()?.Trim().ToLowerInvariant();
            return raw is "true" or "yes" or "ja" or "match" or "selected";
        }

        return false;
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static int ReadInt(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)
            ? parsed
            : 0;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

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
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
            {
                text = text[(firstNewline + 1)..];
            }

            if (text.TrimEnd().EndsWith("```", StringComparison.Ordinal))
            {
                text = text.TrimEnd()[..^3];
            }
        }

        text = text.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}
