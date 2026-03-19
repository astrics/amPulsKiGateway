using AiGateway.Api.Configuration;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Middleware;

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

        // Health-Endpoint ohne Key erreichbar
        if (path.Contains("/api/health"))
        {
            await _next(context);
            return;
        }

        // API-Key prüfen
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey))
        {
            _logger.LogWarning("Request ohne API-Key von {IP}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "API-Key fehlt (Header: X-Api-Key)" });
            return;
        }

        if (!_options.ApiKeys.Contains(providedKey.ToString()))
        {
            _logger.LogWarning("Ungültiger API-Key von {IP}: {Key}",
                context.Connection.RemoteIpAddress, providedKey.ToString()[..4] + "...");
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Ungültiger API-Key " });
            return;
        }

        await _next(context);
    }
}
