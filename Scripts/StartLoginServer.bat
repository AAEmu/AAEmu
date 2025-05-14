@echo off
cd ..
pushd AAEmu.Login
    dotnet build -f net8.0 AAEmu.Login.csproj
	pause
popd
