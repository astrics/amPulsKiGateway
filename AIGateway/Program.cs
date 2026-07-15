using AiGateway.Api.Configuration;
using AiGateway.Api.Middleware;
using AiGateway.Api.Services;
using Serilog;
using System.Runtime;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

builder.Services.AddHttpClient<LmStudioService>();
builder.Services.AddSingleton<ResultStore>();
/*
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Services
builder.Services.AddSingleton<JobStore>();
builder.Services.AddHttpClient<LmStudioClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(300);
});
builder.Services.AddSingleton<BatchJobProcessor>();
builder.Services.AddSingleton<ResultFileWriter>();
builder.Services.AddHttpClient<OllamaService>();
builder.Services.AddSingleton<ResultStore>();
*/
// ── Configuration ──────────────────────────────────
builder.Services.Configure<GatewayOptions>(
    builder.Configuration.GetSection(GatewayOptions.SectionName));

// ── Services registrieren ──────────────────────────
// Queue als Singleton (muss über gesamte App-Laufzeit bestehen)

builder.Services.AddSingleton<MemoryDiagnosticsService>();

// Memory Cache - ANGEPASST: CompactionPercentage hinzugefügt
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100_000_000; // 100MB in Bytes
    options.CompactionPercentage = 0.25; // Bei Überlauf 25% entfernen
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(5); // Alle 5min aufräumen
});


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
