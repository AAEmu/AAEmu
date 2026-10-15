# Clients, reference binaries and reverse-engineering knowledge

Where everything is for porting **zone-server features into the 1.2 Rust stack**, using the
**10.x client/zone binaries as the reference**. Start the stack itself with
[`AGENT-QUICKSTART.md`](AGENT-QUICKSTART.md).

Paths are for this machine. Both client folders are **read-only**: never modify, add or delete
files in them (a client writing its own logs at runtime is fine). Put all RE output under `D:\aa-re\`.

## The two clients

| | 1.2 client (the one we serve) | 10.x client + zone server (reference only) |
|---|---|---|
| Path | `D:\AA1.2` | `D:\ArcheAge Returns 10.0.2.13 - 8yx - r575 - 2026-06-18` |
| Version / arch | 1.2 Trion `r208022`, **x86** (`bin32\`) | 10.0.2.13 `r575`, **x64** (`Bin64\`) |
| Game logic DLL | `bin32\x2game.dll` | `Bin64\x2game.dll`, dev build `x2game-dev.dll` |
| Physics | `bin32\cryphysics.dll` (**VMProtect-packed**; unpacked copy: `D:\aa-re\cryphysics\work_x86\`) | `Bin64\cryphysics.dll` (client), `cryphysics_dedicate.dll` (zone) |
| Zone server | — | `dedicatedserver.exe` + `Bin64\*_dedicate.dll` (`x2game-dev_dedicate`, `cry3dengine_`, `cryaction_`, `cryaisystem_`, `cryentitysystem_`, `crynetwork_`, `cryphysics_dedicate`) |
| Content | `game_pak` (worlds, models, navmesh) | `game_pak` (68 GB) |
| Start it | `Scripts/live-stack.sh client` (our `aaemu-launcher`, ticket login) | `launch_aaemu.bat` (`-devmode`, targets an older local AAEmu setup); see `FAST_START.md` |
| Notes | HackShield does not start under our launcher. Lua can be run inside it with `aaemu-clientdriver`. | `Bin64\xlcommon.dll` is the `aa_debugger` proxy (`aa_debugger\`; the original is needed for standalone loading). `x2game-dev.dll` carries a 2-byte fast-start patch (`patch_fast_start.py status/restore`, backup `.faststart.bak`). |

**Same engine, different game layer.** The CryPhysics in both clients is the same generation (48 of 55
matched vtables have identical slot counts; same contact dispatch tables and default tuning values), so 10.x physics behaviour
transfers to 1.2. The **network protocol differs**: different opcodes (see
`Bin64\decompiled\OPCODES_10x.md`), 10.x adds the encrypted level-5 (DD05) channel, and some packet
layouts changed. Data formats also drift (e.g. navmesh `areas` v22 in 10.x vs v21 in 1.2). Always
confirm a 10.x behaviour against what the 1.2 client sends/expects before porting it.

### The 10.x zone server (`dedicatedserver.exe`)

- `Bin64\dedicatedserver.exe -devmode +zone <name>` (default `w_gweonid_forest_1`); connects to a
  world server on `world_serveraddr:world_serverport` (default 1240).
- It **cannot fully boot**: it needs 41 server-only DB tables the client data lacks, and
  `game\db\game.sqlite3` is encrypted. It gets as far as engine/physics/AI init.
- It writes `ArcheAge.log` into the **real `Documents\ArcheAge`** (and moves the client log into
  `LogBackups`) no matter where it runs from — don't run it casually.
- Its physics DLL *can* be driven standalone: that is the oracle below.

## Tools

- **IDA Pro 9.4**: `C:\Program Files\IDA Professional 9.4` (`ida.exe` GUI, `idat.exe` headless,
  Hex-Rays x86 + x64).
  - Headless batch: `idat.exe -A "-S<script.py>" "-L<log>" <dll-or-.i64>`. Pass output paths through
    environment variables (IDA's `-S` mangles paths with spaces). Copy a DLL into a work dir under
    `D:\aa-re\` first so the `.i64` lands there. Reopening a saved `.i64` skips analysis.
  - **ida-pro-mcp** (MCP tools `mcp__ida-pro-mcp__*`): serves the one database open in an IDA GUI
    instance (Ctrl-Alt-M, port 13337). Good for interactive look-ups; use headless idat for bulk work.
- **Ghidra 12.1.4 PUBLIC**: installed at `D:\Tools\ghidra_12.1.4_PUBLIC` (read-only is fine;
  archive kept at `D:\Tools\_dl\ghidra.zip`), running on Temurin JDK 25
  (`C:\Program Files\Eclipse Adoptium\jdk-25.0.1.8-hotspot`; Ghidra 12.1.4 needs JDK 21+ and
  rejects the also-installed JDK 17). This is the tool for the **1.2 client DLLs** — IDA
  databases below are for physics/zone; the unpacked 1.2 `x2game.dll` lives in Ghidra.
  - **Always launch headless through the wrapper**:
    `ghidra-work\ghidra-headless.cmd <projectDir> <projectName> [analyzeHeadless args...]`
    from the repo root. The wrapper pins `JAVA_HOME` and redirects `APPDATA` to
    `ghidra-work\appdata` — without that, Ghidra dies with "Failed to create directory"
    and a bogus "JDK 21+ could not be found" (the real `%APPDATA%` is unwritable here).
  - Working area: **`ghidra-work\`** at the repo root (gitignored, machine-local). Its
    `README.md` records the traps and run results; `scripts\` holds the post-scripts
    (`CountFunctions.java`, `DumpBlocks.java`, `DumpCoverage.java`); `logs\` the run logs.
    Project directories must have **no dot-prefixed path element** (Ghidra rejects them),
    hence `ghidra-work\proj-*`, never `.ghidra-work\…`.
  - Example (reopen a saved project, no re-analysis):
    `cmd /c "ghidra-work\ghidra-headless.cmd <repo>\ghidra-work\proj-aa12u aa12u -process x2game_unpacked.dll -noanalysis -scriptPath <repo>\ghidra-work\scripts -postScript CountFunctions.java"`.
    Do not use `Start-Process -RedirectStandardOutput` (sandbox denies it); invoke with `cmd /c`.
- **MSVC** (for the oracle and native client-driver parts): found via the `build.bat` files.
- **Python** for the extraction tools: `c:\python312\python.exe` (run with `-I`).

Saved IDA databases (open these instead of re-analysing):

| Binary | `.i64` |
|---|---|
| 10.x `cryphysics_dedicate.dll` | `D:\aa-re\cryphysics\work_x64\` and `D:\aa-re\zone\bin\` |
| 1.2 `cryphysics.dll` (unpacked) | `D:\aa-re\cryphysics\work_x86\cryphysics_unpacked.dll.i64` |
| 10.x zone DLLs (`cry3dengine_`, `cryaction_`, `cryaisystem_`, `cryentitysystem_dedicate`, `crysystem`) | `D:\aa-re\zone\bin\` |
| 1.2 `crysystem.dll`, `cryscriptsystem.dll` | `D:\aa-re\client12\ida\` |

Saved Ghidra projects, all under `ghidra-work\` in the repo (open these instead of re-analysing):

| Project | Binary | State |
|---|---|---|
| `proj-aa12u` (`aa12u`) | `D:\aa-re\client12\x2game_unpacked.dll` | **The 1.2 client logic oracle**: full analysis, 65 203 functions (18 756 named). `.data` is absent from the runtime dump, so globals/RTTI descriptors resolve by reference only |
| `proj-aa12` (`aa12`) | `D:\AA1.2\bin32\x2game.dll` as shipped | Packed: only the 135-function decrypt stub analyses. Useful as the before/after proof, not for logic |
| `proj-smoke` | `notepad.exe` | Headless smoke test only |

Unpacking a packed 1.2 DLL (`x2game.dll`, `crynetwork.dll`, `archeage.exe`; in-place section
encryption, no VMProtect/Themida marks, imports intact, preferred base honoured): dump the
decrypted sections from the live client with
`D:\aa-re\regrade\db-auth\runtime\dump_runtime_modules.py` (update the PID; client via
`Scripts/live-stack.sh client`), then splice them into a copy of the DLL with
`D:\aa-re\client12\rebuild_unpacked.py`. Existing dumps: `x2game.{hdr,text,rdata}.bin`,
`xlcommon.*`, `archeage.*` in that same `runtime\` folder. Never do any of this inside the
client folders — they stay read-only; outputs go to `D:\aa-re\`.

## Knowledge base

### In the repo

| Doc | What |
|---|---|
| `rust/docs/physics/ZONE-PHYSICS-USAGE.md` | **Start here for zone features**: every physics use in the 10.x zone (with x2game addresses), the ZW*/WZ* results the world server receives, world geometry loading, prioritized feature list |
| `rust/docs/physics/CRYPHYSICS-MAP.md` + `cryphysics/{world,entities,geometry,dynamics}.md` | CryPhysics architecture, 1.2 vs 10.x comparison, function addresses in both DLLs, open-question answers |
| `rust/crates/aaemu-physics/README.md` | The bit-exact port, fidelity policy, module map, the **oracle** (`physoracle.exe`, scenarios, goldens, `AAEMU_PHYS_ORACLE`) |
| `rust/crates/aaemu-game/PORTING.md` | Per-subsystem status, deviations, C# and engine bugs mirrored; "systems::ai collision" = how physics is wired into the game server |
| `rust/crates/aaemu-game/CONTRIBUTING-PORT.md` | Game-crate module layout, object model, locking, test conventions |
| `rust/docs/client/LUA-DRIVER.md` | Running Lua inside the 1.2 client (addresses, design, usage) |
| `rust/LIVE-TESTING.md` | Private MySQL, live servers, headless test client, C#-vs-Rust parity method |
| `rust/PORT-AUDIT.md` (+ `rust/tools/port_audit/`) | Every former C# file mapped to its Rust counterpart |
| `rust/CLIENT-DB-GAP-AUDIT.md` | Server vs. 1.2 client + databases gap audit (packets, unused tables, hardcoded values, quests, Ghidra unpack evidence); supersedes `rust/CLIENT-FEATURE-GAPS.md` |
| `FEATURE-GAP-TASKS.md` | The prioritized task list (P0–P3) implementing those gaps, with per-task oracles |

### On disk (`D:\aa-re\`, plus the 10.x folder)

| Path | Contents |
|---|---|
| `<10.x>\Bin64\decompiled\` | Full decompiles: **`x2game-dev_dedicate.c`** (zone game logic, ~89k functions — the main oracle for zone behaviour), `crynetwork.c`, `xlcommon.c`, `xlpack.c`; `.funcs.csv`/`.strings.txt`/`.imports.txt`/`.exports.txt` indexes; `OPCODES_10x.md`; `x2game.cvar*.txt`; `NEXT_SESSION.md` (older notes on the 10.x client handshake) |
| `D:\aa-re\zone\out\` | Decompiles and call inventories of the other `_dedicate` DLLs (physics-call sites, vtable offsets) |
| `D:\aa-re\zone\agent_proto\zone_protocol_physics.md`, `wz_opcodes_fixed.txt` | World↔zone protocol: every WZ*/ZW* packet with handler addresses and constants |
| `D:\aa-re\zone\agent_geom\` | `sub_cgf.md` (CGF chunks + physics-blob format), `sub_navmesh.md` (`.bai` formats, AI loader, Rust gaps), `sub_x2game.md` (zone startup, doodads, ocean, query sites); `tools\` (`pakx.py` pak extractor, `cgf.py`, `physblob.py`, `baiparse.py`, …); `data\` (per-cell `.dat`, model tables) |
| `D:\aa-re\cryphysics\` | `x64_10x\` and `x86_12\` (decompile, RTTI, vtables, funcs, strings), `compare\func_match.csv` (1,122 matched functions 1.2↔10.x), `unpack_x86\` (how the 1.2 DLL was unpacked), `harness\` (first oracle prototype) |
| `D:\aa-re\client12\` | 1.2 client PE info + strings for every main DLL (`*.pe.txt`, `*.strings.txt`), IDA work for the Lua driver (`ida\`), driver scratch, **`x2game_unpacked.dll`** (reconstructed unpacked client DLL — the binary behind Ghidra project `aa12u`) + `rebuild_unpacked.py` |
| `D:\aa-re\regrade\db-auth\runtime\` | Runtime memory dumps from the live 1.2 client (`dump_runtime_modules.py` + `runtime_evidence.json`): decrypted `x2game`/`xlcommon`/`archeage` section `.bin`s — the source material of the unpack above |
| `D:\aaemu-target\phys-bvtree\cgf_corpus\` | 1,500 real game models with physics blobs (used by the physics tests via `AAEMU_PHYS_CGF_DIR`) |

## Porting a zone feature into the 1.2 stack

1. **Find the behaviour**: `ZONE-PHYSICS-USAGE.md` → the zone-protocol notes → search
   `x2game-dev_dedicate.strings.txt` / `.funcs.csv` for the feature's strings or RTTI names, then
   read the function in `x2game-dev_dedicate.c` (open the `.i64` in IDA for xrefs).
2. **Check the 1.2 side**: does the 1.2 client send/expect the same packets and fields? Read the
   handler in the unpacked 1.2 `x2game.dll` first (Ghidra project `ghidra-work/proj-aa12u` — see
   Tools above; its strings include the client's own `Load*Descs` SQL and feature API names).
   1.2 opcode tables are in `rust/crates/aaemu-game/src/opcodes.rs`; packed-DLL strings in
   `D:\aa-re\client12\` are the fallback. 10.x-only mechanics should be skipped or adapted,
   not ported blindly. For client-data features, [`FEATURE-GAP-TASKS.md`](FEATURE-GAP-TASKS.md)
   is the prioritized work list and `rust/CLIENT-DB-GAP-AUDIT.md` the evidence behind it.
3. **Implement** in the owning `aaemu-game` subsystem (see `CONTRIBUTING-PORT.md`), using the
   physics query service / `aaemu-physics` World API rather than new geometry code. Cite the 10.x
   address in a comment, as the existing code does.
4. **Validate**:
   - engine-level behaviour → an `aaemu-physics` oracle scenario (bit-exact against the real DLL);
   - game behaviour → unit/flow tests, then the real 1.2 client
     (`Scripts/live-stack.sh start` + `client`, drive it with screenshots or the Lua driver, and
     confirm each step in `D:\aaemu-live\logs\rs-game.log`).
5. **Document** deviations and mirrored original bugs in `PORTING.md` (fidelity policy: match the
   original, including its bugs, unless the 1.2 client disagrees).

## Rules

- Never use the published CRYENGINE source code (license incompatible with this GPL project) —
  derive everything from the binaries.
- Reference folders are read-only; RE outputs go to `D:\aa-re\`; C: is nearly full.
- The 10.x `game_pak` is 68 GB: never copy it. If a tool needs it next to an exe, don't hard-link
  it either — a write through the link would modify the original.
