#!/usr/bin/env bash
# Stop local Returns client + AAEmu Login/Game for development.
# Does NOT stop MySQL (aaemu-mysql Docker) so character/account data stays.

set -euo pipefail
echo "[dev_stop] Stopping ArcheAge client and AAEmu servers..."

taskkill //F //IM archeage.exe >/dev/null 2>&1 || true
taskkill //F //IM AAEmu.Game.exe >/dev/null 2>&1 || true
taskkill //F //IM AAEmu.Login.exe >/dev/null 2>&1 || true

powershell.exe -NoProfile -Command \
  "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { \$_.CommandLine -match 'AAEmu\\.(Game|Login)' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force -ErrorAction SilentlyContinue }" \
  >/dev/null 2>&1 || true

echo "[dev_stop] Done. MySQL left running."
