@echo off
cd ..
pushd AAEmu.Login
    dotnet build -f net9.0 AAEmu.Login.csproj
	pause
popd
