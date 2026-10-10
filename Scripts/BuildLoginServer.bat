@echo off
rem Builds the login server (release) - binary in the cargo target dir (see rust/.cargo/config.toml)
cd /d "%~dp0..\rust"
cargo build --release -p aaemu-login
pause
