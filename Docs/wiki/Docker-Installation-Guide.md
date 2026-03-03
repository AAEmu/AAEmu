# Docker Installation Guide

- Audience: Operators and contributors
- Last verified against: `develop` on March 3, 2026
- Prerequisites: Docker runtime, Git, required AAEmu data files

## When to use this guide

Use this guide when you want containerized AAEmu using Aspire-generated Docker
Compose artifacts.

For full AppHost details, see
[Aspire Development Guide](Aspire-Development-Guide).

## Prerequisites

1. Install Git.
1. Install Docker Desktop (Windows) or Docker Engine + Compose (Linux).
1. Place required files in the repository:
   - `compact.sqlite3` in `AAEmu.Game/Data`
   - configure `game_pak` path (recommended in `AAEmu.Game/Config.Local.json`)

## Initial install

1. Clone `https://github.com/AAEmu/AAEmu`.
1. Change directory to `Scripts`.
1. Run:
   - Windows: `docker-install-local.ps1`
   - Linux: `docker-install-local.sh`

This generates compose artifacts in `.server_files/aspire/docker`.

## Start and stop

From `Scripts` directory:

- Start:
  - Windows: `docker-start-local.ps1`
  - Linux: `docker-start-local.sh`
- Stop:
  - Windows: `docker-stop-local.ps1`
  - Linux: `docker-stop-local.sh`

## Update an existing install

1. Change directory to `Scripts`.
1. Run:
   - Windows: `docker-update-local.ps1`
   - Linux: `docker-update-local.sh`

This stops containers, updates repository state, and regenerates compose
artifacts from AppHost.

## Important configuration notes

### Service naming and network

Generated compose uses explicit AAEmu names:

- `aaemu-db`
- `aaemu-adminer`
- `aaemu-login`
- `aaemu-game`
- `aaemu-dashboard`
- network: `aaemu-net`

### Env-file values

The generated `.env` file is produced by AppHost (`ConfigureEnvFile`).
The install/start/update scripts also backfill empty values with AAEmu defaults
(ports, compose name, network name, and local DB password) so the stack starts
without manual editing.

### Server listing source

Server listings are configuration-driven (`GameServers`) and should not rely on
MySQL `aaemu_login.game_servers` inserts.

## Troubleshooting

- Docker API or daemon not available: start Docker before running scripts.
- Script execution policy blocks PowerShell scripts: adjust user-level execution
  policy.
- Services start but client cannot connect: verify host port mappings from
  generated `.server_files/aspire/docker/.env` and launcher settings.

## Related

- [Home](Home)
- [Aspire Development Guide](Aspire-Development-Guide)
- [Aspire Docker Publishing Guide](Aspire-Docker-Publishing-Guide)
- [Installation & Setup](Installation-&-Setup)
- [Mini troubleshoot guide](Mini-troubleshoot-guide)
