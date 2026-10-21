#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][uri]$GatewayBaseUrl,
    [Parameter(Mandatory = $true)][string]$ModelLabel,
    [ValidateSet('Css', 'Sympany')][string]$Target = 'Css',
    [int]$ProjectId = 8,
    [string]$CasesPath = (Join-Path $PSScriptRoot 'smoke-cases.json'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts'),
    [string]$SettingsPath,
    [ValidateRange(1, 100)][int]$Repeats = 3,
    [ValidateRange(0, 10)][int]$WarmupRequests = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

function Read-Field($Object, [string]$Name, $Default = $null) {
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function Get-FileDigest([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Percentile([double[]]$Values, [double]$Fraction) {
    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    if ($Fraction -eq 0.5 -and $sorted.Count % 2 -eq 0) {
        $middle = [int]($sorted.Count / 2)
        return ($sorted[$middle - 1] + $sorted[$middle]) / 2
    }
    return $sorted[[Math]::Max(0, [int][Math]::Ceiling($sorted.Count * $Fraction) - 1)]
}

function Send-Request([string]$Method, [string]$Url, $Payload = $null) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Url)
    $response = $null
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $request.Headers.Add('X-Api-Key', $env:AI_GATEWAY_API_KEY)
        if ($null -ne $Payload) {
            $request.Content = [System.Net.Http.StringContent]::new(
                ($Payload | ConvertTo-Json -Depth 20 -Compress), [Text.Encoding]::UTF8, 'application/json')
        }
        $response = $script:http.SendAsync($request).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $watch.Stop()
        $decoded = $null
        try { $decoded = $body | ConvertFrom-Json } catch { }
        return [pscustomobject]@{
            httpStatus = [int]$response.StatusCode; wallMs = $watch.Elapsed.TotalMilliseconds
            body = $body; json = $decoded; transportError = $null
        }
    }
    catch {
        $watch.Stop()
        Write-Verbose $_.Exception.GetBaseException().Message
        # Do not persist exception messages: they can contain endpoints or credentials.
        return [pscustomobject]@{
            httpStatus = 0; wallMs = $watch.Elapsed.TotalMilliseconds
            body = ''; json = $null; transportError = $_.Exception.GetBaseException().GetType().Name
        }
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        $request.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($env:AI_GATEWAY_API_KEY)) {
    throw 'Set AI_GATEWAY_API_KEY in this PowerShell session before running.'
}
if ($GatewayBaseUrl.Scheme -notin @('http', 'https') -or $GatewayBaseUrl.UserInfo -or $GatewayBaseUrl.Query -or $GatewayBaseUrl.Fragment) {
    throw 'GatewayBaseUrl must be an HTTP(S) URL without credentials, query or fragment, ending in /api.'
}
$baseUrl = $GatewayBaseUrl.AbsoluteUri.TrimEnd('/')
if (-not $baseUrl.EndsWith('/api', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'GatewayBaseUrl must include the /api suffix.'
}
$suite = Get-Content -LiteralPath $CasesPath -Raw -Encoding UTF8 | ConvertFrom-Json
$cases = @($suite.cases)
if ($cases.Count -eq 0) { throw 'The suite must contain at least one case.' }
$ids = @{}
foreach ($case in $cases) {
    $caseId = [string](Read-Field $case 'caseId' '')
    if (-not $caseId -or $ids.ContainsKey($caseId) -or [string]::IsNullOrWhiteSpace((Read-Field $case 'text' ''))) {
        throw 'Each case needs a unique caseId and nonempty text.'
    }
    $ids[$caseId] = $true
}

$runId = 'replay-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runDir = Join-Path $OutputRoot $runId
[void](New-Item -ItemType Directory -Path $runDir -Force)
Copy-Item -LiteralPath $CasesPath -Destination (Join-Path $runDir 'cases.json')
$manifest = [ordered]@{
    runId = $runId; startedAtUtc = [DateTime]::UtcNow.ToString('o'); gatewayBaseUrl = $baseUrl
    modelLabel = $ModelLabel; modelLabelIsOperatorSupplied = $true
    target = $Target; projectId = $(if ($Target -eq 'Css') { $ProjectId } else { $null })
    casesSha256 = Get-FileDigest $CasesPath; runnerSha256 = Get-FileDigest $PSCommandPath
    repeats = $Repeats; warmupRequests = $WarmupRequests; concurrency = 1
    powershellVersion = $PSVersionTable.PSVersion.ToString()
}
if ($SettingsPath) {
    # A hand-maintained, non-secret record of model/quant/runtime/context/GPU settings.
    # Do not pass appsettings.json: it can contain API keys.
    Copy-Item -LiteralPath $SettingsPath -Destination (Join-Path $runDir 'settings.json')
    $manifest['settingsSha256'] = Get-FileDigest $SettingsPath
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $runDir 'manifest.json') -Encoding UTF8

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$script:http = [System.Net.Http.HttpClient]::new($handler)
$script:http.Timeout = [TimeSpan]::FromSeconds(600)
$rows = [Collections.Generic.List[object]]::new()
$statementId = Get-Random -Minimum 1000000000 -Maximum 1900000000
try {
    $steps = [Collections.Generic.List[object]]::new()
    for ($w = 0; $w -lt $WarmupRequests; $w++) {
        $steps.Add([pscustomobject]@{ warmup = $true; repeat = 0; case = $cases[0] })
    }
    for ($repeat = 1; $repeat -le $Repeats; $repeat++) {
        foreach ($case in $cases) { $steps.Add([pscustomobject]@{ warmup = $false; repeat = $repeat; case = $case }) }
    }
    foreach ($step in $steps) {
        $statementId++
        $case = $step.case
        $payload = [ordered]@{
            statementId = $statementId; dashboard = $runId
            text = [string]$case.text; textHash = ''; ignoreCache = $true
        }
        if ($Target -eq 'Css') {
            $payload['projectId'] = $ProjectId
            if ($null -ne (Read-Field $case 'customerSatisfactionScore')) {
                $payload['customerSatisfactionScore'] = $case.customerSatisfactionScore
            }
        }
        $reply = Send-Request 'POST' ($baseUrl + '/analysis/analyze') $payload
        $diagnostic = $null
        $problems = [Collections.Generic.List[string]]::new()
        if ($reply.httpStatus -ne 200) { $problems.Add('http_or_transport_error') }
        if ((Read-Field $reply.json 'status') -ne 'completed') { $problems.Add('not_completed') }
        if ((Read-Field $reply.json 'statementId' -1) -ne $statementId) { $problems.Add('wrong_statement') }
        if (Read-Field $reply.json 'cached' $false) { $problems.Add('unexpected_cache_hit') }
        if ((Read-Field $reply.json 'sentiment') -notin @('Positiv', 'Negativ', 'Neutral')) { $problems.Add('invalid_sentiment') }
        if ($null -eq $reply.json -or $null -eq $reply.json.PSObject.Properties['keywords'] -or $reply.json.PSObject.Properties['keywords'].Value -isnot [Array]) {
            $problems.Add('invalid_keywords')
        }

        # The synchronous API can report HTTP 200/completed after a parser fallback.
        # The persisted result exposes ParseError; fetch it before claiming success.
        if ($reply.httpStatus -eq 200) {
            $resultUrl = $baseUrl + '/analysis/result/' + $statementId + '?dashboard=' + [uri]::EscapeDataString($runId)
            if ($Target -eq 'Css') { $resultUrl += '&projectId=' + $ProjectId }
            $diagnostic = Send-Request 'GET' $resultUrl
            if ($diagnostic.httpStatus -ne 200 -or (Read-Field $diagnostic.json 'statementId' -1) -ne $statementId) {
                $problems.Add('diagnostics_unavailable')
            }
            elseif (Read-Field $diagnostic.json 'parseError') { $problems.Add('model_parse_error') }
        }
        $success = $problems.Count -eq 0
        $sentimentCorrect = $null
        $expectedSentiment = Read-Field $case 'expectedSentiment'
        if ($null -ne $expectedSentiment -and $success) {
            $sentimentCorrect = (Read-Field $reply.json 'sentiment') -eq $expectedSentiment
        }
        $predictedIds = @(@(Read-Field $reply.json 'keywords' @()) | ForEach-Object { Read-Field $_ 'id' } | Where-Object { $null -ne $_ } | Sort-Object -Unique)
        $tp = 0; $fp = 0; $fn = 0; $codeExact = $null
        $hasExpectedCodes = $null -ne $case.PSObject.Properties['expectedCodeIds']
        if ($hasExpectedCodes -and $success) {
            $expectedIds = @($case.expectedCodeIds | Sort-Object -Unique)
            $tp = @($predictedIds | Where-Object { $_ -in $expectedIds }).Count
            $fp = @($predictedIds | Where-Object { $_ -notin $expectedIds }).Count
            $fn = @($expectedIds | Where-Object { $_ -notin $predictedIds }).Count
            $codeExact = $fp -eq 0 -and $fn -eq 0
        }
        $row = [pscustomobject][ordered]@{
            caseId = $case.caseId; repeat = $step.repeat; warmup = $step.warmup
            statementId = $statementId; httpStatus = $reply.httpStatus; success = $success
            issues = $problems -join ';'; wallMs = [Math]::Round($reply.wallMs, 1)
            gatewayMs = Read-Field $reply.json 'processingMs'; cached = Read-Field $reply.json 'cached' $false
            sentiment = Read-Field $reply.json 'sentiment'; sentimentCorrect = $sentimentCorrect
            predictedCodeIds = $predictedIds -join ';'; codeExact = $codeExact
            tp = $tp; fp = $fp; fn = $fn; needsReview = Read-Field $reply.json 'needsReview' $false
        }
        $rows.Add($row)
        $rawEntry = [pscustomobject]@{
            caseId = $case.caseId; repeat = $step.repeat; warmup = $step.warmup; request = $payload
            response = $reply; persistedResult = $diagnostic
        }
        $rawEntry | ConvertTo-Json -Depth 40 -Compress | Add-Content -LiteralPath (Join-Path $runDir 'responses.jsonl') -Encoding UTF8
        $rows | Export-Csv -LiteralPath (Join-Path $runDir 'results.csv') -NoTypeInformation -Encoding UTF8
        Write-Host ($case.caseId + ': HTTP ' + $reply.httpStatus + ', ' + [Math]::Round($reply.wallMs) + ' ms, valid=' + $success + ', warmup=' + $step.warmup)
        if ($reply.httpStatus -in @(401, 403) -or $reply.httpStatus -eq 0) { break }
    }
}
finally { $script:http.Dispose() }

$measured = @($rows | Where-Object { -not $_.warmup })
$valid = @($measured | Where-Object success)
$sentimentRows = @($valid | Where-Object { $null -ne $_.sentimentCorrect })
$codeRows = @($valid | Where-Object { $null -ne $_.codeExact })
$totalTp = 0; $totalFp = 0; $totalFn = 0
foreach ($row in $codeRows) { $totalTp += $row.tp; $totalFp += $row.fp; $totalFn += $row.fn }
$summary = [ordered]@{
    runId = $runId; plannedMeasuredRequests = $cases.Count * $Repeats; measuredRequests = $measured.Count
    successfulRequests = $valid.Count; failedRequests = $measured.Count - $valid.Count
    warmupFailures = @($rows | Where-Object { $_.warmup -and -not $_.success }).Count
    wallMedianMs = Get-Percentile @($valid | ForEach-Object { [double]$_.wallMs }) 0.5
    wallP95Ms = Get-Percentile @($valid | ForEach-Object { [double]$_.wallMs }) 0.95
    sentimentScoredRequests = $sentimentRows.Count
    sentimentCorrectRequests = @($sentimentRows | Where-Object sentimentCorrect).Count
    codeScoredRequests = $codeRows.Count
    codeExactRequests = @($codeRows | Where-Object codeExact).Count
    codeTruePositives = $totalTp; codeFalsePositives = $totalFp; codeFalseNegatives = $totalFn
    codeMicroF1 = $(if ((2 * $totalTp + $totalFp + $totalFn) -gt 0) { 2.0 * $totalTp / (2 * $totalTp + $totalFp + $totalFn) } else { $null })
    needsReviewRequests = @($valid | Where-Object needsReview).Count
    repeatInconsistencyCases = @($valid | Group-Object caseId | Where-Object {
        @($_.Group | ForEach-Object { [string]$_.sentiment + '|' + [string]$_.predictedCodeIds } | Sort-Object -Unique).Count -gt 1
    }).Count
    note = 'Quality scores cover valid responses only. Always evaluate failure counts and scored denominators alongside them. Wall time includes the complete POST response, but excludes the diagnostic GET.'
}
$summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $runDir 'summary.json') -Encoding UTF8
Write-Host ('Run saved to ' + $runDir)
$summary | ConvertTo-Json -Depth 20
if ($measured.Count -ne $cases.Count * $Repeats -or $valid.Count -ne $measured.Count -or $summary.warmupFailures -gt 0) { exit 1 }
