# Docker Installation Guide

- Audience: Operators and contributors
- Last verified against: `develop` on February 28, 2026
- Prerequisites: Docker runtime, Git, required AAEmu data files

## When to use this guide

Use this guide when you want containerized AAEmu without Aspire orchestration.

If you want the preferred contributor startup flow, use
[Aspire Development Guide](Aspire-Development-Guide).

## Prerequisites

1. Install Git.
1. Install Docker Desktop (Windows) or Docker Engine + Compose (Linux).
1. Place required files where scripts expect them:
   - `compact.sqlite3`
   - ArcheAge `game_pak`

## Initial install

1. Clone `https://github.com/AAEmu/AAEmu`.
1. Change directory to `Scripts`.
1. Run:
   - Windows: `docker-install-local.ps1`
   - Linux: `docker-install-local.sh`

## Update an existing install

1. Change directory to `Scripts`.
1. Run:
   - Windows: `docker-update-local.ps1`
   - Linux: `docker-update-local.sh`

## Launch

From project root:

- Detached mode: `docker compose up -d`
- Dev/watch mode: `docker compose watch`

## Important configuration notes

### Login container runtime

Login server public networking is ASP.NET Core Kestrel-based.
The login container must use an ASP.NET runtime image.

### Server listing source

Server listings are configuration-driven (`GameServers`) and can be injected
through environment variables in compose, for example:

```text
GameServers__0__ID=1
GameServers__0__Name=AAEmu.Game
GameServers__0__Host=127.0.0.1
GameServers__0__Port=1239
```

Do not depend on MySQL `aaemu_login.game_servers` inserts.

## Troubleshooting

- Docker API or daemon not available: start Docker before running commands.
- Installation script fails on Windows policy: adjust execution policy for your
  user if needed.
- Services start but client cannot connect: verify `GameServers` host/port and
  exposed compose ports.

## Related

- [Home](Home)
- [Aspire Development Guide](Aspire-Development-Guide)
- [Installation & Setup](Installation-&-Setup)
- [Mini troubleshoot guide](Mini-troubleshoot-guide)
