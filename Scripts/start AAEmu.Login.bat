@echo off
rem Builds (if needed) and starts the login server. Configuration: rust\crates\aaemu-login\app\Config.json
rem (+ Config.Local.json), or the folder named by AAEMU_APP_PATH.
cd /d "%~dp0..\rust"
cargo run --release -p aaemu-login
pause
