using AiGateway.CSS.Api.Configuration;
using Microsoft.Extensions.Options;

namespace AiGateway.CSS.Api.Middleware;

/// <summary>
/// Prüft den API-Key im Header "X-Api-Key"
/// Health-Endpoint ist ausgenommen
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GatewayOptions _options;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IOptions<GatewayOptions> options,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";
        var method = context.Request.Method;
        var ip = context.Connection.RemoteIpAddress;

        // Health-Endpoint ohne Key erreichbar
        if (path.Contains("/api/health"))
        {
            _logger.LogTrace("Health-Check von {IP} - Auth übersprungen", ip);
            await _next(context);
            return;
        }

        // API-Key prüfen
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey))
        {
            _logger.LogWarning("❌ Auth fehlgeschlagen: Kein API-Key | {Method} {Path} | IP: {IP}",
                method, path, ip);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "API-Key fehlt (Header: X-Api-Key)" });
            return;
        }

        var keyPreview = providedKey.ToString().Length >= 4 
            ? providedKey.ToString()[..4] + "***" 
            : "***";

        if (!_options.ApiKeys.Contains(providedKey.ToString()))
        {
            _logger.LogWarning("❌ Auth fehlgeschlagen: Ungültiger Key {KeyPreview} | {Method} {Path} | IP: {IP}",
                keyPreview, method, path, ip);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Ungültiger API-Key" });
            return;
        }

        _logger.LogDebug("✅ Auth erfolgreich: {KeyPreview} | {Method} {Path}", 
            keyPreview, method, path);

        await _next(context);
    }
}


