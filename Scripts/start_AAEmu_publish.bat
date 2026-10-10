@echo off
rem Release build + distributable folder in publish\aaemu-<version>-<target>\ (zip archive next to it).
rem Extra options are passed through, e.g. --out D:\aaemu-publish or --no-compact-db
cd /d "%~dp0..\rust"
cargo xtask publish --archive %*

pause
