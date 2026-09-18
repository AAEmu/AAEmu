@echo off
setlocal
cd /d "%~dp0.."
echo Building AAEmu.World (zone-authority host, pulls AAEmu.Game)...
dotnet build AAEmu.WorldServer\AAEmu.World\AAEmu.World.csproj -c Debug --nologo
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo.
echo Output: AAEmu.WorldServer\AAEmu.World\bin\Debug\net10.0\AAEmu.World.exe
echo Data\compact.sqlite3 must be present next to that exe (copied from AAEmu.Game\Data).
exit /b 0
