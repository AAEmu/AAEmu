#!/bin/bash

clear

# Switch to the root folder and clean everything old
echo -e "Switching folder to root folder (AAEmu/)"
cd ..
echo -e "Done"; sleep 0

# Asking the user if he is sure to continue a fresh installation
while true; do
    # Prompt the user for input
    read -p "This script will wipe everything to make a fresh installation. Are you sure. (Y/N): " answer
    # Convert the input to uppercase to handle both 'y' and 'n' cases
    answer=$(echo "$answer" | tr '[:lower:]' '[:upper:]')
    # Act based on the user's input
    if [[ "$answer" == "Y" ]]; then
        echo "You chose to delete everything to start a fresh installation. Proceeding..."  
        docker compose down   
        rm -f .env
        rm -rf .server_files
        git checkout .env
        break
    elif [[ "$answer" == "N" ]]; then
        echo "You chose not to proceed with a fresh installation. Aborting..."
        exit
        break
    else
        break
        echo "Invalid input. Please enter Y or N."
    fi
done



# Get the necessary files
# Prepare the folders
echo -e "Creating the folders needed to works"
mkdir -p .server_files/AAEmu.Database/mysql
mkdir -p .server_files/AAEmu.Login
mkdir -p .server_files/AAEmu.Game
mkdir -p .server_files/AAEmu.Game/Data
mkdir -p .server_files/AAEmu.Game/ClientData
mkdir -p .server_files/AAEmu.Game/Configurations
echo -e "Done"; sleep 1

# Prepare the configuration files
# Generating a strong Database Password
echo -e "Generating strong SQL Database password..."
DB_PASSWORD=$(openssl rand -base64 32)
echo -e "Done"; sleep 1
# Generating a strong secret between login server and game server
echo -e "Generating strong Secret Key between Login Server and Game Server..."
SECRET_KEY=$(openssl rand -base64 32)
echo -e "Done"; sleep 1

# Copying the necessary files in place
echo -e "Copying file AAEmu/.env.example to Copying file AAEmu/.env if not already present."
cp -n .env.example .env
echo -e "Done"; sleep 1

echo -e "Copying file AAEmu/AAEmu.Login/ExampleConfig.json to AAEmu/.server_files/AAEmu.Login/Config.json if not already present."
cp -n AAEmu.Login/ExampleConfig.json .server_files/AAEmu.Login/Config.json
echo -e "Done"; sleep 1

echo -e "Copying file AAEmu/AAEmu.Game/ExampleConfig.json to AAEmu/.server_files/AAEmu.Game/Config.json if not already present."
cp -n AAEmu.Game/ExampleConfig.json .server_files/AAEmu.Game/Config.json
echo -e "Done"; sleep 1

echo -e "Copying folder AAEmu/AAEmu.Game/Configurations to AAEmu/.server_files/AAEmu.Game/Configurations if not already present."
cp -n -r AAEmu.Game/Configurations .server_files/AAEmu.Game/
echo -e "Done"; sleep 1

echo -e "Copying folder AAEmu/AAEmu.Game/Data to AAEmu/.server_files/AAEmu.Game/Data if not already present."
cp -n -r AAEmu.Game/Data .server_files/AAEmu.Game/
echo -e "Done"; sleep 1

# Now configuring files in place
# Configuring Login Server Config.json
echo -e "Configuring Config.json for the Login Server..."
sed -i "s|\"SecretKey\": \"test\"|\"SecretKey\": \"$SECRET_KEY\"|" .server_files/AAEmu.Login/Config.json
sed -i "s|%db_host%|db|" .server_files/AAEmu.Login/Config.json
sed -i "s|%db_port%|3306|" .server_files/AAEmu.Login/Config.json
sed -i "s|%db_user%|root|" .server_files/AAEmu.Login/Config.json
sed -i "s|%db_password%|$DB_PASSWORD|" .server_files/AAEmu.Login/Config.json
echo -e "Done"; sleep 1
# Configuring Game Server Config.json
echo -e "Configuring Config.json for the Game Server..."
sed -i "s|\"SecretKey\": \"test\"|\"SecretKey\": \"$SECRET_KEY\"|" .server_files/AAEmu.Game/Config.json
sed -i "s|%db_host%|db|" .server_files/AAEmu.Game/Config.json
sed -i "s|%db_port%|3306|" .server_files/AAEmu.Game/Config.json
sed -i "s|%db_user%|root|" .server_files/AAEmu.Game/Config.json
sed -i "s|%db_password%|$DB_PASSWORD|" .server_files/AAEmu.Game/Config.json
sed -i "s|%login_host%|login|" .server_files/AAEmu.Game/Config.json
sed -i "s|%login_port%|1234|" .server_files/AAEmu.Game/Config.json
echo -e "Done"; sleep 1

# Build the Dockerfile using the Compose File
sed -i "s|^DB_PASSWORD=.*|DB_PASSWORD=$DB_PASSWORD|" .env
docker compose build --no-cache
echo -e "Done"; sleep 1

clear

echo -e "Theses are the generated password, keep them safe, they will not be showed again except in config files :"
echo "DB Password: $DB_PASSWORD"
echo "SECRET KEY: $SECRET_KEY"
echo -e ""
echo -e "Now to really finish your installation, you need to copy :"
echo -e "compact.sqlite3 to AAEmu/.server_files/AAEmu.Game/Data"
echo -e "game_pak inside AAEmu/.server_files/AAEmu.Game/ClientData "
echo -e "OR modify the AAEmu/.server_files/AAEmu.Game/Configurations/ClientData.json to include the game_pak path. Example : C:/Users/sworyz/Desktop/Archeage_1.2.4/game_pak"
echo -e ""
echo -e "Launch by running 'docker compose up -d' in the root of AAEmu and connect to 127.0.0.1 in AAEmu.Launcher"
echo -e "You can stop the server by issuing the command 'docker compose down' in the root of AAEmu"
echo -e "If you want a development environment (with quick reload and auto-rebuild), use the command docker compose watch or docker compose up --watch and DO NOT CLOSE the terminal"
read -p "Press any key to finish..."