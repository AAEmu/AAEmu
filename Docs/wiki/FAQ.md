# FAQ

- Audience: Contributors, testers, and players
- Last verified against: `develop` on February 28, 2026
- Prerequisites: None

## Project basics

### What is AAEmu

AAEmu is an open source server emulator for ArcheAge 1.2.

### Which client version is supported

`develop` targets ArcheAge 1.2 (`r208022`).

### How can I help

Join the community on [Discord](https://discord.gg/aaemu) and contribute code,
testing, or issue reports.

### Where should I ask support questions

Use both official support channels as needed:

- Real-time troubleshooting:
  [Discord](https://discord.gg/aaemu)
- Searchable long-form support:
  [GitHub Discussions](https://github.com/AAEmu/AAEmu/discussions)

See [Getting Help](Getting-Help) and [Help Us Help You](Help-Us-Help-You).

## Setup and local development

### Preferred way to run locally

Use [Aspire Development Guide](Aspire-Development-Guide).

### Can I still use manual setup or Docker

Yes. Both are still supported:

- [Installation & Setup](Installation-&-Setup)
- [Docker Installation Guide](Docker-Installation-Guide)

### Do I still insert game servers into MySQL `aaemu_login.game_servers`

No. Server listings are now defined in login configuration (`GameServers`) via
JSON and/or environment variables.

### Config precedence after the recent PR wave

For game server config, `Config.Local.json` is loaded last and overrides all
other game config files.

## Client and launcher

### Where can I find the launcher

Use the latest release:
[AAEmu Launcher releases](https://github.com/ZeromusXYZ/AAEmu-Launcher/releases)

### Where can I find client downloads

Use the client list on [Client](Client).

If you need a directory of many client versions, use:
[MEGA client directory](https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg).

For the full dependency checklist (including `compact.sqlite3` and launcher),
see [Dependencies and Downloads](Dependencies-and-Downloads).

## Configuration and networking

### Which ports matter by default

Common defaults:

- `1237`: login public
- `1239`: game public
- `1250`: game stream
- `1280`: optional game Web API

### What should `GameServers` host be for local use

Use `127.0.0.1` for local machine tests.
For LAN or external players, set a reachable address.

## Troubleshooting

### `DataAnnotation validation failed ... GameServers` on login startup

Your login config is missing `GameServers`.
Add at least one entry in `AAEmu.Login/Config.Local.json` or environment
variables.

### Client crashes or cannot enter world

Check:

1. login and game services are both running,
1. `GameServers` host/port are reachable,
1. `game_pak` path and `compact.sqlite3` are valid.

### First stop for common setup failures

Use [Mini troubleshoot guide](Mini-troubleshoot-guide).

## Related

- [Home](Home)
- [Installation & Setup](Installation-&-Setup)
- [Working with the Config.json files and server listings](Working-with-the-Config.json-files-and-server-listings)
- [Getting Help](Getting-Help)
