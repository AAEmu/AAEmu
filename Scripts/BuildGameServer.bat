@echo off
cd ..
pushd AAEmu.Game
    dotnet build -f net9.0 AAEmu.Game.csproj
	pause
popd
