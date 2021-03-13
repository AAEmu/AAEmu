dotnet build
:dotnet publish -c debug -r win7-x64
:dotnet publish -c release --runtime win10-x64

dotnet publish -r win10-x64 -p:Configuration=Release -p:PublishSingleFile=true

pause
