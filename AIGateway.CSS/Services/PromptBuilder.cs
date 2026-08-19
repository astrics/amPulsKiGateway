using System.Text;
using System.Text.Json;
using AiGateway.CSS.Api.Models;

namespace AiGateway.CSS.Api.Services;

public sealed class CssCodebookPromptService
{
    private readonly CssCodebook _codebook;
    private readonly Dictionary<int, CssCodebookLabel> _labelsById;
    private readonly string _systemPrompt;
    private readonly ILogger<CssCodebookPromptService> _logger;

    public CssCodebookPromptService(IConfiguration config, ILogger<CssCodebookPromptService> logger)
    {
        _logger = logger;
        _codebook = LoadCodebook(config);
        _labelsById = _codebook.Labels
            .Where(label => label.Number > 0)
            .GroupBy(label => label.Number)
            .Select(group => group.First())
            .ToDictionary(label => label.Number);
        _systemPrompt = BuildSystemPrompt(_codebook);

        _logger.LogInformation(
            "CSS-Codebook geladen: {Count} Codes in {Groups} Codegruppen",
            _codebook.Labels.Count,
            _codebook.Labels.Select(label => label.CodeGroup).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    public string GetSystemPrompt() => _systemPrompt;

    public string BuildUserPrompt(string statementText)
    {
        return "Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS.\n" +
               "Antworte ausschliesslich mit dem geforderten JSON.\n\n" +
               "Kundenaussage:\n\"" + statementText + "\"";
    }

    public void NormalizeResult(AiResult result)
    {
        if (result == null)
        {
            return;
        }

        var normalizedMatches = result.CodeMatches
            .Select(NormalizeCodeMatch)
            .Where(match => match.Id > 0)
            .GroupBy(match => match.Id)
            .Select(group => group.First())
            .OrderBy(match => match.Id)
            .ToList();

        if (normalizedMatches.Count == 0 && result.Keywords.Count > 0)
        {
            normalizedMatches = result.Keywords
                .Where(keyword => keyword.Id > 0)
                .Select(keyword => NormalizeCodeMatch(new AiCodeMatch { Id = keyword.Id, Code = keyword.Label }))
                .Where(match => match.Id > 0)
                .GroupBy(match => match.Id)
                .Select(group => group.First())
                .OrderBy(match => match.Id)
                .ToList();
        }

        result.CodeMatches = normalizedMatches;
        result.Keywords = normalizedMatches
            .Select(match => new AiKeyword { Id = match.Id, Label = match.Code })
            .ToList();

        var normalizedGroupSentiments = result.CodeGroupSentiments
            .Select(NormalizeGroupSentiment)
            .Where(group => !string.IsNullOrWhiteSpace(group.CodeGroup))
            .GroupBy(group => group.CodeGroup, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                first.MatchedCodeIds = group
                    .SelectMany(item => item.MatchedCodeIds)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
                return first;
            })
            .OrderBy(group => group.CodeGroup)
            .ToList();

        if (normalizedGroupSentiments.Count == 0)
        {
            normalizedGroupSentiments = DeriveGroupSentiments(normalizedMatches);
        }
        else
        {
            foreach (var group in normalizedGroupSentiments)
            {
                if (group.MatchedCodeIds.Count == 0)
                {
                    group.MatchedCodeIds = normalizedMatches
                        .Where(match => string.Equals(match.CodeGroup, group.CodeGroup, StringComparison.OrdinalIgnoreCase))
                        .Select(match => match.Id)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToList();
                }
            }
        }

        result.CodeGroupSentiments = normalizedGroupSentiments;
        result.Sentiment = NormalizeSentiment(result.Sentiment);
        if (string.IsNullOrWhiteSpace(result.Sentiment) || result.Sentiment == "Neutral")
        {
            result.Sentiment = DeriveOverallSentiment(result.CodeGroupSentiments);
        }
    }

    private AiCodeMatch NormalizeCodeMatch(AiCodeMatch input)
    {
        var match = new AiCodeMatch
        {
            Id = input.Id,
            CodeGroup = NormalizeWhitespace(input.CodeGroup),
            Code = NormalizeWhitespace(input.Code),
            Sentiment = NormalizeSentiment(input.Sentiment)
        };

        if (_labelsById.TryGetValue(match.Id, out var label))
        {
            match.CodeGroup = label.CodeGroup;
            match.Code = label.Code;
        }

        return match;
    }

    private AiCodeGroupSentiment NormalizeGroupSentiment(AiCodeGroupSentiment input)
    {
        var groupName = NormalizeWhitespace(input.CodeGroup);
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            var matchingLabel = _codebook.Labels
                .FirstOrDefault(label => string.Equals(label.CodeGroup, groupName, StringComparison.OrdinalIgnoreCase));
            if (matchingLabel != null)
            {
                groupName = matchingLabel.CodeGroup;
            }
        }

        return new AiCodeGroupSentiment
        {
            CodeGroup = groupName,
            Sentiment = NormalizeSentiment(input.Sentiment),
            MatchedCodeIds = input.MatchedCodeIds
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList()
        };
    }

    private List<AiCodeGroupSentiment> DeriveGroupSentiments(IEnumerable<AiCodeMatch> matches)
    {
        return matches
            .Where(match => !string.IsNullOrWhiteSpace(match.CodeGroup))
            .GroupBy(match => match.CodeGroup, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AiCodeGroupSentiment
            {
                CodeGroup = group.First().CodeGroup,
                Sentiment = DeriveSentiment(group.Select(match => match.Sentiment)),
                MatchedCodeIds = group.Select(match => match.Id).Distinct().OrderBy(id => id).ToList()
            })
            .OrderBy(group => group.CodeGroup)
            .ToList();
    }

    private string DeriveOverallSentiment(IEnumerable<AiCodeGroupSentiment> groups)
    {
        var sentiments = groups
            .Select(group => NormalizeSentiment(group.Sentiment))
            .Where(sentiment => !string.IsNullOrWhiteSpace(sentiment))
            .ToList();

        return DeriveSentiment(sentiments);
    }

    private static string DeriveSentiment(IEnumerable<string> rawSentiments)
    {
        var sentiments = rawSentiments
            .Select(NormalizeSentiment)
            .Where(sentiment => !string.IsNullOrWhiteSpace(sentiment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sentiments.Count == 0)
        {
            return "Neutral";
        }

        return sentiments.Count == 1 ? sentiments[0] : "Neutral";
    }

    private static string NormalizeSentiment(string? raw)
    {
        var lower = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return lower switch
        {
            "positiv" or "positive" or "pos" => "Positiv",
            "negativ" or "negative" or "neg" => "Negativ",
            "neutral" or "mixed" or "gemischt" => "Neutral",
            _ => "Neutral"
        };
    }

    private static CssCodebook LoadCodebook(IConfiguration config)
    {
        var configuredPath = config["CssCodebook:Path"] ?? "Codebooks/css-codes.imported.json";
        var resolvedPath = ResolvePath(configuredPath);

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"CSS-Codebook nicht gefunden: {resolvedPath}");
        }

        var json = File.ReadAllText(resolvedPath);
        var codebook = JsonSerializer.Deserialize<CssCodebook>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (codebook == null || codebook.Labels.Count == 0)
        {
            throw new InvalidOperationException($"CSS-Codebook ist leer oder ungueltig: {resolvedPath}");
        }

        return codebook;
    }

    private static string ResolvePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var currentDirPath = Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
        if (File.Exists(currentDirPath))
        {
            return currentDirPath;
        }

        return Path.Combine(AppContext.BaseDirectory, configuredPath);
    }

    private static string BuildSystemPrompt(CssCodebook codebook)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Deine Aufgaben:");
        builder.AppendLine("1. Analysiere genau eine einzelne Kundenaussage.");
        builder.AppendLine("2. Waehle alle fachlich passenden Codes aus der vorgegebenen CSS-Codeliste aus.");
        builder.AppendLine("3. Bestimme fuer jeden ausgewaehlten Code ein Sentiment: Positiv, Negativ oder Neutral.");
        builder.AppendLine("4. Verdichte diese Codings zu einem Sentiment pro Codegruppe.");
        builder.AppendLine("5. Bestimme zusaetzlich ein Gesamt-Sentiment fuer die gesamte Aussage.");
        builder.AppendLine();
        builder.AppendLine("Wichtige Regeln:");
        builder.AppendLine("- Gib IMMER gueltiges JSON zurueck und keinerlei Text ausserhalb des JSON.");
        builder.AppendLine("- Verwende AUSSCHLIESSLICH Codes aus der unten aufgefuehrten Liste.");
        builder.AppendLine("- Erfinde keine neuen Codes oder Codegruppen.");
        builder.AppendLine("- Eine Aussage kann mehrere Codes enthalten, auch aus verschiedenen Codegruppen.");
        builder.AppendLine("- Vergib einen Code nur dann, wenn die Aussage inhaltlich wirklich dazu passt.");
        builder.AppendLine("- Wenn kein Code sicher passt, gib leere Arrays fuer keywords, codeMatches und codeGroupSentiments zurueck.");
        builder.AppendLine("- keywords muss die gleiche Auswahl wie codeMatches enthalten. Jeder keywords-Eintrag nutzt dieselbe id und denselben Code-Text wie der passende codeMatches-Eintrag.");
        builder.AppendLine("- codeGroupSentiments darf nur Codegruppen enthalten, in denen mindestens ein Code gematcht wurde.");
        builder.AppendLine("- Wenn in derselben Codegruppe sowohl positive als auch negative Aspekte vorkommen, setze das Sentiment der Codegruppe auf Neutral.");
        builder.AppendLine("- Wenn eine Aussage fuer einen Code bzw. eine Codegruppe rein beschreibend und nicht klar wertend ist, setze das Sentiment auf Neutral.");
        builder.AppendLine("- Bevorzuge praezise Codings gegenueber moeglichst vielen Codings.");
        builder.AppendLine();
        builder.AppendLine("Verwende fuer die Ausgabe IMMER exakt dieses JSON-Schema:");
        builder.AppendLine("{");
        builder.AppendLine("  \"statement\": \"<Originalaussage unveraendert>\",");
        builder.AppendLine("  \"sentiment\": \"Positiv | Negativ | Neutral\",");
        builder.AppendLine("  \"keywords\": [");
        builder.AppendLine("    { \"id\": 1, \"label\": \"<exakter Code-Text>\" }");
        builder.AppendLine("  ],");
        builder.AppendLine("  \"codeMatches\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"id\": 1,");
        builder.AppendLine("      \"codeGroup\": \"<exakte Codegruppe>\",");
        builder.AppendLine("      \"code\": \"<exakter Code-Text>\",");
        builder.AppendLine("      \"sentiment\": \"Positiv | Negativ | Neutral\"");
        builder.AppendLine("    }");
        builder.AppendLine("  ],");
        builder.AppendLine("  \"codeGroupSentiments\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"codeGroup\": \"<exakte Codegruppe>\",");
        builder.AppendLine("      \"sentiment\": \"Positiv | Negativ | Neutral\",");
        builder.AppendLine("      \"matchedCodeIds\": [1]");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("Erlaubte CSS-Codes:");

        foreach (var group in codebook.Labels.GroupBy(label => label.CodeGroup))
        {
            builder.AppendLine($"Codegruppe: {group.Key}");
            foreach (var label in group)
            {
                builder.AppendLine($"- ID {label.Number}: {label.Code}");
                if (!string.IsNullOrWhiteSpace(label.CodingRule))
                {
                    builder.AppendLine($"  Codierregel: {Condense(label.CodingRule, 500)}");
                }
                if (!string.IsNullOrWhiteSpace(label.ExampleText))
                {
                    builder.AppendLine($"  Beispiele: {Condense(label.ExampleText, 280)}");
                }
            }
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string Condense(string value, int maxLength)
    {
        var normalized = NormalizeWhitespace(value);
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd() + "...";
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var parts = value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', parts).Trim();
    }
}