param(
    [string]$OutputDirectory,
    [switch]$SkipNative,
    [switch]$Player
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$previousRustFlags = $env:RUSTFLAGS
$previousDriverOutput = $env:AADRIVER_OUT
$previousPlayer = $env:AADRIVER_PLAYER
Push-Location (Join-Path $repoRoot 'rust')
try {
    $metadataText = & cargo metadata --no-deps --format-version 1
    if ($LASTEXITCODE -ne 0) { throw 'Cargo metadata failed' }
    $metadata = $metadataText | ConvertFrom-Json
    if (-not $OutputDirectory) {
        $OutputDirectory = Join-Path $metadata.target_directory 'aadriver'
    }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    & rustup target add i686-pc-windows-msvc
    if ($LASTEXITCODE -ne 0) { throw 'The x86 Rust target could not be installed' }
    # Keep the bridge's allocator/runtime inside the DLL. Only byte buffers and opaque
    # handles cross its C ABI; the old client requires an x86 bridge.
    $env:RUSTFLAGS = "$previousRustFlags -C target-feature=+crt-static".Trim()
    & cargo build --release --target i686-pc-windows-msvc -p aaemu-pak --lib
    if ($LASTEXITCODE -ne 0) { throw 'Compressed pak bridge build failed' }
    $bridge = Join-Path $metadata.target_directory 'i686-pc-windows-msvc/release/aaemu_pak_bridge.dll'
    Copy-Item -LiteralPath $bridge -Destination (Join-Path $OutputDirectory 'aaemu_pak.dll')

    $env:RUSTFLAGS = $previousRustFlags
    & cargo build --release -p aaemu-pak -p aaemu-launcher -p aaemu-clientdriver
    if ($LASTEXITCODE -ne 0) { throw 'Pak tool and client tools build failed; close running client tools first' }
    if (-not $SkipNative) {
        $env:AADRIVER_OUT = $OutputDirectory
        $env:AADRIVER_PLAYER = if ($Player) { '1' } else { '0' }
        & (Join-Path $repoRoot 'rust/crates/aaemu-clientdriver/native/build.bat')
        if ($LASTEXITCODE -ne 0) { throw 'Native client driver build/tests failed' }
    }
    Write-Output "Compressed pak bridge and client tools ready: $OutputDirectory"
}
finally {
    $env:RUSTFLAGS = $previousRustFlags
    $env:AADRIVER_OUT = $previousDriverOutput
    $env:AADRIVER_PLAYER = $previousPlayer
    Pop-Location
}
