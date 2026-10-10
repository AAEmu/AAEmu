@echo off
rem Builds (if needed) and starts the game server. Configuration and data: rust\crates\aaemu-game\app\
rem (Config.json, Configurations\, Data\compact.sqlite3), or the folder named by AAEMU_APP_PATH.
cd /d "%~dp0..\rust"
cargo run --release -p aaemu-game
pause
