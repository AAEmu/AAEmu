# Switch to the root folder
Write-Host "Switching folder to root folder (AAEmu\)"
Set-Location ..
Write-Host "Done"

Write-Host "Shutting all containers and Updating AAEmu..."
docker compose down
git pull
Write-Host "Done"

# Build the Dockerfile
Write-Host "Rebuilding Docker Containers..."
docker compose build --no-cache
Write-Host "Done"

# The End
Write-Host "Launch by running 'docker compose up -d' in the root of AAEmu and connect to 127.0.0.1 in AAEmu.Launcher"
Write-Host "You can stop the server by issuing the command 'docker compose down' in the root of AAEmu"
Write-Host "If you want a development environment (with quick reload and auto-rebuild), use the command docker compose watch or docker compose up --watch and DO NOT CLOSE the terminal"
Write-Host "Update done"