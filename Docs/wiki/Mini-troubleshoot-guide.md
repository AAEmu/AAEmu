# Mini Troubleshoot Guide

- Audience: Contributors, players, and testers
- Last verified against: `develop` on October 10, 2026
- Prerequisites: None

Use this page for common startup and connection problems.

## Common issues

### Missing tables or SQL errors on startup

Confirm you imported `SQL/aaemu_login.sql` and `SQL/aaemu_game.sql` (manual
path), or let `cargo xtask dev` / Docker Compose initialize the DB (MySQL
container path). Later changes in `SQL/updates` are only applied when
`Connections:AutoApplyUpdates` is `true`.

### `No game servers have been defined!`

Login configuration is missing `GameServers`.
Add it in `rust/crates/aaemu-login/app/Config.Local.json` (or the
`Config.Local.json` of your login folder) or environment variables.

### Server list is empty in client

Verify `GameServers__0__Host` and `GameServers__0__Port` are reachable by the
client.

### Crash after selecting server

Usually this means wrong game host or port in login `GameServers` config.

### `cargo xtask dev` does not start services

Ensure Docker is installed and running, then rerun `cargo xtask dev`. To use
your own MySQL instead, run `cargo xtask dev --no-db`.

### `cargo` is not found

Install Rust with [rustup](https://rustup.rs/) and open a new terminal.

### Game cannot load world assets

Verify `compact.sqlite3` is in the `Data/` folder of the game application
directory (`rust/crates/aaemu-game/app/Data/` in a source checkout) and the
`game_pak` path is correct.

### Need more detail in the logs

Set `RUST_LOG=debug` (or `RUST_LOG=aaemu_game=debug,info`) before starting the
server.

### Linux file descriptor errors

Increase OS file descriptor limits for the server process.

## Important change

Do not rely on MySQL `aaemu_login.game_servers` as a source of server listings.
Login server listings are configuration-driven now.

## Related

- [FAQ](FAQ)
- [Installation & Setup](Installation-&-Setup)
- [Working with the Config.json files and server listings](Working-with-the-Config.json-files-and-server-listings)
