# Pfad: AiGateway/tools/setup.ps1
#
# Erstellt die komplette Projektstruktur.
# Ausführung: .\tools\setup.ps1
# Danach: cd AiGateway && dotnet restore && dotnet build

$root = "AiGateway"

$directories = @(
    "$root/src/AiGateway.Api/Controllers"
    "$root/src/AiGateway.Api/Models/Requests"
    "$root/src/AiGateway.Api/Models/Responses"
    "$root/src/AiGateway.Api/Models/Internal"
    "$root/src/AiGateway.Api/Services"
    "$root/src/AiGateway.Api/Workers"
    "$root/src/AiGateway.Api/Middleware"
    "$root/src/AiGateway.Api/Configuration"
    "$root/tests/AiGateway.Tests/IntegrationTests"
    "$root/tools"
)

Write-Host "Erstelle Projektstruktur..." -ForegroundColor Cyan

foreach ($dir in $directories) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Write-Host "  ✅ $dir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Struktur erstellt! Nun die Dateien einfügen und dann:" -ForegroundColor Yellow
Write-Host "  cd $root" -ForegroundColor White
Write-Host "  dotnet restore" -ForegroundColor White
Write-Host "  dotnet build" -ForegroundColor White
Write-Host "  dotnet run --project src/AiGateway.Api" -ForegroundColor White
Write-Host ""
Write-Host "LM Studio muss auf localhost:1234 laufen!" -ForegroundColor Yellow
