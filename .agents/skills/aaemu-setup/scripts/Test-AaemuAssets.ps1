<#
.SYNOPSIS
  Inventory AAEmu local assets; skip re-download when present.

.DESCRIPTION
  Checks .client_files client/launcher layout and AAEmu.Game/Data/compact.sqlite3.
  Never downloads MEGA/Drive multi-GB content. Optionally fetches the small
  GitHub launcher release when missing, or opens wiki download pages for HitL.

.PARAMETER RepoRoot
  Repository root. Default: three levels above this script.

.PARAMETER FetchLauncherIfMissing
  If launcher exe is missing, download latest AAEmu-Launcher release asset via gh or HTTPS.

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
    [switch] $FetchLauncherIfMissing,
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
$UrlLauncherReleases = "https://github.com/ZeromusXYZ/AAEmu-Launcher/releases"

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

function Find-LauncherExe {
    $prefer = Join-Path $clientFiles "launcher\AAEmu.Launcher\AAEmu.Launcher.exe"
    if (Test-Path -LiteralPath $prefer) { return $prefer }
    if (-not (Test-Path -LiteralPath $clientFiles)) { return $null }
    $any = Get-ChildItem -LiteralPath $clientFiles -Recurse -Filter "AAEmu.Launcher.exe" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($any) { return $any.FullName }
    return $null
}

function Get-ClientArchives {
    if (-not (Test-Path -LiteralPath $clientFiles)) { return @() }
    Get-ChildItem -LiteralPath $clientFiles -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -match '\.(7z|zip|rar)$' -or $_.Name -match 'ArcheAge|compact|Launcher' } |
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

# compact.sqlite3
$compact = Join-Path $RepoRoot "AAEmu.Game\Data\compact.sqlite3"
if (Test-Path -LiteralPath $compact) {
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
        Add-Result "compact.sqlite3" "PARTIAL" ("archive present; extract and copy to AAEmu.Game\Data\compact.sqlite3: " + $compactArchive[0]) $compactArchive[0]
    }
    else {
        Add-Result "compact.sqlite3" "MISSING" "Need compact.sqlite3 - HitL MEGA download, then place under AAEmu.Game\Data\" ""
    }
}

# Launcher
$launcher = Find-LauncherExe
if ($launcher) {
    Add-Result "launcher" "OK" "present - skip re-download" $launcher
}
elseif ($FetchLauncherIfMissing) {
    Write-Host "Launcher missing - fetching latest GitHub release..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $clientFiles | Out-Null
    $extractDir = Join-Path $clientFiles "launcher"
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
    $destArchive = Join-Path $clientFiles "AAEmu.Launcher-latest.7z"
    try {
        if (Get-Command gh -ErrorAction SilentlyContinue) {
            gh release download --repo ZeromusXYZ/AAEmu-Launcher --pattern "*.7z" --dir $clientFiles --clobber
            $got = Get-ChildItem -LiteralPath $clientFiles -Filter "AAEmu.Launcher*.7z" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($got) { $destArchive = $got.FullName }
        }
        else {
            $api = Invoke-RestMethod -Uri "https://api.github.com/repos/ZeromusXYZ/AAEmu-Launcher/releases/latest" -Headers @{ "User-Agent" = "aaemu-setup" }
            $asset = $api.assets | Where-Object { $_.name -like "*.7z" } | Select-Object -First 1
            if (-not $asset) { throw "No .7z asset on latest release" }
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $destArchive -UseBasicParsing
        }
        $sevenZ = @(
            "${env:ProgramFiles}\7-Zip\7z.exe",
            "${env:ProgramFiles(x86)}\7-Zip\7z.exe"
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $sevenZ -and (Get-Command 7z -ErrorAction SilentlyContinue)) { $sevenZ = "7z" }
        if ($sevenZ) {
            & $sevenZ x $destArchive "-o$extractDir" -y | Out-Null
        }
        else {
            Write-Host "Downloaded $destArchive but no 7z found - extract manually to .client_files\launcher\" -ForegroundColor Yellow
        }
        $launcher = Find-LauncherExe
        if ($launcher) {
            Add-Result "launcher" "OK" "fetched and extracted" $launcher
        }
        else {
            Add-Result "launcher" "PARTIAL" "archive downloaded; extract to .client_files\launcher\" $destArchive
        }
    }
    catch {
        Add-Result "launcher" "MISSING" ("fetch failed: {0} - {1}" -f $_.Exception.Message, $UrlLauncherReleases) ""
    }
}
else {
    Add-Result "launcher" "MISSING" "AAEmu.Launcher.exe not found (use -FetchLauncherIfMissing or HitL GitHub)" ""
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
    if ($missing.Name -contains "launcher") {
        Start-Process $UrlLauncherReleases
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
    Write-Host "    Copy final file to AAEmu.Game\Data\compact.sqlite3"
}
if ($missing.Name -contains "launcher") {
    Write-Host "  - Launcher: $UrlLauncherReleases  (or re-run with -FetchLauncherIfMissing)"
}
Write-Host "  Re-run this script after downloads/extracts. Existing OK assets will be skipped."
exit 1
