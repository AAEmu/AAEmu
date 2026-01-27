@echo off
cd ..
pushd AAEmu.Login
    dotnet build -f net10.0 AAEmu.Login.csproj
	pause
popd
