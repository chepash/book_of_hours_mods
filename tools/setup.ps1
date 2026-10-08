# Sets up the modding environment for Book of Hours:
#   1. .NET SDK 8 (winget) + ilspycmd (dotnet global tool)
#   2. BepInEx 5 (x64) into the game folder
#   3. BepInEx ScriptEngine (hot reload of plugins from BepInEx\scripts, F6)
#   4. Decompiles SecretHistories.Main.dll into .\decompiled (git-ignored)
# Re-runnable: each step is skipped if already done.
param(
    [string]$GameDir = "d:\games\steamapps\common\Book of Hours"
)
$ErrorActionPreference = "Stop"
$RepoDir = Split-Path $PSScriptRoot -Parent
$Tmp = Join-Path $env:TEMP "boh_mods_setup"
New-Item -ItemType Directory -Force $Tmp | Out-Null

function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [Environment]::GetEnvironmentVariable("Path", "User") + ";" +
                (Join-Path $env:USERPROFILE ".dotnet\tools")
}

function Get-GithubAsset([string]$Repo, [string]$Pattern) {
    $releases = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases" -Headers @{ "User-Agent" = "boh-setup" }
    foreach ($r in $releases) {
        $asset = $r.assets | Where-Object { $_.name -match $Pattern } | Select-Object -First 1
        if ($asset) { return $asset }
    }
    throw "No asset matching '$Pattern' in $Repo"
}

# --- 1. .NET SDK + ilspycmd -------------------------------------------------
Refresh-Path
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -or -not (dotnet --list-sdks | Select-String '^8\.')) {
    Write-Host "Installing .NET SDK 8..."
    winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements --silent
    Refresh-Path
}
if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) {
    Write-Host "Installing ilspycmd..."
    # Newer ilspycmd releases target newer runtimes than .NET 8.
    dotnet tool install -g ilspycmd --version 8.2.0.7535
    Refresh-Path
}

# --- 2. BepInEx 5 ---------------------------------------------------------
if (-not (Test-Path (Join-Path $GameDir "BepInEx\core\BepInEx.dll"))) {
    $asset = Get-GithubAsset "BepInEx/BepInEx" '^BepInEx_win_x64_5\.4\.[\d.]+\.zip$'
    Write-Host "Installing $($asset.name)..."
    $zip = Join-Path $Tmp $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip
    Expand-Archive $zip -DestinationPath $GameDir -Force
}

# --- 3. ScriptEngine (hot reload) -----------------------------------------
$scriptEngineDll = Join-Path $GameDir "BepInEx\plugins\ScriptEngine.dll"
if (-not (Test-Path $scriptEngineDll)) {
    $asset = Get-GithubAsset "BepInEx/BepInEx.Debug" '^ScriptEngine.*\.zip$'
    Write-Host "Installing $($asset.name)..."
    $zip = Join-Path $Tmp $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip
    Expand-Archive $zip -DestinationPath (Join-Path $Tmp "se") -Force
    $dll = Get-ChildItem (Join-Path $Tmp "se") -Recurse -Filter ScriptEngine.dll | Select-Object -First 1
    New-Item -ItemType Directory -Force (Split-Path $scriptEngineDll) | Out-Null
    Copy-Item $dll.FullName $scriptEngineDll
}
New-Item -ItemType Directory -Force (Join-Path $GameDir "BepInEx\scripts") | Out-Null

# BepInEx.cfg: show the log console, and start the chainloader from Camera's static ctor.
# The default (Application) fires from the Input System's init, before the engine can tick
# MonoBehaviours, so ScriptEngine's coroutines and Update never run in this game.
# BepInEx fills in every other key on first launch.
$cfgDir = Join-Path $GameDir "BepInEx\config"
$cfg = Join-Path $cfgDir "BepInEx.cfg"
New-Item -ItemType Directory -Force $cfgDir | Out-Null
if (-not (Test-Path $cfg)) {
    Set-Content $cfg -Encoding utf8 -Value "[Logging.Console]`nEnabled = true`n`n[Preloader.Entrypoint]`nType = Camera`n"
} else {
    $lines = Get-Content $cfg
    $section = ""
    $lines = foreach ($line in $lines) {
        if ($line -match '^\[(.+)\]$') { $section = $Matches[1] }
        if ($section -eq "Logging.Console" -and $line -match '^Enabled\s*=') { "Enabled = true" }
        elseif ($section -eq "Preloader.Entrypoint" -and $line -match '^Type\s*=') { "Type = Camera" }
        else { $line }
    }
    Set-Content $cfg -Encoding utf8 -Value $lines
}

# ScriptEngine: load scripts on start and reload them when a dll changes.
$seCfg = Join-Path $cfgDir "com.bepis.bepinex.scriptengine.cfg"
if (-not (Test-Path $seCfg)) {
    Set-Content $seCfg -Encoding utf8 -Value "[General]`nLoadOnStart = true`n`n[AutoReload]`nEnableFileSystemWatcher = true`n"
}

# --- 4. Decompile game code ------------------------------------------------
$out = Join-Path $RepoDir "decompiled\SecretHistories.Main"
if (-not (Test-Path $out)) {
    Write-Host "Decompiling SecretHistories.Main.dll..."
    $managed = Join-Path $GameDir "bh_Data\Managed"
    $env:DOTNET_ROLL_FORWARD = "Major"  # ilspycmd 8.x targets net6, run it on net8

    ilspycmd -p -o $out -r $managed (Join-Path $managed "SecretHistories.Main.dll")
}

Write-Host "Done."
