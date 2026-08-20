using System.Globalization;
using System.Text;
using System.Text.Json;
using AiGateway.CSS.Api.Models;

namespace AiGateway.CSS.Api.Services;

public sealed class CssCodebookPromptService
{
    private const string BaselinePromptVariant = "baseline";
    private const string CompactPromptVariant = "compact_v1";
    private const string SinglePassMode = "single_pass";
    private const string TwoStageMode = "two_stage";

    private readonly CssCodebook _codebook;
    private readonly Dictionary<int, CssCodebookLabel> _labelsById;
    private readonly Dictionary<string, List<CssCodebookLabel>> _labelsByGroup;
    private readonly Dictionary<string, string> _groupAliases;
    private readonly string _classificationMode;
    private readonly string _promptVariant;
    private readonly int _maxCodeGroupsPerStatement;
    private readonly int _maxCodingRuleLength;
    private readonly int _maxExampleLength;
    private readonly string _singlePassSystemPrompt;
    private readonly string _codeGroupSystemPrompt;
    private readonly ILogger<CssCodebookPromptService> _logger;

    public CssCodebookPromptService(IConfiguration config, ILogger<CssCodebookPromptService> logger)
    {
        _logger = logger;
        _codebook = LoadCodebook(config);
        _classificationMode = NormalizeMode(config["CssCodebook:ClassificationMode"]);
        _promptVariant = NormalizePromptVariant(config["CssCodebook:PromptVariant"]);
        _maxCodeGroupsPerStatement = Math.Max(1, config.GetValue<int?>("CssCodebook:MaxCodeGroupsPerStatement") ?? 3);
        _maxCodingRuleLength = Math.Max(80, config.GetValue<int?>("CssCodebook:MaxCodingRuleLength") ?? GetDefaultCodingRuleLength(_promptVariant));
        _maxExampleLength = Math.Max(40, config.GetValue<int?>("CssCodebook:MaxExampleLength") ?? GetDefaultExampleLength(_promptVariant));

        _labelsById = _codebook.Labels
            .Where(label => label.Number > 0)
            .GroupBy(label => label.Number)
            .Select(group => group.First())
            .ToDictionary(label => label.Number);

        _labelsByGroup = _codebook.Labels
            .Where(label => !string.IsNullOrWhiteSpace(label.CodeGroup))
            .GroupBy(label => NormalizeWhitespace(label.CodeGroup), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.First().CodeGroup,
                group => group.OrderBy(label => label.Number).ToList(),
                StringComparer.OrdinalIgnoreCase);
        _groupAliases = BuildGroupAliases();

        _singlePassSystemPrompt = BuildSinglePassSystemPrompt();
        _codeGroupSystemPrompt = BuildCodeGroupSystemPrompt();

        _logger.LogInformation(
            "CSS-Codebook geladen: {Count} Codes in {Groups} Codegruppen | Strategie: {Mode} | PromptVariante: {Variant} | RegelLaenge: {RuleLength} | BeispielLaenge: {ExampleLength}",
            _codebook.Labels.Count,
            _labelsByGroup.Count,
            _classificationMode,
            _promptVariant,
            _maxCodingRuleLength,
            _maxExampleLength);
    }

    public string ClassificationMode => _classificationMode;
    public string PromptVariant => _promptVariant;

    public bool UsesTwoStageClassification => string.Equals(_classificationMode, TwoStageMode, StringComparison.OrdinalIgnoreCase);

    public string GetSystemPrompt() => _singlePassSystemPrompt;

    public string BuildUserPrompt(string statementText)
    {
        return BuildStatementPrompt(
            "Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS und bestimme die passenden Codes.",
            statementText);
    }

    public string GetCodeGroupSystemPrompt() => _codeGroupSystemPrompt;

    public string BuildCodeGroupUserPrompt(string statementText)
    {
        return BuildStatementPrompt(
            "Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS und bestimme zuerst nur die passenden Codegruppen. Die Aussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch formuliert sein. Ordne nach Bedeutung, nicht nach Sprache, zu.",
            statementText);
    }

    public string BuildCodeSelectionSystemPrompt(IEnumerable<string> selectedGroups)
    {
        var groups = SelectKnownCodeGroups(selectedGroups);
        var builder = new StringBuilder();

        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Deine Aufgaben:");
        builder.AppendLine("1. Analysiere genau eine einzelne Kundenaussage.");
        builder.AppendLine("2. Waehle passende Codes ausschliesslich aus den unten freigegebenen Codegruppen aus.");
        builder.AppendLine("3. Bestimme fuer jeden ausgewaehlten Code ein Sentiment: Positiv, Negativ oder Neutral.");
        builder.AppendLine("4. Bestimme daraus ein Sentiment pro verwendeter Codegruppe.");
        builder.AppendLine("5. Bestimme zusaetzlich ein Gesamt-Sentiment fuer die Aussage.");
        builder.AppendLine();
        builder.AppendLine("Wichtige Regeln:");
        builder.AppendLine("- Gib IMMER gueltiges JSON zurueck und keinerlei Text ausserhalb des JSON.");
        builder.AppendLine("- Verwende AUSSCHLIESSLICH Codes aus den unten freigegebenen Codegruppen.");
        builder.AppendLine("- Erfinde keine neuen Codes oder Codegruppen.");
        builder.AppendLine("- Wenn innerhalb der freigegebenen Codegruppen kein Code sicher passt, gib leere Arrays fuer keywords, codeMatches und codeGroupSentiments zurueck.");
        builder.AppendLine("- keywords und codeMatches muessen dieselben Codes enthalten.");
        builder.AppendLine("- codeGroupSentiments darf nur Codegruppen enthalten, in denen mindestens ein Code gematcht wurde.");
        builder.AppendLine("- Wenn in einer Codegruppe sowohl positive als auch negative Aspekte vorkommen, setze das Sentiment der Codegruppe auf Neutral.");
        builder.AppendLine();
        AppendFinalOutputSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Freigegebene CSS-Codegruppen und Codes:");

        foreach (var groupName in groups)
        {
            if (!_labelsByGroup.TryGetValue(groupName, out var labels))
            {
                continue;
            }

            builder.AppendLine($"Codegruppe: {groupName}");
            foreach (var label in labels)
            {
                builder.AppendLine($"- ID {label.Number}: {label.Code}");
                if (!string.IsNullOrWhiteSpace(label.CodingRule))
                {
                    builder.AppendLine($"  Hinweis: {Condense(label.CodingRule, _maxCodingRuleLength)}");
                }
                if (!IsCompactPromptVariant && !string.IsNullOrWhiteSpace(label.ExampleText))
                {
                    builder.AppendLine($"  Beispiele: {Condense(label.ExampleText, _maxExampleLength)}");
                }
            }
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    public string BuildCodeSelectionUserPrompt(string statementText, IEnumerable<AiCodeGroupSentiment> preselectedGroups)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS.");
        builder.AppendLine("Nutze die Vorselektion der Codegruppen als Hinweis, aber pruefe die Aussage selbst sorgfaeltig.");

        var groups = preselectedGroups
            .Where(group => !string.IsNullOrWhiteSpace(group.CodeGroup))
            .Select(group => new
            {
                CodeGroup = ResolveKnownGroupName(group.CodeGroup) ?? NormalizeWhitespace(group.CodeGroup),
                Sentiment = NormalizeSentiment(group.Sentiment)
            })
            .Where(group => !string.IsNullOrWhiteSpace(group.CodeGroup))
            .Distinct()
            .Take(_maxCodeGroupsPerStatement)
            .ToList();

        if (groups.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Vorselektion aus Stufe 1:");
            foreach (var group in groups)
            {
                builder.AppendLine($"- {group.CodeGroup}: {group.Sentiment}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Antworte ausschliesslich mit dem geforderten JSON.");
        builder.AppendLine();
        builder.AppendLine("Kundenaussage:");
        builder.Append('"').Append(statementText).AppendLine("\"");
        return builder.ToString().Trim();
    }

    public List<string> SelectKnownCodeGroups(IEnumerable<string> groups)
    {
        return groups
            .Select(ResolveKnownGroupName)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(_maxCodeGroupsPerStatement)
            .ToList()!;
    }

    public List<string> SelectKnownCodeGroups(AiResult result)
    {
        if (result == null)
        {
            return new List<string>();
        }

        var candidates = new List<string>();
        candidates.AddRange(result.CodeGroupSentiments.Select(group => group.CodeGroup));
        candidates.AddRange(result.CodeMatches.Select(match => match.CodeGroup));
        candidates.AddRange(result.CodeMatches.Select(match => match.Code));
        candidates.AddRange(result.Keywords.Select(keyword => keyword.Label));

        return SelectKnownCodeGroups(candidates);
    }

    public AiResult BuildPreselectionFallback(string statementText, AiResult preselection)
    {
        preselection.Statement = string.IsNullOrWhiteSpace(preselection.Statement) ? statementText : preselection.Statement;
        preselection.Keywords = new List<AiKeyword>();
        preselection.CodeMatches = new List<AiCodeMatch>();
        NormalizeResult(preselection);
        return preselection;
    }

    public void RestrictResultToAllowedGroups(AiResult result, IEnumerable<string> allowedGroups)
    {
        if (result == null)
        {
            return;
        }

        var allowed = new HashSet<string>(SelectKnownCodeGroups(allowedGroups), StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0)
        {
            return;
        }

        result.CodeMatches = result.CodeMatches
            .Where(match => allowed.Contains(match.CodeGroup))
            .ToList();

        result.Keywords = result.Keywords
            .Where(keyword => result.CodeMatches.Any(match => match.Id == keyword.Id))
            .ToList();

        result.CodeGroupSentiments = result.CodeGroupSentiments
            .Where(group => allowed.Contains(group.CodeGroup))
            .ToList();
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
        var groupName = ResolveKnownGroupName(input.CodeGroup) ?? NormalizeWhitespace(input.CodeGroup);
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

    private string BuildSinglePassSystemPrompt()
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
        AppendFinalOutputSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Erlaubte CSS-Codes:");

        foreach (var group in _labelsByGroup)
        {
            builder.AppendLine($"Codegruppe: {group.Key}");
            foreach (var label in group.Value)
            {
                builder.AppendLine($"- ID {label.Number}: {label.Code}");
                if (!string.IsNullOrWhiteSpace(label.CodingRule))
                {
                    builder.AppendLine($"  Hinweis: {Condense(label.CodingRule, _maxCodingRuleLength)}");
                }
                if (!IsCompactPromptVariant && !string.IsNullOrWhiteSpace(label.ExampleText))
                {
                    builder.AppendLine($"  Beispiele: {Condense(label.ExampleText, _maxExampleLength)}");
                }
            }
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private string BuildCodeGroupSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Deine Aufgaben in dieser ersten Stufe:");
        builder.AppendLine("1. Analysiere genau eine einzelne Kundenaussage.");
        builder.AppendLine("2. Bestimme nur die fachlich passenden CSS-Codegruppen.");
        builder.AppendLine("3. Bestimme fuer jede passende Codegruppe ein Sentiment: Positiv, Negativ oder Neutral.");
        builder.AppendLine("4. Bestimme zusaetzlich ein Gesamt-Sentiment fuer die Aussage.");
        builder.AppendLine();
        builder.AppendLine("Wichtige Regeln:");
        builder.AppendLine("- Gib IMMER gueltiges JSON zurueck und keinerlei Text ausserhalb des JSON.");
        builder.AppendLine("- Verwende AUSSCHLIESSLICH Codegruppen aus der unten aufgefuehrten Liste.");
        builder.AppendLine("- Erfinde keine neuen Codegruppen.");
        builder.AppendLine("- Begrenze dich auf die wirklich passenden Codegruppen und waehle hoechstens " + _maxCodeGroupsPerStatement + " Codegruppen aus.");
        builder.AppendLine("- Wenn keine Codegruppe sicher passt, gib leere Arrays fuer keywords, codeMatches und codeGroupSentiments zurueck.");
        builder.AppendLine("- In dieser ersten Stufe bleiben keywords und codeMatches leer.");
        builder.AppendLine("- Die Kundenaussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch sein. Ordne nach inhaltlicher Bedeutung zu.");
        builder.AppendLine();
        AppendCodeGroupPreselectionSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Erlaubte CSS-Codegruppen:");

        foreach (var group in _labelsByGroup)
        {
            var topics = string.Join(" | ", group.Value.Select(label => label.Code));
            var codingHints = string.Join(
                " ",
                group.Value
                    .Select(label => label.CodingRule)
                    .Where(rule => !string.IsNullOrWhiteSpace(rule))
                    .Take(IsCompactPromptVariant ? 1 : 2));
            var examples = string.Join(" ", group.Value.Select(label => label.ExampleText).Where(example => !string.IsNullOrWhiteSpace(example)).Take(1));

            builder.AppendLine($"- {group.Key}: Themen {Condense(topics, IsCompactPromptVariant ? 160 : 220)}");
            if (!string.IsNullOrWhiteSpace(codingHints))
            {
                builder.AppendLine($"  Hinweise: {Condense(codingHints, IsCompactPromptVariant ? 160 : 260)}");
            }
            if (!IsCompactPromptVariant && !string.IsNullOrWhiteSpace(examples))
            {
                builder.AppendLine($"  Beispiel: {Condense(examples, _maxExampleLength)}");
            }
        }

        return builder.ToString().Trim();
    }

    private static void AppendFinalOutputSchema(StringBuilder builder)
    {
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
    }

    private static void AppendCodeGroupPreselectionSchema(StringBuilder builder)
    {
        builder.AppendLine("Verwende fuer die Ausgabe IMMER exakt dieses JSON-Schema:");
        builder.AppendLine("{");
        builder.AppendLine("  \"statement\": \"<Originalaussage unveraendert>\",");
        builder.AppendLine("  \"sentiment\": \"Positiv | Negativ | Neutral\",");
        builder.AppendLine("  \"keywords\": [],");
        builder.AppendLine("  \"codeMatches\": [],");
        builder.AppendLine("  \"codeGroupSentiments\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"codeGroup\": \"<exakte Codegruppe>\",");
        builder.AppendLine("      \"sentiment\": \"Positiv | Negativ | Neutral\",");
        builder.AppendLine("      \"matchedCodeIds\": []");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");
    }

    private static string BuildStatementPrompt(string intro, string statementText)
    {
        return intro + "\n" +
               "Antworte ausschliesslich mit dem geforderten JSON.\n\n" +
               "Kundenaussage:\n\"" + statementText + "\"";
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

    private string? ResolveKnownGroupName(string? rawGroupName)
    {
        var normalized = NormalizeWhitespace(rawGroupName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (_labelsByGroup.Keys.FirstOrDefault(key => string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase)) is { } exactMatch)
        {
            return exactMatch;
        }

        var lookup = BuildLookupKey(normalized);
        if (string.IsNullOrWhiteSpace(lookup))
        {
            return null;
        }

        if (_groupAliases.TryGetValue(lookup, out var aliasMatch))
        {
            return aliasMatch;
        }

        return _groupAliases
            .Where(entry => entry.Key.Length >= 5 &&
                            (lookup.Contains(entry.Key, StringComparison.OrdinalIgnoreCase) ||
                             entry.Key.Contains(lookup, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry => entry.Key.Length)
            .Select(entry => entry.Value)
            .FirstOrDefault();
    }

    private Dictionary<string, string> BuildGroupAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var groupName in _labelsByGroup.Keys)
        {
            AddGroupAlias(aliases, groupName, groupName);
        }

        foreach (var label in _codebook.Labels)
        {
            if (string.IsNullOrWhiteSpace(label.CodeGroup))
            {
                continue;
            }

            AddGroupAlias(aliases, label.CodeGroup, label.CodeGroup);
            AddGroupAlias(aliases, label.Code, label.CodeGroup);
        }

        AddGroupAlias(aliases, "service client", "Kundenbetreuung");
        AddGroupAlias(aliases, "support client", "Kundenbetreuung");
        AddGroupAlias(aliases, "hotline", "Kundenbetreuung");
        AddGroupAlias(aliases, "contact client", "Kundenbetreuung");
        AddGroupAlias(aliases, "employee", "Mitarbeiter");
        AddGroupAlias(aliases, "employe", "Mitarbeiter");
        AddGroupAlias(aliases, "conseiller", "Mitarbeiter");
        AddGroupAlias(aliases, "advisor", "Mitarbeiter");
        AddGroupAlias(aliases, "prime", "Prämien & Bezahlung");
        AddGroupAlias(aliases, "primes", "Prämien & Bezahlung");
        AddGroupAlias(aliases, "paiement", "Prämien & Bezahlung");
        AddGroupAlias(aliases, "payment", "Prämien & Bezahlung");
        AddGroupAlias(aliases, "facturation", "Prämien & Bezahlung");
        AddGroupAlias(aliases, "produit", "Produkt");
        AddGroupAlias(aliases, "product", "Produkt");
        AddGroupAlias(aliases, "offre", "Produkt");
        AddGroupAlias(aliases, "police", "Produkt");
        AddGroupAlias(aliases, "remboursement", "Leistungsbezug");
        AddGroupAlias(aliases, "prestation", "Leistungsbezug");
        AddGroupAlias(aliases, "claim", "Leistungsbezug");
        AddGroupAlias(aliases, "portal", "Digital");
        AddGroupAlias(aliases, "portail", "Digital");
        AddGroupAlias(aliases, "app", "Digital");
        AddGroupAlias(aliases, "application", "Digital");
        AddGroupAlias(aliases, "image", "Image");
        AddGroupAlias(aliases, "reputation", "Image");
        AddGroupAlias(aliases, "general", "Allgemeines");
        AddGroupAlias(aliases, "satisfaction generale", "Allgemeines");
        AddGroupAlias(aliases, "autres", "Sonstiges");
        AddGroupAlias(aliases, "other", "Sonstiges");

        return aliases;
    }

    private static void AddGroupAlias(IDictionary<string, string> aliases, string rawAlias, string groupName)
    {
        var lookup = BuildLookupKey(rawAlias);
        if (string.IsNullOrWhiteSpace(lookup) || aliases.ContainsKey(lookup))
        {
            return;
        }

        aliases[lookup] = groupName;
    }

    private static string BuildLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private bool IsCompactPromptVariant =>
        string.Equals(_promptVariant, CompactPromptVariant, StringComparison.OrdinalIgnoreCase);

    private static int GetDefaultCodingRuleLength(string promptVariant)
    {
        return string.Equals(promptVariant, CompactPromptVariant, StringComparison.OrdinalIgnoreCase)
            ? 140
            : 280;
    }

    private static int GetDefaultExampleLength(string promptVariant)
    {
        return string.Equals(promptVariant, CompactPromptVariant, StringComparison.OrdinalIgnoreCase)
            ? 80
            : 180;
    }

    private static string NormalizePromptVariant(string? rawPromptVariant)
    {
        return string.Equals(rawPromptVariant?.Trim(), CompactPromptVariant, StringComparison.OrdinalIgnoreCase)
            ? CompactPromptVariant
            : BaselinePromptVariant;
    }

    private static string NormalizeMode(string? rawMode)
    {
        return string.Equals(rawMode?.Trim(), SinglePassMode, StringComparison.OrdinalIgnoreCase)
            ? SinglePassMode
            : TwoStageMode;
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
