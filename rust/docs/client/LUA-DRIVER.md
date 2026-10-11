# ArcheAge 1.2 client Lua driver

A way for tools to **operate the real 1.2 client by running Lua inside it** — read game / UI
state and trigger UI actions (login, server select, character select / create, movement, chat) —
instead of screenshot + click automation. Both the client's CryEngine System script state and the
X2 UI Lua state are reachable, with return values marshalled back to an external controller.

This replaces pixel automation with the client's own scripting: the X2 UI is implemented in Lua
(login, lobby, HUD, dialogs are all Lua functions), and gameplay commands are Lua-callable too, so
"press the login button" or "move here" become Lua calls that run on the client's own main thread.

* Client: `D:\AA1.2` (x86; `bin32\archeage.exe`, revision 208022, build 35870). **Read-only** —
  nothing in the install is modified; the driver is a separate DLL loaded into the running process.
* Controller: `rust/crates/aaemu-clientdriver` (CLI). In-client side: `native/aadriver.cpp` +
  `native/aainject.cpp` (32-bit, built with MSVC).
* Status: **working prototype**, verified live against the running Rust login server (see
  "Verification" below).

---

## 1. Options evaluated

The brief asked to try the injection-free routes first (console / remote console / cfg), then a
proxy / injected DLL. Findings:

### (a) CryEngine console — can run Lua, but not usable as an external driver

`CrySystem.dll` has the classic CryEngine console. The relevant code (addresses are file offsets
for the preferred base `0x36500000`; the DLL relocates at run time):

* **`#`-prefixed console input runs Lua.** `CXConsole::ExecuteStringInternal`
  (`sub_36579FA0`, RVA `0x79FA0`) checks `if (*cmd != '#' || con_restricted && fromConsole)` — a
  line that starts with `#` is handed to the current script system's `ExecuteBuffer` (via the
  `lua_handle` CVar's state) instead of being parsed as a console command. So in the console,
  `#System.Log("hi")` runs Lua. The UI also uses `@`-prefixed names for CVar groups
  (`sub_365C78E0`), unrelated.
* **`con_restricted`** (`dword_36636D50`) defaults to **1** and is only relaxed to 0 when the
  client is in dev mode or `sys_spec==2` (`CXConsole` init, RVA `0x97056`). While restricted, `#`
  Lua from the console is rejected unless dev mode is on.
* **`exec <file>`** (`sub_36522AF0`, RVA `0x22AF0`) runs a batch file of **console commands**,
  searched in `game/`, `game/config/`, then `./`. A `#…` line inside such a cfg therefore runs
  Lua at config-load time. The client loads `game/Config/client.cfg`, `system.cfg`,
  `%USERPROFILE%\Documents\ArcheAge\system.cfg`, and `%exe%/devmode.cfg` in dev mode. `+exec` /
  `+<cvar>` on the command line work (confirmed: `+r_width 1280` etc. take effect).
* **Dev mode**: the loader `archeage.exe` reads `%s/devmode.cfg` and, when `-devmode` is passed,
  loads `x2game-dev.dll` instead of `x2game.dll`. **In 1.2 there is no `x2game-dev.dll` in the
  install** (unlike the 10.x client), so passing `-devmode` lowercase makes the loader try to load
  the missing dev DLL. Passing **`-DEVMODE`** (any case the loader's own check doesn't match for
  the dev-DLL swap, while CrySystem's `GetCmdLine` lookup of `"devmode"` is case-insensitive) makes
  CrySystem log `Running in devmode` and sets `con_restricted=0`, **without** requiring the dev
  DLL. This is how the prototype enables unrestricted console Lua.

Why this is not a usable driver on its own: the console has **no return channel** (results only go
to `ArcheAge.log` / the on-screen console), **no on-demand input from outside the process** (you'd
have to drive the console window with keystrokes — i.e. back to UI automation), and a cfg only runs
**once at load**. Good for a fixed boot script, not for an interactive controller.

### (b) Remote console — absent in this build

CrySystem exports the UNIX-console class (`CUNIXConsole`, `g_UnixConsole`) and has
`r_shadersSaveListRemote`, but there is **no CryEngine RemoteConsole TCP server** (no `rcon_*`,
`sv_rcon*`, `log_RemoteConsole*`, or `http_startserver` strings in `crynetwork.dll`,
`crysystem.dll`, `cryaction.dll`, `crygame.dll`, `x2game.dll`). So no network console port to talk
to. `crynetwork.dll`, `cryphysics.dll`, `x2game.dll` are additionally packed (section entropy
7.9), but the console/CVar code lives in the unpacked `crysystem.dll` and was fully decompiled.

### (c) HackShield — not loaded when launched this way

`bin32/hshield/` is AhnLab HackShield (`EhSvc.dll`, `V3*.dll`, `ahnrpt.exe`, `hshield.dat`). The
public AAEmu launcher appends `+acpxmk` (an HShield-related client arg) and the Rust
`aaemu-launcher` mirrors that. **Observed:** when the client is started by `aaemu-launcher`
(launcher-ticket handoff, no official patcher), **HackShield does not initialise** — across every
run, the client's loaded-module list contains **no** `EhSvc.dll` / `V3*.dll` / AhnLab modules and
**no HackShield service or kernel driver is running**; `ArcheAge.log` shows a normal startup with
no anti-cheat init. The game `crygame.dll` has generic `ch_AntiCheat` / `Filter.AntiCheat` CVars
(server-side cheat filters), unrelated to the HShield client shim. The driver works with
HackShield simply **absent**; it does not touch, disable, or circumvent any protection. If a future
client build did load HShield, this approach would be re-evaluated and **not** pursued if it
required defeating anti-cheat — but that situation does not arise here.

### (d) Injected driver DLL exposing a named pipe — the chosen design

Modelled on the repo's `aa_debugger` prior art (10.x `xlcommon.dll` proxy). Rather than a disk
proxy (which would mean writing next to the read-only `archeage.exe`), the driver is a standalone
DLL **loaded into the already-running client** and talking over a named pipe. Nothing in `D:\AA1.2`
is modified. This gives a return channel, on-demand execution, and access to every Lua state.

---

## 2. Chosen design

```
  aaemu-clientdriver (CLI, this repo)                         archeage.exe (1.2, x86)
  ───────────────────────────────────                        ────────────────────────────
  inject ── runs ──► aainject.exe (x86) ── LoadLibraryW ────► aadriver.dll
                                                                 │ waits for CryScriptSystem.dll
                                                                 │ hooks CScriptSystem::Update
                                                                 │ (vtable slot, in memory)
  lua/exec/ping ──► \\.\pipe\aaemu-clientdriver-<pid> ◄────────► named-pipe server
                        (u32 len + bytes frames)                 queue ─► run on main thread
```

* **Injection.** `CreateRemoteThread(LoadLibraryW)`, the same mechanism the `aa_debugger` prior art
  relies on. Windows does **not** support `CreateRemoteThread` across the WOW64 boundary, so a
  64-bit controller cannot inject into the 32-bit client directly. The controller therefore shells
  out to a tiny **32-bit `aainject.exe`** (built with MSVC x86). If the controller itself is ever
  built 32-bit (`i686-pc-windows-msvc`), it injects directly (`win::inject_direct`) and
  `aainject.exe` is not needed.
* **Finding the engine.** On load, `aadriver.dll` waits for `CryScriptSystem.dll` and verifies it
  is the known 1.2 build before touching anything: `TimeDateStamp 0x543CB87C`,
  `SizeOfImage 0x000A2000` (preferred base `0x35500000`). If the DLL is a different build the
  driver logs and stays inert — no blind patching.
* **Main-thread execution.** Lua must run on the thread that owns the Lua state. The driver
  replaces **slot 3** of the `CScriptSystem` vtable (`IScriptSystem::Update`, the per-frame tick)
  **in memory** with a thunk that calls the original and then drains a request queue. Pipe-thread
  requests are queued and run inside that thunk, i.e. on the client main thread between frames.
  This is a single pointer swap in writable vtable memory, restored on unload (verified: the client
  keeps running after `unload`).
* **Lua.** The engine embeds **Lua 5.1.1** (PUC-Rio) inside `CryScriptSystem.dll` with the standard
  libraries present (`string`, `table`, `math`, `os`, `io`, `debug`, `package`, `coroutine`). The
  driver calls the engine's own Lua C API by RVA (see table below): `luaL_loadbuffer` +
  `lua_pcall`, and `lua_tolstring` / `lua_gettop` / `lua_settop` to read the result and keep the
  stack balanced. A small Lua prelude wraps the chunk in `pcall`, serialises return values (strings
  raw, tables to depth 3) and TAB-joins them.
* **Three Lua states.** `gEnv` (the `SSystemGlobalEnvironment*` stored at
  `CryScriptSystem.dll+0x81EF4`) holds three `CScriptSystem` pointers, selected by the engine's
  `lua_handle` CVar: `+0x20` = **System** (`lua_handle 0`: entities, `System.*`, `Script.*`, AI,
  `Particle`, `Physics`, …), `+0x24` = **UI** (`lua_handle 1`: the whole X2 UI — login, lobby, HUD),
  `+0x28` = **third** (`lua_handle 2`). The controller's `--target system|ui|third` picks one.

### Key addresses (CryScriptSystem.dll, 1.2 r208022; RVAs from preferred base `0x35500000`)

| RVA | What | How identified |
|-----|------|----------------|
| `0x81EF4` | `gEnv` (`SSystemGlobalEnvironment*`) | set by `CreateScriptSystem`; `+0x1C`=pConsole, `+0x20/24/28`=script systems |
| `0x703D0` | `CScriptSystem` vftable (instances use this; `0x703C4` is the secondary base) | ctor `sub_3550DB50` stores `off_355703D0` |
| slot 3 → `0xCA70` | `IScriptSystem::Update` (per-frame) | logs `CScriptSystem::Update`, ticks `_time/_frametime/_aitick` |
| slot 6 → `0xBB10` | `IScriptSystem::ExecuteBuffer(buf,len,desc)` | string `[Lua Error] Failed to execute file %s`, calls loadbuffer+pcall |
| slot 58 → `0x6060` | returns `m_pLS` (the `lua_State*`, at `CScriptSystem+0x10`) | `return *(this+4)` |
| `0x48930` | `luaL_loadbuffer` | `sub_35548930` → `luaL_loadbufferx` wrapper used by ExecuteBuffer |
| `0x47920` | `lua_pcall` | stack-slot math `-(8*nargs+8)`, sets errfunc |
| `0x46F90` | `lua_tolstring` | converts non-string via `luaV_tostring`, returns `TValue+16` |
| `0x46AE0` | `lua_gettop` | `(L->top - L->base) >> 3` |
| `0x46B00` | `lua_settop` | `L->top += 8*idx+8` / grows to `base + 8*idx` |

The Lua state layout matches stock Lua 5.1 (8-byte `TValue`): `gettop = (top-base)>>3`, strings are
`TString+16`, confirming the API identification. These are baked into `native/aadriver.cpp` as
`RVA_*` constants and gated behind the build check, so the driver is safe to load into any process
(it self-disables on a mismatch).

---

## 3. Build

Needs MSVC with the **x86** toolset (`vcvars32.bat`) for the 32-bit in-client parts, and the normal
(x86-64) Rust toolchain for the controller. Both are present on the reference machine.

```sh
# in-client driver + injector (writes aadriver.dll and aainject.exe)
set AADRIVER_OUT=D:\aaemu-target\physics\aadriver
rust\crates\aaemu-clientdriver\native\build.bat

# controller
cd rust && cargo build -p aaemu-clientdriver --release
```

The controller finds `aadriver.dll` / `aainject.exe` next to its own exe, in an `aadriver\`
sub-folder, or via `$AADRIVER_DLL` / `$AADRIVER_INJECT`. A fully 32-bit controller
(`cargo build -p aaemu-clientdriver --target i686-pc-windows-msvc`) would not need `aainject.exe`,
but that Rust target is **not installed** on the reference machine (`rustup target list --installed`
shows only `x86_64-pc-windows-msvc` and `wasm32-unknown-unknown`); the `aainject.exe` shell-out
makes the 64-bit controller work regardless.

---

## 4. Usage

```sh
T=D:\aaemu-target\physics\release\aaemu-clientdriver.exe
export AADRIVER_DLL=D:\aaemu-target\physics\aadriver\aadriver.dll   # (next to aainject.exe)

# 1) start the client against the local Rust login server (see LIVE-TESTING.md), e.g.
aaemu-launcher --client D:\AA1.2\bin32\archeage.exe --login 127.0.0.1:21237 \
               --user luadrv --password-file pw.txt --nosplash --arg -DEVMODE --arg +r_fullscreen --arg 0

# 2) once it has reached the script-system init / login screen:
$T list                                   # pid + driver status
$T inject                                 # loads aadriver.dll, then pings
$T lua  -t system 'return _VERSION, type(System), type(Script)'
$T lua  -t ui     'return CheckLoginButtonActivation ~= nil'   # the X2 login UI is here
$T exec -t system 'g_flag = 1'            # ExecuteBuffer, no return value
$T repl -t ui                             # interactive line REPL
$T unload                                 # restore the hook and unload
```

With no `--pid`, the single running client is used. Lua code can be given as arguments or on stdin
(`-`). The response status is `0` ok / `1` Lua error / `2` driver error; `lua`/`exec`/`ping` exit 1
on a Lua error and 2 on a driver error.

Wire format (both directions): `u32 LE length` + bytes. Request = op byte + payload
(`S`/`U`/`T` + Lua, `B` + target + Lua for ExecuteBuffer, `P` ping, `X` unload). Response = status
byte + UTF-8 text. See `src/lib.rs`.

### Driving UI actions

The UI Lua state (`-t ui`) exposes the full X2 UI API as globals — login, server select, character
list / create, HUD, dialogs. Enumerating `_G` on it lists thousands of functions
(`CheckLoginButtonActivation`, `CreateLoginStage*`, `FillCharacterInfo`, `CHARACTER_SELECT_TEXT`,
the `ActionBar_*`/tooltip/skin helpers, …). The System state (`-t system`) exposes `System.*`,
`Script.*`, entity scripting, AI/particle/physics binds for in-world actions (movement, skills,
chat). The intended controller flow is to call the same Lua handlers the UI buttons call, rather
than synthesising clicks; mapping the exact call sequence for each step (login button → server
select → character select/create → enter world → move/chat) is the next task on top of this driver.

---

## 5. Verification (live, this machine)

Client started with `aaemu-launcher` against the running Rust login server (port 21237), driver
injected at the login screen, HackShield absent. Observed:

```
$T ping     → aadriver 1 pid=… css=28100000 gEnv=033EEAF4 system=1CD2196C ui=1CD218E4 third=1CD21A7C hooked=1
$T lua -t system '1+2*3'                                   → 7
$T lua -t system 'return _VERSION, type(System), type(Script)'  → Lua 5.1   table   table
$T lua -t ui     'return _VERSION, type(X2), type(gUIParent)'    → Lua 5.1   nil     nil   (X2 UI uses global funcs, not an X2 table)
$T lua -t system 'return tostring(os.time()), tostring(io.open~=nil)'  → 1.79165e+009   true
$T lua -t system 'return {a=1, b={2,3}, c="hi"}'           → {["a"]=1, ["c"]="hi", ["b"]={[1]=2, [2]=3}}
$T lua -t system 'return 1, "two", true, nil'              → 1   two   true   nil
$T lua -t system 'return nonexistent.field'                → lua error: aadriver:26: attempt to index global 'nonexistent' (a nil value)
$T lua -t system 'this is not lua'                         → lua error: compile: '=' expected near 'is'
$T lua -t ui     '…count LoginStage globals…'              → 17   (X2 login UI present in the UI state)
$T exec -t system 'g_aadriver_marker=12345'; lua 'return g_aadriver_marker'  → 12345
$T unload                                                  → unloading   (client keeps running; hook restored)
```

Log from the in-client side (`aadriver.log`, next to the DLL):
```
aadriver loaded into pid …
hooked CScriptSystem::Update (vtable 281703D0, CryScriptSystem.dll at 28100000)
serving \\.\pipe\aaemu-clientdriver-…
unhooked
unloading
```

### Blocked / caveats

* **The client self-exits ~1–2.5 min after reaching the login screen when left unattended** (it
  finishes the intro cinematic, sits at the login/server-select UI, the idle login socket is
  dropped, and the client quits). This is client/server-session behaviour, **not** a driver issue.
  Inject promptly (the script states are up by "Script System Initialization", a few seconds in),
  or keep the client interactive / in-world. A follow-up should drive the login→enter-world Lua
  sequence so the client goes in-world and stays alive, and/or raise the client/login idle timeout.
* Requests run inside the per-frame tick, so a **minimised or non-rendering** client that stops
  ticking will make Lua requests time out (30 s); the request is dequeued safely on timeout.
* Single named-pipe instance: one controller connection at a time (serialised; fine for a CLI).
* `-DEVMODE` (not lowercase `-devmode`, which makes the 1.2 loader look for the absent
  `x2game-dev.dll`) is used to get `con_restricted=0`; the driver itself does not need the console,
  but dev mode is convenient for also using `#`-Lua in the on-screen console.

---

## 6. Reverse-engineering notes

* Decompiled headless with IDA 9.4 (`idat.exe -A -S<script>`), Hex-Rays x86, to
  `D:\aa-re\client12\` (C: is nearly full). Full decompiles of `CryScriptSystem.dll` and
  `CrySystem.dll` are there (`ida/cryscriptsystem.c`, `ida/crysystem.c`), plus strings / imports /
  exports dumps for the main client DLLs (`*.strings.txt`, `*.pe.txt`).
* The Lua version banner (`Lua 5.1.1 Copyright … PUC-Rio`), the CVar registrations
  (`lua_handle`, `lua_debugger`, `lua_use_binary`, `con_restricted`, …) and the `ExecuteBuffer`
  error strings are all in `CryScriptSystem.dll` / `CrySystem.dll`.
* `archeage.exe` is a thin loader (packed `.text`); its only notable strings are the dev-DLL swap
  (`%s/devmode.cfg`, `x2game.dll`, `x2game-dev.dll`) and the single-instance mutex.
