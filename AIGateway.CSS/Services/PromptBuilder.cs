using System.Globalization;
using System.Text;
using System.Text.Json;
using AiGateway.CSS.Api.Models;

namespace AiGateway.CSS.Api.Services;

public sealed class CssCodebookPromptService
{
    private const string BaselinePromptVariant = "baseline";
    private const string CompactPromptVariantV1 = "compact_v1";
    private const string CompactPromptVariantV2 = "compact_v2";
    private const string QualityPromptVariantV1 = "quality_v1";
    private const string QualityPromptVariantV2 = "quality_v2";
    private const string SinglePassMode = "single_pass";
    private const string TwoStageMode = "two_stage";
    private const string CandidateRerankMode = "candidate_rerank";

    private readonly CssCodebook _codebook;
    private readonly Dictionary<int, CssCodebookLabel> _labelsById;
    private readonly Dictionary<string, List<CssCodebookLabel>> _labelsByGroup;
    private readonly Dictionary<string, string> _groupAliases;
    private readonly string _classificationMode;
    private readonly Dictionary<int, string> _classificationModeByProject;
    private readonly string _candidateSurveyContext;
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
        _classificationModeByProject = config
            .GetSection("CssCodebook:ClassificationModeByProject")
            .GetChildren()
            .Select(entry => new
            {
                IsValidProject = int.TryParse(entry.Key, out var projectId),
                ProjectId = int.TryParse(entry.Key, out projectId) ? projectId : 0,
                Mode = NormalizeMode(entry.Value)
            })
            .Where(entry => entry.IsValidProject && entry.ProjectId > 0)
            .ToDictionary(entry => entry.ProjectId, entry => entry.Mode);
        _candidateSurveyContext = NormalizeWhitespace(config["CssCodebook:CandidateSurveyContext"]);
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

    public IReadOnlyList<CssCodebookLabel> GetCodebookLabels() => _codebook.Labels;

    public string ResolveClassificationMode(int? projectId = null)
    {
        return projectId.HasValue && _classificationModeByProject.TryGetValue(projectId.Value, out var projectMode)
            ? projectMode
            : _classificationMode;
    }

    public bool UsesTwoStageClassification => string.Equals(_classificationMode, TwoStageMode, StringComparison.OrdinalIgnoreCase);
    public bool UsesCandidateRerankClassification => string.Equals(_classificationMode, CandidateRerankMode, StringComparison.OrdinalIgnoreCase);

    public bool UsesTwoStageClassificationForProject(int? projectId) =>
        string.Equals(ResolveClassificationMode(projectId), TwoStageMode, StringComparison.OrdinalIgnoreCase);

    public bool UsesCandidateRerankClassificationForProject(int? projectId) =>
        string.Equals(ResolveClassificationMode(projectId), CandidateRerankMode, StringComparison.OrdinalIgnoreCase);

    public string GetSystemPrompt() => _singlePassSystemPrompt;

    public string BuildUserPrompt(string statementText, int? customerSatisfactionScore = null)
    {
        return BuildStatementPrompt(
            IsQualityPromptVariantV1
                ? "Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS. Waehle nur fachlich klar belegte Codes und nenne pro gewaehltm Code eine kurze Text-Evidenz aus der Aussage."
                : IsQualityPromptVariantV2
                    ? "Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS. Waehle alle fachlich passenden Codes, gib die exakten Codebezeichnungen und Codegruppen aus und trenne aehnliche Themen sorgfaeltig voneinander."
                    : "Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS und bestimme die passenden Codes.",
            statementText,
            customerSatisfactionScore);
    }

    public string GetCodeGroupSystemPrompt() => _codeGroupSystemPrompt;

    public string BuildCodeGroupUserPrompt(string statementText, int? customerSatisfactionScore = null)
    {
        return BuildStatementPrompt(
            IsQualityPromptVariantV1
                ? "Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS und bestimme zuerst nur die passenden Codegruppen. Die Aussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch formuliert sein. Ordne nach inhaltlicher Bedeutung zu und waehle nur Gruppen mit klarer fachlicher Evidenz."
                : IsQualityPromptVariantV2
                    ? "Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS und bestimme zuerst nur die passenden Codegruppen. Die Aussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch formuliert sein. Ordne semantisch zu und trenne besonders zwischen Mitarbeiter, Kundenbetreuung, Produkt, Praemien, Leistungsbezug und Digital."
                    : "Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS und bestimme zuerst nur die passenden Codegruppen. Die Aussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch formuliert sein. Ordne nach Bedeutung, nicht nach Sprache, zu.",
            statementText,
            customerSatisfactionScore);
    }

    public string BuildCodeSelectionSystemPrompt(IEnumerable<string> selectedGroups)
    {
        var groups = SelectKnownCodeGroups(selectedGroups);
        if (IsQualityPromptVariantV1)
        {
            return BuildQualityV1CodeSelectionSystemPrompt(groups);
        }

        if (IsQualityPromptVariantV2)
        {
            return BuildQualityV2CodeSelectionSystemPrompt(groups);
        }

        if (IsCompactPromptVariantV2)
        {
            return BuildCompactV2CodeSelectionSystemPrompt(groups);
        }

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
                if (!UsesCompactPromptVariant && !string.IsNullOrWhiteSpace(label.ExampleText))
                {
                    builder.AppendLine($"  Beispiele: {Condense(label.ExampleText, _maxExampleLength)}");
                }
            }
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    public string BuildCodeSelectionUserPrompt(string statementText, IEnumerable<AiCodeGroupSentiment> preselectedGroups, int? customerSatisfactionScore = null)
    {
        var builder = new StringBuilder();
        if (IsQualityPromptVariantV1)
        {
            builder.AppendLine("Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS.");
            builder.AppendLine("Nutze die Vorselektion der Codegruppen nur als Hinweis.");
            builder.AppendLine("Waehle nur Codes, die inhaltlich klar aus dem Text folgen.");
            builder.AppendLine("Gib fuer jeden gewaehltm Code eine sehr kurze Text-Evidenz aus der Aussage zurueck.");
        }
        else if (IsQualityPromptVariantV2)
        {
            builder.AppendLine("Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS.");
            builder.AppendLine("Nutze die Vorselektion der Codegruppen nur als Hinweis.");
            builder.AppendLine("Waehle alle fachlich passenden Codes, trenne aehnliche Themen sorgfaeltig voneinander und gib nur Codes aus der freigegebenen Liste zurueck.");
        }
        else if (IsCompactPromptVariantV2)
        {
            builder.AppendLine("Analysiere genau eine einzelne CSS-Kundenaussage.");
            builder.AppendLine("Nutze die Vorselektion nur als Hinweis.");
            builder.AppendLine("Waehle nur Codes, die im Text klar belegt sind.");
        }
        else
        {
            builder.AppendLine("Analysiere jetzt genau diese einzelne Kundenaussage fuer CSS.");
            builder.AppendLine("Nutze die Vorselektion der Codegruppen als Hinweis, aber pruefe die Aussage selbst sorgfaeltig.");
        }

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
        AppendCustomerSatisfactionContext(builder, customerSatisfactionScore);
        builder.AppendLine("Antworte ausschliesslich mit dem geforderten JSON.");
        builder.AppendLine();
        builder.AppendLine("Kundenaussage:");
        builder.Append('"').Append(statementText).AppendLine("\"");
        return builder.ToString().Trim();
    }

    public string BuildCandidateRerankSystemPrompt(IReadOnlyList<CssCodeCandidate> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Du klassifizierst genau eine offene Kundenaussage einer Krankenversicherung.");
        builder.AppendLine("Pruefe jeden bereitgestellten CSS-Codekandidaten unabhaengig mit match=true oder match=false.");
        builder.AppendLine();
        builder.AppendLine("Erfolgskriterien:");
        builder.AppendLine("- Entscheide anhand der gesamten Bedeutung, nicht aufgrund eines einzelnen aehnlichen Wortes.");
        builder.AppendLine("- Beachte die Abgrenzung 'Nicht waehlen bei' besonders.");
        builder.AppendLine("- Mehrere Codes duerfen match=true haben, wenn mehrere Themen tatsaechlich belegt sind.");
        builder.AppendLine("- Fuer match=true muss evidence ein kurzes, WOERTLICHES Zitat aus der Kundenaussage sein.");
        builder.AppendLine("- Fuer match=false muss evidence eine leere Zeichenfolge sein.");
        builder.AppendLine("- Gib fuer JEDEN Kandidaten genau eine Entscheidung zurueck.");
        builder.AppendLine("- Verwende keine IDs ausserhalb der Kandidatenliste.");
        builder.AppendLine("- Setze needsReview=true, wenn die Aussage ohne weiteren Kontext nicht verlaesslich codiert werden kann.");
        builder.AppendLine("- Der Zufriedenheitsscore darf nur das Sentiment beeinflussen, niemals die Codeauswahl.");
        builder.AppendLine();
        builder.AppendLine("Ausgabe ausschliesslich als JSON in dieser Form:");
        builder.AppendLine("{");
        builder.AppendLine("  \"decisions\": [");
        builder.AppendLine("    { \"id\": 19, \"match\": true, \"sentiment\": \"Positiv | Negativ | Neutral\", \"evidence\": \"<exaktes Zitat>\" },");
        builder.AppendLine("    { \"id\": 15, \"match\": false, \"sentiment\": \"Neutral\", \"evidence\": \"\" }");
        builder.AppendLine("  ],");
        builder.AppendLine("  \"needsReview\": false");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("Codekandidaten:");

        foreach (var candidate in candidates)
        {
            builder.AppendLine($"ID {candidate.Id}: {candidate.Code}");
            builder.AppendLine($"Codegruppe: {candidate.CodeGroup}");
            if (!string.IsNullOrWhiteSpace(candidate.CodingRule))
            {
                builder.AppendLine($"Waehlen bei: {NormalizeWhitespace(candidate.CodingRule)}");
            }

            var boundaryHint = CssCandidateKnowledgeBase.GetBoundaryHint(candidate.Id);
            if (!string.IsNullOrWhiteSpace(boundaryHint))
            {
                builder.AppendLine($"Nicht waehlen bei / Abgrenzung: {boundaryHint}");
            }

            if (!string.IsNullOrWhiteSpace(candidate.ExampleText))
            {
                builder.AppendLine($"Beispiele: {NormalizeWhitespace(candidate.ExampleText)}");
            }

            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    public string BuildCandidateRerankUserPrompt(
        string statementText,
        IReadOnlyList<CssCodeCandidate> candidates,
        int? customerSatisfactionScore = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Pruefe jetzt alle Kandidaten und gib fuer jeden genau eine Entscheidung zurueck.");
        builder.AppendLine($"Kandidaten-IDs: {string.Join(", ", candidates.Select(candidate => candidate.Id))}");

        if (!string.IsNullOrWhiteSpace(_candidateSurveyContext))
        {
            builder.AppendLine();
            builder.AppendLine("Umfragekontext:");
            builder.AppendLine(_candidateSurveyContext);
        }

        AppendCustomerSatisfactionContext(builder, customerSatisfactionScore);
        builder.AppendLine();
        builder.AppendLine("Kundenaussage:");
        builder.AppendLine(statementText);
        builder.AppendLine();
        builder.AppendLine("Antworte ausschliesslich mit dem geforderten JSON.");
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
            .Select(match => new AiKeyword
            {
                Id = match.Id,
                ExternalCodeNr = string.IsNullOrWhiteSpace(match.ExternalCodeNr) && match.Id > 0 ? match.Id.ToString() : match.ExternalCodeNr,
                CodeGroup = match.CodeGroup,
                Label = match.Code
            })
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

        if ((IsQualityPromptVariantV1 || IsQualityPromptVariantV2) && normalizedMatches.Count > 0)
        {
            normalizedGroupSentiments = DeriveGroupSentiments(normalizedMatches);
        }
        else if (normalizedGroupSentiments.Count == 0)
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
            ExternalCodeNr = NormalizeWhitespace(input.ExternalCodeNr),
            CodeGroup = NormalizeWhitespace(input.CodeGroup),
            Code = NormalizeWhitespace(input.Code),
            Sentiment = NormalizeSentiment(input.Sentiment),
            Evidence = NormalizeWhitespace(input.Evidence)
        };

        if (string.IsNullOrWhiteSpace(match.ExternalCodeNr) && match.Id > 0)
        {
            match.ExternalCodeNr = match.Id.ToString();
        }

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
        if (IsQualityPromptVariantV1)
        {
            return BuildQualityV1SinglePassSystemPrompt();
        }

        if (IsQualityPromptVariantV2)
        {
            return BuildQualityV2SinglePassSystemPrompt();
        }

        if (IsCompactPromptVariantV2)
        {
            return BuildCompactV2SinglePassSystemPrompt();
        }

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
                if (!UsesCompactPromptVariant && !string.IsNullOrWhiteSpace(label.ExampleText))
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
        if (IsQualityPromptVariantV1)
        {
            return BuildQualityV1CodeGroupSystemPrompt();
        }

        if (IsQualityPromptVariantV2)
        {
            return BuildQualityV2CodeGroupSystemPrompt();
        }

        if (IsCompactPromptVariantV2)
        {
            return BuildCompactV2CodeGroupSystemPrompt();
        }

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
                    .Take(IsCompactPromptVariantV1 ? 1 : 2));
            var examples = string.Join(" ", group.Value.Select(label => label.ExampleText).Where(example => !string.IsNullOrWhiteSpace(example)).Take(1));

            builder.AppendLine($"- {group.Key}: Themen {Condense(topics, IsCompactPromptVariantV1 ? 160 : 220)}");
            if (!string.IsNullOrWhiteSpace(codingHints))
            {
                builder.AppendLine($"  Hinweise: {Condense(codingHints, IsCompactPromptVariantV1 ? 160 : 260)}");
            }
            if (!UsesCompactPromptVariant && !string.IsNullOrWhiteSpace(examples))
            {
                builder.AppendLine($"  Beispiel: {Condense(examples, _maxExampleLength)}");
            }
        }

        return builder.ToString().Trim();
    }

    private string BuildCompactV2SinglePassSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Analysiere genau eine einzelne Kundenaussage fuer CSS.");
        builder.AppendLine("Waehle nur Codes, die im Text klar belegt sind.");
        builder.AppendLine();
        AppendCompactV2SelectionRules(builder, restrictToSelectedGroups: false);
        builder.AppendLine();
        AppendFinalOutputSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Erlaubte CSS-Codegruppen und Codes:");
        AppendCompactV2AllowedCodes(builder, _labelsByGroup.Keys);
        return builder.ToString().Trim();
    }

    private string BuildCompactV2CodeGroupSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Erste Stufe: Bestimme nur die passenden CSS-Codegruppen fuer genau eine Kundenaussage.");
        builder.AppendLine();
        builder.AppendLine("Wichtige Regeln:");
        builder.AppendLine("- Gib IMMER gueltiges JSON zurueck und keinerlei Text ausserhalb des JSON.");
        builder.AppendLine("- Verwende AUSSCHLIESSLICH Codegruppen aus der unten aufgefuehrten Liste.");
        builder.AppendLine("- Erfinde keine neuen Codegruppen.");
        builder.AppendLine("- Waehle hoechstens " + _maxCodeGroupsPerStatement + " Codegruppen.");
        builder.AppendLine("- Waehle eine Codegruppe nur, wenn sie im Text explizit oder klar implizit belegt ist.");
        builder.AppendLine("- Allgemeines Lob oder Tadel darf nicht automatisch zu Produkt, Digital, Leistungsbezug oder Praemien fuehren.");
        builder.AppendLine("- In dieser ersten Stufe bleiben keywords und codeMatches leer.");
        builder.AppendLine("- Die Kundenaussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch sein. Ordne nach inhaltlicher Bedeutung zu.");
        builder.AppendLine("- Wenn keine Codegruppe sicher passt, gib leere Arrays zurueck.");
        builder.AppendLine();
        AppendCodeGroupPreselectionSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Erlaubte CSS-Codegruppen:");
        AppendCompactV2AllowedCodeGroups(builder);
        return builder.ToString().Trim();
    }

    private string BuildCompactV2CodeSelectionSystemPrompt(IEnumerable<string> selectedGroups)
    {
        var groups = SelectKnownCodeGroups(selectedGroups);
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Analysiere genau eine einzelne Kundenaussage fuer CSS.");
        builder.AppendLine("Waehle passende Codes ausschliesslich aus den unten freigegebenen Codegruppen.");
        builder.AppendLine();
        AppendCompactV2SelectionRules(builder, restrictToSelectedGroups: true);
        builder.AppendLine();
        AppendFinalOutputSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Freigegebene CSS-Codegruppen und Codes:");
        AppendCompactV2AllowedCodes(builder, groups);
        return builder.ToString().Trim();
    }

    private void AppendCompactV2SelectionRules(StringBuilder builder, bool restrictToSelectedGroups)
    {
        builder.AppendLine("Wichtige Regeln:");
        builder.AppendLine("- Gib IMMER gueltiges JSON zurueck und keinerlei Text ausserhalb des JSON.");
        builder.AppendLine(restrictToSelectedGroups
            ? "- Verwende AUSSCHLIESSLICH Codes aus den freigegebenen Codegruppen."
            : "- Verwende AUSSCHLIESSLICH Codes aus der unten aufgefuehrten Liste.");
        builder.AppendLine("- Erfinde keine neuen Codes oder Codegruppen.");
        builder.AppendLine("- Vergib einen Code nur, wenn er im Text klar belegt ist.");
        builder.AppendLine("- Bevorzuge wenige praezise Codes gegenueber vielen unsicheren Codes.");
        builder.AppendLine("- keywords und codeMatches muessen dieselben Codes enthalten.");
        builder.AppendLine("- codeGroupSentiments darf nur Codegruppen enthalten, in denen mindestens ein Code gematcht wurde.");
        builder.AppendLine("- Wenn innerhalb einer Codegruppe positive und negative Aspekte vorkommen, setze das Gruppensentiment auf Neutral.");
        builder.AppendLine("- Wenn kein Code sicher passt, gib leere Arrays fuer keywords, codeMatches und codeGroupSentiments zurueck.");
        builder.AppendLine("- Mitarbeiter und Kundenbetreuung koennen gemeinsam vorkommen: Mitarbeiter = Verhalten, Freundlichkeit, Kompetenz; Kundenbetreuung = Erreichbarkeit, Dauer, Kontinuitaet, Anliegensloesung.");
        builder.AppendLine("- Praemien & Bezahlung nur bei Preis, Praemie, Rechnung, Zahlung, Mahnung oder Betreibung.");
        builder.AppendLine("- Digital nur bei Homepage, App, Portal, Usability oder technischem Problem.");
        builder.AppendLine("- Leistungsbezug nur bei Rueckerstattung, Leistungsabrechnung, Leistungsentscheid oder Bezug zu Arzt, Therapeut, Medgate oder Medi24.");
        builder.AppendLine("- Allgemeines Lob oder Tadel ohne konkreten Bezug nicht automatisch als Produkt, Digital oder Leistungsbezug codieren.");
    }

    private void AppendCompactV2AllowedCodeGroups(StringBuilder builder)
    {
        foreach (var group in _labelsByGroup)
        {
            builder.AppendLine($"- {group.Key}: Signale {BuildCompactV2TopicSummary(group.Value, 4, 120)}");

            var focusHint = BuildCompactV2GroupHint(group.Value);
            if (!string.IsNullOrWhiteSpace(focusHint))
            {
                builder.AppendLine($"  Fokus: {focusHint}");
            }

            var boundaryHint = GetCompactV2GroupBoundaryHint(group.Key);
            if (!string.IsNullOrWhiteSpace(boundaryHint))
            {
                builder.AppendLine($"  Nicht waehlen bei: {boundaryHint}");
            }
        }
    }

    private void AppendCompactV2AllowedCodes(StringBuilder builder, IEnumerable<string> groups)
    {
        foreach (var groupName in groups)
        {
            if (!_labelsByGroup.TryGetValue(groupName, out var labels))
            {
                continue;
            }

            builder.AppendLine($"Codegruppe: {groupName}");
            builder.AppendLine($"- Fokus: {BuildCompactV2TopicSummary(labels, 4, 120)}");

            var boundaryHint = GetCompactV2GroupBoundaryHint(groupName);
            if (!string.IsNullOrWhiteSpace(boundaryHint))
            {
                builder.AppendLine($"- Nicht waehlen bei: {boundaryHint}");
            }

            foreach (var label in labels)
            {
                builder.AppendLine($"- ID {label.Number}: {label.Code}");
                var codeHint = GetCompactV2CodeHint(label);
                if (!string.IsNullOrWhiteSpace(codeHint))
                {
                    builder.AppendLine($"  Passt bei: {codeHint}");
                }
            }

            builder.AppendLine();
        }
    }

    private string BuildCompactV2GroupHint(IEnumerable<CssCodebookLabel> labels)
    {
        var hint = labels
            .Select(label => label.CodingRule)
            .FirstOrDefault(rule => !string.IsNullOrWhiteSpace(rule));

        return string.IsNullOrWhiteSpace(hint)
            ? string.Empty
            : Condense(hint, Math.Min(_maxCodingRuleLength, 110));
    }

    private static string BuildCompactV2TopicSummary(IEnumerable<CssCodebookLabel> labels, int take, int maxLength)
    {
        var summary = string.Join(
            " | ",
            labels
                .Select(label => NormalizeWhitespace(label.Code))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(take));

        return Condense(summary, maxLength);
    }

    private string GetCompactV2CodeHint(CssCodebookLabel label)
    {
        var source = !string.IsNullOrWhiteSpace(label.CodingRule)
            ? label.CodingRule
            : label.ExampleText;

        return string.IsNullOrWhiteSpace(source)
            ? string.Empty
            : Condense(source, Math.Min(_maxCodingRuleLength, 110));
    }

    private static string GetCompactV2GroupBoundaryHint(string groupName)
    {
        return groupName switch
        {
            "Mitarbeiter" => "rein prozessuale Themen wie Wartezeit, Erreichbarkeit oder Rueckerstattungsdauer ohne Bezug zum Verhalten des Mitarbeiters",
            "Prämien & Bezahlung" => "allgemeine Zufriedenheit oder Unzufriedenheit ohne Preis-, Praemien- oder Zahlungsbezug",
            "Produkt" => "reines Serviceerlebnis ohne Bezug zu Angebot, Police, Modell, Aufnahmebedingungen oder Offerte",
            "Kundenbetreuung" => "reine Freundlichkeit oder Kompetenz ohne Bezug zu Erreichbarkeit, Dauer, Kontinuitaet oder Anliegensloesung",
            "Leistungsbezug" => "Preis-, Service- oder Digitalthemen ohne Bezug zu Rueckerstattung, Abrechnung oder Leistungsentscheid",
            "Digital" => "allgemeiner Service ohne Homepage-, App-, Portal- oder Technikbezug",
            "Image" => "konkrete Einzelfallprobleme ohne Aussage zum generellen Bild der Krankenversicherung",
            "Sonstiges" => "wenn ein spezifischerer Code aus einer anderen Codegruppe sicher passt",
            "Allgemeines" => "wenn bereits ein konkreteres Thema eindeutig genannt ist",
            _ => string.Empty
        };
    }

    private string BuildQualityV1SinglePassSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Analysiere genau eine einzelne CSS-Kundenaussage.");
        builder.AppendLine("Arbeite semantisch: Die Aussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch formuliert sein.");
        builder.AppendLine("Waehle nur Codes, die fachlich klar durch den Inhalt der Aussage belegt sind.");
        builder.AppendLine();
        AppendQualityV1SelectionRules(builder, restrictToSelectedGroups: false);
        builder.AppendLine();
        AppendQualityV1OutputSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Erlaubte CSS-Codegruppen und Codes:");
        AppendQualityV1AllowedCodes(builder, _labelsByGroup.Keys);
        return builder.ToString().Trim();
    }

    private string BuildQualityV1CodeGroupSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Erste Stufe: Bestimme nur die fachlich passenden CSS-Codegruppen fuer genau eine Kundenaussage.");
        builder.AppendLine("Ordne nach Bedeutung, nicht nach Sprache oder einzelnen Woertern, zu.");
        builder.AppendLine();
        builder.AppendLine("Wichtige Regeln:");
        builder.AppendLine("- Gib IMMER gueltiges JSON zurueck und keinerlei Text ausserhalb des JSON.");
        builder.AppendLine("- Verwende AUSSCHLIESSLICH Codegruppen aus der unten aufgefuehrten Liste.");
        builder.AppendLine("- Erfinde keine neuen Codegruppen.");
        builder.AppendLine("- Waehle hoechstens " + _maxCodeGroupsPerStatement + " Codegruppen.");
        builder.AppendLine("- Waehle eine Codegruppe nur, wenn das Thema fachlich klar in der Aussage vorkommt.");
        builder.AppendLine("- Allgemeines Lob oder Tadel ohne Themenbezug darf nicht automatisch zu Produkt, Digital, Leistungsbezug oder Praemien fuehren.");
        builder.AppendLine("- In dieser ersten Stufe bleiben keywords und codeMatches leer.");
        builder.AppendLine("- Wenn keine Codegruppe sicher passt, gib leere Arrays zurueck.");
        builder.AppendLine();
        AppendCodeGroupPreselectionSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Erlaubte CSS-Codegruppen:");
        AppendQualityV1AllowedCodeGroups(builder);
        return builder.ToString().Trim();
    }

    private string BuildQualityV1CodeSelectionSystemPrompt(IEnumerable<string> selectedGroups)
    {
        var groups = SelectKnownCodeGroups(selectedGroups);
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Zweite Stufe: Waehle passende CSS-Codes nur aus den freigegebenen Codegruppen.");
        builder.AppendLine("Arbeite semantisch. Die Aussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch formuliert sein.");
        builder.AppendLine();
        AppendQualityV1SelectionRules(builder, restrictToSelectedGroups: true);
        builder.AppendLine();
        AppendQualityV1OutputSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Freigegebene CSS-Codegruppen und Codes:");
        AppendQualityV1AllowedCodes(builder, groups);
        return builder.ToString().Trim();
    }

    private static void AppendQualityV1OutputSchema(StringBuilder builder)
    {
        builder.AppendLine("Verwende fuer die Ausgabe IMMER exakt dieses JSON-Schema:");
        builder.AppendLine("{");
        builder.AppendLine("  \"statement\": \"<Originalaussage unveraendert>\",");
        builder.AppendLine("  \"sentiment\": \"Positiv | Negativ | Neutral\",");
        builder.AppendLine("  \"keywords\": [1],");
        builder.AppendLine("  \"codeMatches\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"id\": 1,");
        builder.AppendLine("      \"sentiment\": \"Positiv | Negativ | Neutral\",");
        builder.AppendLine("      \"evidence\": \"<kurzer Textausschnitt aus der Aussage>\"");
        builder.AppendLine("    }");
        builder.AppendLine("  ],");
        builder.AppendLine("  \"codeGroupSentiments\": []");
        builder.AppendLine("}");
    }

    private void AppendQualityV1SelectionRules(StringBuilder builder, bool restrictToSelectedGroups)
    {
        builder.AppendLine("Wichtige Regeln:");
        builder.AppendLine("- Gib IMMER gueltiges JSON zurueck und keinerlei Text ausserhalb des JSON.");
        builder.AppendLine(restrictToSelectedGroups
            ? "- Verwende AUSSCHLIESSLICH Codes aus den freigegebenen Codegruppen."
            : "- Verwende AUSSCHLIESSLICH Codes aus der unten aufgefuehrten Liste.");
        builder.AppendLine("- Erfinde keine neuen Codes oder Codegruppen.");
        builder.AppendLine("- keywords muss nur die numerischen Code-IDs enthalten.");
        builder.AppendLine("- codeMatches muss dieselben numerischen Code-IDs enthalten wie keywords.");
        builder.AppendLine("- Gib pro codeMatches-Eintrag nur id, sentiment und evidence zurueck.");
        builder.AppendLine("- evidence muss ein sehr kurzer Textausschnitt oder eine knappe Paraphrase aus genau dieser Aussage sein.");
        builder.AppendLine("- codeGroupSentiments muss leer bleiben; die Gruppensentiments werden nachgelagert berechnet.");
        builder.AppendLine("- Vergib einen Code nur, wenn die Aussage fachlich klar zu genau diesem Code passt.");
        builder.AppendLine("- Bevorzuge wenige praezise Codes gegenueber vielen unsicheren Codes.");
        builder.AppendLine("- Wenn kein Code sicher passt, gib leere Arrays fuer keywords, codeMatches und codeGroupSentiments zurueck.");
        builder.AppendLine("- Mitarbeiter = Verhalten, Freundlichkeit, Kompetenz oder Eingehen auf den Kunden.");
        builder.AppendLine("- Kundenbetreuung = Erreichbarkeit, Wartezeit, Antwortdauer, Kontinuitaet oder Anliegensloesung.");
        builder.AppendLine("- Praemien & Bezahlung nur bei Preis, Praemie, Rechnung, Zahlung, Mahnung oder Betreibung.");
        builder.AppendLine("- Produkt nur bei Angebot, Modell, Police, Offerte, Aufnahmebedingungen oder Leistungsumfang.");
        builder.AppendLine("- Leistungsbezug nur bei Rueckerstattung, Leistungsabrechnung, Leistungsentscheid oder Bezug zu Arzt, Therapeut, Medgate oder Medi24.");
        builder.AppendLine("- Digital nur bei Homepage, App, Portal, Usability oder technischem Problem.");
        builder.AppendLine("- Allgemeines Lob oder Tadel ohne Themenbezug nicht als konkreteren Fachcode codieren.");
    }

    private void AppendQualityV1AllowedCodeGroups(StringBuilder builder)
    {
        foreach (var group in _labelsByGroup)
        {
            builder.AppendLine($"- {group.Key}: Themen {Condense(string.Join(" | ", group.Value.Select(label => label.Code)), 260)}");

            var hint = string.Join(
                " ",
                group.Value
                    .Select(label => label.CodingRule)
                    .Where(rule => !string.IsNullOrWhiteSpace(rule))
                    .Take(2));
            if (!string.IsNullOrWhiteSpace(hint))
            {
                builder.AppendLine($"  Hinweise: {Condense(hint, 320)}");
            }

            var boundaryHint = GetQualityV1GroupBoundaryHint(group.Key);
            if (!string.IsNullOrWhiteSpace(boundaryHint))
            {
                builder.AppendLine($"  Nicht waehlen bei: {boundaryHint}");
            }
        }
    }

    private void AppendQualityV1AllowedCodes(StringBuilder builder, IEnumerable<string> groups)
    {
        foreach (var groupName in groups)
        {
            if (!_labelsByGroup.TryGetValue(groupName, out var labels))
            {
                continue;
            }

            builder.AppendLine($"Codegruppe: {groupName}");
            var boundaryHint = GetQualityV1GroupBoundaryHint(groupName);
            if (!string.IsNullOrWhiteSpace(boundaryHint))
            {
                builder.AppendLine($"- Abgrenzung: {boundaryHint}");
            }

            foreach (var label in labels)
            {
                builder.AppendLine($"- ID {label.Number}: {label.Code}");
                if (!string.IsNullOrWhiteSpace(label.CodingRule))
                {
                    builder.AppendLine($"  Codierregel: {Condense(label.CodingRule, _maxCodingRuleLength)}");
                }

                if (!string.IsNullOrWhiteSpace(label.ExampleText))
                {
                    builder.AppendLine($"  Beispiele: {Condense(label.ExampleText, _maxExampleLength)}");
                }
            }

            builder.AppendLine();
        }
    }

    private static string GetQualityV1GroupBoundaryHint(string groupName)
    {
        return groupName switch
        {
            "Mitarbeiter" => "rein organisatorische Themen wie Erreichbarkeit, Wartezeit oder Rueckerstattungsdauer ohne Aussage zum Verhalten des Mitarbeiters",
            "PrÃ¤mien & Bezahlung" => "allgemeine Zufriedenheit ohne klaren Bezug zu Preis, Praemie, Rechnung, Zahlung, Mahnung oder Betreibung",
            "Produkt" => "reines Serviceerlebnis ohne Bezug zu Angebot, Modell, Police, Offerte, Aufnahmebedingungen oder Leistungsumfang",
            "Kundenbetreuung" => "reine Freundlichkeit oder Kompetenz ohne Bezug zu Erreichbarkeit, Antwortdauer, Kontinuitaet oder geloestem Anliegen",
            "Leistungsbezug" => "Preis-, Produkt-, Service- oder Digitalthemen ohne Bezug zu Leistungsabrechnung, Rueckerstattung oder Leistungsentscheid",
            "Digital" => "allgemeiner Service ohne konkreten Homepage-, App-, Portal- oder Technikbezug",
            "Image" => "konkrete Einzelfallthemen ohne Aussage zum generellen Bild der Krankenversicherung",
            "Sonstiges" => "wenn ein spezifischerer Code aus einer anderen Codegruppe fachlich besser passt",
            "Allgemeines" => "wenn bereits ein konkreteres Thema eindeutig benannt ist",
            _ => string.Empty
        };
    }

    private string BuildQualityV2SinglePassSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Analysiere genau eine einzelne CSS-Kundenaussage.");
        builder.AppendLine("Arbeite semantisch: Die Aussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch formuliert sein.");
        builder.AppendLine("Waehle alle fachlich passenden Codes aus der CSS-Codeliste und trenne aehnliche Themen sorgfaeltig voneinander.");
        builder.AppendLine();
        AppendQualityV2SelectionRules(builder, restrictToSelectedGroups: false);
        builder.AppendLine();
        AppendFinalOutputSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Erlaubte CSS-Codegruppen und Codes:");
        AppendQualityV2AllowedCodes(builder, _labelsByGroup.Keys);
        return builder.ToString().Trim();
    }

    private string BuildQualityV2CodeGroupSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Erste Stufe: Bestimme nur die fachlich passenden CSS-Codegruppen fuer genau eine Kundenaussage.");
        builder.AppendLine("Arbeite semantisch. Die Aussage kann kurz, stichwortartig sowie auf Deutsch, Franzoesisch, Italienisch oder Englisch formuliert sein.");
        builder.AppendLine();
        builder.AppendLine("Wichtige Regeln:");
        builder.AppendLine("- Gib IMMER gueltiges JSON zurueck und keinerlei Text ausserhalb des JSON.");
        builder.AppendLine("- Verwende AUSSCHLIESSLICH Codegruppen aus der unten aufgefuehrten Liste.");
        builder.AppendLine("- Erfinde keine neuen Codegruppen.");
        builder.AppendLine("- Waehle hoechstens " + _maxCodeGroupsPerStatement + " Codegruppen.");
        builder.AppendLine("- Waehle eine Codegruppe, wenn das Thema fachlich erkennbar ist, auch bei kurzen oder telegrammartigen Aussagen.");
        builder.AppendLine("- Reines Lob oder Tadel ohne Themenbezug darf nicht automatisch zu Produkt, Digital, Leistungsbezug oder Praemien fuehren.");
        builder.AppendLine("- In dieser ersten Stufe bleiben keywords und codeMatches leer.");
        builder.AppendLine("- Wenn keine Codegruppe sicher passt, gib leere Arrays zurueck.");
        builder.AppendLine();
        AppendCodeGroupPreselectionSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Erlaubte CSS-Codegruppen:");
        AppendQualityV2AllowedCodeGroups(builder);
        return builder.ToString().Trim();
    }

    private string BuildQualityV2CodeSelectionSystemPrompt(IEnumerable<string> selectedGroups)
    {
        var groups = SelectKnownCodeGroups(selectedGroups);
        var builder = new StringBuilder();
        builder.AppendLine("Du bist ein spezialisiertes Klassifikationsmodell fuer offene Kundenaussagen einer Krankenversicherung.");
        builder.AppendLine();
        builder.AppendLine("Zweite Stufe: Waehle passende CSS-Codes nur aus den freigegebenen Codegruppen.");
        builder.AppendLine("Arbeite semantisch. Die Aussage kann auf Deutsch, Franzoesisch, Italienisch oder Englisch formuliert sein.");
        builder.AppendLine("Trenne aehnliche Themen sorgfaeltig und bevorzuge die fachlich treffendste Zuordnung.");
        builder.AppendLine();
        AppendQualityV2SelectionRules(builder, restrictToSelectedGroups: true);
        builder.AppendLine();
        AppendFinalOutputSchema(builder);
        builder.AppendLine();
        builder.AppendLine("Freigegebene CSS-Codegruppen und Codes:");
        AppendQualityV2AllowedCodes(builder, groups);
        return builder.ToString().Trim();
    }

    private void AppendQualityV2SelectionRules(StringBuilder builder, bool restrictToSelectedGroups)
    {
        builder.AppendLine("Wichtige Regeln:");
        builder.AppendLine("- Gib IMMER gueltiges JSON zurueck und keinerlei Text ausserhalb des JSON.");
        builder.AppendLine(restrictToSelectedGroups
            ? "- Verwende AUSSCHLIESSLICH Codes aus den freigegebenen Codegruppen."
            : "- Verwende AUSSCHLIESSLICH Codes aus der unten aufgefuehrten Liste.");
        builder.AppendLine("- Erfinde keine neuen Codes oder Codegruppen.");
        builder.AppendLine("- Eine Aussage kann mehrere Codes enthalten, wenn mehrere fachliche Themen wirklich angesprochen werden.");
        builder.AppendLine("- Waehle einen Code nicht nur wegen einzelner Woerter, sondern wegen der inhaltlichen Bedeutung der gesamten Aussage.");
        builder.AppendLine("- Wenn eine kurze Aussage fachlich klar ein Thema benennt, darfst du dieses Thema trotzdem codieren.");
        builder.AppendLine("- Bevorzuge die fachlich treffendste Zuordnung, aber sei nicht uebervorsichtig: klare Themen sollen codiert werden.");
        builder.AppendLine("- Mitarbeiter = Verhalten, Freundlichkeit, Kompetenz oder Eingehen auf den Kunden durch Personen.");
        builder.AppendLine("- Kundenbetreuung = Erreichbarkeit, Antwortdauer, Kontinuitaet, Weiterverbinden oder geloestes/nicht geloestes Anliegen.");
        builder.AppendLine("- Produkt = Angebot, Modell, Police, Offerte, Aufnahmebedingungen oder Leistungsumfang eines Produkts.");
        builder.AppendLine("- Praemien & Bezahlung = Preis, Praemie, Rechnung, Zahlung, Mahnung oder Betreibung.");
        builder.AppendLine("- Leistungsbezug = Rueckerstattung, Leistungsabrechnung, Leistungsentscheid oder Bezug zu Arzt, Therapeut, Medgate oder Medi24.");
        builder.AppendLine("- Digital = Homepage, App, Portal, Usability oder technische Probleme digitaler Kanaele.");
        builder.AppendLine("- Nutze den Kundenzufriedenheits-Score nur als Zusatzsignal fuer das Sentiment, nicht fuer die Themenauswahl.");
        builder.AppendLine("- keywords muss die gleiche Auswahl wie codeMatches enthalten. Jeder keywords-Eintrag nutzt dieselbe id und denselben Code-Text wie der passende codeMatches-Eintrag.");
        builder.AppendLine("- codeGroupSentiments darf nur Codegruppen enthalten, in denen mindestens ein Code gematcht wurde.");
        builder.AppendLine("- Wenn in derselben Codegruppe sowohl positive als auch negative Aspekte vorkommen, setze das Sentiment der Codegruppe auf Neutral.");
        builder.AppendLine("- Wenn eine Aussage fuer einen Code bzw. eine Codegruppe rein beschreibend und nicht klar wertend ist, setze das Sentiment auf Neutral.");
        builder.AppendLine("- Wenn kein Code sicher passt, gib leere Arrays fuer keywords, codeMatches und codeGroupSentiments zurueck.");
    }

    private void AppendQualityV2AllowedCodeGroups(StringBuilder builder)
    {
        foreach (var group in _labelsByGroup)
        {
            builder.AppendLine($"- {group.Key}: Themen {Condense(string.Join(" | ", group.Value.Select(label => NormalizeWhitespace(label.Code)).Distinct(StringComparer.OrdinalIgnoreCase)), 240)}");

            var hints = group.Value
                .Select(label => NormalizeWhitespace(label.CodingRule))
                .Where(rule => !string.IsNullOrWhiteSpace(rule))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();
            if (hints.Count > 0)
            {
                builder.AppendLine($"  Wann waehlen: {Condense(string.Join(" ", hints), Math.Min(_maxCodingRuleLength, 240))}");
            }

            var example = group.Value
                .Select(label => NormalizeWhitespace(label.ExampleText))
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            if (!string.IsNullOrWhiteSpace(example))
            {
                builder.AppendLine($"  Beispiel: {Condense(example, Math.Min(_maxExampleLength, 140))}");
            }

            var boundaryHint = GetQualityV2GroupBoundaryHint(group.Key);
            if (!string.IsNullOrWhiteSpace(boundaryHint))
            {
                builder.AppendLine($"  Nicht waehlen bei: {boundaryHint}");
            }
        }
    }

    private void AppendQualityV2AllowedCodes(StringBuilder builder, IEnumerable<string> groups)
    {
        foreach (var groupName in groups)
        {
            if (!_labelsByGroup.TryGetValue(groupName, out var labels))
            {
                continue;
            }

            builder.AppendLine($"Codegruppe: {groupName}");
            var boundaryHint = GetQualityV2GroupBoundaryHint(groupName);
            if (!string.IsNullOrWhiteSpace(boundaryHint))
            {
                builder.AppendLine($"- Abgrenzung: {boundaryHint}");
            }

            foreach (var label in labels)
            {
                builder.AppendLine($"- ID {label.Number}: {label.Code}");
                if (!string.IsNullOrWhiteSpace(label.CodingRule))
                {
                    builder.AppendLine($"  Wann waehlen: {Condense(label.CodingRule, Math.Min(_maxCodingRuleLength, 240))}");
                }

                if (!string.IsNullOrWhiteSpace(label.ExampleText))
                {
                    builder.AppendLine($"  Beispiel: {Condense(label.ExampleText, Math.Min(_maxExampleLength, 140))}");
                }
            }

            builder.AppendLine();
        }
    }

    private static string GetQualityV2GroupBoundaryHint(string groupName)
    {
        return groupName switch
        {
            "Mitarbeiter" => "wenn es primaer um Erreichbarkeit, Weiterverbinden, Antwortdauer oder Prozessgeschwindigkeit geht und nicht um das Verhalten einer Person",
            "Prämien & Bezahlung" => "wenn keine Aussage zu Preis, Praemie, Rechnung, Zahlung, Mahnung oder Betreibung vorliegt",
            "Produkt" => "wenn primaer Service, App, Portal oder allgemeine Zufriedenheit beschrieben wird statt eines Versicherungsangebots oder Produkts",
            "Kundenbetreuung" => "wenn vor allem Freundlichkeit, Kompetenz oder Engagement eines Mitarbeiters bewertet wird statt Erreichbarkeit oder Loesungsprozess",
            "Leistungsbezug" => "wenn kein Bezug zu Rueckerstattung, Leistungsabrechnung, Leistungsentscheid oder Leistungserbringern vorhanden ist",
            "Digital" => "wenn eher Produkt, Preis oder allgemeiner Service gemeint ist und kein digitaler Kanal erkennbar ist",
            "Image" => "wenn nur ein konkreter Einzelfall genannt wird, ohne Aussage zum generellen Bild der Krankenversicherung",
            "Sonstiges" => "wenn ein spezifischerer Code aus einer anderen Codegruppe fachlich besser passt",
            "Allgemeines" => "wenn bereits ein konkreteres Thema eindeutig benannt ist",
            _ => string.Empty
        };
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

    private string BuildStatementPrompt(string intro, string statementText, int? customerSatisfactionScore)
    {
        var builder = new StringBuilder();
        builder.AppendLine(intro);
        AppendCustomerSatisfactionContext(builder, customerSatisfactionScore);
        builder.AppendLine();
        builder.AppendLine("Antworte ausschliesslich mit dem geforderten JSON.");
        builder.AppendLine();
        builder.AppendLine("Kundenaussage:");
        builder.Append('"').Append(statementText).AppendLine("\"");
        return builder.ToString().Trim();
    }

    private void AppendCustomerSatisfactionContext(StringBuilder builder, int? customerSatisfactionScore)
    {
        if (!customerSatisfactionScore.HasValue || customerSatisfactionScore.Value is < 1 or > 10)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("Zusaetzlicher Kontext:");
        if (IsCompactPromptVariantV2)
        {
            builder.AppendLine($"- Zufriedenheitsscore 1-10: {customerSatisfactionScore.Value}");
            builder.AppendLine("- Nur Nebensignal fuer das Sentiment; bei Widerspruch hat der Text Vorrang.");
            builder.AppendLine("- Bei mehrdeutigen Nennungen: 1-4 negativ, 5-7 neutral, 8-10 positiv.");
            return;
        }

        builder.AppendLine($"- Kundenzufriedenheit auf einer Skala von 1 bis 10: {customerSatisfactionScore.Value}");
        builder.AppendLine("- Nutze diesen Wert nur als unterstuetzendes Signal fuer das Sentiment.");
        builder.AppendLine("- Die Auswahl von Codegruppen und Codes muss primaer aus dem Text folgen.");
        builder.AppendLine("- Falls Text und Zufriedenheitsscore widerspruechlich sind, hat der Text Vorrang.");
        builder.AppendLine("- Bei mehrdeutigen Nennungen: 1-4 eher negativ, 5-7 eher neutral oder ambivalent, 8-10 eher positiv.");
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

    private bool IsCompactPromptVariantV1 =>
        string.Equals(_promptVariant, CompactPromptVariantV1, StringComparison.OrdinalIgnoreCase);

    private bool IsCompactPromptVariantV2 =>
        string.Equals(_promptVariant, CompactPromptVariantV2, StringComparison.OrdinalIgnoreCase);

    private bool IsQualityPromptVariantV1 =>
        string.Equals(_promptVariant, QualityPromptVariantV1, StringComparison.OrdinalIgnoreCase);

    private bool IsQualityPromptVariantV2 =>
        string.Equals(_promptVariant, QualityPromptVariantV2, StringComparison.OrdinalIgnoreCase);

    private bool UsesCompactPromptVariant => IsCompactPromptVariantV1 || IsCompactPromptVariantV2;

    private static int GetDefaultCodingRuleLength(string promptVariant)
    {
        if (string.Equals(promptVariant, QualityPromptVariantV1, StringComparison.OrdinalIgnoreCase))
        {
            return 320;
        }

        if (string.Equals(promptVariant, QualityPromptVariantV2, StringComparison.OrdinalIgnoreCase))
        {
            return 240;
        }

        if (string.Equals(promptVariant, CompactPromptVariantV2, StringComparison.OrdinalIgnoreCase))
        {
            return 110;
        }

        return string.Equals(promptVariant, CompactPromptVariantV1, StringComparison.OrdinalIgnoreCase)
            ? 140
            : 280;
    }

    private static int GetDefaultExampleLength(string promptVariant)
    {
        if (string.Equals(promptVariant, QualityPromptVariantV1, StringComparison.OrdinalIgnoreCase))
        {
            return 220;
        }

        if (string.Equals(promptVariant, QualityPromptVariantV2, StringComparison.OrdinalIgnoreCase))
        {
            return 140;
        }

        if (string.Equals(promptVariant, CompactPromptVariantV2, StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        return string.Equals(promptVariant, CompactPromptVariantV1, StringComparison.OrdinalIgnoreCase)
            ? 80
            : 180;
    }

    private static string NormalizePromptVariant(string? rawPromptVariant)
    {
        if (string.Equals(rawPromptVariant?.Trim(), QualityPromptVariantV1, StringComparison.OrdinalIgnoreCase))
        {
            return QualityPromptVariantV1;
        }

        if (string.Equals(rawPromptVariant?.Trim(), QualityPromptVariantV2, StringComparison.OrdinalIgnoreCase))
        {
            return QualityPromptVariantV2;
        }

        if (string.Equals(rawPromptVariant?.Trim(), CompactPromptVariantV2, StringComparison.OrdinalIgnoreCase))
        {
            return CompactPromptVariantV2;
        }

        return string.Equals(rawPromptVariant?.Trim(), CompactPromptVariantV1, StringComparison.OrdinalIgnoreCase)
            ? CompactPromptVariantV1
            : BaselinePromptVariant;
    }

    private static string NormalizeMode(string? rawMode)
    {
        if (string.Equals(rawMode?.Trim(), SinglePassMode, StringComparison.OrdinalIgnoreCase))
        {
            return SinglePassMode;
        }

        if (string.Equals(rawMode?.Trim(), CandidateRerankMode, StringComparison.OrdinalIgnoreCase))
        {
            return CandidateRerankMode;
        }

        return TwoStageMode;
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






