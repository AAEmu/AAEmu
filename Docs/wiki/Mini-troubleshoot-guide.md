# Mini Troubleshoot Guide

- Audience: Contributors, players, and testers
- Last verified against: `develop` on February 28, 2026
- Prerequisites: None

Use this page for common startup and connection problems.

## Common issues

### Missing tables or SQL errors on startup

Confirm you imported `SQL/aaemu_login.sql` and `SQL/aaemu_game.sql` (manual
path), or let Aspire initialize DB (Aspire path).

### `DataAnnotation validation failed ... GameServers`

Login configuration is missing `GameServers`.
Add it in `AAEmu.Login/Config.Local.json` or environment variables.

### Server list is empty in client

Verify `GameServers__0__Host` and `GameServers__0__Port` are reachable by the
client.

### Crash after selecting server

Usually this means wrong game host or port in login `GameServers` config.

### Aspire does not start services

Ensure Docker or Podman is installed and running, then relaunch
`AAEmu.Aspire.AppHost`.

### Game cannot load world assets

Verify `compact.sqlite3` is in `AAEmu.Game/Data` and `game_pak` path is
correct.

### Linux file descriptor errors

Increase OS file descriptor limits for the server process.

## Important change

Do not rely on MySQL `aaemu_login.game_servers` as a source of server listings.
Login server listings are configuration-driven now.

## Related

- [FAQ](FAQ)
- [Installation & Setup](Installation-&-Setup)
- [Working with the Config.json files and server listings](Working-with-the-Config.json-files-and-server-listings)
