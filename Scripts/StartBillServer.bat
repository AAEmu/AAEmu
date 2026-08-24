@echo off
setlocal
cd /d "%~dp0.."
dotnet build AAEmu.BillServer\AAEmu.BillServer.csproj -c Debug --nologo
if errorlevel 1 exit /b 1
echo Starting BillServer...
cd AAEmu.BillServer\bin\Debug\net10.0
AAEmu.BillServer.exe
