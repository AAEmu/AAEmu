#!/usr/bin/env bash
set -euo pipefail

echo -e "Switching folder to root folder (AAEmu/)"
cd ..
echo -e "Done"

APPHOST_PROJECT="AAEmu.Aspire.AppHost/AAEmu.Aspire.AppHost.csproj"
ASPIRE_CLI_VERSION="13.1.2"
ASPIRE_TOOL_DIR=".server_files/tools"
ASPIRE_OUTPUT_DIR=".server_files/aspire/docker"
ASPIRE_CLI="$ASPIRE_TOOL_DIR/aspire"
COMPOSE_FILE="$ASPIRE_OUTPUT_DIR/docker-compose.yaml"
ENV_FILE="$ASPIRE_OUTPUT_DIR/.env"

set_env_default() {
    local env_file="$1"
    local key="$2"
    local default_value="$3"

    if grep -q "^${key}=" "$env_file"; then
        if grep -q "^${key}=$" "$env_file"; then
            sed -i "s|^${key}=$|${key}=${default_value}|" "$env_file"
        fi
    else
        printf "%s=%s\n" "$key" "$default_value" >> "$env_file"
    fi
}

apply_env_defaults() {
    set_env_default "$ENV_FILE" "COMPOSE_PROJECT_NAME" "aaemu"
    set_env_default "$ENV_FILE" "AAEMU_NETWORK_NAME" "aaemu-net"
    set_env_default "$ENV_FILE" "AAEMU_DB_HOST_PORT" "3306"
    set_env_default "$ENV_FILE" "AAEMU_ADMINER_HOST_PORT" "8080"
    set_env_default "$ENV_FILE" "AAEMU_LOGIN_PUBLIC_PORT" "1237"
    set_env_default "$ENV_FILE" "AAEMU_GAME_PUBLIC_PORT" "1239"
    set_env_default "$ENV_FILE" "AAEMU_GAME_STREAM_PUBLIC_PORT" "1250"
    set_env_default "$ENV_FILE" "AAEMU_DASHBOARD_HOST_PORT" "18888"
    set_env_default "$ENV_FILE" "AAEMU_LOGIN_PORT" "8080"
    set_env_default "$ENV_FILE" "AAEMU_DB_PASSWORD" "password"
}

echo -e "Stopping existing AAEmu Docker stack (if any)..."
if [[ -f "$COMPOSE_FILE" && -f "$ENV_FILE" ]]; then
    docker compose --project-name aaemu --env-file "$ENV_FILE" -f "$COMPOSE_FILE" down || true
fi
echo -e "Done"

echo -e "Updating repository..."
git pull
echo -e "Done"

echo -e "Ensuring Aspire CLI is available..."
mkdir -p "$ASPIRE_TOOL_DIR" "$ASPIRE_OUTPUT_DIR"
if [[ -x "$ASPIRE_CLI" ]]; then
    dotnet tool update --tool-path "$ASPIRE_TOOL_DIR" aspire.cli --version "$ASPIRE_CLI_VERSION"
else
    dotnet tool install --tool-path "$ASPIRE_TOOL_DIR" aspire.cli --version "$ASPIRE_CLI_VERSION"
fi
echo -e "Done"

echo -e "Regenerating Docker Compose artifacts from AppHost..."
"$ASPIRE_CLI" publish --project "$APPHOST_PROJECT" --output-path "$ASPIRE_OUTPUT_DIR" --non-interactive
apply_env_defaults
echo -e "Done"

echo -e "Update done."
echo -e "Start containers: Scripts/docker-start-local.sh"
