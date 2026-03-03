#!/usr/bin/env bash
set -euo pipefail

echo -e "Switching folder to root folder (AAEmu/)"
cd ..
echo -e "Done"

ASPIRE_OUTPUT_DIR=".server_files/aspire/docker"
COMPOSE_FILE="$ASPIRE_OUTPUT_DIR/docker-compose.yaml"
ENV_FILE="$ASPIRE_OUTPUT_DIR/.env"

if [[ ! -f "$COMPOSE_FILE" || ! -f "$ENV_FILE" ]]; then
    echo -e "No generated compose artifacts found at $ASPIRE_OUTPUT_DIR"
    exit 1
fi

echo -e "Stopping AAEmu containers..."
docker compose --project-name aaemu --env-file "$ENV_FILE" -f "$COMPOSE_FILE" down
echo -e "AAEmu containers stopped."
