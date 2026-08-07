using AiGateway.Sympany.Api.Configuration;
using AiGateway.Sympany.Api.Middleware;
using AiGateway.Sympany.Api.Services;
using Serilog;
using System.Runtime;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSingleton<JobStore>();
builder.Services.AddHttpClient<ILmStudioClient, LmStudioRuntimeClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(300);
});
builder.Services.AddHttpClient<LmStudioService>();
builder.Services.AddSingleton<LmStudioConcurrencyGate>();
builder.Services.AddSingleton<BatchJobProcessor>();
builder.Services.AddSingleton<ResultFileWriter>();
builder.Services.AddSingleton<ResultStore>();

builder.Services.Configure<GatewayOptions>(
    builder.Configuration.GetSection(GatewayOptions.SectionName));

builder.Services.AddSingleton<MemoryDiagnosticsService>();

builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100_000_000;
    options.CompactionPercentage = 0.25;
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(
                "http://localhost",
                "http://localhost:8080",
                "https://dein-php-server.local",
                "https://dashboard.firma.local"
            )
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<MemoryMonitoringMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseCors();
app.MapControllers();

var gatewayOptions = builder.Configuration
    .GetSection(GatewayOptions.SectionName)
    .Get<GatewayOptions>()!;

Log.Information("===============================================");
Log.Information("  AI Gateway Sympany gestartet");
Log.Information("===============================================");
Log.Information("  LM Studio: {Url}", gatewayOptions.LmStudioBaseUrl);
Log.Information("  Modell: {Model}", gatewayOptions.ModelName);
Log.Information("  Max Concurrency: {Concurrency}", gatewayOptions.MaxConcurrency);
Log.Information("  Max Tokens/Chunk: {Tokens}", gatewayOptions.MaxTokensPerChunk);
Log.Information("  Cache-Dauer: {Minutes} Minuten", gatewayOptions.CacheDurationMinutes);
Log.Information("  Cache-Limit: {Limit:N0} Bytes (~{MB}MB)", 100_000_000, 100);
Log.Information("-----------------------------------------------");
Log.Information("  .NET Version: {Version}", Environment.Version);
Log.Information("  Prozessoren: {Count}", Environment.ProcessorCount);
Log.Information(
    "  Arbeitsspeicher (Total): {Memory:N0} MB verfuegbar",
    GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
Log.Information("  GC-Modus: {Mode}", GCSettings.IsServerGC ? "Server" : "Workstation");
Log.Information("===============================================");

var memoryService = app.Services.GetRequiredService<MemoryDiagnosticsService>();
memoryService.WriteMemorySnapshot("Application Startup");

app.Run();
