# Docker Installation Guide

- Audience: Operators and contributors
- Last verified against: `develop` on October 10, 2026
- Prerequisites: Docker runtime, Git, required AAEmu data files

## When to use this guide

Use this guide when you want to run the AAEmu servers themselves in containers.

If you want the preferred contributor startup flow (servers on the host, only
MySQL in Docker), use [Local Development Guide](Local-Development-Guide).

## Prerequisites

1. Install Git.
1. Install Docker Desktop (Windows) or Docker Engine + Compose (Linux).
1. Have the required files ready (see the last step of the install script):
   - `compact.sqlite3` goes to `.server_files/AAEmu.Game/Data/`
   - ArcheAge `game_pak` goes to `.server_files/AAEmu.Game/ClientData/` (or
     set its path in `.server_files/AAEmu.Game/Configurations/ClientData.json`)

Rust does not need to be installed on the host: the images are built with
`docker/Dockerfile` (a Rust build stage and a slim Debian runtime stage, targets
`login` and `game`).

## Initial install

1. Clone `https://github.com/AAEmu/AAEmu`.
1. Change directory to `Scripts`.
1. Run:
   - Windows: `docker-install-local.ps1`
   - Linux: `docker-install-local.sh`

The install script creates `.env` from `.env.example`, generates a database
password and a `SecretKey`, copies the default configuration from
`rust/crates/aaemu-login/app/` and `rust/crates/aaemu-game/app/` to
`.server_files/AAEmu.Login/` and `.server_files/AAEmu.Game/` (mounted into the
containers at `/app`), and builds the images.

## Update an existing install

1. Change directory to `Scripts`.
1. Run:
   - Windows: `docker-update-local.ps1`
   - Linux: `docker-update-local.sh`

## Launch

From project root:

- Detached mode: `docker compose up -d`
- Dev/watch mode: `docker compose watch` (rebuilds the images when files under
  `rust/` change)
- Database UI (optional): `docker compose up -d adminer`, then open
  <http://localhost:8080>

Exposed ports: login `1237`, game `1239` and `1250`, MySQL `DB_PORT` (default
`3306`).

## Important configuration notes

### `.env` settings

- `PROJECT_VERSION_PREFIX`, `PROJECT_VERSION_SUFFIX`: image tags.
- `BUILD_PROFILE`: cargo profile of the server images (`release` or `dev`).
- `DB_USER`, `DB_PASSWORD`, optional `DB_PORT`: MySQL container settings.

Logging goes to the container output (`docker compose logs -f game`); set
`RUST_LOG` (for example `RUST_LOG=debug`) in the environment of
`docker compose up` to change the log level.

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
- [Local Development Guide](Local-Development-Guide)
- [Installation & Setup](Installation-&-Setup)
- [Mini troubleshoot guide](Mini-troubleshoot-guide)
