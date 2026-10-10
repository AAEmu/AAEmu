# Builds and runs the Rust login server, restarting it when it exits and rebuilding/restarting it
# when its sources change. Application directory: .server_files\AAEmu.Login when it holds a
# Config.json, else rust\crates\aaemu-login\app. Pass -Release for an optimized build.
param([switch]$Release)
& (Join-Path $PSScriptRoot "WatchServer.ps1") -Package "aaemu-login" -ServerName "AAEmu.Login" -Release:$Release
