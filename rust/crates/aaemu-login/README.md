# aaemu-login

The AAEmu login server (Rust port of the former `AAEmu.Login` C# project).

## Running

From a source checkout (start the login server before the game server):

```sh
cd rust
cargo run --release -p aaemu-login
```

or use `Scripts/start_login.sh` / `Scripts/start AAEmu.Login.bat`. For a full local stack (MySQL in
Docker + login + game) use `cargo xtask dev`, see the
[Local Development Guide](../../../Docs/wiki/Local-Development-Guide.md).

### Application directory

`Config.json` and `Config.Local.json` are read from the *application directory*, resolved in this
order:

1. the `AAEMU_APP_PATH` environment variable;
1. the executable's directory, if it contains a `Config.json` (published layout);
1. the current directory, if it contains a `Config.json`;
1. the crate's [`app/`](app/) folder, so `cargo run` works from anywhere in the checkout.

### Logging

Logs go to stdout. The level is controlled by `RUST_LOG` (default `info`), for example
`RUST_LOG=debug` or `RUST_LOG=aaemu_login=debug`. Set `NO_COLOR=1` to disable ANSI colors.

## Setup instructions

### Configuration

The login server is configured with `Config.json` and an optional `Config.Local.json` (your local
overrides) in the application directory. Environment variables and command-line arguments are
applied on top of the files.

The configuration structure is as follows:

```
"SecretKey": "test",
"AutoAccount": true,
"InternalNetwork": {  <-- Internal network (for game server)
    "Host": "*",
    "Port": 1234
},
"Network": {          <-- External network (for clients)
    "Host": "*",
    "Port": 1237,
    "NumConnections": 10
},
"Connections": {
    "MySQLProvider": {
        "Host": "%db_host%",         <-- localhost or any specific
        "Port": "%db_port%",         <-- 3306 or any specific
        "User": "%db_user%",         <-- root or any specific
        "Password": "%db_password%", <-- password
        "Database": "aaemu_login"
    }
}

```

### Create Configuration File

1. Create a file named `Config.Local.json` next to `Config.json` in the application directory
   (by default `rust/crates/aaemu-login/app/`). It is gitignored and overrides the default
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

Command-line arguments in the form `--Section:Key=value` override everything else.

Precedence (last wins): `Config.json` < `Config.Local.json` < environment variables < command line.

> Migrating from the C# server: the login server does not read user secrets. Move any values you
> stored with `user-secrets` into `Config.Local.json` or environment variables.
