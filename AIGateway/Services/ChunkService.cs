using AiGateway.Api.Configuration;
using AiGateway.Api.Models.Internal;
using AiGateway.Api.Models.Requests;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Services;

public class ChunkService : IChunkService
{
    private readonly GatewayOptions _options;
    private readonly ILogger<ChunkService> _logger;

    public ChunkService(IOptions<GatewayOptions> options, ILogger<ChunkService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Teilt eine Liste von Kundenaussagen in Token-begrenzte Chunks.
    /// Jeder Chunk bleibt unter MaxTokensPerChunk.
    /// </summary>
    public List<Chunk> Split(List<CustomerStatement> statements)
    {
        var chunks = new List<Chunk>();
        var currentChunk = new Chunk { Index = 0 };
        int currentTokens = 0;

        // Overhead pro Statement: ~30 Tokens für JSON-Struktur, ID, etc.
        const int overheadPerStatement = 30;
        // System-Prompt Overhead: reservieren wir pauschal
        const int systemPromptReserve = 300;

        int availableTokens = _options.MaxTokensPerChunk - systemPromptReserve;

        foreach (var statement in statements)
        {
            int statementTokens = EstimateTokens(statement.Text) + overheadPerStatement;

            // Passt diese Aussage noch in den aktuellen Chunk?
            if (currentTokens + statementTokens > availableTokens && currentChunk.Statements.Any())
            {
                // Aktuellen Chunk abschließen
                currentChunk.EstimatedTokens = currentTokens;
                chunks.Add(currentChunk);

                // Neuen Chunk beginnen
                currentChunk = new Chunk { Index = chunks.Count };
                currentTokens = 0;
            }

            // Einzelne Aussage ist größer als ein ganzer Chunk?
            if (statementTokens > availableTokens)
            {
                _logger.LogWarning(
                    "Aussage ID {Id} mit ~{Tokens} Tokens überschreitet Chunk-Limit ({Limit}). " +
                    "Text wird gekürzt.",
                    statement.Id, statementTokens, availableTokens);

                // Text kürzen auf erlaubte Länge
                int maxChars = (availableTokens - overheadPerStatement) * _options.CharsPerToken;
                statement.Text = statement.Text[..Math.Min(statement.Text.Length, maxChars)]
                    + " [GEKÜRZT]";
                statementTokens = availableTokens;
            }

            currentChunk.Statements.Add(statement);
            currentTokens += statementTokens;
        }

        // Letzten Chunk hinzufügen
        if (currentChunk.Statements.Any())
        {
            currentChunk.EstimatedTokens = currentTokens;
            chunks.Add(currentChunk);
        }

        _logger.LogInformation(
            "Chunking: {Total} Aussagen → {Chunks} Chunks (max ~{MaxTokens} Tokens/Chunk)",
            statements.Count, chunks.Count, availableTokens);

        foreach (var chunk in chunks)
        {
            _logger.LogDebug(
                "  Chunk {Index}: {Count} Aussagen, ~{Tokens} Tokens",
                chunk.Index, chunk.Statements.Count, chunk.EstimatedTokens);
        }

        return chunks;
    }

    /// <summary>
    /// Grobe Token-Schätzung basierend auf Zeichenanzahl.
    /// Deutsch hat ~3-4 Zeichen pro Token.
    /// </summary>
    private int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling((double)text.Length / _options.CharsPerToken);
    }
}
