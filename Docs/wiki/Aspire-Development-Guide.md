# Aspire Development Guide

- Audience: Contributors
- Last verified against: `develop` on March 3, 2026
- Prerequisites: `.NET 10 SDK`, OCI runtime, and required downloaded
  dependencies

## Why this guide exists

The preferred way to run AAEmu locally is the Aspire AppHost project.
It orchestrates MySQL, Adminer, login server, and game server.

## Prerequisites

1. Install `.NET 10 SDK`.
1. Install an OCI-compliant container runtime:
   - Docker Desktop, or
   - Podman.
1. Clone the `AAEmu` repository (recommended branch: `develop`).
1. Download required files from
   [Dependencies and Downloads](Dependencies-and-Downloads):
   - `compact.sqlite3`
   - ArcheAge 1.2 client
   - AAEmu Launcher
1. Place `compact.sqlite3` in `AAEmu.Game/Data`.
1. Configure `game_pak` path in game configuration.

### Recommended way to set `game_pak`

Set the path in `AAEmu.Game/Config.Local.json` so your local machine override
wins over all other game config files.
`Config.Local.json` is loaded last and overrides all previous game config JSON sources.

## First run (preferred local workflow)

1. Open the AAEmu solution in your IDE.
1. Select launch profile `AAEmu.Aspire.AppHost: http`.
1. Run in Debug.

Expected behavior:

1. Aspire starts the MySQL container.
1. Aspire initializes `aaemu_login` and `aaemu_game` using idempotent SQL scripts.
1. Login and game services start after dependencies are ready.
1. Aspire dashboard opens and shows service health/state.

## Aspire Docker publishing workflow

Use this path when you want Docker Compose artifacts generated from AppHost.

1. Change directory to `Scripts`.
1. Initial install:
   - Windows: `./docker-install-local.ps1`
   - Linux: `./docker-install-local.sh`
1. Start containers:
   - Windows: `./docker-start-local.ps1`
   - Linux: `./docker-start-local.sh`
1. Stop containers:
   - Windows: `./docker-stop-local.ps1`
   - Linux: `./docker-stop-local.sh`
1. Refresh generated artifacts after updates:
   - Windows: `./docker-update-local.ps1`
   - Linux: `./docker-update-local.sh`

Generated files are written under `.server_files/aspire/docker`.
Scripts backfill empty `.env` values with AAEmu defaults before startup.

### Services and networking in generated compose

The generated compose uses explicit AAEmu service naming and an AAEmu network:

- `aaemu-db`
- `aaemu-adminer`
- `aaemu-login`
- `aaemu-game`
- `aaemu-dashboard`
- network: `aaemu-net`

### Major note about package versions

The Docker publishing integration currently depends on
`Aspire.Hosting.Docker` prerelease package versions.
This is expected and tracked in project package management.

## What Aspire wires automatically

Aspire passes runtime configuration through environment variables, including:

- Login and game DB connection settings.
- Login server `GameServers` values for local development.
- Internal login/game endpoint values used for service-to-service communication.

This means local startup no longer requires manually inserting game server
listing rows into MySQL.

## Debugging with Aspire

Running AppHost in Debug still allows breakpoints in `AAEmu.Login` and `AAEmu.Game`.
Use the dashboard and project logs to identify startup sequencing and
dependency issues.

## Health and readiness

Login service includes health endpoints:

- `/health/live`
- `/health/ready`

In Aspire, monitor readiness from dashboard state and resource logs.

## Common issues

- OCI runtime not running: start Docker Desktop or Podman first.
- `compact.sqlite3` missing: place it in `AAEmu.Game/Data`.
- Invalid `game_pak` path: set it in `Config.Local.json` and re-run.
- Port conflict on `1237`, `1239`, or `1250`: free the port or adjust local setup.
- Missing server list in client: verify login `GameServers` config values, not
  MySQL `game_servers`.

## Related

- [Home](Home)
- [Aspire Docker Publishing Guide](Aspire-Docker-Publishing-Guide)
- [Dependencies and Downloads](Dependencies-and-Downloads)
- [Installation & Setup](Installation-&-Setup)
- [Working with the Config.json files and server listings](Working-with-the-Config.json-files-and-server-listings)
- [Mini troubleshoot guide](Mini-troubleshoot-guide)
