Write-Host "Switching folder to root folder (AAEmu/)"
Set-Location ..
Write-Host "Done"

$AspireOutputDir = ".server_files/aspire/docker"
$ComposeFile = "$AspireOutputDir/docker-compose.yaml"
$EnvFile = "$AspireOutputDir/.env"

if ((-not (Test-Path $ComposeFile)) -or (-not (Test-Path $EnvFile))) {
    Write-Error "No generated compose artifacts found at $AspireOutputDir"
    exit 1
}

Write-Host "Stopping AAEmu containers..."
docker compose --project-name aaemu --env-file $EnvFile -f $ComposeFile down
Write-Host "AAEmu containers stopped."
