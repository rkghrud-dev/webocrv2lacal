$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$pidPath = Join-Path $Root ".localhost.pid"

if (-not (Test-Path -LiteralPath $pidPath)) {
    Write-Host "No localhost PID file found."
    return
}

$rawPid = Get-Content -LiteralPath $pidPath -ErrorAction SilentlyContinue | Select-Object -First 1
$parsedPid = 0
if (-not [int]::TryParse($rawPid, [ref]$parsedPid)) {
    Remove-Item -LiteralPath $pidPath -Force
    Write-Host "Invalid PID file removed."
    return
}

$serverPid = $parsedPid
$process = Get-Process -Id $serverPid -ErrorAction SilentlyContinue
if ($process) {
    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$serverPid" -ErrorAction SilentlyContinue
    foreach ($child in $children) {
        Stop-Process -Id $child.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Stop-Process -Id $serverPid -Force
    Write-Host "WebOcrClude localhost stopped. PID: $serverPid" -ForegroundColor Green
}
else {
    Write-Host "Process already stopped. PID: $serverPid"
}

Remove-Item -LiteralPath $pidPath -Force
