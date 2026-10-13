param(
    [Parameter(Mandatory = $true)][string]$ClientDirectory,
    [Parameter(Mandatory = $true)][string]$PackDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$LoginServer = '127.0.0.1:1237',
    [string]$Language = 'en_us',
    [string]$LauncherExe,
    [string]$SevenZip = 'C:\Program Files\7-Zip\7z.exe'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ClientDirectory = (Resolve-Path -LiteralPath $ClientDirectory).Path.TrimEnd('\')
$PackDirectory = (Resolve-Path -LiteralPath $PackDirectory).Path.TrimEnd('\')
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
foreach ($source in @($ClientDirectory, $PackDirectory, $repoRoot)) {
    if ($OutputDirectory.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
        $OutputDirectory.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Distribution output must be outside the source client, pack and repository.'
    }
}
$archive = $OutputDirectory + '.zip'
if ((Test-Path -LiteralPath $OutputDirectory) -or (Test-Path -LiteralPath $archive)) {
    throw 'Use a new output path; this script never replaces an existing distribution.'
}
foreach ($required in @((Join-Path $ClientDirectory 'bin32\archeage.exe'),
    (Join-Path $ClientDirectory 'game_pak'), (Join-Path $PackDirectory 'game.aacm'), $SevenZip)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required file missing: $required" }
}

Push-Location (Join-Path $repoRoot 'rust')
try {
    $metadata = (& cargo metadata --no-deps --format-version 1) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Cargo metadata failed' }
}
finally { Pop-Location }
$buildDirectory = Join-Path ([IO.Path]::GetDirectoryName($OutputDirectory)) 'build\player'
& (Join-Path $PSScriptRoot 'build-client-pak.ps1') -Player -OutputDirectory $buildDirectory
$tool = Join-Path $metadata.target_directory 'release\aaemu-pak.exe'
$packInfo = (& $tool info (Join-Path $PackDirectory 'game.aacm')) | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Compressed pack manifest is invalid' }
$blobs = @(Get-ChildItem -LiteralPath $PackDirectory -Filter '*.aacb' -File)
if (($blobs | Measure-Object Length -Sum).Sum -ne $packInfo.stored_bytes) {
    throw 'Pack directory has missing or unused blobs; supply a clean complete pack.'
}
Write-Output 'Checking that the compressed pack matches the source client...'
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $ClientDirectory 'game_pak') -Algorithm SHA256).Hash
if ($sourceHash.ToLowerInvariant() -ne $packInfo.logical_sha256 -or
    (Get-Item -LiteralPath (Join-Path $ClientDirectory 'game_pak')).Length -ne $packInfo.logical_size) {
    throw 'Compressed pack does not match this client game_pak.'
}

New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
Write-Output 'Copying the client and compressed pack...'
foreach ($subdirectory in @('bin32', 'game')) {
    $sourceRoot = Join-Path $ClientDirectory $subdirectory
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        if ($file.Extension -in @('.log', '.csv', '.bak', '.pdb', '.i64', '.idb', '.obj', '.lib', '.exp')) { continue }
        $relative = $file.FullName.Substring($ClientDirectory.Length + 1)
        $destination = Join-Path $OutputDirectory $relative
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
foreach ($name in @('GameIcon.ico', 'hotkey.g')) {
    $file = Join-Path $ClientDirectory $name
    if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $OutputDirectory }
}
Copy-Item -LiteralPath (Join-Path $PackDirectory 'game.aacm') -Destination $OutputDirectory
foreach ($blob in $blobs) { Copy-Item -LiteralPath $blob.FullName -Destination $OutputDirectory }
foreach ($name in @('aadriver.dll', 'aaemu_pak.dll', 'aainject.exe')) {
    Copy-Item -LiteralPath (Join-Path $buildDirectory $name) -Destination $OutputDirectory
}
Copy-Item -LiteralPath (Join-Path $metadata.target_directory 'release\aaemu-launcher.exe') -Destination $OutputDirectory
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'client-distribution') -File |
    Copy-Item -Destination $OutputDirectory
if ($LauncherExe) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-player-launcher.ps1') `
        -LauncherExe $LauncherExe -PlayerDriver (Join-Path $buildDirectory 'aadriver.dll') `
        -NativeLauncher (Join-Path $metadata.target_directory 'release\aaemu-launcher.exe') `
        -OutputExe (Join-Path $OutputDirectory 'AAEmuLauncher.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Player GUI launcher adaptation failed' }
}
@{ login = $LoginServer; language = $Language } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'server.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE.GPL') -Destination (Join-Path $OutputDirectory 'LICENSE-AAEmu.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE.MIT') -Destination $OutputDirectory

# Include the corresponding repository source, overlaying only the packaging/runtime
# files used for this build. Personal configs and local client assets are untracked.
$sourceArchive = Join-Path $OutputDirectory 'AAEmu-tools-source.zip'
& git -C $repoRoot archive --format=zip "--output=$sourceArchive" HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source archive failed' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceZip = [IO.Compression.ZipFile]::Open($sourceArchive, [IO.Compression.ZipArchiveMode]::Update)
try {
    $overlay = @('Docs/wiki/Client.md', 'Scripts/build-client-pak.ps1', 'Scripts/build-client-distribution.ps1', 'Scripts/build-player-launcher.ps1',
        'rust/crates/aaemu-clientdriver/native/aadriver.cpp', 'rust/crates/aaemu-clientdriver/native/build.bat',
        'rust/crates/aaemu-clientdriver/native/virtualpak.cpp', 'rust/crates/aaemu-clientdriver/native/auction_ui.lua',
        'rust/crates/aaemu-clientdriver/native/auction_ui.rc', 'rust/crates/aaemu-clientdriver/native/auction_ui_tests.lua',
        'rust/crates/aaemu-launcher/src/main.rs')
    $overlay += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'client-distribution') -File |
        ForEach-Object { 'Scripts/client-distribution/' + $_.Name }
    foreach ($relative in $overlay) {
        $entry = $sourceZip.GetEntry($relative)
        if ($entry) { $entry.Delete() }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceZip,
            (Join-Path $repoRoot $relative), $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $sourceZip.Dispose() }

Write-Output 'Verifying every compressed chunk and the complete decoded stream...'
& $tool verify (Join-Path $OutputDirectory 'game.aacm')
if ($LASTEXITCODE -ne 0) { throw 'Distribution compressed pack verification failed' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $OutputDirectory 'Play.ps1') -User distributioncheck -DryRun
if ($LASTEXITCODE -ne 0) { throw 'Distribution launcher dry run failed' }

Write-Output 'Writing ZIP64 archive (the pack is already compressed)...'
Push-Location ([IO.Path]::GetDirectoryName($OutputDirectory))
try {
    & $SevenZip a -tzip -mx=0 -bd $archive ([IO.Path]::GetFileName($OutputDirectory))
    if ($LASTEXITCODE -ne 0) { throw 'Distribution ZIP creation failed' }
}
finally { Pop-Location }
& $SevenZip t -bd $archive
if ($LASTEXITCODE -ne 0) { throw 'Distribution ZIP integrity check failed' }
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $([IO.Path]::GetFileName($archive))" |
    Set-Content -LiteralPath ($archive + '.sha256') -Encoding ASCII
Write-Output "Distribution ready: $archive"
