# aaemu-game

The AAEmu game server (Rust port of the former `AAEmu.Game` C# project).

## Running

From a source checkout (start the login server first):

```sh
cd rust
cargo run --release -p aaemu-game
```

or use `Scripts/start_game.sh` / `Scripts/start AAEmu.Game.bat`. For a full local stack (MySQL in
Docker + login + game) use `cargo xtask dev`, see the
[Local Development Guide](../../../Docs/wiki/Local-Development-Guide.md).

### Application directory

All runtime files (`Config.json`, `Configurations/`, `Data/`, `ClientData/`, `Scripts/`) are read from
the *application directory*, resolved in this order:

1. the first command-line argument (`aaemu-game <APP_PATH>`);
1. the `AAEMU_APP_PATH` environment variable;
1. the executable's directory, if it contains a `Config.json` (published layout);
1. the current directory, if it contains a `Config.json`;
1. the crate's [`app/`](app/) folder, so `cargo run` works from anywhere in the checkout.

The default files live in [`app/`](app/). Put your `compact.sqlite3` in `app/Data/` and the 1.2
client `game_pak` in `app/ClientData/` (or point `Configurations/ClientData.json` at it).

### Logging

Logs go to stdout. The level is controlled by `RUST_LOG` (default `info`), for example
`RUST_LOG=debug` or `RUST_LOG=aaemu_game=debug`. Set `NO_COLOR=1` to disable ANSI colors.

## Setup instructions

### Configuration

The game server is configured with JSON files in the application directory: `Config.json`, every
`Configurations/**/*.json` file, then `Config.Local.json` (your local overrides). Environment
variables and command-line arguments are applied on top of the files.

The configuration structure is as follows:

```
"Id": 1,
"AdditionalesId": [],
"SecretKey": "test",
"Network": {
    "Host": "*",
    "Port": 1239,
    "NumConnections": 10
},
"StreamNetwork": {
    "Host": "*",
    "Port": 1250
},
"WebApiNetwork": {
    "Host": "*",
    "Port": 1280
},
"LoginNetwork": {
    "Host": "%login_host%",             <-- the IP address of the login server
    "Port": "%login_port%"              <-- the port of the login server
},
"Connections": {
    "MySQLProvider": {
        "Host": "%db_host%",            <-- localhost or any specific
        "Port": "%db_port%",            <-- 3306 or any specific
        "User": "%db_user%",            <-- root or any specific
        "Password": "%db_password%",    <-- password
        "Database": "aaemu_game"
    }
},
"CharacterNameRegex": "^[a-zA-Z0-9а-яА-Я]{1,18}$",
"MaxConcurencyThreadPool": 8,
"HeightMapsEnable": false
```

### Create Configuration File

1. Create a file named `Config.Local.json` next to `Config.json` in the application directory
   (by default `rust/crates/aaemu-game/app/`). It is gitignored and overrides the default
   `Config.json` with your local changes. `ExampleConfig.Local.json` is a starting point.
1. Open `Config.Local.json` and add the configuration details as required.

For example, the `Config.Local.json` file could look like this:

```
{
    "Connections": {
        "MySQLProvider": {
            "Host": "localhost",
            "Port": "3306",
            "User": "root",
            "Password": "MySuperSecurePassword"
        }
    }
}
```

### Environment variables and command line

To keep credentials out of files entirely, set them as environment variables. A double underscore
separates the sections (`Section__Key` is `Section:Key`):

```sh
export Connections__MySQLProvider__User=root
export Connections__MySQLProvider__Password=yourpassword
```

```powershell
$env:Connections__MySQLProvider__Password = "yourpassword"
```

Command-line arguments in the form `--Section:Key=value` override everything else, for example
`cargo run --release -p aaemu-game -- --LoginNetwork:Port=1234`.

Precedence (last wins): `Config.json` < `Configurations/**/*.json` < `Config.Local.json` <
user-secrets file < environment variables < command line.

> Migrating from the C# server: an existing user-secrets file
> (`%APPDATA%/Microsoft/UserSecrets/<id>/secrets.json`, `~/.microsoft/usersecrets/<id>/secrets.json`
> on Linux/macOS) is still read if present, but there is no `user-secrets` tool any more. Use
> `Config.Local.json` or environment variables instead.
