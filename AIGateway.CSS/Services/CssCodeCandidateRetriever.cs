using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AiGateway.CSS.Api.Models;

namespace AiGateway.CSS.Api.Services;

public sealed class CssCodeCandidateRetriever
{
    private static readonly Regex TokenRegex = new("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly List<CandidateProfile> _profiles;
    private readonly Dictionary<string, int> _documentFrequency;
    private readonly int _initialCandidateCount;
    private readonly int _candidateLimit;
    private readonly ILogger<CssCodeCandidateRetriever> _logger;

    public CssCodeCandidateRetriever(
        CssCodebookPromptService promptService,
        IConfiguration config,
        ILogger<CssCodeCandidateRetriever> logger)
    {
        _logger = logger;
        _initialCandidateCount = Math.Clamp(
            config.GetValue<int?>("CssCodebook:CandidateInitialCount") ?? 6,
            3,
            15);
        _candidateLimit = Math.Clamp(
            config.GetValue<int?>("CssCodebook:CandidateLimit") ?? 10,
            _initialCandidateCount,
            20);

        _profiles = promptService.GetCodebookLabels()
            .Where(label => label.Number > 0)
            .Select(BuildProfile)
            .OrderBy(profile => profile.Label.Number)
            .ToList();

        _documentFrequency = _profiles
            .SelectMany(profile => profile.Tokens.Distinct())
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        _logger.LogInformation(
            "CSS Candidate Retriever bereit: {Codes} Codes | Initial={Initial} | Limit={Limit}",
            _profiles.Count,
            _initialCandidateCount,
            _candidateLimit);
    }

    public IReadOnlyList<CssCodeCandidate> Retrieve(string statementText)
    {
        var normalizedStatement = Normalize(statementText);
        var statementTokens = Tokenize(normalizedStatement).Distinct(StringComparer.Ordinal).ToList();

        var scored = _profiles
            .Select(profile => Score(profile, normalizedStatement, statementTokens))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Profile.Label.Number)
            .ToList();

        var selected = scored
            .Take(_initialCandidateCount)
            .ToDictionary(
                candidate => candidate.Profile.Label.Number,
                candidate => new MutableCandidate(candidate.Profile, candidate.Score, candidate.MatchedTerms),
                EqualityComparer<int>.Default);

        if (normalizedStatement.Length <= 1)
        {
            var placeholder = scored.First(candidate => candidate.Profile.Label.Number == 34);
            selected[34] = new MutableCandidate(
                placeholder.Profile,
                Math.Max(100.0, placeholder.Score),
                new[] { "placeholder" });
        }

        foreach (var source in scored.Take(Math.Min(4, _initialCandidateCount)))
        {
            foreach (var relatedId in CssCandidateKnowledgeBase.GetConfusableCodeIds(source.Profile.Label.Number))
            {
                var related = scored.FirstOrDefault(candidate => candidate.Profile.Label.Number == relatedId);
                if (related == null)
                {
                    continue;
                }

                var expandedScore = Math.Max(related.Score, Math.Max(0.01, source.Score * 0.72));
                if (selected.TryGetValue(relatedId, out var existing))
                {
                    existing.Score = Math.Max(existing.Score, expandedScore);
                    if (!existing.ExpandedFromCodeIds.Contains(source.Profile.Label.Number))
                    {
                        existing.ExpandedFromCodeIds.Add(source.Profile.Label.Number);
                    }
                    continue;
                }

                selected[relatedId] = new MutableCandidate(related.Profile, expandedScore, related.MatchedTerms)
                {
                    ExpandedFromCodeIds = { source.Profile.Label.Number }
                };
            }
        }

        var result = selected.Values
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Profile.Label.Number)
            .Take(_candidateLimit)
            .Select(ToCandidate)
            .ToList();

        _logger.LogInformation(
            "CSS Code-Kandidaten fuer '{Preview}': {Candidates}",
            statementText[..Math.Min(100, statementText.Length)],
            string.Join(
                " | ",
                result.Select(candidate =>
                    $"{candidate.Id}:{candidate.Score:F2}" +
                    (candidate.ExpandedFromCodeIds.Count > 0
                        ? $"<-{string.Join(',', candidate.ExpandedFromCodeIds)}"
                        : string.Empty))));

        return result;
    }

    private ScoredCandidate Score(
        CandidateProfile profile,
        string normalizedStatement,
        IReadOnlyList<string> statementTokens)
    {
        var score = 0.0;
        var matchedTerms = new HashSet<string>(StringComparer.Ordinal);

        foreach (var alias in profile.NormalizedAliases)
        {
            if (alias.Length < 2 || !ContainsPhrase(normalizedStatement, alias))
            {
                continue;
            }

            var aliasTokenCount = Tokenize(alias).Count;
            score += 9.0 + Math.Min(5, aliasTokenCount * 1.25);
            matchedTerms.Add(alias);
        }

        foreach (var token in statementTokens)
        {
            if (profile.Tokens.Contains(token))
            {
                score += 2.5 * InverseDocumentFrequency(token);
                matchedTerms.Add(token);
                continue;
            }

            if (token.Length < 5)
            {
                continue;
            }

            var bestSimilarity = profile.Tokens
                .Where(candidateToken => candidateToken.Length >= 4)
                .Select(candidateToken => DiceCoefficient(token, candidateToken))
                .DefaultIfEmpty(0)
                .Max();

            if (bestSimilarity >= 0.72)
            {
                score += bestSimilarity * 1.35 * InverseDocumentFrequency(token);
                matchedTerms.Add(token);
            }
        }

        if (normalizedStatement.Length >= 4 && profile.NormalizedLabel.Contains(normalizedStatement, StringComparison.Ordinal))
        {
            score += 5.0;
        }

        return new ScoredCandidate(profile, score, matchedTerms.OrderBy(term => term).ToList());
    }

    private double InverseDocumentFrequency(string token)
    {
        var frequency = _documentFrequency.GetValueOrDefault(token, 0);
        return Math.Log((_profiles.Count + 1.0) / (frequency + 1.0)) + 1.0;
    }

    private static CandidateProfile BuildProfile(Models.CssCodebookLabel label)
    {
        var aliases = CssCandidateKnowledgeBase.GetAliases(label.Number)
            .Select(Normalize)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var document = string.Join(
            ' ',
            new[]
            {
                label.Code,
                label.CodeGroup,
                label.CodingRule,
                label.ExampleText,
                string.Join(' ', aliases),
                CssCandidateKnowledgeBase.GetBoundaryHint(label.Number)
            });

        return new CandidateProfile(
            label,
            Normalize(label.Code),
            aliases,
            Tokenize(Normalize(document)).ToHashSet(StringComparer.Ordinal));
    }

    private static CssCodeCandidate ToCandidate(MutableCandidate candidate)
    {
        return new CssCodeCandidate
        {
            Id = candidate.Profile.Label.Number,
            CodeGroup = candidate.Profile.Label.CodeGroup,
            Code = candidate.Profile.Label.Code,
            CodingRule = candidate.Profile.Label.CodingRule,
            ExampleText = candidate.Profile.Label.ExampleText,
            Score = Math.Round(candidate.Score, 4),
            MatchedTerms = candidate.MatchedTerms.ToList(),
            ExpandedFromCodeIds = candidate.ExpandedFromCodeIds.Distinct().OrderBy(id => id).ToList()
        };
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        if (text.Equals(phrase, StringComparison.Ordinal))
        {
            return true;
        }

        return $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);
    }

    private static List<string> Tokenize(string text) =>
        TokenRegex.Matches(text).Select(match => match.Value).Where(token => token.Length >= 2).ToList();

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal);

        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return Regex.Replace(builder.ToString(), "\\s+", " ").Trim();
    }

    private static double DiceCoefficient(string left, string right)
    {
        if (left.Equals(right, StringComparison.Ordinal))
        {
            return 1.0;
        }

        var leftPairs = CharacterPairs(left);
        var rightPairs = CharacterPairs(right);
        if (leftPairs.Count == 0 || rightPairs.Count == 0)
        {
            return 0;
        }

        var intersection = leftPairs.Intersect(rightPairs, StringComparer.Ordinal).Count();
        return 2.0 * intersection / (leftPairs.Count + rightPairs.Count);
    }

    private static HashSet<string> CharacterPairs(string value)
    {
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < value.Length - 1; index++)
        {
            pairs.Add(value.Substring(index, 2));
        }

        return pairs;
    }

    private sealed record CandidateProfile(
        Models.CssCodebookLabel Label,
        string NormalizedLabel,
        List<string> NormalizedAliases,
        HashSet<string> Tokens);

    private sealed record ScoredCandidate(
        CandidateProfile Profile,
        double Score,
        List<string> MatchedTerms);

    private sealed class MutableCandidate
    {
        public MutableCandidate(CandidateProfile profile, double score, IEnumerable<string> matchedTerms)
        {
            Profile = profile;
            Score = score;
            MatchedTerms = matchedTerms.ToList();
        }

        public CandidateProfile Profile { get; }
        public double Score { get; set; }
        public List<string> MatchedTerms { get; }
        public List<int> ExpandedFromCodeIds { get; } = new();
    }
}
