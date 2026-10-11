# Local Development Guide

- Audience: Contributors
- Last verified against: `develop` on October 10, 2026
- Prerequisites: stable Rust (rustup), Docker, and required downloaded
  dependencies

## Why this guide exists

The preferred way to run AAEmu locally is `cargo xtask dev`. It starts MySQL
in Docker, builds the servers, and starts the login server and the game server
with the right settings, in the right order.

`cargo xtask dev` is optional: the manual and Docker workflows are still
supported.

## Prerequisites

1. Install stable Rust with [rustup](https://rustup.rs/) (provides `cargo`).
1. Install Docker (Docker Desktop, or Docker Engine with the `docker compose`
   plugin) and make sure it is running.
1. Clone the `AAEmu` repository (recommended branch: `develop`).
1. Download required files from
   [Dependencies and Downloads](Dependencies-and-Downloads):
   - `compact.sqlite3`
   - ArcheAge 1.2 client
1. Place `compact.sqlite3` in `rust/crates/aaemu-game/app/Data/`.
1. Configure the `game_pak` path in the game configuration.

### Recommended way to set `game_pak`

Set the path in `rust/crates/aaemu-game/app/Config.Local.json` so your local
machine override wins over all other game config files:

```json
{
  "ClientData": {
    "Sources": [
      "C:\\path\\to\\ArcheAge 1.2 (r208022)\\game_pak"
    ]
  }
}
```

`Config.Local.json` is loaded after `Config.json` and `Configurations/*.json`.
Only environment variables and command-line arguments override it. You can also
put `game_pak` itself in `rust/crates/aaemu-game/app/ClientData/`.

## First run (preferred local workflow)

```sh
cd rust
cargo xtask dev --release
```

What happens:

1. `.env` is created from `.env.example` if it does not exist yet.
1. MySQL is started with `docker compose up -d --wait db`. On the first start
   the container imports `SQL/aaemu_login.sql` and `SQL/aaemu_game.sql`; the
   data is kept in `.server_files/AAEmu.Database/mysql`.
1. `aaemu-login` and `aaemu-game` are built.
1. The login server starts. `cargo xtask dev` waits until the internal login
   port `1234` accepts connections.
1. The game server starts and registers with the login server.

Press `Ctrl+C` to stop both servers. The MySQL container keeps running; stop it
with `docker compose stop db`.

Options:

| Option | Effect |
| --- | --- |
| `--release` | Build and run optimized binaries (recommended for playing; debug builds are much slower) |
| `--no-db` | Do not start MySQL; use your own server (credentials from `Config.Local.json`) |
| `--db-host <host>` | MySQL host passed to the servers (default `127.0.0.1`) |
| `--db-port <port>` | MySQL port (default `DB_PORT` from `.env`, else `3306`) |

## What `cargo xtask dev` wires automatically

The servers get their settings through environment variables, so no
`Config.Local.json` is needed for the database or the server list:

- Login and game DB connection settings (`Connections__MySQLProvider__*`, user
  and password from `DB_USER` / `DB_PASSWORD` in `.env`, databases
  `aaemu_login` and `aaemu_game`).
- `Connections__AutoApplyUpdates=true`, so `SQL/updates` are applied at startup.
- Login server `GameServers` for local development (id `1`, `AAEmu.Game`,
  `127.0.0.1:1239`).
- Game server `LoginNetwork` (`127.0.0.1:1234`).

This means local startup does not require inserting game server listing rows
into MySQL.

## Logs and debugging

Both servers log to the same console. Use `RUST_LOG` for more detail, for
example `RUST_LOG=debug cargo xtask dev` or
`RUST_LOG=aaemu_game=debug,info cargo xtask dev`.

To debug with breakpoints, start MySQL with `docker compose up -d db` and run
the servers from your IDE (for example VS Code with rust-analyzer and CodeLLDB,
or RustRover) with the same settings in `Config.Local.json`, login first.

Database inspection: `docker compose up -d adminer`, then open
<http://localhost:8080>.

For automated end-to-end checks against the running servers, see
[`rust/LIVE-TESTING.md`](https://github.com/AAEmu/AAEmu/blob/develop/rust/LIVE-TESTING.md)
(the `aaemu-testclient` bot).

## Starting the client

Start the 1.2 client against the local servers with AAEmu's own launcher
(Windows):

```sh
cd rust
cargo run --release -p aaemu-launcher -- --client "C:/ArcheAge/bin32/archeage.exe" --login 127.0.0.1:1237 --user myname
```

It asks for the password; `--password PW`, `--password-file FILE` or the
`AAEMU_PASSWORD` environment variable avoid the prompt (handy for scripts).
With `AutoAccount` (default) the account is created on the first login. See
[Client](Client#launcher-aaemu-launcher) for all options.

## Driving the client from tools (Lua driver)

For automated tests against the real client, `aaemu-clientdriver` loads a
driver DLL into the running client and runs Lua in its script states (read UI
or game state, call the same Lua the UI buttons call):

```sh
cargo run --release -p aaemu-launcher -- --client "C:/ArcheAge/bin32/archeage.exe" --user luadrv --nosplash --arg -DEVMODE
cargo run --release -p aaemu-clientdriver -- inject
cargo run --release -p aaemu-clientdriver -- lua -t ui "return CheckLoginButtonActivation ~= nil"
```

The in-client part is 32-bit C++ and needs MSVC with the x86 toolset
(`rust/crates/aaemu-clientdriver/native/build.bat`). Build steps, commands and
caveats:
[`rust/docs/client/LUA-DRIVER.md`](https://github.com/AAEmu/AAEmu/blob/develop/rust/docs/client/LUA-DRIVER.md).
For headless protocol-level tests use `aaemu-testclient` instead (see
`rust/LIVE-TESTING.md`).

## Physics

The game server's collision world (`aaemu-physics`, a CryPhysics port) loads
zone cells from the client data around players. It is configured in
`rust/crates/aaemu-game/app/Configurations/Physics.json`; for a faster start on
small machines lower `Geometry` (`Terrain` / `Brushes`) or `LoadRadiusCells`,
or set `Enable` to `false`. See
[Working with the Config.json files](Working-with-the-Config.json-files-and-server-listings#physics-configurationsphysicsjson).

## Health and readiness

The login server can serve the health endpoints `/health/live` and
`/health/ready` when the `urls` setting is set (for example
`urls=http://+:5000`, or `ASPNETCORE_URLS` for compatibility).

## Common issues

- Docker not running: start Docker first, or use `--no-db` with a local MySQL.
- `compact.sqlite3` missing: place it in `rust/crates/aaemu-game/app/Data/`.
- Invalid `game_pak` path: set it in `Config.Local.json` and re-run.
- Port conflict on `1234`, `1237`, `1239`, `1250` or `3306`: free the port
  (for MySQL, change `DB_PORT` in `.env`).
- Missing server list in client: verify login `GameServers` config values, not
  MySQL `game_servers`.

## Related

- [Home](Home)
- [Dependencies and Downloads](Dependencies-and-Downloads)
- [Installation & Setup](Installation-&-Setup)
- [Working with the Config.json files and server listings](Working-with-the-Config.json-files-and-server-listings)
- [Mini troubleshoot guide](Mini-troubleshoot-guide)
