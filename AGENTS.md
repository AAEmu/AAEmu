# AGENTS.md — AAEmu

Guidance for coding agents working in this repository.

## What this repo is

Open-source **ArcheAge** server emulator in **.NET** (`AAEmu.Login`, `AAEmu.Game`, shared `AAEmu.Commons`). Preferred local orchestration is **.NET Aspire** (`AAEmu.Aspire.AppHost`). Branch of record for active work: **`develop`**.

Human docs live under `Docs/wiki/` (synced to GitHub wiki). Prefer those over inventing setup steps.

## Getting the stack running (players and contributors)

Use the in-repo skill — **not developer-only**:

- **[`.agents/skills/aaemu-setup/SKILL.md`](.agents/skills/aaemu-setup/SKILL.md)** — guided setup with HitL downloads  
- **[`.agents/skills/aaemu-setup/REFERENCE.md`](.agents/skills/aaemu-setup/REFERENCE.md)** — ports, configs, troubleshooting  
- **Inventory (skip re-downloads)** — use the host shell:  
  - PowerShell: `powershell -File .agents/skills/aaemu-setup/scripts/Test-AaemuAssets.ps1`  
  - Bash: `bash .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh`

| Path | When | MySQL | Apps |
| --- | --- | --- | --- |
| **A – Aspire** | Docker Desktop or Podman available | Container via AppHost | Login/Game as host projects |
| **B – Standalone** | No container runtime | **Host MySQL 8 only** | Host `dotnet` Login then Game |

**No hybrids** for non-Docker: if there is no Docker/Podman, do not introduce containers for MySQL either.

**Assets:** always run the inventory script before downloading. Multi‑GB MEGA/Drive packages are **Human-in-the-Loop**; do not re-fetch when the script reports **OK**.

Wiki mirrors: `Docs/wiki/Installation-&-Setup.md`, `Aspire-Development-Guide.md`, `Dependencies-and-Downloads.md`, `Client.md`.

## Layout (high signal)

| Path | Role |
| --- | --- |
| `AAEmu.Login/` | Login server (public client port **1237**, internal GS port **1234** default) |
| `AAEmu.Game/` | Game server (public **1239**, stream **1250**) |
| `AAEmu.Aspire.AppHost/` | Aspire orchestrator (preferred when Docker/Podman available) |
| `SQL/` | Schema / updates (`aaemu_login.sql`, `aaemu_game.sql`) |
| `Docs/wiki/` | Setup and contributor documentation |
| `.client_files/` | **Local only** (gitignored): extracted 1.2 client + launcher |
| `.server_files/` | **Local only** (gitignored): Compose/runtime data, optional logs |
| `**/Config.Local.json` | **Local only** (gitignored): machine overrides |

Do not commit client packs, launcher binaries, `compact.sqlite3`, or secrets.

## Configuration rules

- Game config load order: `Config.json` → `Configurations/*.json` → **`Config.Local.json` (wins)**.
- Login listings: **`GameServers` in config**, not MySQL `game_servers` inserts.
- `SecretKey` must match between Login and Game.
- `ClientData.Sources` should include the 1.2 `game_pak` (absolute path under `.client_files/` is fine).
- `compact.sqlite3` → `AAEmu.Game/Data/` (required for game data).
- `Config.Local.json` is copied to output on build when present in the project directory.

## Build and test

```powershell
dotnet build
dotnet test
```

- SDK: .NET **10** (`global.json`).
- Solution: `AAEmu.slnx` (and related projects).
- Prefer existing test projects: `AAEmu.UnitTests`, integration test projects as applicable.

## Coding norms

- Match existing style in the area you touch (C# naming, managers/packets layout).
- Prefer small, focused changes; no drive-by refactors.
- Do not add unsolicited markdown docs; update wiki-facing docs only when setup/behavior changes require it.
- Game “Skills” under `AAEmu.Game/Models/Game/Skills` are **game mechanics**, not agent skills.

## Windows agent pitfalls

- Detach long-lived GUIs (launcher, server consoles) from the agent shell Job Object or they die when the command ends.
- Port **1234** often conflicted (e.g. ManyCam): remap Login `InternalNetwork` + Game `LoginNetwork` together or the client shows **Maintenance**.
- Aspire dashboard token is printed by AppHost at startup (`/login?t=...`).

## Out of scope unless asked

- Shipping client assets into git
- Silent multi‑GB MEGA re-downloads without inventory + HitL
- Changing default production Docker Compose passwords casually
- Full protocol reverse-engineering writeups without a concrete task

## Quick verify (before saying “ready to play”)

1. Inventory script: client + compact + launcher **OK**
2. Login listens on **1237**
3. Game listens on **1239** / **1250**
4. Standalone: login log shows **Registered GameServer**
5. Launcher points at `.client_files/.../bin32/archeage.exe`, server `127.0.0.1`
