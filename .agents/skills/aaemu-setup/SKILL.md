---
name: aaemu-setup
description: >
  Guide anyone (player or contributor) through getting AAEmu running and
  playable: asset inventory, Human-in-the-Loop downloads, config, then either
  Docker/Podman + Aspire or non-Docker host MySQL + standalone Login/Game.
  Use when the user wants to set up AAEmu, install/run the server, play on a
  local server, get the client/launcher working, choose Docker vs non-Docker,
  or mentions game_pak, compact.sqlite3, Aspire, Config.Local, or Maintenance.
---

# AAEmu setup (players and contributors)

Guide the human end-to-end. Do not assume they are a developer. Prefer plain
language; use technical detail only when needed for the chosen path.

## Non-negotiables

1. **Two pure run paths only** — no hybrids:

| Environment | Path | Database | Login / Game |
| --- | --- | --- | --- |
| Docker Desktop **or** Podman available | **A – Aspire** | MySQL **container** (AppHost) | Started by Aspire as host projects |
| **No** container runtime | **B – Standalone** | **Host MySQL 8 only** | Host processes; Login then Game |

Non-Docker means **zero** containers, including MySQL. Never invent “Docker only for the database” for Path B.

2. **Human-in-the-Loop (HitL) for large game assets** — client and compact DB
   archives live on MEGA/Drive (~multi‑GB). The agent must **not** silently
   re-download them. Always inventory first; only ask the human to download
   what is missing.

3. Official human docs: `Docs/wiki/Installation-&-Setup.md`,
   `Dependencies-and-Downloads.md`, `Client.md`, `Aspire-Development-Guide.md`.

## Workflow (always in this order)

### Step 0 — Detect audience and path

Ask if unclear:

- Goal: **play** on a local server, **contribute/code**, or both?
- Is **Docker Desktop or Podman** installed and usable?

Default: Path A if OCI works; Path B if user has or wants no Docker.

### Step 1 — Inventory assets (never blind-download)

Use the matching shell for the host (same checks, same exit codes):

| Shell | Inventory | Fetch launcher if missing | Open missing download pages (HitL) |
| --- | --- | --- | --- |
| **PowerShell** (Windows) | `powershell -File .agents/skills/aaemu-setup/scripts/Test-AaemuAssets.ps1` | add `-FetchLauncherIfMissing` | add `-OpenMissingDownloadPages` |
| **Bash** (Linux / macOS / WSL / Git Bash) | `bash .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh` | add `--fetch-launcher` | add `--open-missing` |

Examples:

```powershell
# Windows PowerShell
powershell -File .agents/skills/aaemu-setup/scripts/Test-AaemuAssets.ps1
powershell -File .agents/skills/aaemu-setup/scripts/Test-AaemuAssets.ps1 -FetchLauncherIfMissing
```

```bash
# Linux / macOS / WSL
bash .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh
bash .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh --fetch-launcher
chmod +x .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh   # optional once
```

Interpret the report:

| Status | Action |
| --- | --- |
| **OK** | Do not download or re-extract that asset |
| **MISSING** | HitL: give the human the link, wait, re-scan |
| **PARTIAL** | Archive present but not extracted (or wrong layout) — extract only |

Canonical local layout (gitignored `.client_files/`):

```text
.client_files/
  ArcheAge 1.2 (r208022) for AAEmu/     # game_pak + bin32/archeage.exe
  launcher/AAEmu.Launcher/              # AAEmu.Launcher.exe
  *.7z / *.zip                          # optional kept archives
AAEmu.Game/Data/compact.sqlite3
```

**MEGA / Drive (client, compact.sqlite3):** open the wiki links for the human
(or browser). Wait until they confirm the file is saved (prefer
`.client_files/`). Re-run the inventory script. Extract if needed.  
**Do not** loop re-downloads when `game_pak` / `archeage.exe` / `compact.sqlite3`
already satisfy the script checks.

**Launcher (GitHub):** safe to auto-fetch with `-FetchLauncherIfMissing` /
`--fetch-launcher` when absent; still skip when present.

**Note:** The official game client/launcher are **Windows** binaries. Bash
inventory still validates server-side assets (`game_pak`, `compact.sqlite3`) on
Linux hosts that only run Login/Game.

Details and URLs: [REFERENCE.md](REFERENCE.md#downloads-and-hitl).

### Step 2 — Machine prerequisites

- **Both paths:** .NET **10** SDK (`dotnet --version`).
- **Path A:** Docker Desktop or Podman **running**.
- **Path B:** MySQL **8** installed and running **on the host** (service),
  schemas imported once (`SQL/aaemu_login.sql`, `SQL/aaemu_game.sql`).

Help non-developers install these with OS-appropriate steps; do not skip
waiting for services to actually start.

### Step 3 — Local config (gitignored)

Write/update (templates in [REFERENCE.md](REFERENCE.md#configlocal-templates)):

- `AAEmu.Game/Config.Local.json` — at least `ClientData.Sources` (+ DB/LoginNetwork on Path B)
- `AAEmu.Login/Config.Local.json` — Path B: DB + `GameServers` (+ `InternalNetwork` if port remap)

Use absolute paths under this repo for `game_pak`. Match `SecretKey` on both
servers. Rebuild after creating `Config.Local.json` so it copies to output.

### Step 4 — Start servers

**Path A** (same `dotnet` commands on PowerShell or bash):

```bash
dotnet run --project AAEmu.Aspire.AppHost --launch-profile http
```

Share the dashboard login URL/token from the console. Only MySQL is a container;
Login/Game are normal processes.

**Path B:**

```bash
dotnet build
dotnet run --project AAEmu.Login
# then
dotnet run --project AAEmu.Game
```

Prefer visible consoles + tee to `.server_files/logs/` when helping a human
watch progress. Detach Windows GUIs so agent shells do not kill them.

### Step 5 — Launcher and first login

1. Start `.client_files/launcher/AAEmu.Launcher/AAEmu.Launcher.exe` (detached).
2. Path to Game → `.../bin32/archeage.exe`; server IP → `127.0.0.1`.
3. Account: AutoAccount usually creates on first login.

### Step 6 — Verify “ready to play”

- [ ] Login port **1237** listening  
- [ ] Game ports **1239** and **1250** listening  
- [ ] Path B: login log contains `Registered GameServer`  
- [ ] Client server list is **not** stuck on Maintenance  

If Maintenance: almost always game failed to register (often **port 1234**
taken). Remap internal Login+Game ports together — see REFERENCE.

## Agent behavior

- Explain what you are doing in short human-facing steps; pause for HitL
  downloads and software installs.
- Prefer inventory script over guessing file presence.
- Never commit `.client_files/`, `Config.Local.json`, `*.sqlite3`, `.server_files/`.
- Do not re-download multi‑GB assets when checks already pass.
- Path A and Path B use **different** databases by default — characters do not
  carry over unless the human migrates data.

More detail: [REFERENCE.md](REFERENCE.md).
