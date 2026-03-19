using System.Text;
using System.Text.Json;
using AiGateway.Api.Models.Internal;

namespace AiGateway.Api.Services;

/// <summary>
/// Baut die Prompts für verschiedene Analysetypen.
/// ═══════════════════════════════════════════════════════
/// ANPASSEN: Hier die Prompts für eure Anwendungsfälle
/// definieren! Das ist das Herzstück der Analyse-Qualität.
/// ═══════════════════════════════════════════════════════
/// </summary>
public class PromptBuilder : IPromptBuilder
{
    /// <summary>
    /// System-Prompt definiert die Rolle der KI.
    /// ANPASSEN: Eigene Analysetypen hinzufügen!
    /// </summary>
    public string BuildSystemPrompt(string analysisType)
    {
        return analysisType.ToLower() switch
        {
            "sentiment" => @"Du bist ein Sentiment-Analyse-Experte für deutschsprachige Kundenaussagen.
Analysiere jede Aussage und antworte AUSSCHLIESSLICH im folgenden JSON-Format:
{
  ""results"": [
    {
      ""id"": <ID der Aussage>,
      ""sentiment"": ""positiv"" | ""negativ"" | ""neutral"" | ""gemischt"",
      ""confidence"": <0.0 bis 1.0>,
      ""keywords"": [""keyword1"", ""keyword2""],
      ""summary"": ""Kurze Zusammenfassung in einem Satz""
    }
  ]
}
Antworte NUR mit dem JSON. Kein zusätzlicher Text.",

            "category" => @"Du bist ein Kategorisierungs-Experte für deutschsprachige Kundenaussagen.
Ordne jede Aussage einer oder mehreren Kategorien zu.
Mögliche Kategorien: Beschwerde, Lob, Anfrage, Reklamation, Kündigung, Feedback, Verbesserungsvorschlag, Sonstiges.
Antworte AUSSCHLIESSLICH im folgenden JSON-Format:
{
  ""results"": [
    {
      ""id"": <ID der Aussage>,
      ""categories"": [""Kategorie1"", ""Kategorie2""],
      ""primaryCategory"": ""Hauptkategorie"",
      ""urgency"": ""hoch"" | ""mittel"" | ""niedrig"",
      ""summary"": ""Kurze Zusammenfassung""
    }
  ]
}
Antworte NUR mit dem JSON. Kein zusätzlicher Text.",

            "summary" => @"Du bist ein Zusammenfassungs-Experte für deutschsprachige Kundenaussagen.
Erstelle für jede Aussage eine knappe Zusammenfassung und extrahiere die Kernthemen.
Antworte AUSSCHLIESSLICH im folgenden JSON-Format:
{
  ""results"": [
    {
      ""id"": <ID der Aussage>,
      ""summary"": ""Zusammenfassung in 1-2 Sätzen"",
      ""topics"": [""Thema1"", ""Thema2""],
      ""actionRequired"": true | false,
      ""suggestedAction"": ""Empfohlene Maßnahme oder null""
    }
  ]
}
Antworte NUR mit dem JSON. Kein zusätzlicher Text.",

            // ═══════════════════════════════════════
            // ANPASSEN: Eigene Analyse-Typen hier!
            // ═══════════════════════════════════════
            // "churn_risk" => @"...",
            // "product_feedback" => @"...",
            // "nps_analysis" => @"...",

            "custom" => @"Du bist ein Analyse-Experte für deutschsprachige Kundenaussagen.
Antworte immer im JSON-Format mit einem ""results""-Array.",

            _ => @"Du bist ein Analyse-Experte für deutschsprachige Kundenaussagen.
Analysiere die Aussagen und antworte im JSON-Format mit einem ""results""-Array."
        };
    }

    /// <summary>
    /// User-Prompt mit den eigentlichen Daten.
    /// </summary>
    public string BuildUserPrompt(string analysisType, Chunk chunk, string? customPrompt)
    {
        var sb = new StringBuilder();

        // Bei Custom-Prompt diesen voranstellen
        if (analysisType.ToLower() == "custom" && !string.IsNullOrEmpty(customPrompt))
        {
            sb.AppendLine(customPrompt);
            sb.AppendLine();
        }

        sb.AppendLine($"Analysiere die folgenden {chunk.Statements.Count} Kundenaussagen:");
        sb.AppendLine();

        foreach (var statement in chunk.Statements)
        {
            sb.AppendLine($"[ID: {statement.Id}]");
            sb.AppendLine($"\"{statement.Text}\"");

            if (!string.IsNullOrEmpty(statement.Timestamp))
            {
                sb.AppendLine($"(Zeitpunkt: {statement.Timestamp})");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
