@echo off

cd ..
pushd AAEmu.WorldServer
cd AAEmu.World

dotnet build AAEmu.World.csproj -c Debug

if errorlevel 1 (
    echo.
    echo ОШИБКА: проект не скомпилирован.
    pause
    popd
    exit /b 1
)

dotnet run --project AAEmu.World.csproj -c Debug --no-build

pause
popd