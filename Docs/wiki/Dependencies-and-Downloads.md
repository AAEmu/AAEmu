# Dependencies and Downloads

- Audience: Contributors, players, and testers
- Last verified against: `develop` on October 10, 2026
- Prerequisites: None

Use this page as the single source of truth for required downloads before setup.

## Core runtime dependencies

### AAEmu source code

- Repository: [AAEmu](https://github.com/AAEmu/AAEmu)
- Recommended branch: `develop`

### Rust toolchain (building from source)

- Required version: current stable Rust
- Install: [rustup.rs](https://rustup.rs/) (provides `cargo` and `rustc`; on
  Windows rustup also installs or asks for the MSVC build tools)
- Not needed when you run a published release build.
- AAEmu no longer uses .NET; no .NET SDK is required.

## Workflow-specific dependencies

### `cargo xtask dev` and Docker workflows

- [Docker Desktop](https://www.docker.com/products/docker-desktop/), or Docker
  Engine with the Compose plugin on Linux

### Manual workflow

- [MySQL Community Downloads](https://dev.mysql.com/downloads/mysql/)

## Game data and client dependencies

### `compact.sqlite3` (required)

Primary link:

- [MEGA compact.sqlite3](https://mega.nz/file/6vxlAZiJ#Xb8VWnG4rFRRklxw6LZaYBPdZvC6j73xLPKIPAK80S8)

Alternative links (older copy):

- [Google Drive old compact.sqlite3](https://drive.google.com/file/d/18Nm_Q7OgWOfdw_8Xl4TBXa1Z51uGHEIh/view)
- [MEGA old compact.sqlite3](https://mega.nz/file/ujhFAaIS#disveSrjdUVjY9mZ3Q2xJ2b7I4te2gwbKnzMYD8HLZ4)

### ArcheAge 1.2 client (required for login/play tests)

- [MEGA client option 1](https://mega.nz/folder/GnwjQCrZ#WNWzX_lDvkzCqoTtt7I42Q)
- [MEGA client option 2](https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg/file/qyAVQY4I)
- [Google Drive client folder](https://drive.google.com/drive/folders/1_pIBVHIm1YFal-nteGaVuXjTv3Yrsv4Q)

Directory with many client versions:

- [MEGA client directory](https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg)

### AAEmu Launcher (required for normal client login flow)

- [AAEmu Launcher releases](https://github.com/ZeromusXYZ/AAEmu-Launcher/releases)
- [AAEmu Launcher wiki](https://github.com/ZeromusXYZ/AAEmu-Launcher/wiki)

## Where files go after download

1. Place `compact.sqlite3` in `rust/crates/aaemu-game/app/Data/` (source
   checkout), `AAEmu.Game/Data/` (published build) or
   `.server_files/AAEmu.Game/Data/` (Docker).
1. Configure `game_pak` path in game config using
   `rust/crates/aaemu-game/app/Config.Local.json` (preferred) or
   `rust/crates/aaemu-game/app/Configurations/ClientData.json`, or put
   `game_pak` in `rust/crates/aaemu-game/app/ClientData/`.
1. Keep launcher extracted in any folder, then set `Path to Game` to
   `archeage.exe` (usually in client `bin32`).

## Related

- [Installation & Setup](Installation-&-Setup)
- [Local Development Guide](Local-Development-Guide)
- [Client](Client)
- [FAQ](FAQ)
