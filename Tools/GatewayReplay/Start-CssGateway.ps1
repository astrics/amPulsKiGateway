[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Production', 'VpnBenchmark')][string]$Profile,
    [string]$ModelName,
    [string]$RunLabel = 'pilot',
    [string]$DataRoot = 'D:/AI/Tests/Gateway'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$names = @('DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT','Gateway__ModelName',
    'Jobs__StoragePath','ResultFiles__OutputDir','Storage__ResultsPath')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    # Fail rather than start an apparent production profile with stale pilot overrides.
    $unexpected = @(Get-ChildItem Env: | Where-Object {
        $_.Name -match '^(Gateway__|Kestrel__|Jobs__|ResultFiles__|Storage__)' -and $_.Name -notin $names
    })
    if ($unexpected.Count -gt 0) {
        throw ('Existing configuration overrides must be checked first: ' + (($unexpected | ForEach-Object Name) -join ', '))
    }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    $env:DOTNET_ENVIRONMENT = $Profile
    $env:ASPNETCORE_ENVIRONMENT = $Profile
    if ($Profile -eq 'VpnBenchmark') {
        if ($RunLabel -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunLabel may contain letters, digits, _ and - only.' }
        if (-not [IO.Path]::IsPathRooted($DataRoot)) { throw 'DataRoot must be an absolute path.' }
        $folder = $DataRoot.TrimEnd('/', '\') + '/' + $RunLabel
        $env:Jobs__StoragePath = $folder + '/jobs'
        $env:ResultFiles__OutputDir = $folder + '/output'
        $env:Storage__ResultsPath = $folder + '/results'
        if ($ModelName) { $env:Gateway__ModelName = $ModelName }
    } elseif ($ModelName) {
        throw 'ModelName is only supported for VpnBenchmark. Production uses its existing configuration.'
    }
    & dotnet run --no-launch-profile --project (Join-Path $repoRoot 'AIGateway.CSS/AiGateway.CSS.Api.csproj')
    if ($LASTEXITCODE -ne 0) { throw "Gateway exited with code $LASTEXITCODE." }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
