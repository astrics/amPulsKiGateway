using AiGateway.Api.Configuration;
using AiGateway.Api.Middleware;
using AiGateway.Api.Services;
using AiGateway.Api.Workers;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// ── Configuration ──────────────────────────────────
builder.Services.Configure<GatewayOptions>(
    builder.Configuration.GetSection(GatewayOptions.SectionName));

// ── Services registrieren ──────────────────────────
// Queue als Singleton (muss über gesamte App-Laufzeit bestehen)
builder.Services.AddSingleton<QueueService>();
builder.Services.AddSingleton<IQueueService>(sp => sp.GetRequiredService<QueueService>());

builder.Services.AddSingleton<IChunkService, ChunkService>();
builder.Services.AddSingleton<IPromptBuilder, PromptBuilder>();
builder.Services.AddSingleton<ICacheService, CacheService>();

// HttpClient für LM Studio
builder.Services.AddHttpClient<ILmStudioClient, LmStudioClient>();

// Memory Cache
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100_000_000; // ~100MB Cache-Limit
});

// Background Worker
builder.Services.AddHostedService<QueueWorkerService>();

// ── ASP.NET Core ───────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// CORS für PHP-Frontends
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // ═══════════════════════════════════════════════════════
        // ANPASSEN: Hier die URLs deiner PHP-Server eintragen!
        // ═══════════════════════════════════════════════════════
        policy
            .WithOrigins(
                "http://localhost",
                "http://localhost:8080",
                "https://dein-php-server.local",  // ← ANPASSEN
                "https://dashboard.firma.local"    // ← ANPASSEN
            )
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// ── Middleware Pipeline ────────────────────────────
app.UseSerilogRequestLogging();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseCors();
app.MapControllers();

// ── Startup-Info ───────────────────────────────────
var gatewayOptions = builder.Configuration
    .GetSection(GatewayOptions.SectionName)
    .Get<GatewayOptions>()!;

Log.Information("═══════════════════════════════════════════════");
Log.Information("  AI Gateway gestartet");
Log.Information("  LM Studio: {Url}", gatewayOptions.LmStudioBaseUrl);
Log.Information("  Modell: {Model}", gatewayOptions.ModelName);
Log.Information("  Max Concurrency: {Concurrency}", gatewayOptions.MaxConcurrency);
Log.Information("  Max Tokens/Chunk: {Tokens}", gatewayOptions.MaxTokensPerChunk);
Log.Information("  Cache-Dauer: {Minutes} Minuten", gatewayOptions.CacheDurationMinutes);
Log.Information("═══════════════════════════════════════════════");

app.Run();
