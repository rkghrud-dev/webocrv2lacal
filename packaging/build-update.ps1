param(
    [string]$Version = "0.1.1",
    [switch]$IncludeCategoryDb
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$BuildRoot = Join-Path $Root "dist"
$StageRoot = Join-Path $BuildRoot "WebOCR-update-$Version"
$PayloadRoot = Join-Path $StageRoot "WebOCR"

function Copy-CleanTree {
    param(
        [Parameter(Mandatory=$true)][string]$Source,
        [Parameter(Mandatory=$true)][string]$Destination,
        [string[]]$ExcludeRegex = @()
    )
    if (-not (Test-Path -LiteralPath $Source)) { return }
    $sourceRoot = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\', '/')
    Get-ChildItem -LiteralPath $Source -Recurse -File -Force | ForEach-Object {
        $rel = $_.FullName.Substring($sourceRoot.Length).TrimStart('\', '/')
        foreach ($rx in $ExcludeRegex) {
            if ($rel -match $rx) { return }
        }
        $target = Join-Path $Destination $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $PayloadRoot | Out-Null

dotnet publish (Join-Path $Root "WebOCR.Launcher\WebOCR.Launcher.csproj") -c Release -r win-x64 --self-contained true -o (Join-Path $PayloadRoot "_launcher_publish") | Out-Host
Move-Item -LiteralPath (Join-Path $PayloadRoot "_launcher_publish\WebOCR.exe") -Destination (Join-Path $PayloadRoot "WebOCR.exe") -Force
Remove-Item -LiteralPath (Join-Path $PayloadRoot "_launcher_publish") -Recurse -Force

dotnet publish (Join-Path $Root "MarketKeyMaker\MarketKeyMaker.csproj") -c Release -r win-x64 --self-contained true -o (Join-Path $PayloadRoot "_keymaker_publish") | Out-Host
Move-Item -LiteralPath (Join-Path $PayloadRoot "_keymaker_publish\MarketKeyMaker.exe") -Destination (Join-Path $PayloadRoot "MarketKeyMaker.exe") -Force
Remove-Item -LiteralPath (Join-Path $PayloadRoot "_keymaker_publish") -Recurse -Force

$webExcludes = @(
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
if (-not $IncludeCategoryDb) {
    $webExcludes += "^data[\\/]category[\\/]category_matching\.db$"
}

Copy-CleanTree -Source (Join-Path $Root "webocrcludev2") -Destination (Join-Path $PayloadRoot "webocrcludev2") -ExcludeRegex $webExcludes
Copy-CleanTree -Source (Join-Path $Root "backend\app") -Destination (Join-Path $PayloadRoot "backend\app") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "backend\data") -Destination (Join-Path $PayloadRoot "backend\data") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "backend\prompts") -Destination (Join-Path $PayloadRoot "backend\prompts") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "KeywordOcr.App\Bridge") -Destination (Join-Path $PayloadRoot "KeywordOcr.App\Bridge") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "tools") -Destination (Join-Path $PayloadRoot "tools") -ExcludeRegex @("__pycache__", "\.pyc$")
Copy-CleanTree -Source (Join-Path $Root "_EXCEL_BASIC_DATA") -Destination (Join-Path $PayloadRoot "_EXCEL_BASIC_DATA") -ExcludeRegex @("~\$", "\.tmp$")
Copy-CleanTree -Source (Join-Path $Root "ProductManager") -Destination (Join-Path $PayloadRoot "ProductManager") -ExcludeRegex @(
    "__pycache__",
    "\.pyc$",
    "^exports[\\/]",
    "^data[\\/]products_before_.*\.db$",
    "^data[\\/]result_uploads[\\/]"
)

Set-Content -LiteralPath (Join-Path $PayloadRoot "VERSION.txt") -Value $Version -Encoding ASCII
$UpdateZip = Join-Path $BuildRoot "WebOCR_Update_$Version.zip"
Remove-Item -LiteralPath $UpdateZip -Force -ErrorAction SilentlyContinue
Push-Location $StageRoot
try {
    tar.exe -a -c -f $UpdateZip "WebOCR"
}
finally {
    Pop-Location
}

$Size = (Get-Item -LiteralPath $UpdateZip).Length
Write-Host ("Update zip: {0}" -f $UpdateZip)
Write-Host ("Update size: {0:N2} MB" -f ($Size / 1MB))
