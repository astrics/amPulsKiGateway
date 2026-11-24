using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using AiGateway.CSS.Api.Services;

namespace AiGateway.CSS.Api.Controllers;

[ApiController]
[Route("api/analysis/preflight")]
public class PreflightController(IHttpClientFactory clients, IConfiguration config,
    LmStudioConcurrencyGate gate) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Check(CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        var settings = new Dictionary<string, JsonElement>();
        var model = config["Gateway:ModelName"];
        using var client = clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);
        var url = (config["Gateway:LmStudioBaseUrl"] ?? "http://127.0.0.1:1234").TrimEnd('/');
        try
        {
            using var lease = await gate.EnterAsync(cancellationToken);
            using var response = await client.GetAsync(url + "/api/v1/models", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var models = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var loaded = models.RootElement.GetProperty("models").EnumerateArray()
                    .Where(item => item.TryGetProperty("loaded_instances", out _))
                    .SelectMany(item => item.GetProperty("loaded_instances").EnumerateArray())
                    .Where(item => item.GetProperty("id").GetString() == model).ToList();
                if (loaded.Count != 1)
                    problems.Add("Das konfigurierte Gemma-Modell muss genau einmal geladen sein.");
                else
                {
                    foreach (var property in loaded[0].GetProperty("config").EnumerateObject())
                        settings[property.Name] = property.Value.Clone();
                    foreach (var (name, expected) in new[] { ("context_length", 16384), ("parallel", 1) })
                    {
                        if (!settings.TryGetValue(name, out var value))
                            warnings.Add(name + " konnte nicht automatisch gelesen werden; in LM Studio prüfen.");
                        else if (!value.TryGetInt32(out var actual) || actual != expected)
                            problems.Add(name + " muss " + expected + " sein.");
                    }
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                warnings.Add("Diese LM-Studio-Version liefert keine Ladeparameter. Kontext 16384 und Parallelität 1 manuell prüfen.");
            else
                problems.Add("LM-Studio-Einstellungen nicht erreichbar: HTTP " + (int)response.StatusCode);

            if (problems.Count == 0)
            {
                var payload = JsonSerializer.Serialize(new
                {
                    model, temperature = 0, max_tokens = 128, stream = false,
                    messages = new[] {
                        new { role = "system", content = "Antworte ausschliesslich mit dem JSON-Objekt {\"ok\":true}." },
                        new { role = "user", content = "Gib jetzt das geforderte JSON aus." }
                    }
                });
                using var body = new StringContent(payload, Encoding.UTF8, "application/json");
                using var completion = await client.PostAsync(url + "/v1/chat/completions", body, cancellationToken);
                completion.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await completion.Content.ReadAsStringAsync(cancellationToken));
                var choice = document.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");
                var content = message.GetProperty("content").GetString() ?? "";
                if (message.TryGetProperty("reasoning_content", out var reasoning)
                    && reasoning.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(reasoning.GetString()))
                    problems.Add("Thinking ist aktiv. Im Gemma-Template enable_thinking = false setzen und das Modell neu laden.");
                if (choice.GetProperty("finish_reason").GetString() == "length")
                    problems.Add("Testantwort hat das Tokenlimit erreicht.");
                try
                {
                    using var json = JsonDocument.Parse(content);
                    if (!json.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                        problems.Add("Testantwort enthält keine gültige Bestätigung.");
                }
                catch (JsonException)
                {
                    problems.Add("Testantwort ist kein reines JSON. Chat-Template und Thinking prüfen.");
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException
                                         or KeyNotFoundException or InvalidOperationException)
        {
            problems.Add("LM-Studio-Vorprüfung fehlgeschlagen: " + exception.GetType().Name);
        }
        return Ok(new { passed = problems.Count == 0, model, settings, problems, warnings });
    }
}
