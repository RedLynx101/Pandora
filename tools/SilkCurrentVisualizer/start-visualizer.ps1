[CmdletBinding()]
param(
    [switch]$Serve,
    [ValidateRange(1024, 65512)][int]$Port = 8787
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path -LiteralPath $PSScriptRoot).Path

function Test-PortFree {
    param([int]$CandidatePort)

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $async = $client.BeginConnect("127.0.0.1", $CandidatePort, $null, $null)
        $connected = $async.AsyncWaitHandle.WaitOne(160)
        if ($connected) {
            $client.EndConnect($async)
            return $false
        }

        return $true
    }
    catch {
        return $true
    }
    finally {
        $client.Close()
    }
}

function Wait-Visualizer {
    param([string]$Url)

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 1
            if ($response.Content -match "Silk Current") {
                return $true
            }
        }
        catch {
            Start-Sleep -Milliseconds 120
        }
    }

    return $false
}

function Get-ContentType {
    param([string]$Path)

    switch ([System.IO.Path]::GetExtension($Path).ToLowerInvariant()) {
        ".html" { "text/html; charset=utf-8" }
        ".css" { "text/css; charset=utf-8" }
        ".js" { "text/javascript; charset=utf-8" }
        ".json" { "application/json; charset=utf-8" }
        ".png" { "image/png" }
        ".jpg" { "image/jpeg" }
        ".jpeg" { "image/jpeg" }
        ".svg" { "image/svg+xml" }
        default { "application/octet-stream" }
    }
}

function Send-Text {
    param(
        [System.Net.HttpListenerContext]$Context,
        [int]$StatusCode,
        [string]$Text
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $Context.Response.StatusCode = $StatusCode
    $Context.Response.ContentType = "text/plain; charset=utf-8"
    $Context.Response.ContentLength64 = $bytes.Length
    if ($Context.Request.HttpMethod -ne "HEAD") {
        $Context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    }
}

if (-not $Serve) {
    $selectedPort = $null
    for ($candidate = $Port; $candidate -lt ($Port + 24); $candidate++) {
        $url = "http://127.0.0.1:$candidate/"
        if (-not (Test-PortFree -CandidatePort $candidate)) {
            if (Wait-Visualizer -Url $url) {
                Start-Process $url | Out-Null
                return
            }

            continue
        }

        $selectedPort = $candidate
        break
    }

    if ($null -eq $selectedPort) {
        throw "Could not find a free localhost port for Silk Current."
    }

    $quotedScript = '"' + $PSCommandPath + '"'
    $serverArgs = "-NoProfile -ExecutionPolicy Bypass -File $quotedScript -Serve -Port $selectedPort"
    $serverProcess = Start-Process -FilePath "powershell.exe" -ArgumentList $serverArgs -WindowStyle Hidden -PassThru

    $selectedUrl = "http://127.0.0.1:$selectedPort/"
    if (-not (Wait-Visualizer -Url $selectedUrl)) {
        if (-not $serverProcess.HasExited) { $serverProcess.Kill() }
        throw "Silk Current could not start its local preview server."
    }
    Start-Process $selectedUrl | Out-Null
    return
}

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            if ($context.Request.HttpMethod -notin @("GET", "HEAD")) {
                Send-Text -Context $context -StatusCode 405 -Text "Method not allowed"
                continue
            }
            $requestPath = [System.Uri]::UnescapeDataString($context.Request.Url.AbsolutePath.TrimStart("/"))
            if ([string]::IsNullOrWhiteSpace($requestPath)) {
                $requestPath = "index.html"
            }

            # Only public companion assets are served, never scripts or adjacent project files.
            if ($requestPath -cnotin @("index.html", "styles.css", "src/visualizer.js")) {
                Send-Text -Context $context -StatusCode 404 -Text "Not found"
                continue
            }

            $relativePath = $requestPath.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
            $fullPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Root, $relativePath))
            $rootWithSeparator = $Root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

            if (-not $fullPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
                Send-Text -Context $context -StatusCode 403 -Text "Forbidden"
                continue
            }

            if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
                Send-Text -Context $context -StatusCode 404 -Text "Not found"
                continue
            }

            $assetPart = Get-Item -LiteralPath $fullPath -Force
            while ($null -ne $assetPart) {
                if (($assetPart.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Linked asset paths are not served."
                }
                if ($assetPart -is [System.IO.FileInfo]) { $assetPart = $assetPart.Directory }
                else { $assetPart = $assetPart.Parent }
            }

            $bytes = [System.IO.File]::ReadAllBytes($fullPath)
            $context.Response.StatusCode = 200
            $context.Response.ContentType = Get-ContentType -Path $fullPath
            $context.Response.Headers["Cache-Control"] = "no-store"
            $context.Response.Headers["X-Content-Type-Options"] = "nosniff"
            $context.Response.ContentLength64 = $bytes.Length
            if ($context.Request.HttpMethod -ne "HEAD") {
                $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            }
        }
        catch {
            try { Send-Text -Context $context -StatusCode 500 -Text "Server error" } catch { }
        }
        finally {
            try { $context.Response.OutputStream.Close() } catch { }
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
