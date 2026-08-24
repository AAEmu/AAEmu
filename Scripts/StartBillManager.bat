@echo off
setlocal
cd /d "%~dp0.."
dotnet build AAEmu.BillManager\AAEmu.BillManager.csproj -c Debug --nologo
if errorlevel 1 exit /b 1
start "" "AAEmu.BillManager\bin\Debug\net10.0-windows\AAEmu.BillManager.exe"
