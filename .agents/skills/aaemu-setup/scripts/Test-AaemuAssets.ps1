<#
.SYNOPSIS
  Inventory AAEmu local assets; skip re-download when present.

.DESCRIPTION
  Checks the .client_files client layout, the game server's compact.sqlite3
  (rust/crates/aaemu-game/app/Data/ or .server_files/AAEmu.Game/Data/) and
  whether the repository's own client launcher (aaemu-launcher) is built.
  Never downloads MEGA/Drive multi-GB content. Optionally builds aaemu-launcher
  with cargo when missing, or opens wiki download pages for HitL.

.PARAMETER RepoRoot
  Repository root. Default: three levels above this script.

.PARAMETER BuildLauncherIfMissing
  If aaemu-launcher is not built, run `cargo build --release -p aaemu-launcher`
  in rust/ (needs Rust via rustup). Alias: -FetchLauncherIfMissing.

.PARAMETER OpenMissingDownloadPages
  Open browser tabs for missing client/compact downloads (human completes MEGA/Drive).

.PARAMETER MinGamePakBytes
  Minimum size to treat game_pak as complete (default 1 GiB).

.PARAMETER MinCompactBytes
  Minimum size for compact.sqlite3 (default 1 MiB).
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = "",
    [Alias("FetchLauncherIfMissing")]
    [switch] $BuildLauncherIfMissing,
    [switch] $OpenMissingDownloadPages,
    [long] $MinGamePakBytes = 1GB,
    [long] $MinCompactBytes = 1MB
)

$ErrorActionPreference = "Stop"

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
}
$RepoRoot = (Resolve-Path $RepoRoot).Path
Set-Location $RepoRoot

$clientFiles = Join-Path $RepoRoot ".client_files"
$clientRootCandidates = @(
    (Join-Path $clientFiles "ArcheAge 1.2 (r208022) for AAEmu"),
    (Join-Path $clientFiles "ArcheAge 1.2.4.13 (r208022) for AAEmu")
)

$UrlClientMega = "https://mega.nz/folder/GnwjQCrZ#WNWzX_lDvkzCqoTtt7I42Q"
$UrlClientMegaAlt = "https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg"
$UrlCompactMega = "https://mega.nz/file/6vxlAZiJ#Xb8VWnG4rFRRklxw6LZaYBPdZvC6j73xLPKIPAK80S8"
$RustDir = Join-Path $RepoRoot "rust"
$LauncherBuildCmd = "cd rust; cargo build --release -p aaemu-launcher"

function Format-Size([long] $n) {
    if ($n -ge 1GB) { return ("{0:N2} GB" -f ($n / 1GB)) }
    if ($n -ge 1MB) { return ("{0:N2} MB" -f ($n / 1MB)) }
    if ($n -ge 1KB) { return ("{0:N2} KB" -f ($n / 1KB)) }
    return "$n B"
}

function Find-GamePak {
    foreach ($root in $clientRootCandidates) {
        $pak = Join-Path $root "game_pak"
        if (Test-Path -LiteralPath $pak) {
            return [pscustomobject]@{ Path = $pak; Root = $root }
        }
    }
    if (-not (Test-Path -LiteralPath $clientFiles)) { return $null }
    $any = Get-ChildItem -LiteralPath $clientFiles -Recurse -Filter "game_pak" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($any) {
        return [pscustomobject]@{ Path = $any.FullName; Root = $any.DirectoryName }
    }
    return $null
}

function Find-ArcheageExe {
    param([string] $PreferRoot)
    if ($PreferRoot) {
        $p = Join-Path $PreferRoot "bin32\archeage.exe"
        if (Test-Path -LiteralPath $p) { return $p }
    }
    if (-not (Test-Path -LiteralPath $clientFiles)) { return $null }
    $any = Get-ChildItem -LiteralPath $clientFiles -Recurse -Filter "archeage.exe" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($any) { return $any.FullName }
    return $null
}

function Get-CargoTargetDir {
    # Honors CARGO_TARGET_DIR and rust/.cargo/config.toml (target-dir) through cargo metadata.
    if ($env:CARGO_TARGET_DIR) {
        if ([System.IO.Path]::IsPathRooted($env:CARGO_TARGET_DIR)) { return $env:CARGO_TARGET_DIR }
        return (Join-Path $RustDir $env:CARGO_TARGET_DIR)
    }
    if ((Test-Path -LiteralPath $RustDir) -and (Get-Command cargo -ErrorAction SilentlyContinue)) {
        Push-Location $RustDir
        try {
            $json = cargo metadata --format-version 1 --no-deps --offline 2>$null
            if ($LASTEXITCODE -eq 0 -and $json) {
                $dir = ($json | Out-String | ConvertFrom-Json).target_directory
                if ($dir) { return $dir }
            }
        }
        catch { }
        finally { Pop-Location }
    }
    return (Join-Path $RustDir "target")
}

function Find-LauncherExe {
    $target = Get-CargoTargetDir
    $candidates = foreach ($prof in @("release", "debug")) {
        foreach ($name in @("aaemu-launcher.exe", "aaemu-launcher")) {
            $p = Join-Path (Join-Path $target $prof) $name
            if (Test-Path -LiteralPath $p -PathType Leaf) { Get-Item -LiteralPath $p }
        }
    }
    $newest = $candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest) { return $newest.FullName }
    return $null
}

function Get-ClientArchives {
    if (-not (Test-Path -LiteralPath $clientFiles)) { return @() }
    Get-ChildItem -LiteralPath $clientFiles -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -match '\.(7z|zip|rar)$' -or $_.Name -match 'ArcheAge|compact' } |
        ForEach-Object { $_.FullName }
}

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Name, [string]$Status, [string]$Detail, [string]$Path = "") {
    $results.Add([pscustomobject]@{ Name = $Name; Status = $Status; Detail = $Detail; Path = $Path }) | Out-Null
}

# Client
$pakInfo = Find-GamePak
if ($pakInfo) {
    $pakItem = Get-Item -LiteralPath $pakInfo.Path
    $exe = Find-ArcheageExe -PreferRoot $pakInfo.Root
    $sizeText = Format-Size $pakItem.Length
    if ($pakItem.Length -ge $MinGamePakBytes -and $exe) {
        Add-Result "client" "OK" "game_pak $sizeText; archeage.exe present - skip re-download" $pakInfo.Path
    }
    elseif ($pakItem.Length -lt $MinGamePakBytes) {
        Add-Result "client" "PARTIAL" "game_pak too small ($sizeText); re-download or extract may be needed" $pakInfo.Path
    }
    else {
        Add-Result "client" "PARTIAL" "game_pak present but archeage.exe missing - finish extract" $pakInfo.Path
    }
}
else {
    $archives = @(Get-ClientArchives | Where-Object { $_ -match 'ArcheAge|r208022|1\.2' })
    if ($archives.Count -gt 0) {
        Add-Result "client" "PARTIAL" ("archive present, not extracted: " + ($archives -join "; ")) $archives[0]
    }
    else {
        Add-Result "client" "MISSING" "Need ArcheAge 1.2 (r208022) client - HitL MEGA/Drive download" ""
    }
}

# compact.sqlite3: game application directory (source checkout) or the Docker server files
$compactTarget = Join-Path $RepoRoot "rust\crates\aaemu-game\app\Data\compact.sqlite3"
$compactCandidates = @(
    $compactTarget,
    (Join-Path $RepoRoot ".server_files\AAEmu.Game\Data\compact.sqlite3")
)
$compact = $compactCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($compact) {
    $c = Get-Item -LiteralPath $compact
    $sizeText = Format-Size $c.Length
    if ($c.Length -ge $MinCompactBytes) {
        Add-Result "compact.sqlite3" "OK" "$sizeText - skip re-download" $compact
    }
    else {
        Add-Result "compact.sqlite3" "PARTIAL" "file too small ($sizeText)" $compact
    }
}
else {
    $compactArchive = @(Get-ClientArchives | Where-Object { $_ -match 'compact|Compact|Server_Compact' })
    if ($compactArchive.Count -gt 0) {
        Add-Result "compact.sqlite3" "PARTIAL" ("archive present; extract and copy to rust\crates\aaemu-game\app\Data\compact.sqlite3: " + $compactArchive[0]) $compactArchive[0]
    }
    else {
        Add-Result "compact.sqlite3" "MISSING" "Need compact.sqlite3 - HitL MEGA download, then place under rust\crates\aaemu-game\app\Data\" ""
    }
}

# Launcher: the repository's own aaemu-launcher (rust/crates/aaemu-launcher)
$launcher = Find-LauncherExe
if ($launcher) {
    Add-Result "launcher" "OK" "aaemu-launcher built - start the client with: aaemu-launcher --client <client>\bin32\archeage.exe --login 127.0.0.1:1237 --user NAME" $launcher
}
elseif ($BuildLauncherIfMissing) {
    if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
        Add-Result "launcher" "MISSING" "cargo not found - install Rust via https://rustup.rs/, then: $LauncherBuildCmd" ""
    }
    else {
        Write-Host "aaemu-launcher not built - running cargo build --release -p aaemu-launcher ..." -ForegroundColor Cyan
        Push-Location $RustDir
        try { cargo build --release -p aaemu-launcher }
        finally { Pop-Location }
        $launcher = Find-LauncherExe
        if ($launcher) {
            Add-Result "launcher" "OK" "aaemu-launcher built" $launcher
        }
        else {
            Add-Result "launcher" "MISSING" "cargo build failed - see the output above" ""
        }
    }
}
else {
    Add-Result "launcher" "MISSING" "aaemu-launcher not built - run: $LauncherBuildCmd (or re-run with -BuildLauncherIfMissing)" ""
}

Write-Host ""
Write-Host "AAEmu asset inventory - $RepoRoot" -ForegroundColor Cyan
Write-Host ("-" * 72)
foreach ($r in $results) {
    $color = switch ($r.Status) {
        "OK" { "Green" }
        "PARTIAL" { "Yellow" }
        default { "Red" }
    }
    Write-Host ("[{0}] {1}" -f $r.Status, $r.Name) -ForegroundColor $color
    Write-Host ("      {0}" -f $r.Detail)
    if ($r.Path) { Write-Host ("      {0}" -f $r.Path) -ForegroundColor DarkGray }
}
Write-Host ("-" * 72)

$missing = @($results | Where-Object { $_.Status -eq "MISSING" })
$partial = @($results | Where-Object { $_.Status -eq "PARTIAL" })

if ($OpenMissingDownloadPages) {
    if ($missing.Name -contains "client" -or $partial.Name -contains "client") {
        Start-Process $UrlClientMega
        Start-Process $UrlClientMegaAlt
    }
    if ($missing.Name -contains "compact.sqlite3" -or $partial.Name -contains "compact.sqlite3") {
        Start-Process $UrlCompactMega
    }
}

if ($missing.Count -eq 0 -and $partial.Count -eq 0) {
    Write-Host "All required assets present. Do not re-download multi-GB packages." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "HitL next steps:" -ForegroundColor Yellow
if ($missing.Name -contains "client" -or $partial.Name -contains "client") {
    Write-Host "  - Client: $UrlClientMega"
    Write-Host "    Save under .client_files\ then extract so game_pak and bin32\archeage.exe exist."
}
if ($missing.Name -contains "compact.sqlite3" -or $partial.Name -contains "compact.sqlite3") {
    Write-Host "  - compact.sqlite3: $UrlCompactMega"
    Write-Host "    Copy final file to rust\crates\aaemu-game\app\Data\compact.sqlite3"
}
if ($missing.Name -contains "launcher") {
    Write-Host "  - Launcher: $LauncherBuildCmd  (or re-run with -BuildLauncherIfMissing)"
}
Write-Host "  Re-run this script after downloads/extracts. Existing OK assets will be skipped."
exit 1
