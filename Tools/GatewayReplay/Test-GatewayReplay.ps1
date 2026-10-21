#requires -Version 7.0
param([string]$RunnerExecutable = (Get-Process -Id $PID).Path)
# Offline contract checks; the only HTTP endpoint is a synthetic loopback server.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Concurrent;

public sealed class ReplayFake : IDisposable {
    private TcpListener listener;
    private Task worker;
    public string Mode;
    public int Port;
    public ConcurrentQueue<string> Requests = new ConcurrentQueue<string>();
    public ReplayFake(string mode) {
        Mode = mode;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        worker = Task.Run(() => Run());
    }
    private void Run() {
        try {
            while (true) {
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream()) {
                    stream.ReadTimeout = 10000;
                    var header = new StringBuilder();
                    while (!header.ToString().EndsWith("\r\n\r\n")) {
                        int value = stream.ReadByte();
                        if (value < 0) throw new IOException("Unexpected EOF");
                        header.Append((char)value);
                    }
                    var lengthMatch = Regex.Match(header.ToString(), "Content-Length: ([0-9]+)", RegexOptions.IgnoreCase);
                    int length = lengthMatch.Success ? int.Parse(lengthMatch.Groups[1].Value) : 0;
                    var bytes = new byte[length];
                    int read = 0;
                    while (read < length) { int n = stream.Read(bytes, read, length - read); if (n == 0) throw new IOException(); read += n; }
                    string input = Encoding.UTF8.GetString(bytes);
                    bool post = header.ToString().StartsWith("POST ");
                    string id;
                    if (post) {
                        Requests.Enqueue(input);
                        id = Regex.Match(input, "\"statementId\":([0-9]+)").Groups[1].Value;
                    } else {
                        id = Regex.Match(header.ToString(), "/analysis/result/([0-9]+)").Groups[1].Value;
                    }
                    string output;
                    int status = Mode == "http-error" ? 500 : 200;
                    if (post) {
                        output = "{\"statementId\":" + id + ",\"status\":\"completed\",\"cached\":" +
                            (Mode == "cached" ? "true" : "false") + ",\"sentiment\":\"Positiv\",\"keywords\":[],\"processingMs\":12}";
                    } else {
                        output = "{\"StatementId\":" + id + ",\"ParseError\":" + (Mode == "parse-error" ? "\"broken model JSON\"" : "null") + "}";
                    }
                    if (Mode == "malformed") output = "not JSON";
                    var body = Encoding.UTF8.GetBytes(output);
                    var responseHeader = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Error") +
                        "\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(responseHeader, 0, responseHeader.Length);
                    stream.Write(body, 0, body.Length);
                }
            }
        } catch (SocketException) { } catch (ObjectDisposedException) { }
    }
    public void Dispose() { listener.Stop(); worker.Wait(10000); }
}
'@

$testRoot = Join-Path $PSScriptRoot ('artifacts/checks-' + [Guid]::NewGuid().ToString('N'))
$previousKey = $env:AI_GATEWAY_API_KEY
$env:AI_GATEWAY_API_KEY = 'synthetic-contract-check'
try {
    foreach ($mode in @('valid', 'parse-error', 'cached', 'malformed', 'http-error')) {
        $fake = [ReplayFake]::new($mode)
        try {
            $modeDir = Join-Path $testRoot $mode
            $repeats = $(if ($mode -eq 'valid') { 3 } else { 1 })
            & $RunnerExecutable -NoProfile -File (Join-Path $PSScriptRoot 'Invoke-GatewayReplay.ps1') `
                -GatewayBaseUrl ('http://127.0.0.1:' + $fake.Port + '/api') -ModelLabel 'synthetic' `
                -OutputRoot $modeDir -Repeats $repeats -WarmupRequests 1 | Out-Null
            $runExit = $LASTEXITCODE
            if (-not (Test-Path -LiteralPath $modeDir)) {
                throw ('Runner did not start or produced no artifacts. Exit=' + $runExit + '. Check the script execution/signature policy.')
            }
            $run = Get-ChildItem -LiteralPath $modeDir -Directory | Select-Object -First 1
            $summary = Get-Content -LiteralPath (Join-Path $run.FullName 'summary.json') -Raw | ConvertFrom-Json
            if ($mode -eq 'valid') {
                if ($runExit -ne 0 -or $summary.successfulRequests -ne 12 -or $summary.sentimentCorrectRequests -ne 6 -or $summary.repeatInconsistencyCases -ne 0) {
                    throw 'Valid fixture or sentiment scoring failed.'
                }
            }
            elseif ($runExit -ne 1 -or $summary.failedRequests -ne 4 -or $summary.successfulRequests -ne 0) {
                throw ('Failure was incorrectly accepted: ' + $mode)
            }
            foreach ($request in $fake.Requests) {
                $payload = $request | ConvertFrom-Json
                if ($payload.textHash -ne '' -or -not $payload.ignoreCache -or $payload.projectId -ne 8) {
                    throw 'Cache bypass or project selection missing.'
                }
            }
            $manifest = Get-Content -LiteralPath (Join-Path $run.FullName 'manifest.json') -Raw
            if ($manifest.Contains($env:AI_GATEWAY_API_KEY)) { throw 'API key leaked into manifest.' }
            Write-Host ('PASS: ' + $mode)
        }
        finally { $fake.Dispose() }
    }
}
finally { $env:AI_GATEWAY_API_KEY = $previousKey }
Write-Host ('All 5 loopback scenarios passed. Artifacts: ' + $testRoot)
