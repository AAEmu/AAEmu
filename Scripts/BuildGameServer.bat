@echo off
cd ..
pushd AAEmu.Game
    dotnet build -f net10.0 AAEmu.Game.csproj
	pause
popd
