@echo off
REM Stop local Returns client + AAEmu Login/Game for development.
REM Does NOT stop MySQL (aaemu-mysql Docker) so character/account data stays.

echo [dev_stop] Stopping ArcheAge client and AAEmu servers...
taskkill /F /IM archeage.exe >nul 2>&1
taskkill /F /IM AAEmu.Game.exe >nul 2>&1
taskkill /F /IM AAEmu.Login.exe >nul 2>&1

REM Kill dotnet hosts that are actually running AAEmu.Game / AAEmu.Login
powershell -NoProfile -Command ^
  "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" ^| Where-Object { $_.CommandLine -match 'AAEmu\\.(Game|Login)' } ^| ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"

echo [dev_stop] Done. MySQL left running.
exit /b 0
