# AAEmu setup — reference

## Downloads and HitL

Wiki sources of truth:

- `Docs/wiki/Dependencies-and-Downloads.md`
- `Docs/wiki/Client.md`

| Asset | Approx size | Source | Automation |
| --- | --- | --- | --- |
| Client 1.2 `r208022` | multi‑GB (archive ~8GB+) | MEGA / Google Drive (wiki) | **HitL only** — inventory + user download |
| Compact / `compact.sqlite3` | tens of MB archive | MEGA (wiki) | **HitL only** — then copy into `rust/crates/aaemu-game/app/Data/` |
| AAEmu Launcher | ~few MB | GitHub releases | Optional script fetch if missing |

### Inventory scripts (PowerShell + Bash)

Same behavior on both shells: inventory only by default; never pull MEGA multi‑GB
packages. Exit `0` when all OK; exit `1` when anything MISSING/PARTIAL.

| Action | PowerShell | Bash |
| --- | --- | --- |
| Report only | `powershell -File .agents/skills/aaemu-setup/scripts/Test-AaemuAssets.ps1` | `bash .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh` |
| Fetch launcher if missing | add `-FetchLauncherIfMissing` | add `--fetch-launcher` |
| Open missing download pages | add `-OpenMissingDownloadPages` | add `--open-missing` |
| Custom repo root | `-RepoRoot 'D:\path\AAEmu'` | `--repo-root /path/AAEmu` |

```powershell
# Windows PowerShell — from repo root
powershell -File .agents/skills/aaemu-setup/scripts/Test-AaemuAssets.ps1
powershell -File .agents/skills/aaemu-setup/scripts/Test-AaemuAssets.ps1 -FetchLauncherIfMissing
powershell -File .agents/skills/aaemu-setup/scripts/Test-AaemuAssets.ps1 -OpenMissingDownloadPages
```

```bash
# Linux / macOS / WSL / Git Bash — from repo root
bash .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh
bash .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh --fetch-launcher
bash .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh --open-missing
```

Optional: `chmod +x .agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh` then run as `./.agents/skills/aaemu-setup/scripts/test-aaemu-assets.sh`.

**Skip rules (do not re-download):**

- Extracted client: `game_pak` exists and size is large (script default ≥ 1 GB) **and** `bin32/archeage.exe` exists  
- Compact: `rust/crates/aaemu-game/app/Data/compact.sqlite3` (or `.server_files/AAEmu.Game/Data/compact.sqlite3` for Docker) exists and size ≥ 1 MB  
- Launcher: `AAEmu.Launcher.exe` under `.client_files/launcher/`  
- Archives under `.client_files/*.7z` count as “archive present” but still need extract if runtime files are missing  

**Platform note:** Playing the official client requires Windows (or Wine, unsupported here). Linux hosts commonly run **Path A/B servers only**; bash still validates `game_pak` + `compact.sqlite3` for the game process.

### Expected paths after extract

```text
.client_files/ArcheAge 1.2 (r208022) for AAEmu/game_pak
.client_files/ArcheAge 1.2 (r208022) for AAEmu/bin32/archeage.exe
.client_files/launcher/AAEmu.Launcher/AAEmu.Launcher.exe
rust/crates/aaemu-game/app/Data/compact.sqlite3
```

Nested extract folders: move contents up so the paths above resolve.

### Launcher settings (`settings.aelcf`)

- `pathToGame` → absolute `archeage.exe`
- `serverIPAddress` → `127.0.0.1`
- `loginType` → `trino_1_2`

## Ports

| Port | Role |
| --- | --- |
| 1237 | Login **public** (client) |
| 1234 | Login **internal** (game registration) default |
| 1235 | Suggested alternate internal if 1234 busy |
| 1239 | Game public |
| 1250 | Game stream |
| 1280 | Game Web API (optional) |
| 3306 | MySQL (host service on Path B; Compose `db` container on Path A, `DB_PORT` in `.env`) |
| 8080 | Adminer (optional, `docker compose up -d adminer`) |

Server list for the client comes from login **`GameServers` config**, not MySQL `game_servers` rows.

## Config.Local templates

### Login (`rust/crates/aaemu-login/app/Config.Local.json`) — Path B (+ optional port remap)

```json
{
  "SecretKey": "test",
  "AutoAccount": true,
  "InternalNetwork": {
    "Host": "*",
    "Port": 1235
  },
  "Connections": {
    "MySQLProvider": {
      "Host": "127.0.0.1",
      "Port": "3306",
      "User": "root",
      "Password": "YOUR_HOST_MYSQL_PASSWORD",
      "Database": "aaemu_login"
    }
  },
  "GameServers": [
    {
      "Id": 1,
      "Name": "AAEmu.Game",
      "Host": "127.0.0.1",
      "Port": 1239,
      "Hidden": false
    }
  ]
}
```

### Game (`rust/crates/aaemu-game/app/Config.Local.json`)

Path A often only needs `ClientData`. Path B needs DB + LoginNetwork + ClientData.

```json
{
  "SecretKey": "test",
  "LoginNetwork": {
    "Host": "127.0.0.1",
    "Port": 1235
  },
  "Connections": {
    "MySQLProvider": {
      "Host": "127.0.0.1",
      "Port": "3306",
      "User": "root",
      "Password": "YOUR_HOST_MYSQL_PASSWORD",
      "Database": "aaemu_game"
    }
  },
  "ClientData": {
    "Sources": [
      "REPO_ROOT\\.client_files\\ArcheAge 1.2 (r208022) for AAEmu\\game_pak",
      "REPO_ROOT\\.client_files\\ArcheAge 1.2 (r208022) for AAEmu"
    ]
  }
}
```

Replace `REPO_ROOT` with the absolute repo path. Keep `SecretKey` identical on both sides.

Game load order: `Config.json` → `Configurations/*.json` → **`Config.Local.json`** → environment variables (`Section__Key`) → command line (`--Section:Key=value`).

`Config.Local.json` is read from the application directory at startup (by default the crate `app/` folder; `AAEMU_APP_PATH` overrides it). No rebuild is needed after editing it.

## Path A — `cargo xtask dev`

- `cd rust && cargo xtask dev --release`
- MySQL container from `docker-compose.yaml` (service `db`); user/password/port from `.env` (`DB_USER`, `DB_PASSWORD`, `DB_PORT`); data in `.server_files/AAEmu.Database/mysql`
- Login/Game are **not** Docker app containers; xtask passes DB settings, `GameServers` and `LoginNetwork` as environment variables
- Expects the login internal port **1234**; options: `--no-db`, `--db-host`, `--db-port`
- Adminer: `docker compose up -d adminer` → http://localhost:8080

## Path B — Host MySQL

```text
[ ] MySQL 8 host service up
[ ] aaemu_login / aaemu_game created and SQL imported
[ ] Config.Local on Login + Game
[ ] SecretKey match; internal ports match and free
[ ] compact.sqlite3 + game_pak OK (inventory script)
[ ] Login, then Game
[ ] Log: Registered GameServer
```

```bash
mysql -u root -p -e "CREATE DATABASE IF NOT EXISTS aaemu_login; CREATE DATABASE IF NOT EXISTS aaemu_game;"
mysql -u root -p aaemu_login < SQL/aaemu_login.sql
mysql -u root -p aaemu_game  < SQL/aaemu_game.sql
```

## Process / log hygiene

**Windows (PowerShell agents):**

- Detach launcher/server windows (`Win32_Process.Create`) so agent Job Objects do not kill them.
- Tee logs: `cargo run --release -p aaemu-login 2>&1 | Tee-Object -FilePath .server_files/logs/login.log`
  (logs go to stdout; set `$env:RUST_LOG = "debug"` for more detail, `$env:NO_COLOR = "1"` for plain text files)

**Linux / macOS (bash agents):**

- Run Login/Game in separate terminals or `tmux`/`screen`, or:

```bash
mkdir -p .server_files/logs
cd rust
NO_COLOR=1 cargo run --release -p aaemu-login 2>&1 | tee ../.server_files/logs/login.log
NO_COLOR=1 cargo run --release -p aaemu-game  2>&1 | tee ../.server_files/logs/game.log
```

- Launcher/client play remains a Windows-side step if the human has no Windows client host.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| **Maintenance** on server list | Game not registered (port 1234 conflict, bad LoginNetwork, SecretKey) |
| `cargo xtask dev` fails at start | Docker not running (or use `--no-db` with a host MySQL) |
| Missing data / sqlite errors | No `compact.sqlite3` in the game `Data/` folder |
| `cargo` not found | Rust not installed or shell not restarted after rustup |
| Bad client data | Wrong `ClientData.Sources` |
| Lost characters after path switch | Path A and Path B databases are separate |
| Multi‑GB download again | Agent skipped inventory — always run `Test-AaemuAssets.ps1` or `test-aaemu-assets.sh` first |

Success line:

```text
Registered GameServer ... (AAEmu.Game) from ...
```

## Not supported as “the” path

- Docker MySQL + host apps labeled as non-Docker setup  
- `cargo xtask dev` without Docker (except `--no-db` against a host MySQL)  
- Committing client/launcher/sqlite into git  
