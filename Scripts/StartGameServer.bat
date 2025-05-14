@echo off
cd ..
pushd AAEmu.Game
    dotnet build -f net8.0 AAEmu.Game.csproj
	pause
popd
