# Installation & Setup

- Audience: Contributors, players, and testers
- Last verified against: `develop` on March 3, 2026
- Prerequisites: `.NET 10 SDK`, required AAEmu dependencies/downloads, and
  MySQL for manual track

This page now has two setup paths:

1. `Track A (Preferred)`: Aspire local development workflow.
1. `Track B`: Manual setup workflow.

## Track A (Preferred): Aspire workflow

Use this path if you want the fastest contributor onboarding.

### Requirements

1. Install `.NET 10 SDK`.
1. Install an OCI-compliant runtime (Docker Desktop or Podman).
1. Clone [AAEmu](https://github.com/AAEmu/AAEmu) (`develop` branch recommended).
1. Download required files from [Dependencies and Downloads](Dependencies-and-Downloads):
   - `compact.sqlite3`
   - ArcheAge 1.2 client
   - AAEmu Launcher
1. Place `compact.sqlite3` in `AAEmu.Game/Data`.
1. Set your `game_pak` path.

### Set `game_pak` path

Recommended: put the path in `AAEmu.Game/Config.Local.json` so it overrides
other game config files.

### Launch with Aspire

1. Open the solution in your IDE.
1. Select launch profile `AAEmu.Aspire.AppHost: http`.
1. Run in Debug.

Expected startup sequence:

1. MySQL container starts.
1. `aaemu_login` and `aaemu_game` are initialized with idempotent SQL.
1. Login and game services start.
1. Aspire dashboard opens with service state and logs.

For full details, see [Aspire Development Guide](Aspire-Development-Guide).

## Track B: Manual setup workflow

Use this path if you do not want to use Aspire.

### Manual requirements

1. Install MySQL 8.x.
1. Install `.NET 10 SDK`.
1. Clone [AAEmu](https://github.com/AAEmu/AAEmu) (`develop` branch recommended).
1. Download required files from [Dependencies and Downloads](Dependencies-and-Downloads):
   - `compact.sqlite3`
   - ArcheAge 1.2 client
   - AAEmu Launcher
1. Place `compact.sqlite3` in `AAEmu.Game/Data`.

### Database setup (manual)

1. Create two schemas in MySQL:
   - `aaemu_login`
   - `aaemu_game`
1. Import:
   - `SQL/aaemu_login.sql`
   - `SQL/aaemu_game.sql`

Do not insert rows into `aaemu_login.game_servers`.
Game server listing is now configured via login server configuration
(`GameServers`).

### Login server configuration (manual)

Create or edit `AAEmu.Login/Config.Local.json` and set DB credentials plus
`GameServers`.

Example:

```json
{
  "Connections": {
    "MySQLProvider": {
      "Host": "127.0.0.1",
      "Port": "3306",
      "User": "your_user",
      "Password": "your_password",
      "Database": "aaemu_login"
    }
  },
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

### Game server configuration (manual)

Create or edit `AAEmu.Game/Config.Local.json`.
Because `Config.Local.json` is loaded last, it overrides all other game config
JSON files.

At minimum, set database and login network values for your machine.

Set `game_pak` source in either:

- `AAEmu.Game/Configurations/ClientData.json`, or
- `AAEmu.Game/Config.Local.json` as an override.

### Build and run (manual)

1. Build:

```bash
dotnet build
```

1. Start login server.
1. Start game server.

You can run through your IDE or with `dotnet run --project ...` commands.
Start login before game.

### Launcher setup

1. Open AAEmu Launcher.
1. Set `Path to Game` to your `archeage.exe` in the client `bin32` folder.
1. Set login credentials.

If auto-account creation is enabled (default), accounts are created on first
login.

## Docker workflow

Docker workflow now uses AppHost-generated compose artifacts.

Use:

- [Aspire Docker Publishing Guide](Aspire-Docker-Publishing-Guide)
- [Docker Installation Guide](Docker-Installation-Guide)

## Related

- [Home](Home)
- [Dependencies and Downloads](Dependencies-and-Downloads)
- [Aspire Development Guide](Aspire-Development-Guide)
- [Aspire Docker Publishing Guide](Aspire-Docker-Publishing-Guide)
- [Working with the Config.json files and server listings](Working-with-the-Config.json-files-and-server-listings)
- [Mini troubleshoot guide](Mini-troubleshoot-guide)
