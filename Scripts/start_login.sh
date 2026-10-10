#!/bin/bash
# Builds (if needed) and starts the login server.
# Configuration: rust/crates/aaemu-login/app/Config.json (+ Config.Local.json), or AAEMU_APP_PATH.
echo "Please wait while compiling the Login server"
cd "$(dirname "$0")/../rust" || exit 1
cargo run --release -p aaemu-login -- "$@"
read -p "Press enter to continue ..."
