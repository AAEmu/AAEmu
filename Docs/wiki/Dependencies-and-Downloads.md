# Dependencies and Downloads

- Audience: Contributors, players, and testers
- Last verified against: `develop` on February 28, 2026
- Prerequisites: None

Use this page as the single source of truth for required downloads before setup.

## Core runtime dependencies

### AAEmu source code

- Repository: [AAEmu](https://github.com/AAEmu/AAEmu)
- Recommended branch: `develop`

### .NET SDK

- Required version: `.NET 10 SDK`
- Download: [dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## Workflow-specific dependencies

### Aspire workflow

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Podman](https://podman.io/)

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

1. Place `compact.sqlite3` in `AAEmu.Game/Data`.
1. Configure `game_pak` path in game config using
   `AAEmu.Game/Config.Local.json` (preferred) or
   `AAEmu.Game/Configurations/ClientData.json`.
1. Keep launcher extracted in any folder, then set `Path to Game` to
   `archeage.exe` (usually in client `bin32`).

## Related

- [Installation & Setup](Installation-&-Setup)
- [Aspire Development Guide](Aspire-Development-Guide)
- [Client](Client)
- [FAQ](FAQ)
