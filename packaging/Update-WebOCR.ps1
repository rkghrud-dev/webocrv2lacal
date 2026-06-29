param(
    [Parameter(Mandatory=$true)]
    [string]$UpdateZip
)

$ErrorActionPreference = "Stop"

$InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\WebOCR"
if (-not (Test-Path -LiteralPath $InstallRoot)) {
    throw "WebOCR install folder not found: $InstallRoot"
}
if (-not (Test-Path -LiteralPath $UpdateZip)) {
    throw "Update zip not found: $UpdateZip"
}

$TempRoot = Join-Path $env:TEMP ("webocr_update_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null
try {
    Expand-Archive -LiteralPath $UpdateZip -DestinationPath $TempRoot -Force
    $PayloadRoot = Join-Path $TempRoot "WebOCR"
    if (-not (Test-Path -LiteralPath $PayloadRoot)) {
        throw "Invalid update: WebOCR folder not found."
    }

    Get-ChildItem -LiteralPath $PayloadRoot -Force | ForEach-Object {
        $target = Join-Path $InstallRoot $_.Name
        if ($_.Name -eq "defaults") {
            return
        }
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }
        Copy-Item -LiteralPath $_.FullName -Destination $InstallRoot -Recurse -Force
    }
}
finally {
    if (Test-Path -LiteralPath $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force
    }
}

Write-Host "WebOCR update complete."
