# Aspire Docker Publishing Guide

- Audience: Contributors and operators
- Last verified against: `develop` on March 3, 2026
- Prerequisites: `.NET 10 SDK`, Docker/Podman runtime, Aspire AppHost project

## Purpose

This guide covers Docker Compose artifact generation from
`AAEmu.Aspire.AppHost`.

It replaces legacy repository-level Dockerfile and root compose plumbing.

## Generated output

AppHost publish output is written to:

- `.server_files/aspire/docker/docker-compose.yaml`
- `.server_files/aspire/docker/.env`

## Service model

Generated compose includes:

- `aaemu-db`
- `aaemu-adminer`
- `aaemu-login`
- `aaemu-game`
- `aaemu-dashboard`

All services run on the AAEmu network `aaemu-net`.

## Preferred workflow (scripts)

From `Scripts`:

1. Install/generate artifacts:
   - Windows: `docker-install-local.ps1`
   - Linux: `docker-install-local.sh`
1. Start stack:
   - Windows: `docker-start-local.ps1`
   - Linux: `docker-start-local.sh`
1. Stop stack:
   - Windows: `docker-stop-local.ps1`
   - Linux: `docker-stop-local.sh`
1. Update and republish:
   - Windows: `docker-update-local.ps1`
   - Linux: `docker-update-local.sh`

## Direct CLI workflow

You can generate artifacts directly:

```bash
dotnet tool install --tool-path .server_files/tools aspire.cli --version 13.1.2
.server_files/tools/aspire publish --project AAEmu.Aspire.AppHost/AAEmu.Aspire.AppHost.csproj --output-path .server_files/aspire/docker --non-interactive
```

Then run:

```bash
sed -i "s/^AAEMU_DB_PASSWORD=$/AAEMU_DB_PASSWORD=password/" .server_files/aspire/docker/.env
docker compose --project-name aaemu --env-file .server_files/aspire/docker/.env -f .server_files/aspire/docker/docker-compose.yaml up -d
```

If you use the provided install/start/update scripts, this env-file backfill is
done automatically.

## Major note: prerelease Docker integration package

`Aspire.Hosting.Docker` is currently consumed as a prerelease package.
This is intentional until an equivalent stable release is available.

## Related

- [Aspire Development Guide](Aspire-Development-Guide)
- [Docker Installation Guide](Docker-Installation-Guide)
- [Installation & Setup](Installation-&-Setup)
