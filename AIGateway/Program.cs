using AiGateway.Api.Configuration;
using AiGateway.Api.Middleware;
using AiGateway.Api.Services;
using AiGateway.Api.Workers;
using Serilog;
using System.Runtime;

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
builder.Services.AddSingleton<MemoryDiagnosticsService>();
builder.Services.AddSingleton<IJobPersistenceService, JobPersistenceService>();

// HttpClient für LM Studio
builder.Services.AddHttpClient<ILmStudioClient, LmStudioClient>();

// Memory Cache - ANGEPASST: CompactionPercentage hinzugefügt
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100_000_000; // 100MB in Bytes
    options.CompactionPercentage = 0.25; // Bei Überlauf 25% entfernen
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(5); // Alle 5min aufräumen
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
app.UseMiddleware<MemoryMonitoringMiddleware>();
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
Log.Information("═══════════════════════════════════════════════");
Log.Information("  LM Studio: {Url}", gatewayOptions.LmStudioBaseUrl);
Log.Information("  Modell: {Model}", gatewayOptions.ModelName);
Log.Information("  Max Concurrency: {Concurrency}", gatewayOptions.MaxConcurrency);
Log.Information("  Max Tokens/Chunk: {Tokens}", gatewayOptions.MaxTokensPerChunk);
Log.Information("  Cache-Dauer: {Minutes} Minuten", gatewayOptions.CacheDurationMinutes);
Log.Information("  Cache-Limit: {Limit:N0} Bytes (~{MB}MB)", 100_000_000, 100);
Log.Information("───────────────────────────────────────────────");
Log.Information("  .NET Version: {Version}", Environment.Version);
Log.Information("  Prozessoren: {Count}", Environment.ProcessorCount);
Log.Information("  Arbeitsspeicher (Total): {Memory:N0} MB verfügbar", 
    GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
Log.Information("  GC-Modus: {Mode}", GCSettings.IsServerGC ? "Server" : "Workstation");
Log.Information("═══════════════════════════════════════════════");

// Initiales Memory-Snapshot erstellen
var memoryService = app.Services.GetRequiredService<MemoryDiagnosticsService>();
memoryService.WriteMemorySnapshot("Application Startup");

app.Run();
