# FAQ

- Audience: Contributors, testers, and players
- Last verified against: working tree on October 11, 2026
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

Use [Local Development Guide](Local-Development-Guide) (`cargo xtask dev`).

### Is AAEmu still written in C#

No. The servers and tools were ported to Rust; the code is in the `rust/`
Cargo workspace. You need Rust (via [rustup](https://rustup.rs/)) to build from
source, not the .NET SDK.

### Can I still use manual setup or Docker

Yes. Both are still supported:

- [Installation & Setup](Installation-&-Setup)
- [Docker Installation Guide](Docker-Installation-Guide)

### Do I still insert game servers into MySQL `aaemu_login.game_servers`

No. Server listings are now defined in login configuration (`GameServers`) via
JSON and/or environment variables.

### Config precedence after the recent PR wave

For game server config, `Config.Local.json` is loaded after `Config.json` and
`Configurations/*.json` and overrides them. Environment variables
(`Section__Key`) and command-line arguments (`--Section:Key=value`) override
the JSON files.

### Where are the config files

In a source checkout: `rust/crates/aaemu-login/app/` and
`rust/crates/aaemu-game/app/`. In a published build: next to each binary. See
[Working with the Config.json files and server listings](Working-with-the-Config.json-files-and-server-listings).

## Client and launcher

### Where can I find the launcher

It is part of this repository: `aaemu-launcher` (`rust/crates/aaemu-launcher`).
Build it with `cargo build --release -p aaemu-launcher` in `rust/` (a published
build has it in `Tools/`) and start the client with

```sh
aaemu-launcher --client "C:\ArcheAge\bin32\archeage.exe" --login 127.0.0.1:1237 --user myname
```

It asks for the password (or use `--password`, `--password-file` or
`AAEMU_PASSWORD`). See [Client](Client#launcher-aaemu-launcher). The public AAEmu
Launcher also still works, but is not needed.

Normal launches enable fast startup, smooth startup and optimized pak loading.
Build the native driver first using [Client](Client#faster-startup-and-automated-client-tests);
the launcher finds it automatically. Individual opt-outs are `--no-fast-start`,
`--no-smooth-start` and `--no-fast-pak`.

### Where can I find client downloads

Use the client list on [Client](Client).

If you need a directory of many client versions, use:
[MEGA client directory](https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg).

For the full dependency checklist (including `compact.sqlite3`),
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

### `No game servers have been defined!` on login startup

Your login config is missing `GameServers`.
Add at least one entry in `rust/crates/aaemu-login/app/Config.Local.json` (or
the `Config.Local.json` of your login folder) or environment variables.

### Client crashes or cannot enter world

Check:

1. login and game services are both running,
1. `GameServers` host/port are reachable,
1. `game_pak` path and `compact.sqlite3` are valid.

### First stop for common setup failures

Use [Mini troubleshoot guide](Mini-troubleshoot-guide).

### Why can NPCs float above the ground

Earlier visibility packets recalculated NPC heights from the navigation mesh, whose
coarse triangles can differ from collision ground by several metres. They could also
reset an idling NPC to its original spawn height after it moved across a slope.
The current server preserves the position selected by spawning and movement when it
sends visibility packets. Rebuild and restart the game server to use this correction.

Flying and swimming NPCs retain their intended heights. For remaining cases, include
the NPC name, location and whether it floated immediately or after moving when
reporting the issue; an incorrect spawn position needs a separate correction.

## Related

- [Home](Home)
- [Installation & Setup](Installation-&-Setup)
- [Working with the Config.json files and server listings](Working-with-the-Config.json-files-and-server-listings)
- [Getting Help](Getting-Help)
