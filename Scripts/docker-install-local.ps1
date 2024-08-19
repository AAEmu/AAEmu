# Switch to the root folder
Write-Host "Switching folder to root folder (AAEmu\)"
Set-Location ..
Write-Host "Done"


# Asking the user if they are sure to continue with a fresh installation
while ($true) {
    # Prompt the user for input
    $answer = Read-Host "This script will wipe everything to make a fresh installation. Are you sure? (Y/N)"
    # Convert the input to uppercase to handle both 'y' and 'n' cases
    $answer = $answer.ToUpper()
    # Act based on the user's input
    if ($answer -eq "Y") {
        Write-Host "You chose to delete everything to start a fresh installation. Proceeding..."
        docker compose down
        Remove-Item -Recurse -Force .server_files
        break
    }
    elseif ($answer -eq "N") {
        Write-Host "You chose not to proceed with a fresh installation. Aborting..."
        exit
    }
    else {
        Write-Host "Invalid input. Please enter Y or N."
    }
}

# Get the necessary files
# Prepare the folders
Write-Host "Creating the folders needed to work"
mkdir ".server_files\AAEmu.Database\mysql", ".server_files\AAEmu.Login", ".server_files\AAEmu.Game", ".server_files\AAEmu.Game\Data", `
      ".server_files\AAEmu.Game\ClientData", ".server_files\AAEmu.Game\Configurations"
Write-Host "Done"

# Prepare the configuration files
# Generating a strong Database Password
Write-Host "Generating strong SQL Database password..."
$DB_PASSWORD = [Convert]::ToBase64String((Get-Random -Count 32 -InputObject ([byte[]]@(0..255))))
Write-Host "Done"

# Generating a strong secret between login server and game server
Write-Host "Generating strong Secret Key between Login Server and Game Server..."
$SECRET_KEY = [Convert]::ToBase64String((Get-Random -Count 32 -InputObject ([byte[]]@(0..255))))
Write-Host "Done"

# Copying the necessary files in place
Write-Host "Copying file AAEmu/.env.example to Copying file AAEmu/.env if not already present."
if ((Test-Path -Path ".env" -PathType leaf) -eq $False) {
    Copy-Item -Path ".env.example" -Destination ".env" -PassThru
}
Write-Host "Done"

Write-Host "Copying file AAEmu\AAEmu.Login\ExampleConfig.json to AAEmu\.server_files\AAEmu.Login\Config.json if not already present."
Copy-Item -Path "AAEmu.Login\ExampleConfig.json" -Destination ".server_files\AAEmu.Login\Config.json" -Force -PassThru
Write-Host "Done"

Write-Host "Copying file AAEmu\AAEmu.Game\ExampleConfig.json to AAEmu\.server_files\AAEmu.Game\Config.json if not already present."
Copy-Item -Path "AAEmu.Game\ExampleConfig.json" -Destination ".server_files\AAEmu.Game\Config.json" -Force -PassThru
Write-Host "Done"

Write-Host "Copying folder AAEmu\AAEmu.Game\Configurations to AAEmu\.server_files\AAEmu.Game\ if not already present."
Copy-Item -Path "AAEmu.Game\Configurations" -Destination ".server_files\AAEmu.Game\" -Recurse -Force -PassThru
Write-Host "Done"

Write-Host "Copying folder AAEmu\AAEmu.Game\Data to AAEmu\.server_files\AAEmu.Game\ if not already present."
Copy-Item -Path "AAEmu.Game\Data" -Destination ".server_files\AAEmu.Game\" -Recurse -Force -PassThru
Write-Host "Done"

# Now configuring files in place
# Configuring Login Server Config.json
Write-Host "Configuring Config.json for the Login Server..."
(Get-Content -Path ".server_files\AAEmu.Login\Config.json") -replace '"SecretKey": "test"', "`"SecretKey`": `"$SECRET_KEY`"" | Set-Content -Path ".server_files\AAEmu.Login\Config.json"
(Get-Content -Path ".server_files\AAEmu.Login\Config.json") -replace "%db_host%", "db" | Set-Content -Path ".server_files\AAEmu.Login\Config.json"
(Get-Content -Path ".server_files\AAEmu.Login\Config.json") -replace "%db_port%", "3306" | Set-Content -Path ".server_files\AAEmu.Login\Config.json"
(Get-Content -Path ".server_files\AAEmu.Login\Config.json") -replace "%db_user%", "root" | Set-Content -Path ".server_files\AAEmu.Login\Config.json"
(Get-Content -Path ".server_files\AAEmu.Login\Config.json") -replace "%db_password%", "$DB_PASSWORD" | Set-Content -Path ".server_files\AAEmu.Login\Config.json"
Write-Host "Done"
# Configuring Game Server Config.json
Write-Host "Configuring Config.json for the Game Server..."
(Get-Content -Path ".server_files\AAEmu.Game\Config.json") -replace '"SecretKey": "test"', "`"SecretKey`": `"$SECRET_KEY`"" | Set-Content -Path ".server_files\AAEmu.Game\Config.json"
(Get-Content -Path ".server_files\AAEmu.Game\Config.json") -replace "%db_host%", "db" | Set-Content -Path ".server_files\AAEmu.Game\Config.json"
(Get-Content -Path ".server_files\AAEmu.Game\Config.json") -replace "%db_port%", "3306" | Set-Content -Path ".server_files\AAEmu.Game\Config.json"
(Get-Content -Path ".server_files\AAEmu.Game\Config.json") -replace "%db_user%", "root" | Set-Content -Path ".server_files\AAEmu.Game\Config.json"
(Get-Content -Path ".server_files\AAEmu.Game\Config.json") -replace "%db_password%", "$DB_PASSWORD" | Set-Content -Path ".server_files\AAEmu.Game\Config.json"
(Get-Content -Path ".server_files\AAEmu.Game\Config.json") -replace "%login_host%", "login" | Set-Content -Path ".server_files\AAEmu.Game\Config.json"
(Get-Content -Path ".server_files\AAEmu.Game\Config.json") -replace "%login_port%", "1234" | Set-Content -Path ".server_files\AAEmu.Game\Config.json"
Write-Host "Done"
# Configuring .env
(Get-Content -Path ".env") -replace '^DB_PASSWORD=.*$', "DB_PASSWORD=$DB_PASSWORD" | Set-Content -Path ".env"

# Build the Dockerfile using the Compose File
docker compose build --no-cache
Write-Host "Done"

Write-Host "These are the generated passwords. Keep them safe; they will not be shown again except in config files:"
Write-Host "DB Password: $DB_PASSWORD"
Write-Host "SECRET KEY: $SECRET_KEY"
Write-Host ""
Write-Host "To complete your installation, you need to copy:"
Write-Host "compact.sqlite3 to AAEmu\.server_files\AAEmu.Game\Data"
Write-Host "game_pak inside AAEmu\.server_files\AAEmu.Game\ClientData"
Write-Host "OR modify the AAEmu\.server_files\AAEmu.Game\Configurations\ClientData.json to include the game_pak path. Example: C:/Users/sworyz/Desktop/Archeage_1.2.4/game_pak"
Write-Host ""
Write-Host "Launch by running 'docker compose up -d' in the root of AAEmu and connect to 127.0.0.1 in AAEmu.Launcher"
Write-Host "You can stop the server by issuing the command 'docker compose down' in the root of AAEmu"
Write-Host "If you want a development environment (with quick reload and auto-rebuild), use the command docker compose watch or docker compose up --watch and DO NOT CLOSE the terminal"
Write-Host "Docker setup done"
