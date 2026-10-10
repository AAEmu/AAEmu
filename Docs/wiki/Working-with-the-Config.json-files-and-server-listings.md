# Working with Config Files and Server Listings

- Audience: Contributors, players, and testers
- Last verified against: `develop` on October 10, 2026
- Prerequisites: Basic JSON editing and AAEmu project structure familiarity

## Overview

AAEmu now stores login server listings in configuration (`GameServers`) instead
of MySQL `aaemu_login.game_servers`.

This allows consistent behavior across manual, Docker, and `cargo xtask dev`
workflows.

## Where the files are

Each server reads its configuration from its *application directory*:

| Setup | Login | Game |
| --- | --- | --- |
| Source checkout (`cargo run`) | `rust/crates/aaemu-login/app/` | `rust/crates/aaemu-game/app/` |
| Published build | `AAEmu.Login/` (next to the binary) | `AAEmu.Game/` (next to the binary) |
| Docker | `.server_files/AAEmu.Login/` | `.server_files/AAEmu.Game/` |

The directory is chosen in this order: the `AAEMU_APP_PATH` environment
variable; the executable's directory if it contains `Config.json`; the current
directory if it contains `Config.json`; otherwise the crate's `app/` folder.
The game server also accepts the directory as its first argument
(`aaemu-game <APP_PATH>`).

## Login server `Config.json` and `Config.Local.json`

The login server reads `Config.json`, then `Config.Local.json`, then
environment variables, then command-line arguments (`--Section:Key=value`).
Use `Config.Local.json` (gitignored) for machine-specific overrides;
`ExampleConfig.Local.json` is a starting point.

### `GameServers` schema

`GameServers` is a list of server entries with this shape:

```json
{
  "GameServers": [
    {
      "Id": 1,
      "Name": "AAEmu.Game",
      "Host": "127.0.0.1",
      "Port": 1239,
      "Hidden": false
    }
  ]
}
```

Field meanings:

- `Id`: unique game server id.
- `Name`: display name in server selection.
- `Host`: address reachable by clients.
- `Port`: client connection port.
- `Hidden`: whether to hide this entry from listing.

### Environment variable mapping (login)

You can define server entries via environment variables:

```text
GameServers__0__ID=1
GameServers__0__Name=AAEmu.Game
GameServers__0__Host=127.0.0.1
GameServers__0__Port=1239
GameServers__0__Hidden=false
```

## Game server configuration and precedence

The game server supports `Config.Local.json` as the final override layer.

Effective load order (paths relative to the game application directory):

1. `Config.json`
1. `Configurations/*.json` (all matching files)
1. `Config.Local.json`
1. Environment variables (`Section__Key`, for example
   `Connections__MySQLProvider__Password`)
1. Command-line arguments (`--Section:Key=value`)

If the same setting exists in multiple places, the later source wins, so
`Config.Local.json` overrides all JSON files. Environment variables are a good
place for passwords you do not want in a file. (A user-secrets file left over
from the C# version is still read between `Config.Local.json` and the
environment variables, but it can no longer be managed with a tool; move those
values to `Config.Local.json` or environment variables.)

## Logging

Logging is not configured in `Config.json`. Both servers log to stdout; the
level is set with the `RUST_LOG` environment variable (default `info`, for
example `RUST_LOG=debug` or `RUST_LOG=aaemu_game=debug,info`). `NO_COLOR=1`
disables colored output.

## `game_pak` configuration

Set `game_pak` source in one of these places:

- `Configurations/ClientData.json`, or
- `Config.Local.json` for local override,

or put `game_pak` into the `ClientData/` folder of the game application
directory.

For contributor workflows, prefer `Config.Local.json`.

## Migration note from old setup docs

Older instructions referenced `aaemu_login.game_servers` and SQL inserts.
That is no longer the source of truth for server listings.

Use login `GameServers` configuration instead.

## Related

- [Installation & Setup](Installation-&-Setup)
- [Local Development Guide](Local-Development-Guide)
- [Mini troubleshoot guide](Mini-troubleshoot-guide)
- [FAQ](FAQ)
