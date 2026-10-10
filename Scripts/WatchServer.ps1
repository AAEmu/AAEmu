# Shared watcher for WatchLoginServer.ps1 / WatchGameServer.ps1:
#   * builds the server with cargo and runs it;
#   * restarts it when it exits (crash, shutdown command, ...);
#   * rebuilds and restarts it when a Rust source file of the server or of aaemu-commons changes
#     (the successor of `dotnet watch` hot reload, which restarted on rude edits);
#   * uses .server_files\<ServerName> as application directory (Config.json, Data, ...) when it
#     holds a Config.json (the folder created by docker-install-local), otherwise the crate's
#     app\ folder (rust\crates\<Package>\app).
# Ctrl+C reaches the server too (same console) so it shuts down gracefully; source-change
# restarts stop the server process immediately.
param(
    [Parameter(Mandatory = $true)][string]$Package,
    [Parameter(Mandatory = $true)][string]$ServerName,
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$rustDir = Join-Path $repoRoot "rust"
$profileDir = if ($Release) { "release" } else { "debug" }
$cargoProfileArgs = if ($Release) { @("--release") } else { @() }

function Log([string]$message, [string]$color = "Cyan") {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $message" -ForegroundColor $color
}

# Application directory
$serverFiles = Join-Path $repoRoot ".server_files\$ServerName"
if (Test-Path (Join-Path $serverFiles "Config.json")) {
    $env:AAEMU_APP_PATH = $serverFiles
    Log "Using application directory $serverFiles"
} else {
    Remove-Item Env:AAEMU_APP_PATH -ErrorAction SilentlyContinue
    Log "Using application directory $(Join-Path $rustDir "crates\$Package\app")"
}

# Executable path (honours a custom target-dir from rust\.cargo\config.toml)
Push-Location $rustDir
try {
    $metadata = & cargo metadata --format-version 1 --no-deps | ConvertFrom-Json
} finally {
    Pop-Location
}
$exeName = if ($IsLinux -or $IsMacOS) { $Package } else { "$Package.exe" }
$executable = Join-Path (Join-Path $metadata.target_directory $profileDir) $exeName

# Source watchers
$global:lastChange = $null
$watchers = @()
foreach ($dir in @("crates\$Package\src", "crates\aaemu-commons\src")) {
    $path = Join-Path $rustDir $dir
    if (-not (Test-Path $path)) { continue }
    $watcher = New-Object System.IO.FileSystemWatcher $path, "*.rs"
    $watcher.IncludeSubdirectories = $true
    $watcher.EnableRaisingEvents = $true
    foreach ($eventName in @("Changed", "Created", "Deleted", "Renamed")) {
        Register-ObjectEvent -InputObject $watcher -EventName $eventName -Action { $global:lastChange = Get-Date } | Out-Null
    }
    $watchers += $watcher
}

function Build-Server {
    Log "Building $Package ($profileDir)..."
    Push-Location $rustDir
    try {
        & cargo build -p $Package @cargoProfileArgs
        return ($LASTEXITCODE -eq 0)
    } finally {
        Pop-Location
    }
}

# Waits until sources changed and stayed quiet for a second; returns when they did.
function Wait-ForChange {
    while ($true) {
        Start-Sleep -Milliseconds 500
        if ($null -ne $global:lastChange -and ((Get-Date) - $global:lastChange).TotalSeconds -ge 1) {
            $global:lastChange = $null
            return
        }
    }
}

$global:serverProcess = $null
try {
    Log "Watching $Package. Press Ctrl+C to stop."
    while ($true) {
        $global:lastChange = $null
        if (-not (Build-Server)) {
            Log "Build failed; waiting for source changes..." "Red"
            Wait-ForChange
            continue
        }
        Log "Starting $executable"
        $global:serverProcess = Start-Process -FilePath $executable -WorkingDirectory $rustDir -NoNewWindow -PassThru
        $null = $global:serverProcess.Handle # keeps the handle open so ExitCode is available
        Log "$Package started with PID $($global:serverProcess.Id)" "Green"
        while ($true) {
            Start-Sleep -Milliseconds 500
            if ($global:serverProcess.HasExited) {
                Log "$Package exited with code $($global:serverProcess.ExitCode); restarting..." "Yellow"
                Start-Sleep -Seconds 2
                break
            }
            if ($null -ne $global:lastChange -and ((Get-Date) - $global:lastChange).TotalSeconds -ge 1) {
                Log "Source change detected; rebuilding and restarting $Package..." "Yellow"
                Stop-Process -Id $global:serverProcess.Id -Force -ErrorAction SilentlyContinue
                $global:serverProcess.WaitForExit()
                break
            }
        }
    }
} finally {
    Log "Shutting down..." "Yellow"
    if ($null -ne $global:serverProcess -and -not $global:serverProcess.HasExited) {
        # Ctrl+C was delivered to the server as well: give it time to save and exit.
        if (-not $global:serverProcess.WaitForExit(30000)) {
            Log "Server did not exit in time, forcing termination" "Yellow"
            Stop-Process -Id $global:serverProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
    foreach ($watcher in $watchers) { $watcher.Dispose() }
    Get-EventSubscriber | Unregister-Event -ErrorAction SilentlyContinue
    Get-Job | Remove-Job -Force -ErrorAction SilentlyContinue
    Log "Watcher stopped" "Yellow"
}
