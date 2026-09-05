[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start()
$port = $probe.LocalEndpoint.Port
$probe.Stop()
$script = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\start-visualizer.ps1'))
$child = $null
$logDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Pandora-visualizer-test-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $logDirectory)
$errorLog = Join-Path $logDirectory 'server-error.txt'
$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(3)
try {
    $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $script + '"'), '-Serve', '-Port', $port) -WindowStyle Hidden -PassThru -RedirectStandardError $errorLog
    $base = "http://127.0.0.1:$port/"
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try { $response = $client.GetAsync($base).GetAwaiter().GetResult(); $ready = $response.IsSuccessStatusCode; $response.Dispose() } catch { }
        if ($ready) { break }
        if ($child.HasExited) { throw ('Fixture server exited during startup: ' + (Get-Content -LiteralPath $errorLog -Raw)) }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw 'Fixture server did not become ready.' }
    foreach ($case in @(@('index.html',200),@('styles.css',200),@('src/visualizer.js',200),@('start-visualizer.ps1',404),@('tests/server.test.ps1',404),@('src%2F..%2Fstart-visualizer.ps1',404))) {
        $response = $client.GetAsync($base + $case[0]).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne $case[1]) { throw "Unexpected status for $($case[0]): $($response.StatusCode)" }
            if ($case[1] -eq 200 -and $response.Content.Headers.ContentLength -le 0) { throw 'Empty asset response.' }
        } finally { $response.Dispose() }
    }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head, $base)
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode -or $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length -ne 0) { throw 'HEAD contract failed.' }
    $response.Dispose(); $request.Dispose()
    $response = $client.PostAsync($base, [Net.Http.StringContent]::new('fixture')).GetAwaiter().GetResult()
    if ([int]$response.StatusCode -ne 405) { throw 'POST must be rejected.' }
    $response.Dispose()
    # Abort several clients before response consumption; a later independent request must still work.
    for ($index = 0; $index -lt 12; $index++) {
        $socket = [Net.Sockets.TcpClient]::new('127.0.0.1', $port)
        $bytes = [Text.Encoding]::ASCII.GetBytes("GET /src/visualizer.js HTTP/1.1`r`nHost: 127.0.0.1`r`nConnection: close`r`n`r`n")
        $socket.GetStream().Write($bytes, 0, $bytes.Length)
        $socket.Dispose()
    }
    $response = $client.GetAsync($base).GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode -or $child.HasExited) { throw 'Server did not survive client disconnects.' }
    $response.Dispose()
    'PASS loopback asset allowlist, methods, HEAD and disconnected-client recovery'
} finally {
    $client.Dispose()
    if ($null -ne $child) { if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit(5000) | Out-Null }; $child.Dispose() }
}
