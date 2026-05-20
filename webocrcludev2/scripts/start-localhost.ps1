$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ProjectRoot = Split-Path -Parent $Root
$PreferredPort = 5556
$MaxPort = 5576

function Test-PortAvailable {
    param([int]$Port)

    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse("127.0.0.1"), $Port)
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($listener -ne $null) {
            $listener.Stop()
        }
    }
}

function Get-PythonCommand {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        return @{ FileName = "python"; PrefixArgs = @() }
    }

    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($py) {
        return @{ FileName = "py"; PrefixArgs = @("-3") }
    }

    throw "Python was not found. Install Python or add py/python to PATH."
}

$Port = $PreferredPort
while ($Port -le $MaxPort -and -not (Test-PortAvailable -Port $Port)) {
    $Port++
}

if ($Port -gt $MaxPort) {
    throw "No available localhost port found in range ${PreferredPort}-${MaxPort}."
}

$python = Get-PythonCommand
$serverArgs = @()
$serverArgs += $python.PrefixArgs
$serverArgs += @((Join-Path $Root "scripts\local_api_server.py"), "--port", "$Port", "--host", "127.0.0.1")

$localKeyRoot = Join-Path $ProjectRoot "key"
$localProductManagerRoot = Join-Path $ProjectRoot "ProductManager"
New-Item -ItemType Directory -Force -Path $localKeyRoot | Out-Null
$env:WEBOCR_KEY_ROOT = $localKeyRoot
$env:KEYWORDOCR_KEY_DIR = $localKeyRoot
if (Test-Path -LiteralPath $localProductManagerRoot) {
    $env:WEBOCR_PRODUCT_MANAGER_ROOT = $localProductManagerRoot
}

$stdoutLogPath = Join-Path $Root "localhost.out.log"
$stderrLogPath = Join-Path $Root "localhost.err.log"
$process = Start-Process `
    -FilePath $python.FileName `
    -ArgumentList $serverArgs `
    -WorkingDirectory $Root `
    -RedirectStandardOutput $stdoutLogPath `
    -RedirectStandardError $stderrLogPath `
    -WindowStyle Hidden `
    -PassThru

$pidPath = Join-Path $Root ".localhost.pid"
Set-Content -LiteralPath $pidPath -Value $process.Id -Encoding ASCII

$url = "http://localhost:$Port/index.html"
Start-Sleep -Milliseconds 500
Start-Process $url

Write-Host ""
Write-Host "WebOcrClude localhost started" -ForegroundColor Green
Write-Host "URL: $url"
Write-Host "PID: $($process.Id)"
Write-Host "Logs: $stdoutLogPath / $stderrLogPath"
Write-Host ""
Write-Host "Run STOP.BAT to stop the localhost server."
