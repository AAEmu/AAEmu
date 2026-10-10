# AGENTS.md — AAEmu

Guidance for coding agents working in this repository.

## What this repo is

Open-source **ArcheAge** server emulator written in **Rust** (Cargo workspace in `rust/`: `aaemu-login`, `aaemu-game`, shared `aaemu-commons`, tools, a headless test client and `xtask`). Local orchestration is **`cargo xtask dev`** (MySQL in Docker + login + game). Branch of record for active work: **`develop`**.

The servers were ported from C# (.NET). The old C# projects (`AAEmu.*`, `Tools/`) may still be on disk during the transition; they are **not** built or used any more. Do not edit them or add code there.

Target client: **ArcheAge 1.2** (`r208022`).

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
| **A – `cargo xtask dev`** | Docker Desktop available | Compose `db` service, started by xtask | Login/Game as host processes (`cargo run`) |
| **B – Standalone** | No container runtime | **Host MySQL 8 only** | Host `cargo run -p aaemu-login` then `-p aaemu-game` |

**No hybrids** for non-Docker: if there is no Docker, do not introduce containers for MySQL either (`cargo xtask dev --no-db` against a host MySQL is fine).

**Assets:** always run the inventory script before downloading. Multi‑GB MEGA/Drive packages are **Human-in-the-Loop**; do not re-fetch when the script reports **OK**.

Wiki mirrors: `Docs/wiki/Installation-&-Setup.md`, `Local-Development-Guide.md`, `Dependencies-and-Downloads.md`, `Client.md`.

---

## Runtime architecture

```text
                    ┌─────────────────┐
  Client 1.2 ──────►│  aaemu-login    │◄── MySQL aaemu_login (accounts)
  (launcher)        │  :1237 public   │
                    │  :1234 internal │
                    └────────┬────────┘
                             │ GameServer register + enter-world
                    ┌────────▼────────┐
                    │  aaemu-game     │◄── MySQL aaemu_game (mutable state)
                    │  :1239 game     │◄── compact.sqlite3 (read-only reference)
                    │  :1250 stream   │◄── game_pak / client files
                    │  :1280 web API  │
                    └─────────────────┘
                             ▲
              optional: cargo xtask dev (MySQL container + env injection)
```

| Component | Role |
| --- | --- |
| **Login** (`aaemu-login`) | Auth, world list, enter-world handoff. Public client TCP (tokio) on 1237, internal TCP for Game registration on 1234. |
| **Game** (`aaemu-game`) | World simulation: packets, managers, entities, combat, quests, housing, etc. tokio runtime; startup in `main.rs`. |
| **Stream** | Side channel on Game (`:1250`) for UCC/emblems and related transfers (`aaemu-game/src/stream/`). |
| **`cargo xtask dev`** | Local orchestration only; does **not** replace Login/Game. |
| **SQLite `compact.sqlite3`** | **Read-only** static game data (items, NPCs, skills, quests templates, …). |
| **MySQL** | **Read/write** state: `aaemu_login` (accounts) + `aaemu_game` (characters, items, world). |

Sequence (play): start Login → start Game (registers with Login) → launcher/client auth on Login → select server → connect to Game.

Authoritative component diagram: [`Docs/wiki/Components.md`](Docs/wiki/Components.md).

---

## Workspace and crate map

| Path | Role |
| --- | --- |
| `rust/Cargo.toml` | Workspace root: members and shared `[workspace.dependencies]` — bump deps here |
| `rust/Cargo.lock` | Locked dependency versions (committed) |
| `.cargo/config.toml` | `cargo xtask` alias (machine-local settings go in `rust/.cargo/config.toml`, gitignored) |
| `rust/crates/aaemu-commons/` | Shared network primitives (`packet_stream.rs`, `net.rs`), MySQL helpers + SQL updater (`mysql.rs`), AAPak (`aapak/`), client files, utilities |
| `rust/crates/aaemu-login/` | Login server (`src/`, runtime files in `app/`) |
| `rust/crates/aaemu-game/` | Game server (largest crate; runtime files in `app/`) |
| `rust/crates/aaemu-testclient/` | Headless 1.2 client bot: live smoke tests, parity diffs, load tests ([`rust/LIVE-TESTING.md`](rust/LIVE-TESTING.md)) |
| `rust/crates/aaemu-tool-worldconverter/` | Offline world / heightmap converter |
| `rust/crates/aaemu-tool-updatesfortransform/` | Offline spawn-data update tool |
| `rust/crates/xtask/` | `cargo xtask ci` / `dev` / `publish` |
| `rust/PORT-AUDIT.md` | C# → Rust coverage audit (regenerate with `python rust/tools/port_audit/gen.py rust/PORT-AUDIT.md`) |
| `SQL/` | Base schema + incremental updates |
| `Docs/wiki/` | Human-facing setup and architecture docs |
| `Scripts/` | Build/start/publish/watch helpers (bat/ps1/sh) |
| `docker/Dockerfile`, `docker-compose.yaml` | Container images (`login`, `game` targets) and the Compose stack |
| `.client_files/` | **Local only** (gitignored): extracted 1.2 client + launcher |
| `.server_files/` | **Local only** (gitignored): Compose/runtime data |
| `**/Config.Local.json` | **Local only** (gitignored): machine overrides |

Do not commit client packs, launcher binaries, `compact.sqlite3`, or secrets.

### Game crate layout (high signal)

Read [`rust/crates/aaemu-game/CONTRIBUTING-PORT.md`](rust/crates/aaemu-game/CONTRIBUTING-PORT.md) before changing game code; [`PORTING.md`](rust/crates/aaemu-game/PORTING.md) records status, deviations and mirrored C# bugs.

| Path under `rust/crates/aaemu-game/` | Role |
| --- | --- |
| `src/main.rs` | Startup order (DB updates, client files, game data, id/name/item loads, `systems::start_all`, networks) |
| `src/server.rs` | `GameServer`: config, db pool, game data, packet registry, managers, `systems` |
| `src/config.rs` | Configuration model + loading (JSON files, env, command line) |
| `src/opcodes.rs` | Complete 1.2 opcode tables (`cs`, `sc`) — never edit |
| `src/managers/` | Core runtime managers (account, character, chat, expedition, friend, id, item, name, save, stream, time) |
| `src/net/` | Game TCP listener, connection, packet encode/decode, `PacketRegistry` |
| `src/packets/` | Core packets: `c2g*.rs` (client → game), `g2c*.rs` / `world*.rs` (game → client), `proxy.rs` |
| `src/login/` | Game ↔ Login internal link and its packets |
| `src/stream/` | Stream server (`:1250`): `c2s.rs`, `s2c.rs`, UCC |
| `src/data/` | Static data loaders (compact.sqlite3, `Data/*.json`, client files) → `GameData` |
| `src/models/` | Domain types (unit, character, npc, doodad, slave, mate, house, buff, item, transform, …) |
| `src/world/` | World instances, object handles (`object.rs`), visibility, unit state, player session |
| `src/systems/` | Subsystems, one directory each: `ai`, `economy`, `housing`, `instances`, `npcs`, `quests`, `services`, `skills`, `social`, `vehicles`; `systems/mod.rs` holds the only central lists |
| `src/systems/services/commands/` | In-game GM/admin commands |
| `src/systems/services/webapi/`, `discord.rs` | Web API (`:1280`), Discord bot |
| `src/systems/vehicles/` | Slaves, mates, transfers, shipyards, ship physics |
| `src/test_support.rs`, `*_tests.rs`, `tests/` | Test helpers, flow tests, fixtures |
| `app/` | Application directory: `Config.json`, `Configurations/`, `Data/`, `ClientData/`, `Scripts/Commands/kits.json` |

### Login crate layout

| Path under `rust/crates/aaemu-login/` | Role |
| --- | --- |
| `src/main.rs`, `src/server.rs` | Startup, listeners, DB updates |
| `src/config.rs` | Configuration model + loading |
| `src/network/` | Public client listener (`login.rs`) and Game ↔ Login internal protocol (`internal.rs`) |
| `src/packets/{c2l,l2c,g2l,l2g}.rs` | Packet types |
| `src/handlers.rs` | Packet handlers (separate from packet types) |
| `src/controllers/` | Login / Game / Request controllers |
| `src/auth.rs`, `src/services/` | Auth flows (password, Korea challenge, OTP/2FA, reconnect), password hashing, 2FA |
| `src/db.rs`, `src/backend.rs` | MySQL access |
| `tests/` | Network and MySQL-gated integration tests |
| `app/` | Application directory: `Config.json`, `ExampleConfig.Local.json` |

---

## Configuration rules

- Application directory: `AAEMU_APP_PATH`, else the executable's directory or the current directory when it holds a `Config.json`, else the crate's `app/` folder (game: the first CLI argument wins over all of these).
- Game config load order: `Config.json` → `Configurations/**/*.json` → **`Config.Local.json`** → legacy user-secrets file (if present) → env vars (`Section__Key`) → command line (`--Section:Key=value`).
- Login: `Config.json` → `Config.Local.json` → env vars → command line (`cargo xtask dev` injects env).
- Login listings: **`GameServers` in config**, not MySQL `game_servers` inserts.
- `SecretKey` must match between Login and Game.
- `ClientData.Sources` should include the 1.2 `game_pak` (absolute path under `.client_files/` is fine).
- `compact.sqlite3` → `rust/crates/aaemu-game/app/Data/` (or `Data/` of the published / Docker game folder; required for game data).
- Logging: `RUST_LOG` (default `info`), output on stdout; `NO_COLOR=1` disables colors.

Details: [`Docs/wiki/Working-with-the-Config.json-files-and-server-listings.md`](Docs/wiki/Working-with-the-Config.json-files-and-server-listings.md).

---

## Data stores

| Store | Location / schema | Mutability | Used for |
| --- | --- | --- | --- |
| **compact.sqlite3** | `rust/crates/aaemu-game/app/Data/compact.sqlite3` | Read-only at runtime | Templates: items, NPCs, skills, quests, doodads, … via `src/data/` and `systems/*/data.rs` |
| **MySQL aaemu_login** | `SQL/aaemu_login.sql` + `SQL/updates/*login*` | Read/write | Accounts, 2FA, bans |
| **MySQL aaemu_game** | `SQL/aaemu_game.sql` + `SQL/updates/*game*` | Read/write | Characters, inventories, housing, mails, auction, … |
| **Client files** | `game_pak` / extracted client | Read-only | Models, geodata, assets via the client file manager |
| **JSON configs** | `Config*.json`, `Configurations/`, `Data/**/*.json` | Config-time | Server params, worlds, spawns-related data |

### SQL change workflow

When code needs a schema change:

1. Add `SQL/updates/YYYY-MM-DD_aaemu_{login|game}_*.sql` (date orders application).
2. **Also** patch the base file `SQL/aaemu_login.sql` or `SQL/aaemu_game.sql`.
3. Servers apply relevant updates once at startup when `Connections:AutoApplyUpdates` is true (`aaemu_commons::mysql`); applied scripts are recorded in an updates table.

See `SQL/updates/readme.txt`. Prefer `SQL/patches/compact/` only for intentional compact.sqlite3 fixups (not normal gameplay state).

---

## Game startup lifecycle

Entry: `rust/crates/aaemu-game/src/main.rs` (`#[tokio::main]`).

Approximate stages:

1. **Config + logging** — resolve the application directory, load config, init `tracing` from `RUST_LOG`.
2. **DB migrate** — MySQL updates for `aaemu_game`.
3. **Client files** — client file sources from `ClientData.Sources` (fatal if none).
4. **Game data** — `GameData::load` (compact.sqlite3, JSON, client files; includes every `systems::*::Data::load`). A failing loader is fatal.
5. **`GameServer::new`** — managers, packet registry, static world instances.
6. **DB loads** — id managers, user items, names, expeditions, friends, UCCs.
7. **`systems::start_all`** — subsystem DB loads, timers and hook installation; then `SaveManager::start`.
8. **Networks** — game, stream, login link and web API listeners.

Shutdown (Ctrl+C) saves and stops networks and ticks.

Ordering is explicit code in `main.rs` and `systems::start_all`, not a DI container. Cross-subsystem calls go through the hook traits in `systems/mod.rs` (`systems::Hooks`, `UnitHooks`), which subsystems install in their `System::start`.

---

## Packet map and conventions

### Packet locations (Game)

| Location | Prefix | Direction | Notes |
| --- | --- | --- | --- |
| `src/packets/c2g*.rs`, `systems/*/packets.rs` | `CS*` | Client → Game | Implement `ClientPacket`; registered in `systems::X::register` or the core registry |
| `src/packets/g2c*.rs`, `world*.rs`, `systems/*/packets.rs` | `SC*` | Game → Client | `packet_def!` + `impl Marshal` |
| `src/stream/c2s.rs` | `CT*` | Client → Stream | Stream server |
| `src/stream/s2c.rs` | `TC*` | Stream → Client | Stream responses |
| `src/login/packets.rs` | `GL*` / `LG*` | Game ↔ Login | Internal |
| `src/packets/proxy.rs` | Proxy | Level-2 / proxy packets | Legacy/proxy protocol helpers |

### Packet locations (Login)

| File | Prefix | Direction |
| --- | --- | --- |
| `src/packets/c2l.rs` | `CA*` | Client → Login |
| `src/packets/l2c.rs` | `AC*` | Login → Client |
| `src/packets/g2l.rs` / `l2g.rs` | `GL*` / `LG*` | Game ↔ Login |

### Patterns

**Game packets** (see `CONTRIBUTING-PORT.md` § "Registering packets"):

- One struct per opcode; inbound packets implement `ClientPacket` (`read(&mut PacketStream)` + `handle(server, conn)`), registered with `reg.register::<CSFooPacket>(LEVEL_PLAIN, cs::CSFooPacket)`.
- Outbound packets: struct + `packet_def!(Name, level = ..., opcode = sc::Name)` + `impl Marshal`; packets built from a live object are encoded while holding its lock and sent as `raw_body_packet!`.
- `packets::tests::no_duplicate_registrations` fails if two modules register the same opcode.
- Wire formats must match the 1.2 protocol byte for byte; add byte-exact tests for every S → C packet.
- Access the player through the connection's active character; world lookups through `server.world`.

**Login packets**: packet types in `src/packets/`, handlers in `src/handlers.rs` / `controllers/`.

Do not invent opcodes; use the 1.2 tables in `opcodes.rs` (game) and the login packet modules.

---

## Domain model and terminology

Use **code/wiki terms**, not modern player slang, in identifiers and discussions.

| Term | Meaning |
| --- | --- |
| **Doodad** | Spawnable object without a health bar (crops, doors, furniture) |
| **Unit** | Entity with health / combat participation |
| **NPC** | Non-player unit |
| **Mate** | Pet / mount companion |
| **Slave** | Vehicle (cart, ship, car) |
| **Transfer** | Fixed-route transport (carriage, airship) |
| **Expedition** | Guild |
| **Appellation** | Title |
| **Ability** | Class combat skill tree |
| **ActAbility** | Vocational skill |
| **Dominion** | Castle siege content (not GvG shorthand) |
| **Indun** | Instance dungeon |
| **Gimmick** | Moving unit-like object (e.g. elevator) |
| **Skills** under `systems/skills/` | **Game combat mechanics**, not agent skills |

### Object model (simplified)

```text
WorldObject (enum, world/object.rs)
  ├─ units: embed UnitBase (models/unit.rs)
  │    ├─ Character (PlayerRef), Npc (+ Portal), Mate, Slave
  │    └─ House, Shipyard, Gimmick, Transfer
  └─ Doodad (BaseUnit only)
```

Each handle (`NpcRef`, `DoodadRef`, `SlaveRef`, …) is an `Entity<D>` (immutable obj id + `ObjectCore`) with the data behind one `Mutex`. Follow the locking rules in `CONTRIBUTING-PORT.md` (hold at most one object data lock; never call into the world while holding it).

Full glossary: [`Docs/wiki/Code-Terminology.md`](Docs/wiki/Code-Terminology.md).

### Models vs data vs managers

| Layer | Responsibility | Example |
| --- | --- | --- |
| **Models** (`src/models/`) | Entity state and domain behavior | `Character`, `Npc`, `Doodad`, `UnitBase` |
| **Data** (`src/data/`, `systems/*/data.rs`) | Load/cache **static** templates from SQLite/JSON | item, skill, NPC template tables in `GameData` |
| **Managers / systems** (`src/managers/`, `systems/*`) | Runtime orchestration, spawns, persistence | `ItemManager`, `WorldManager`, quest engine |
| **Packets** | Wire protocol edge | `CSStartSkillPacket` → system/model |

Static reference data → `GameData` + `compact.sqlite3`.  
Per-character / world mutable state → managers/systems + MySQL.

---

## Where to change X (task routing)

Paths below are relative to `rust/crates/aaemu-game/src/` unless stated otherwise.

| Task | Start here |
| --- | --- |
| Client packet handling (gameplay action) | `systems/<area>/packets.rs` or `packets/c2g*.rs` → related system/model |
| Server→client notify | `systems/<area>/packets.rs` or `packets/g2c*.rs` / `world_objects.rs` |
| Login auth / world list | `rust/crates/aaemu-login/src/handlers.rs`, `auth.rs`, `controllers/`, `services/` |
| New runtime state / manager | Field of `systems::X::System` in `systems/X/`; startup in `System::start` |
| Static template data | `systems/X/data.rs` (`Data::load`) or `data/`; optional `SQL/patches/compact/` |
| Character behavior | `models/character.rs`, `managers/character.rs`, `systems/services/character.rs` |
| NPC / AI | `systems/npcs/`, `systems/ai/` |
| Skills / buffs / effects | `systems/skills/` (`effects/`, `buffs.rs`), `models/buff.rs` |
| Quests | `systems/quests/` |
| Housing / doodads | `systems/housing/`, `systems/npcs/doodad/`, `models/{house,doodad}.rs` |
| World / zones / spawns | `world/`, `systems/npcs/spawn_manager.rs`, `app/Data/Worlds/` |
| Ships / vehicle physics | `systems/vehicles/` (`ship_physics.rs`, `slaves.rs`) |
| GM commands | `systems/services/commands/` |
| Schema migration | `SQL/updates/` + base `SQL/aaemu_*.sql` |
| Web API / Discord | `systems/services/webapi/`, `systems/services/discord.rs` |
| Shared serialization / MySQL util | `rust/crates/aaemu-commons/` |
| Dependency version bump | `[workspace.dependencies]` in `rust/Cargo.toml` |

---

## Build and test

Run from `rust/`:

```sh
cargo build --workspace --all-targets
cargo test --workspace
cargo fmt --all --check
cargo clippy --workspace --all-targets -- -D warnings
cargo xtask ci            # fmt --check + clippy -D warnings + test, exactly as CI
```

- Toolchain: stable Rust via rustup.
- Filter example: `cargo test -p aaemu-game unit_state` (substring of the test path).
- Unit tests live in `#[cfg(test)]` modules / `*_tests.rs` next to the code; integration tests in each crate's `tests/`.
- Game flow tests run whole packet flows without MySQL (`test_support::test_server()`, `TestClient`); see `object_flow_tests.rs`, `world_flow_tests.rs`.
- Tests needing real data are `#[ignore]`d and gated by env vars: `AAEMU_COMPACT_DB=rust/crates/aaemu-game/app/Data/compact.sqlite3 cargo test -p aaemu-game -- --ignored`; MySQL tests by `AAEMU_TEST_MYSQL`. CI does not run them — run them locally when touching that code.
- Live end-to-end tests against running servers: `aaemu-testclient`, see [`rust/LIVE-TESTING.md`](rust/LIVE-TESTING.md).
- Subsystem test priorities: [`Docs/TestingPlan_en.md`](Docs/TestingPlan_en.md).

---

## Code changes — read first, then match the repo

When asked to modify, fix, or improve code, **do not invent conventions**. Inspect the target area and the sources below, then mirror what is already there.

### Authoritative sources (in order)

1. **Neighboring code** in the same module and subsystem — this is the primary style guide.
2. **[`rust/crates/aaemu-game/CONTRIBUTING-PORT.md`](rust/crates/aaemu-game/CONTRIBUTING-PORT.md)** — module layout, shared-file rules, object model, locking rules, testing conventions.
3. **`rustfmt` + `clippy`** — `cargo fmt --all` and `cargo clippy --workspace --all-targets -- -D warnings` (0 warnings).
4. **[`CONTRIBUTING.md`](CONTRIBUTING.md)** — branch from `develop`, Conventional Commits, tests with changes.
5. **`Docs/wiki/`** — domain language and architecture context:
   - [`Code-Terminology.md`](Docs/wiki/Code-Terminology.md) — game-object hierarchy, in-game terms.
   - [`Components.md`](Docs/wiki/Components.md) — Login/Game roles and data stores.
   - [`Developer-Notes.md`](Docs/wiki/Developer-Notes.md) — startup order and subsystem wiring.
   - [`Documentation-Maintenance.md`](Docs/wiki/Documentation-Maintenance.md) — when and how to update wiki pages.
   - [`Home.md`](Docs/wiki/Home.md) — documentation map.
6. **[`Docs/TestingPlan_en.md`](Docs/TestingPlan_en.md)** — subsystem map and testing priorities.
7. **[`rust/PORT-AUDIT.md`](rust/PORT-AUDIT.md)** — what was ported from the C# code and what is still missing.

### Rust style highlights

- `rustfmt` defaults; edition 2021 and shared settings from the workspace `Cargo.toml`.
- Errors: `anyhow::Result` with `.context(..)` at startup, typed errors (`error.rs`) in library code; no `unwrap()` on network or DB input.
- Logging via `tracing` (`tracing::info!`, `warn!`, `error!`); no `println!` in the servers.
- Async on tokio; never hold a `std::sync::Mutex` guard across `.await`.
- Keep identifiers in wiki terminology; packet structs keep their 1.2 names (`CSFooPacket`, `SCFooPacket`).

### Project patterns to follow

| Area | Location | Pattern |
| --- | --- | --- |
| **Subsystems** | `aaemu-game/src/systems/X/` | Everything of subsystem `X` lives here: `packets.rs`, `data.rs` (`Data::load`), runtime state on `System`, `System::start`, `System::save`. |
| **Shared files** | `server.rs`, `main.rs`, `opcodes.rs`, `systems/mod.rs`, `models/{character,unit}.rs` | Edit additively and only where marked (see `CONTRIBUTING-PORT.md` § 3). |
| **Packets (Game)** | `src/packets/`, `systems/*/packets.rs` | `ClientPacket` / `packet_def!` + `Marshal`; byte-exact tests. |
| **Packets (Login)** | `src/packets/` + `src/handlers.rs` | Packet types separate from handlers. |
| **Game data** | `src/data/`, `systems/*/data.rs` | Loaded once into `GameData`; read-only afterwards. |
| **Models** | `src/models/` | Domain types separate from managers/packets; use wiki terminology. |
| **ID allocation** | `src/managers/id.rs` | `server.ids.object`, `.doodad`, `.housing`, … (every id manager exists already). |
| **Commands** | `systems/services/commands/` | Compiled into the server. |
| **Shared** | `rust/crates/aaemu-commons/` | Network primitives and utilities for Login + Game. |
| **Tests** | next to the code / `tests/` | Reuse `test_support` helpers and existing fixtures. |

### Workflow for modifications and improvements

1. **Scope** — identify the subsystem (packet, system, data, model, config) and read 2–3 representative files plus their tests.
2. **Implement** — smallest change that solves the task; match naming, file placement, logging, and error-handling patterns of the surrounding code.
3. **Wire-up** — new game packet: struct + `register` in the subsystem; new state/startup work: `systems::X::System` / `start`; new login handler: packet + handler.
4. **SQL** — if schema changes, add `SQL/updates/…` **and** update base `SQL/aaemu_*.sql`.
5. **Test** — add or extend tests when changing behavior; reuse existing patterns.
6. **Verify** — `cargo xtask ci` (or fmt + clippy + test) must pass before claiming done.
7. **Document** — update `Docs/wiki/` only when user-facing setup, config, or behavior changes; follow `Documentation-Maintenance.md`. Record deviations from the C# behavior in `PORTING.md`. Do not add unsolicited markdown elsewhere.

**Avoid:** drive-by refactors, new frameworks or crates without need, reformatting unrelated files, renaming domain terms away from wiki vocabulary, and broad style “cleanup” outside the requested change.

---

## Windows agent pitfalls

- Detach long-lived GUIs (launcher, server consoles) from the agent shell Job Object or they die when the command ends.
- Port **1234** often conflicted (e.g. ManyCam): remap Login `InternalNetwork` + Game `LoginNetwork` together or the client shows **Maintenance**.
- A running `aaemu-game.exe` / `aaemu-login.exe` locks its binary; stop it before `cargo build` rebuilds it.

---

## Out of scope unless asked

- Shipping client assets into git
- Silent multi‑GB MEGA re-downloads without inventory + HitL
- Changing default production Docker Compose passwords casually
- Full protocol reverse-engineering writeups without a concrete task
- Editing the legacy C# projects (they are being removed)

---

## Quick verify (before saying “ready to play”)

1. Inventory script: client + compact + launcher **OK**
2. Login listens on **1237**
3. Game listens on **1239** / **1250**
4. Login log shows **Registered GameServer**
5. Launcher points at `.client_files/.../bin32/archeage.exe`, server `127.0.0.1`
