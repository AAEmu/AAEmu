Write-Host "Switching folder to root folder (AAEmu/)"
Set-Location ..
Write-Host "Done"

$AppHostProject = "AAEmu.Aspire.AppHost/AAEmu.Aspire.AppHost.csproj"
$AspireCliVersion = "13.1.2"
$AspireToolDir = ".server_files/tools"
$AspireOutputDir = ".server_files/aspire/docker"
$AspireCli = "$AspireToolDir/aspire"
$ComposeFile = "$AspireOutputDir/docker-compose.yaml"
$EnvFile = "$AspireOutputDir/.env"

function Set-EnvDefault {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    if (-not (Test-Path $Path)) {
        return
    }

    $lines = Get-Content -Path $Path
    $prefix = "$Name="
    $found = $false

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].StartsWith($prefix)) {
            $found = $true
            if ($lines[$i] -eq $prefix) {
                $lines[$i] = "$prefix$Value"
            }
            break
        }
    }

    if (-not $found) {
        $lines += "$prefix$Value"
    }

    Set-Content -Path $Path -Value $lines
}

function Apply-EnvDefaults {
    Set-EnvDefault -Path $EnvFile -Name "COMPOSE_PROJECT_NAME" -Value "aaemu"
    Set-EnvDefault -Path $EnvFile -Name "AAEMU_NETWORK_NAME" -Value "aaemu-net"
    Set-EnvDefault -Path $EnvFile -Name "AAEMU_DB_HOST_PORT" -Value "3306"
    Set-EnvDefault -Path $EnvFile -Name "AAEMU_ADMINER_HOST_PORT" -Value "8080"
    Set-EnvDefault -Path $EnvFile -Name "AAEMU_LOGIN_PUBLIC_PORT" -Value "1237"
    Set-EnvDefault -Path $EnvFile -Name "AAEMU_GAME_PUBLIC_PORT" -Value "1239"
    Set-EnvDefault -Path $EnvFile -Name "AAEMU_GAME_STREAM_PUBLIC_PORT" -Value "1250"
    Set-EnvDefault -Path $EnvFile -Name "AAEMU_DASHBOARD_HOST_PORT" -Value "18888"
    Set-EnvDefault -Path $EnvFile -Name "AAEMU_LOGIN_PORT" -Value "8080"
    Set-EnvDefault -Path $EnvFile -Name "AAEMU_DB_PASSWORD" -Value "password"
}

New-Item -ItemType Directory -Path $AspireToolDir -Force | Out-Null
New-Item -ItemType Directory -Path $AspireOutputDir -Force | Out-Null

if (-not (Test-Path $AspireCli)) {
    Write-Host "Installing Aspire CLI..."
    dotnet tool install --tool-path $AspireToolDir aspire.cli --version $AspireCliVersion
}

if ((-not (Test-Path $ComposeFile)) -or (-not (Test-Path $EnvFile))) {
    Write-Host "Compose artifacts not found. Generating..."
    & $AspireCli publish --project $AppHostProject --output-path $AspireOutputDir --non-interactive
}

Apply-EnvDefaults

Write-Host "Starting AAEmu containers..."
docker compose --project-name aaemu --env-file $EnvFile -f $ComposeFile up -d
Write-Host "AAEmu containers are running."
