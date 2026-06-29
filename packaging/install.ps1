$ErrorActionPreference = "Stop"

$PackageRoot = Split-Path -Parent $PSScriptRoot
$InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\WebOCR"
$DataRoot = Join-Path $env:LOCALAPPDATA "WebOCR"

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null

$PayloadZip = Join-Path $PackageRoot "payload.zip"
if (-not (Test-Path -LiteralPath $PayloadZip)) {
    throw "payload.zip not found next to installer script."
}

$TempRoot = Join-Path $env:TEMP ("webocr_install_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null
try {
    Expand-Archive -LiteralPath $PayloadZip -DestinationPath $TempRoot -Force
    $PayloadRoot = Join-Path $TempRoot "WebOCR"
    if (-not (Test-Path -LiteralPath $PayloadRoot)) {
        throw "Invalid payload: WebOCR folder not found."
    }

    Get-ChildItem -LiteralPath $PayloadRoot -Force | ForEach-Object {
        $target = Join-Path $InstallRoot $_.Name
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }
        Copy-Item -LiteralPath $_.FullName -Destination $InstallRoot -Recurse -Force
    }

    $Shell = New-Object -ComObject WScript.Shell
    $Shortcut = $Shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "WebOCR.lnk"))
    $Shortcut.TargetPath = Join-Path $InstallRoot "WebOCR.exe"
    $Shortcut.WorkingDirectory = $InstallRoot
    $Shortcut.IconLocation = Join-Path $InstallRoot "WebOCR.exe"
    $Shortcut.Save()
}
finally {
    if (Test-Path -LiteralPath $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force
    }
}
