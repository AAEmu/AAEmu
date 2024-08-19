# Switch to the root folder
echo -e "Switching folder to root folder (AAEmu\)"
cd ..
echo -e "Done"

echo -e "Shutting all containers and Updating AAEmu..."
docker compose down
git pull
echo -e "Done"

# Build the Dockerfile
echo -e "Rebuilding Docker Containers..."
docker compose build --no-cache
echo -e "Done"

# The End
echo -e "Launch by running 'docker compose up -d' in the root of AAEmu and connect to 127.0.0.1 in AAEmu.Launcher"
echo -e "You can stop the server by issuing the command 'docker compose down' in the root of AAEmu"
echo -e "If you want a development environment (with quick reload and auto-rebuild), use the command docker compose watch or docker compose up --watch and DO NOT CLOSE the terminal"
echo -e "Update done"