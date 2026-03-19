namespace AiGateway.Api.Models.Requests;

/// <summary>
/// Eingehende Analyse-Anfrage von PHP
/// </summary>
public class AnalysisRequest
{
    /// <summary>
    /// Quelle der Anfrage (Dashboard-Name)
    /// Hilft beim Logging und Priorisierung
    /// </summary>
    public string Source { get; set; } = "unknown";

    /// <summary>
    /// Art der Analyse
    /// Unterstützt: "sentiment", "category", "summary", "custom"
    /// ANPASSEN: Weitere Typen in PromptBuilder hinzufügen
    /// </summary>
    public string AnalysisType { get; set; } = "sentiment";

    /// <summary>
    /// Priorität: 1 (höchste) bis 10 (niedrigste)
    /// </summary>
    public int Priority { get; set; } = 5;

    /// <summary>
    /// Die zu analysierenden Kundenaussagen
    /// </summary>
    public List<CustomerStatement> Statements { get; set; } = new();

    /// <summary>
    /// Optionaler Custom-Prompt (bei AnalysisType = "custom")
    /// ANPASSEN: Eigene Analyse-Szenarien
    /// </summary>
    public string? CustomPrompt { get; set; }

    /// <summary>
    /// Optional: Webhook-URL für Callback wenn fertig
    /// </summary>
    public string? CallbackUrl { get; set; }
}

public class CustomerStatement
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Timestamp { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
