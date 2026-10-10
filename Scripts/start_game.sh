#!/bin/bash
# Builds (if needed) and starts the game server.
# Configuration and data: rust/crates/aaemu-game/app/ (Config.json, Configurations/,
# Data/compact.sqlite3), or AAEMU_APP_PATH / the first argument.
echo "Please wait while compiling the Game server ..."
cd "$(dirname "$0")/../rust" || exit 1
cargo run --release -p aaemu-game -- "$@"
read -p "Press enter to continue ..."
