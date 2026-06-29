param(
    [string]$Version = "0.1.0",
    [switch]$SkipInstallerExe,
    [switch]$NoKeys
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$BuildRoot = Join-Path $Root "dist"
$StageRoot = Join-Path $BuildRoot "WebOCR-$Version"
$PayloadRoot = Join-Path $StageRoot "WebOCR"
$CacheRoot = Join-Path $BuildRoot "cache"
$PythonVersion = "3.10.11"
$PythonZipName = "python-$PythonVersion-embed-amd64.zip"
$PythonZip = Join-Path $CacheRoot $PythonZipName
$PythonUrl = "https://www.python.org/ftp/python/$PythonVersion/$PythonZipName"
$GetPip = Join-Path $CacheRoot "get-pip.py"

function Copy-CleanTree {
    param(
        [Parameter(Mandatory=$true)][string]$Source,
        [Parameter(Mandatory=$true)][string]$Destination,
        [string[]]$ExcludeRegex = @()
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        return
    }
    $sourceRoot = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\', '/')
    Get-ChildItem -LiteralPath $Source -Recurse -File -Force | ForEach-Object {
        $rel = $_.FullName.Substring($sourceRoot.Length).TrimStart('\', '/')
        $skip = $false
        foreach ($rx in $ExcludeRegex) {
            if ($rel -match $rx) {
                $skip = $true
                break
            }
        }
        if ($skip) {
            return
        }
        $target = Join-Path $Destination $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $PayloadRoot, $CacheRoot | Out-Null

Write-Host "Publishing WebOCR launcher..."
dotnet publish (Join-Path $Root "WebOCR.Launcher\WebOCR.Launcher.csproj") -c Release -r win-x64 --self-contained true -o (Join-Path $PayloadRoot "_launcher_publish") | Out-Host
Move-Item -LiteralPath (Join-Path $PayloadRoot "_launcher_publish\WebOCR.exe") -Destination (Join-Path $PayloadRoot "WebOCR.exe") -Force
Remove-Item -LiteralPath (Join-Path $PayloadRoot "_launcher_publish") -Recurse -Force

Write-Host "Publishing API key maker..."
dotnet publish (Join-Path $Root "MarketKeyMaker\MarketKeyMaker.csproj") -c Release -r win-x64 --self-contained true -o (Join-Path $PayloadRoot "_keymaker_publish") | Out-Host
Move-Item -LiteralPath (Join-Path $PayloadRoot "_keymaker_publish\MarketKeyMaker.exe") -Destination (Join-Path $PayloadRoot "MarketKeyMaker.exe") -Force
Remove-Item -LiteralPath (Join-Path $PayloadRoot "_keymaker_publish") -Recurse -Force

Write-Host "Preparing embedded Python..."
if (-not (Test-Path -LiteralPath $PythonZip)) {
    Invoke-WebRequest -Uri $PythonUrl -OutFile $PythonZip
}
$PythonRoot = Join-Path $PayloadRoot "runtime\python"
New-Item -ItemType Directory -Force -Path $PythonRoot | Out-Null
Expand-Archive -LiteralPath $PythonZip -DestinationPath $PythonRoot -Force
$Pth = Get-ChildItem -LiteralPath $PythonRoot -Filter "python*._pth" | Select-Object -First 1
if ($Pth) {
    $pthText = Get-Content -LiteralPath $Pth.FullName
    $pthText = $pthText | ForEach-Object {
        if ($_ -eq "#import site") { "import site" } else { $_ }
    }
    Set-Content -LiteralPath $Pth.FullName -Value $pthText -Encoding ASCII
}
if (-not (Test-Path -LiteralPath $GetPip)) {
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $GetPip
}
& (Join-Path $PythonRoot "python.exe") $GetPip
$env:PYTHONNOUSERSITE = "1"
$SitePackages = Join-Path $PythonRoot "Lib\site-packages"
New-Item -ItemType Directory -Force -Path $SitePackages | Out-Null
& (Join-Path $PythonRoot "python.exe") -m pip install --no-cache-dir --no-compile --upgrade --target $SitePackages -r (Join-Path $PSScriptRoot "requirements-runtime.txt")
Get-ChildItem -LiteralPath $PythonRoot -Recurse -Directory -Force -Filter "__pycache__" | Remove-Item -Recurse -Force

Write-Host "Copying app files..."
Copy-CleanTree -Source (Join-Path $Root "webocrcludev2") -Destination (Join-Path $PayloadRoot "webocrcludev2") -ExcludeRegex @(
    "^data[\\/](exports|uploads|jobs|emergency|desktop_key|market_keys|logos)[\\/]",
    "^data[\\/]seeds[\\/](backup|backups|deleted_|backup_before_)",
    "^data[\\/]reports[\\/]",
    "__pycache__",
    "\.pyc$",
    "\.log$",
    "\.pid$",
    "^RUN\.BAT$",
    "^STOP\.BAT$"
)
Copy-CleanTree -Source (Join-Path $Root "backend\app") -Destination (Join-Path $PayloadRoot "backend\app") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "backend\data") -Destination (Join-Path $PayloadRoot "backend\data") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "backend\prompts") -Destination (Join-Path $PayloadRoot "backend\prompts") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "KeywordOcr.App\Bridge") -Destination (Join-Path $PayloadRoot "KeywordOcr.App\Bridge") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "tools") -Destination (Join-Path $PayloadRoot "tools") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "_EXCEL_BASIC_DATA") -Destination (Join-Path $PayloadRoot "_EXCEL_BASIC_DATA") -ExcludeRegex @("~\$", "\.tmp$")

$pmExclude = @(
    "__pycache__",
    "\.pyc$",
    "^exports[\\/]",
    "^data[\\/]products_before_.*\.db$",
    "^data[\\/]result_uploads[\\/]"
)
Copy-CleanTree -Source (Join-Path $Root "ProductManager") -Destination (Join-Path $PayloadRoot "ProductManager") -ExcludeRegex $pmExclude
New-Item -ItemType Directory -Force -Path (Join-Path $PayloadRoot "ProductManager\data") | Out-Null

foreach ($dir in @("uploads", "exports", "jobs", "seeds", "logos", "emergency", "market_keys")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $PayloadRoot "webocrcludev2\data\$dir") | Out-Null
}

# category_matching.db is reference data required by the category auto-matching
# feature. Copy it explicitly so it always ships, even if the exclude list above
# is later changed to drop data\category.
$CategoryDbSource = Join-Path $Root "webocrcludev2\data\category\category_matching.db"
if (Test-Path -LiteralPath $CategoryDbSource) {
    $CategoryDbTarget = Join-Path $PayloadRoot "webocrcludev2\data\category\category_matching.db"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $CategoryDbTarget) | Out-Null
    Copy-Item -LiteralPath $CategoryDbSource -Destination $CategoryDbTarget -Force
    Write-Host ("Included category_matching.db ({0:N1} MB)" -f ((Get-Item -LiteralPath $CategoryDbSource).Length / 1MB))
} else {
    Write-Warning "category_matching.db not found at $CategoryDbSource - category matching will be unavailable."
}

if (-not $NoKeys) {
    Write-Host "Copying default key folder..."
    Copy-CleanTree -Source (Join-Path $Root "key") -Destination (Join-Path $PayloadRoot "defaults\key") -ExcludeRegex @("__pycache__", "\.pyc$")
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Update-WebOCR.ps1") -Destination (Join-Path $PayloadRoot "Update-WebOCR.ps1") -Force
Set-Content -LiteralPath (Join-Path $PayloadRoot "VERSION.txt") -Value $Version -Encoding ASCII

Write-Host "Creating payload zip..."
$PayloadZip = Join-Path $StageRoot "payload.zip"
Remove-Item -LiteralPath $PayloadZip -Force -ErrorAction SilentlyContinue
Push-Location $StageRoot
try {
    tar.exe -a -c -f $PayloadZip "WebOCR"
}
finally {
    Pop-Location
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install.ps1") -Destination (Join-Path $StageRoot "install.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "setup.cmd") -Destination (Join-Path $StageRoot "setup.cmd") -Force

if (-not $SkipInstallerExe) {
    $SetupExe = Join-Path $BuildRoot "WebOCR_Setup_$Version.exe"
    $SetupPublishRoot = Join-Path $BuildRoot "_setup_publish_$Version"
    Remove-Item -LiteralPath $SetupPublishRoot -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $PayloadZip -Destination (Join-Path $Root "WebOCR.Setup\payload.zip") -Force
    dotnet publish (Join-Path $Root "WebOCR.Setup\WebOCR.Setup.csproj") -c Release -r win-x64 --self-contained true -o $SetupPublishRoot | Out-Host
    Move-Item -LiteralPath (Join-Path $SetupPublishRoot "WebOCR_Setup.exe") -Destination $SetupExe -Force
    Remove-Item -LiteralPath $SetupPublishRoot -Recurse -Force
}

$Size = (Get-ChildItem -LiteralPath $StageRoot -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Release stage: {0}" -f $StageRoot)
Write-Host ("Stage size: {0:N2} MB" -f ($Size / 1MB))
