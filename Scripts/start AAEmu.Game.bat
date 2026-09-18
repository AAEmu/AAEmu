@echo off
REM Prefer the zone-authority host. Bare AAEmu.Game.exe is library-era only —
REM do not run it on :1239 next to World.
echo Redirect: this branch runs World, not standalone Game.
call "%~dp0Start AAemu.World.bat"
