# Builds and runs the Rust game server, restarting it when it exits and rebuilding/restarting it
# when its sources change. Application directory: .server_files\AAEmu.Game when it holds a
# Config.json, else rust\crates\aaemu-game\app. Pass -Release for an optimized build.
param([switch]$Release)
& (Join-Path $PSScriptRoot "WatchServer.ps1") -Package "aaemu-game" -ServerName "AAEmu.Game" -Release:$Release
