# AGENTS.md — AAEmu

Guidance for coding agents working in this repository.

## What this repo is

Open-source **ArcheAge** server emulator in **.NET** (`AAEmu.Login`, `AAEmu.Game`, shared `AAEmu.Commons`). Preferred local orchestration is **.NET Aspire** (`AAEmu.Aspire.AppHost`). Branch of record for active work: **`develop`**.

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
| **A – Aspire** | Docker Desktop or Podman available | Container via AppHost | Login/Game as host projects |
| **B – Standalone** | No container runtime | **Host MySQL 8 only** | Host `dotnet` Login then Game |

**No hybrids** for non-Docker: if there is no Docker/Podman, do not introduce containers for MySQL either.

**Assets:** always run the inventory script before downloading. Multi‑GB MEGA/Drive packages are **Human-in-the-Loop**; do not re-fetch when the script reports **OK**.

Wiki mirrors: `Docs/wiki/Installation-&-Setup.md`, `Aspire-Development-Guide.md`, `Dependencies-and-Downloads.md`, `Client.md`.

---

## Runtime architecture

```text
                    ┌─────────────────┐
  Client 1.2 ──────►│  AAEmu.Login    │◄── MySQL aaemu_login (accounts)
  (launcher)        │  :1237 public   │
                    │  :1234 internal │
                    └────────┬────────┘
                             │ GameServer register + enter-world
                    ┌────────▼────────┐
                    │  AAEmu.Game     │◄── MySQL aaemu_game (mutable state)
                    │  :1239 game     │◄── compact.sqlite3 (read-only reference)
                    │  :1250 stream   │◄── game_pak / client files
                    └─────────────────┘
                             ▲
              optional: Aspire AppHost (MySQL container + env injection)
```

| Component | Role |
| --- | --- |
| **Login** | Auth, world list, enter-world handoff. Public client TCP via **ASP.NET Core Kestrel**. Internal TCP for Game registration. |
| **Game** | World simulation: packets, managers, entities, combat, quests, housing, etc. Generic host + `GameService`. |
| **Stream** | Side channel on Game (`:1250`) for UCC/emblems and related transfers. |
| **Aspire AppHost** | Local orchestration only; does **not** replace Login/Game. |
| **SQLite `compact.sqlite3`** | **Read-only** static game data (items, NPCs, skills, quests templates, …). |
| **MySQL** | **Read/write** state: `aaemu_login` (accounts) + `aaemu_game` (characters, items, world). |

Sequence (play): start Login → start Game (registers with Login) → launcher/client auth on Login → select server → connect to Game.

Authoritative component diagram: [`Docs/wiki/Components.md`](Docs/wiki/Components.md).

---

## Solution and project map

| Path | Role |
| --- | --- |
| `AAEmu.slnx` | Solution entry (SDK from `global.json`: **.NET 10**) |
| `Directory.Packages.props` | Central package versions (CPM) — bump deps here |
| `Directory.Build.props` | Shared MSBuild props |
| `AAEmu.Commons/` | Shared network primitives (`PacketStream`, `PacketBase`), MySQL helpers, `Singleton<T>`, AAPak, utilities |
| `AAEmu.Login/` | Login server |
| `AAEmu.Game/` | Game server (largest codebase) |
| `AAEmu.Aspire.AppHost/` | .NET Aspire orchestrator |
| `AAEmu.UnitTests/` | xUnit unit tests (mirror source layout) |
| `AAEmu.IntegrationTests/` | Game-focused integration tests |
| `AAEmu.Login.IntegrationTests/` | Login + Testcontainers MySQL |
| `SQL/` | Base schema + incremental updates |
| `Docs/wiki/` | Human-facing setup and architecture docs |
| `Scripts/` | Build/start helpers (bat/ps1/sh) |
| `Tools/` | Offline utilities (e.g. WorldConverter) |
| `.client_files/` | **Local only** (gitignored): extracted 1.2 client + launcher |
| `.server_files/` | **Local only** (gitignored): Compose/runtime data, optional logs |
| `**/Config.Local.json` | **Local only** (gitignored): machine overrides |

Do not commit client packs, launcher binaries, `compact.sqlite3`, or secrets.

### Game project layout (high signal)

| Path under `AAEmu.Game/` | Role |
| --- | --- |
| `Program.cs` | Host builder, **DI registration** for managers/services |
| `GameService.cs` | Startup/shutdown lifecycle (`IHostedService`) |
| `Core/Managers/` | Runtime managers (`*Manager` / `I*Manager`); subfolders `Id/`, `UnitManagers/`, `World/`, `Stream/` |
| `Core/Network/` | `Game/`, `Login/`, `Stream/` networks + protocol handlers + connections |
| `Core/Packets/` | Wire packets by direction (see [Packet map](#packet-map-and-conventions)) |
| `GameData/` | Static loaders from SQLite (`IGameDataLoader`); `Framework/GameDataManager` |
| `Models/Game/` | Domain models (entities, skills, quests, world, items, …) |
| `Models/Tasks/` | Scheduled/async game tasks |
| `Physics/` | Ship/vehicle physics (Jitter2) |
| `Scripts/Commands/` | In-game GM/admin commands (`ICommand`) |
| `Scripts/SubCommands/` | Nested command implementations |
| `Services/` | WebApi, Discord bot |
| `IO/` | Client file / `game_pak` access |
| `Data/` | Runtime data files (`compact.sqlite3`, worlds JSON, paths) |
| `Configurations/` | Split JSON config fragments |

### Login project layout

| Path under `AAEmu.Login/` | Role |
| --- | --- |
| `Program.cs` | Kestrel + DI; options validation |
| `LoginService.cs` | Hosted service lifecycle |
| `Core/Network/Login/` | Public client TCP (Kestrel connection handler) |
| `Core/Network/Internal/` | Game ↔ Login internal protocol |
| `Core/Packets/{C2L,L2C,G2L,L2G}/` | Packet DTOs + offset constants |
| `Core/PacketHandlers/` | Handlers separate from packet types (DI-registered) |
| `Core/Controllers/` | Login / Game / Request controllers |
| `Core/Authentication/` | Auth flows (password, Korea challenge, OTP/2FA, reconnect) |
| `Core/Services/` | Password, 2FA, etc. |
| `Docs/networking.md` | Login networking deep-dive |

---

## Configuration rules

- Game config load order: `Config.json` → `Configurations/*.json` → **`Config.Local.json` (wins)**.
- Login: `Config.json` → `Config.Local.json` → env vars / command line (Aspire injects env).
- Login listings: **`GameServers` in config**, not MySQL `game_servers` inserts.
- `SecretKey` must match between Login and Game.
- `ClientData.Sources` should include the 1.2 `game_pak` (absolute path under `.client_files/` is fine).
- `compact.sqlite3` → `AAEmu.Game/Data/` (required for game data).
- `Config.Local.json` is copied to output on build when present in the project directory.

Details: [`Docs/wiki/Working-with-the-Config.json-files-and-server-listings.md`](Docs/wiki/Working-with-the-Config.json-files-and-server-listings.md).

---

## Data stores

| Store | Location / schema | Mutability | Used for |
| --- | --- | --- | --- |
| **compact.sqlite3** | `AAEmu.Game/Data/compact.sqlite3` | Read-only at runtime | Templates: items, NPCs, skills, quests, doodads, … via `GameData/*` |
| **MySQL aaemu_login** | `SQL/aaemu_login.sql` + `SQL/updates/*login*` | Read/write | Accounts, 2FA, bans |
| **MySQL aaemu_game** | `SQL/aaemu_game.sql` + `SQL/updates/*game*` | Read/write | Characters, inventories, housing, mails, auction, … |
| **Client files** | `game_pak` / extracted client | Read-only | Models, geodata, assets via `ClientFileManager` |
| **JSON configs** | `Config*.json`, `Configurations/`, `Data/**/*.json` | Config-time | Server params, worlds, spawns-related data |

### SQL change workflow

When code needs a schema change:

1. Add `SQL/updates/YYYY-MM-DD_aaemu_{login|game}_*.sql` (date orders application).
2. **Also** patch the base file `SQL/aaemu_login.sql` or `SQL/aaemu_game.sql`.
3. Servers apply relevant updates once at startup (`MySqlDatabaseUpdater`); applied scripts are recorded in an updates table.

See `SQL/updates/readme.txt`. Prefer `SQL/patches/compact/` only for intentional compact.sqlite3 fixups (not normal gameplay state).

---

## Game startup lifecycle

Entry: `AAEmu.Game/Program.cs` → host → `GameService.StartAsync`.

Approximate stages in `GameService.cs`:

1. **DB migrate** — MySQL updates for `aaemu_game`.
2. **Client files** — `ClientFileManager.Initialize()` (fatal if no sources).
3. **Early managers** — some loads still run before orchestration (e.g. Formula/Item user data paths may be hybrid during migration).
4. **`ManagerOrchestrator.RunLoadAsync()`** — all `ILoadable` managers in **dependency-ordered parallel batches** (topo sort from constructor deps).
5. **GameData post-load** — `GameDataManager.PostLoadGameData()`.
6. **Scripts** — compile or reflect `Scripts/Commands` (prefer reflection when debugging).
7. **`RunInitializeAsync()`** — all `IInitializable` managers, same batching rules.
8. **World + networks** — static instances, then `GameNetwork` / `StreamNetwork` / `LoginNetwork` start.

Shutdown stops networks, AI, world, ticks, and clears client sources.

`ManagerOrchestrator` (`Core/Managers/ManagerOrchestrator.cs`):

- Builds batches from DI singleton types implementing `ILoadable` / `IInitializable`.
- Edges come from constructor parameters; **`Lazy<T>` is ignored** (cycle break pattern).
- Cycles throw; fix by reordering deps or introducing `Lazy<T>`.

---

## Packet map and conventions

### Direction folders (Game)

| Folder | Prefix | Direction | Notes |
| --- | --- | --- | --- |
| `Core/Packets/C2G/` | `CS*` | Client → Game | Handled on game TCP; offsets in `CSOffsets.cs` |
| `Core/Packets/G2C/` | `SC*` | Game → Client | Server responses/events |
| `Core/Packets/C2S/` | `CT*` | Client → Stream | Stream server |
| `Core/Packets/S2C/` | `TC*` / stream | Stream → Client | Stream responses |
| `Core/Packets/G2L/` | `GL*` | Game → Login | Internal |
| `Core/Packets/L2G/` | `LG*` | Login → Game | Internal |
| `Core/Packets/Proxy/` | Proxy | Login/proxy-related | Legacy/proxy protocol helpers |

### Direction folders (Login)

| Folder | Prefix | Direction |
| --- | --- | --- |
| `Core/Packets/C2L/` | `CA*` | Client → Login |
| `Core/Packets/L2C/` | `AC*` | Login → Client |
| `Core/Packets/G2L/` / `L2G/` | `GL*` / `LG*` | Game ↔ Login |

### Patterns

**Game packets** (typical):

- One class per opcode; constructor passes offset + level: `GamePacket(CSOffsets.CSBuyItemsPacket, 1)`.
- Override `Read(PacketStream)` for inbound; outbound packets implement `Write`.
- Inbound: `Read` often contains behavior (legacy style). Prefer following neighbors; `Execute()` exists to separate decode vs behavior when used.
- Register new C2G/G2C types in `GameNetwork` (`RegisterPacket`). Stream/Login side networks have their own `RegisterPacket` lists.
- Access player via `Connection.ActiveChar`; world lookups via `ParentWorld`.

**Login packets**:

- Packet type (DTO) + **separate** `*PacketHandler` under `Core/PacketHandlers/` (cleaner DI style).
- Handlers registered via `ServiceCollectionExtensions` in PacketHandlers/Network.

Do not invent opcodes; match client 1.2 tables already in `*Offsets.cs`.

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
| **Skills** under `Models/Game/Skills` | **Game combat mechanics**, not agent skills |

### Object hierarchy (simplified)

```text
GameObject          Models/Game/World/GameObject.cs
  └─ BaseUnit       factions, buffs
       ├─ Unit      stats, combat, skill controllers
       │    ├─ Character, Npc (+ Portal), Mate, Slave
       │    ├─ House, Shipyard, Gimmick, Transfer
       └─ Doodad (+ DoodadCoffer)
```

Full glossary: [`Docs/wiki/Code-Terminology.md`](Docs/wiki/Code-Terminology.md).

### Managers vs GameData vs Models

| Layer | Responsibility | Example |
| --- | --- | --- |
| **Models** | Entity state and domain behavior | `Character`, `Skill`, `Quest`, `Doodad` |
| **GameData** | Load/cache **static** templates from SQLite | `ItemGameData`, `NpcGameData`, `BuffGameData` |
| **Managers** | Runtime orchestration, spawns, persistence, systems | `ItemManager`, `WorldManager`, `QuestManager` |
| **Packets** | Wire protocol edge | `CSStartSkillPacket` → manager/model |

Static reference data → `GameData` + `compact.sqlite3`.  
Per-character / world mutable state → managers + MySQL.

---

## Where to change X (task routing)

| Task | Start here |
| --- | --- |
| Client packet handling (gameplay action) | `Core/Packets/C2G/CS*.cs` → related `*Manager` / model |
| Server→client notify | `Core/Packets/G2C/SC*.cs` |
| Login auth / world list | `AAEmu.Login/Core/PacketHandlers/`, `Authentication/`, `Controllers/` |
| New manager | Class + `I*` in `Core/Managers/`; implement `ILoadable`/`IInitializable` if needed; **register both concrete and interface in `Program.cs`** |
| Static template data | `GameData/*` + SQLite schema; optional `SQL/patches/compact/` |
| Character behavior | `Models/Game/Char/Character*.cs` + `UnitManagers/CharacterManager` |
| NPC / AI | `Models/Game/NPChar/`, `Models/Game/AI/v2/`, `AIManager` |
| Skills / buffs / effects | `Models/Game/Skills/` (large `Effects/` tree), `SkillManager`, `BuffGameData` |
| Quests | `Models/Game/Quests/`, `QuestManager` |
| Housing / doodads | `Models/Game/Housing/`, `DoodadObj/`, `HousingManager`, `DoodadManager` |
| World / zones / spawns | `Core/Managers/World/`, `Models/Game/World/`, `Data/Worlds/` |
| Ships / vehicle physics | `Physics/`, `Models/Game/Units/Slave.cs`, `SlaveManager` |
| GM commands | `Scripts/Commands/`, `Scripts/SubCommands/`, `CommandManager` |
| Schema migration | `SQL/updates/` + base `SQL/aaemu_*.sql` |
| Web API / Discord | `Services/WebApi/`, `Services/DiscordBotService.cs` |
| Shared serialization / MySQL util | `AAEmu.Commons/` |
| Package version bump | `Directory.Packages.props` |

---

## Build and test

```powershell
dotnet build
dotnet test
```

- SDK: .NET **10** (`global.json`).
- Solution: `AAEmu.slnx`.
- Filter example: `dotnet test --filter "FullyQualifiedName~GameNetworkTests"`.
- Test projects: `AAEmu.UnitTests` (primary), `AAEmu.IntegrationTests`, `AAEmu.Login.IntegrationTests`.
- Unit test bases: `TestBase`, `SqliteTestBase`, `IntegrationTestBase`; mocks under `Utils/Mocks/`.
- Naming: `MethodName_Scenario_ExpectedResult` (see `AAEmu.UnitTests/README.md`).
- Subsystem test priorities: [`Docs/TestingPlan_en.md`](Docs/TestingPlan_en.md).

---

## Code changes — read first, then match the repo

When asked to modify, fix, or improve code, **do not invent conventions**. Inspect the target area and the sources below, then mirror what is already there.

### Authoritative sources (in order)

1. **Neighboring code** in the same folder, namespace, and subsystem — this is the primary style guide.
2. **[`.editorconfig`](.editorconfig)** — formatting, naming, analyzer severities (IDE/CA rules). Run `dotnet build` to surface violations.
3. **[`CONTRIBUTING.md`](CONTRIBUTING.md)** — branch from `develop`, present-tense commits, tests with changes, follow project code style.
4. **`Docs/wiki/`** — domain language and architecture context:
   - [`Code-Terminology.md`](Docs/wiki/Code-Terminology.md) — game-object hierarchy, in-game terms.
   - [`Components.md`](Docs/wiki/Components.md) — Login/Game/Aspire roles and data stores.
   - [`Developer-Notes.md`](Docs/wiki/Developer-Notes.md) — manager DI and parallel loading.
   - [`Documentation-Maintenance.md`](Docs/wiki/Documentation-Maintenance.md) — when and how to update wiki pages.
   - [`Home.md`](Docs/wiki/Home.md) — documentation map.
5. **[`Docs/TestingPlan_en.md`](Docs/TestingPlan_en.md)** — subsystem map and testing priorities.
6. **Login networking** — [`AAEmu.Login/Docs/networking.md`](AAEmu.Login/Docs/networking.md).

### C# style highlights (from `.editorconfig`)

- 4-space indent, CRLF, UTF-8 BOM, file-scoped namespaces matching folder paths.
- `#nullable enable` at file top where the area already uses it.
- `var` preferred; block bodies for methods; expression bodies for properties/accessors.
- Naming: `_camelCase` instance fields, `s_camelCase` static fields, `PascalCase` types/members/locals-functions.
- Avoid `this.` qualification; sort `using` with `System.*` first; NLog `Logger` via `LogManager.GetCurrentClassLogger()`.

### Project patterns to follow

| Area | Location | Pattern |
| --- | --- | --- |
| **Managers** | `AAEmu.Game/Core/Managers/` | `*Manager` + `I*Manager`; many still extend `Singleton<T>` **and** are registered in DI. Newer code: constructor injection, `ILoadable` / `IInitializable`. Orchestrator runs Load/Initialize — **register new managers in `Program.cs`** (concrete + interface). |
| **Packets (Game)** | `Core/Packets/{C2G,G2C,...}/` | One class per packet; prefix = direction; offsets in `*Offsets.cs`; inherit `GamePacket` / stream variants; register in `*Network`. |
| **Packets (Login)** | `Packets/` + `PacketHandlers/` | Split DTO vs handler; handlers in DI. |
| **Game data** | `GameData/` | `IGameDataLoader` (`Load(SqliteConnection)`, `PostLoad`); discovered/orchestrated by `GameDataManager`. |
| **Models** | `Models/Game/` | Domain types separate from managers/packets; use wiki terminology. |
| **Network** | `Core/Network/` | Protocol handlers route to packet classes; connections in `Connections/`. |
| **ID allocation** | `Core/Managers/Id/` | Typed `*IdManager` per object kind. |
| **Commands** | `Scripts/Commands/` | `ICommand` implementations loaded by script reflector/compiler. |
| **Shared** | `AAEmu.Commons/` | Network primitives and utilities for Login + Game. |
| **Tests** | `AAEmu.UnitTests/` | xUnit; mirror source layout; reuse fixtures/mocks. |

Legacy `Singleton<T>.Instance` and static access still exist — **do not mass-migrate** unless the task explicitly calls for it. For new dependencies, follow the constructor-injection style used in recently touched managers. `SingletonContainer.ServiceProvider` bridges some legacy paths.

Circular manager deps: inject `Lazy<T>` so the orchestrator does not treat them as hard edges.

### Workflow for modifications and improvements

1. **Scope** — identify the subsystem (packet, manager, GameData, model, config) and read 2–3 representative files plus any `I*` interface and tests for that type.
2. **Implement** — smallest change that solves the task; match naming, file placement, logging, and error-handling patterns of the surrounding code.
3. **Wire-up** — new manager/service: register in `Program.cs` like peers; new game packet: offsets + `RegisterPacket` in the correct `*Network`; new login handler: packet + handler + DI extension.
4. **SQL** — if schema changes, add `SQL/updates/…` **and** update base `SQL/aaemu_*.sql`.
5. **Test** — add or extend tests in `AAEmu.UnitTests` when changing behavior; reuse existing patterns.
6. **Verify** — `dotnet build` and `dotnet test` must pass before claiming done.
7. **Document** — update `Docs/wiki/` only when user-facing setup, config, or behavior changes; follow `Documentation-Maintenance.md`. Do not add unsolicited markdown elsewhere.

**Avoid:** drive-by refactors, new frameworks, reformatting unrelated files, renaming domain terms away from wiki vocabulary, and broad style “cleanup” outside the requested change.

---

## Windows agent pitfalls

- Detach long-lived GUIs (launcher, server consoles) from the agent shell Job Object or they die when the command ends.
- Port **1234** often conflicted (e.g. ManyCam): remap Login `InternalNetwork` + Game `LoginNetwork` together or the client shows **Maintenance**.
- Aspire dashboard token is printed by AppHost at startup (`/login?t=...`).

---

## Out of scope unless asked

- Shipping client assets into git
- Silent multi‑GB MEGA re-downloads without inventory + HitL
- Changing default production Docker Compose passwords casually
- Full protocol reverse-engineering writeups without a concrete task
- Mass migration off `Singleton<T>` without an explicit request

---

## Quick verify (before saying “ready to play”)

1. Inventory script: client + compact + launcher **OK**
2. Login listens on **1237**
3. Game listens on **1239** / **1250**
4. Standalone: login log shows **Registered GameServer**
5. Launcher points at `.client_files/.../bin32/archeage.exe`, server `127.0.0.1`
