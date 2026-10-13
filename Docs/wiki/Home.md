# ![AAEmu](https://i.imgur.com/NFDY376.png)

Open source server software for ArcheAge written in Rust (originally written in C#/.NET; the C# code has been removed).

## Start Here

- Audience: Contributors, players, and testers
- Last verified against: `develop` on October 10, 2026
- Prerequisites: None
- Preferred local development: [Local Development Guide](Local-Development-Guide)
- Manual setup fallback: [Installation & Setup](Installation-&-Setup)
- Docker setup alternative: [Docker Installation Guide](Docker-Installation-Guide)
- Troubleshooting first stop: [Mini troubleshoot guide](Mini-troubleshoot-guide)
- Need help: [Getting Help](Getting-Help)

## Documentation Map

### Getting Started

- [Dependencies and Downloads](Dependencies-and-Downloads)
- [Local Development Guide](Local-Development-Guide)
- [Installation & Setup](Installation-&-Setup)
- [Docker Installation Guide](Docker-Installation-Guide)

### Configuration

- [Working with the Config.json files and server listings](Working-with-the-Config.json-files-and-server-listings)

### Operations and Troubleshooting

- [Mini troubleshoot guide](Mini-troubleshoot-guide)
- [FAQ](FAQ)

### Help and Support

- [Getting Help](Getting-Help)
- [Asking for Help on Discord](Asking-for-Help-on-Discord)
- [Asking for Help on GitHub Discussions](Asking-for-Help-on-GitHub-Discussions)
- [Help Us Help You](Help-Us-Help-You)

### Project Reference

- [Components](Components)
- [Code Terminology](Code-Terminology)
- [Developer Notes](Developer-Notes)
- [Dungeons and Mentoring](Dungeons)
- [Quest Technical Audit](Quest-Audit)
- [Mail Technical Audit](Mail-Audit)
- [Classes](Classes)
- [Client](Client)
- [Server](Server)

### Legacy Community Guides

- [Alternative Guide](https://docs.google.com/document/d/1XfZR6zb9-n2oldu1NB9e5_9eLEwam8r2wAt3vKA5Vpo/edit)
- [Alternative Guide (Russian/на русском языке)](https://docs.google.com/document/d/1O_v6dTyvv99tBgjvoGXUReKUj5azmzn7mdAD4FsUfKM/edit?usp=sharing)

### Contributing Docs

- [Documentation Maintenance](Documentation-Maintenance)

## Recent Platform Changes

- The servers have been ported from C# (.NET) to Rust. The Cargo workspace is
  in `rust/`; build with `cargo build --release`.
- The former C# projects have been removed from the repository; they remain
  only in the git history.
- `cargo xtask dev` (MySQL in Docker + login + game) is the preferred
  contributor workflow.
- The client is started with AAEmu's own `aaemu-launcher`; no third-party
  launcher is needed (see [Client](Client)).
- Developer tooling: `aaemu-clientdriver` (Lua driver for the real client) and
  the CryPhysics port `aaemu-physics` (zone collision, configured in
  `Configurations/Physics.json`).
- Runtime configuration and data live in the crates' application directories:
  `rust/crates/aaemu-login/app/` and `rust/crates/aaemu-game/app/`.
- Logging goes to stdout and is controlled with `RUST_LOG`.
- Login server game server listings are configured via `GameServers`
  configuration (not MySQL `game_servers`).
- `Config.Local.json` is loaded last and overrides all other game server
  configuration files.

## Related

- [Local Development Guide](Local-Development-Guide)
- [Dependencies and Downloads](Dependencies-and-Downloads)
- [Installation & Setup](Installation-&-Setup)
- [FAQ](FAQ)
