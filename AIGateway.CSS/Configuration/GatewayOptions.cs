namespace AiGateway.CSS.Api.Configuration;

/// <summary>
/// Zentrale Konfiguration des Gateways.
/// ══════════════════════════════════════════════════════════
/// ANPASSEN: Diese Werte in appsettings.json konfigurieren!
/// ══════════════════════════════════════════════════════════
/// </summary>
public class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>
    /// URL von LM Studio (Standard: http://localhost:1234)
    /// ANPASSEN wenn LM Studio auf anderem Port läuft
    /// </summary>
    public string LmStudioBaseUrl { get; set; } = "http://localhost:1234";

    /// <summary>
    /// Modellname in LM Studio
    /// ANPASSEN auf dein geladenes Modell
    /// </summary>
    public string ModelName { get; set; } = "local-model";

    /// <summary>
    /// Maximale parallele Anfragen an LM Studio
    /// Bei CPU-only: 1 empfohlen, maximal 2
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// Maximale Tokens pro Chunk (Input)
    /// ANPASSEN je nach Modell Context Window
    /// 7B Modelle: meist 4096 total → ~3000 für Input
    /// </summary>
    public int MaxTokensPerChunk { get; set; } = 3000;

    /// <summary>
    /// Maximale Tokens für die Antwort
    /// </summary>
    public int MaxResponseTokens { get; set; } = 2000;

    /// <summary>
    /// Temperature (0 = deterministisch, gut für Caching)
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// API-Keys die Zugriff haben
    /// ANPASSEN: Eigene sichere Keys eintragen!
    /// </summary>
    public List<string> ApiKeys { get; set; } = new() { "test-key-12345" };

    /// <summary>
    /// Cache-Dauer in Minuten
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 60;

    /// <summary>
    /// Timeout für einen einzelnen LM Studio Request in Sekunden
    /// CPU-Inferenz kann lange dauern!
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 3000;

    /// <summary>
    /// Geschätzte Zeichen pro Token (Deutsch ≈ 3-4)
    /// </summary>
    public int CharsPerToken { get; set; } = 4;

    /// <summary>
    /// Maximale Queue-Größe (verhindert Memory-Overflow)
    /// </summary>
    public int MaxQueueSize { get; set; } = 100;
}


