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
* Status: Lua control, early fast startup, hidden DX10/DX11 rendering, PNG capture and a local
  stdio MCP server are implemented. See "Verification" and section 7 below.

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
  keeps running after `unload`). Detaching retains the DLL until process exit so a previously
  dispatched virtual call cannot enter unmapped code; restart the client before reattaching.
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
* Single named-pipe instance: one controller connection at a time (serialised). MCP opens a
  short-lived connection per tool call. Remote pipe clients are rejected.
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

## 7. Fast startup, hidden rendering and MCP

Build both Rust controllers and the x86 native driver. On the reference machine, from the repo
root in PowerShell:

```powershell
$env:AADRIVER_OUT = 'D:\aaemu-target\main\aadriver'
.\rust\crates\aaemu-clientdriver\native\build.bat
cd rust
cargo build --release -p aaemu-clientdriver -p aaemu-launcher
```

The normal launcher enables fast startup, smooth startup and 64 KB pak read-ahead by
default, with a visible game window. It finds `aadriver.dll` beside the launcher, in
`aadriver/` beside it, or in the Cargo target directory's `aadriver/` folder.
Source builds also check `aaemu-clientdriver/native/out/`, the batch script's default output.
Override with `--driver-dll` or `AADRIVER_DLL`. Published launchers must include the native DLL
and `aainject.exe` together in one of these locations. Use `--no-fast-start`,
`--no-smooth-start` and `--no-fast-pak` for individual opt-outs. Startup audio policy
still requires the current driver when all three optimizations are disabled.

The driver injects immediately after process creation, before engine initialization.
Fast startup enables `login_fast_start` on the Lua owner thread and uses
`X2LoginCharacter:StartLoginPage()` once the logo stage is active. The 1.2 game registers the CVar
late and chooses its initial stage before the first script update, so setting it only on the
command line does not skip that initial sequence. No files or executable bytes are patched.
The client still loads its game data, shaders and login world normally.

Two alternating normal/fast pairs on October 11, 2026 reached server select in 46.250 / 22.906
and 45.484 / 22.172 seconds. Median startup fell from **45.867 to 22.539 seconds (50.9%)** with
the same hidden renderer settings. These are warm-cache results on the reference machine,
measured from the MCP launch request to stage 3, including ticket handoff and polling latency;
they are not cold-disk or enter-world timings. `Scripts/client-smoke.py` reproduces the comparison.

```powershell
D:\aaemu-target\main\release\aaemu-launcher.exe --client D:\AA1.2\bin32\archeage.exe `
  --login 127.0.0.1:21237 --user clienttest `
  --password-file D:\aaemu-live\cfg\client-test-password.txt `
  --hidden
```

`--hidden` requests a windowed DX10 renderer at 1280×720, then hides the window without
minimising it. The client's later configuration can override the requested resolution. The
driver discovers the SDK-defined `IDXGISwapChain::Present` slot with a tiny WARP software
device, then hooks it and copies the next DX10 or DX11
back buffer into a staging texture. It returns PNG bytes, independent of desktop visibility.
An occluded hidden window is allowed to keep producing frames. This is a real rendered client
with a hidden window: it still needs Windows, a graphics device and a desktop session. For
GPU-free protocol tests use `aaemu-testclient` as described in `LIVE-TESTING.md`.

```powershell
$driver = 'D:\aaemu-target\main\release\aaemu-clientdriver.exe'
& $driver screenshot D:\aaemu-live\shots\client.png
& $driver window show
& $driver window hide
```

The pipe adds `C` (capture; status byte plus **binary PNG**, rather than UTF-8) and `Whide` /
`Wshow` (main-thread window operations). `ping` includes capture availability, frame count,
hidden state and whether fast startup completed. Capture has a ten-second frame timeout;
minimising the client or selecting the DX9 renderer can prevent it from succeeding.

### MCP connection

The same executable's `mcp` command provides newline-delimited JSON-RPC over stdio, with
initialization, tool discovery, argument validation and image content. Stdout contains only MCP
messages. The launcher inherits only the two ticket handles so its output pipes close promptly
while the client stays alive. Tools are `client_list`, `client_attach`, `client_status`,
`client_launch`, `client_lua`, `client_screenshot`, `client_window`, `client_pak` and `client_stop`.
Vehicle and gameplay tests also have `client_input`, `client_action`, `client_inventory`, `client_mode_actions`,
`client_unit_state`, `client_mode_input`, `client_loot`, `client_use_item`, `client_chat`, `client_vehicle`, `client_entities` and `client_perf`.
Launch defaults to silent audio and fast hidden rendering with normal-priority CPU calibration and 64 KB pak
read-ahead; pass `fast_start: false`, `smooth_start: false`, `fast_pak: false` or `hidden: false` to compare
profiles. Pass `sound: true` to enable sound and music; normal launcher runs use `--sound`.
The launcher injects the driver into a suspended client and waits for its own-process
audio-session policy before resuming it. Silent sessions stay muted across profile resets;
the driver also maintains the engine sound/music gates on the main thread. `sound: true`
unmutes each new client session once, preserving later manual mute and volume choices.
This policy requires the current driver even with all startup optimizations disabled.
It changes no endpoint volume or other process's sessions. Stop accepts only clients
launched by the current MCP session. Closing MCP does not
explicitly stop clients, and a new session must attach to an existing client.

Gameplay tools require an in-world character. `client_input` posts supported keyboard events
through the r208022 CryInput on the main thread (`gEnv+16`, virtual slot 8); its build guard
checks the module timestamp, image size and slot address. It releases keys after 1–10,000 ms,
including when the MCP session disconnects, and `key: "release"` clears every test key.
The native fixture tests cover key IDs, overlapping presses, deadlines, release and invalid
commands. These inputs work while hidden and do not send keys to another desktop application.

Number-row keys `"1"` through `"0"` also accept an optional `modifiers` array containing
distinct `"ctrl"`, `"shift"`, and `"alt"` values. For example,
`client_input {"pid":1234,"key":"1","modifiers":["ctrl"],"duration_ms":100}` presses a
bounded Ctrl+1 chord through the normal client input listeners. Modifiers precede the key;
key release precedes modifier release. Overlapping chords retain shared modifiers until the
last chord ends, and `release` clears all driver-held keys. A failed primary press releases
only modifiers newly pressed for that request. Existing physical modifier flags are included
in events. Each synchronous dispatch temporarily supplies the event's modifier mask to the
native getter so stock UI listeners see the chord, then restores the exact saved physical
state, including after a failed dispatch or C++ listener exception. This shadow lasts only
for `PostInputEvent`; between events, including during a held chord, native modifier getters
report physical state. Unsupported modifier getter/setter slots reject modified chords before
posting any events; plain inputs retain their existing compatibility path.
An accepted chord confirms input submission. Confirm a skill cast separately with
server packets and resource, cooldown or buff changes; an empty hotkey query alone
does not establish the binding's native signature or prove the action is unbound.

This mapping comes from the target binary, not a modern engine header: r208022
`CryInput.dll` (timestamp `0x543CB7CE`, image size `0x14000`) registers number-row IDs 1–10
at `0x335034A4` onward, Ctrl ID 28 at `0x3350381A`, Shift ID 41 at `0x335039B9`, and
Alt ID 55 at `0x33503B9E`. Their native modifier masks are `0x11`, `0x22`, and `0x44`.
The keyboard event builder `0x3350416C` supplies modifier flags to the 32-byte event at
offset 20; `0x3350439C` writes its value at offset 24. The driver reads physical modifiers
through guarded slot 22 (`0x33501E65`) and composes each synthetic event's mask. The exact
vtable at `0x3350C388` identifies `SetModifiers` as slot 24 (`0x33501E6D`); the stock keyboard
path calls it at `0x335041E8` before dispatch. Native tests also check the global getter inside
dispatch and exact restoration afterward, modifier overlap, release ordering, deadlines,
unsupported modifier slots and failed-post cleanup.
Input submission does not prove that a skill cast succeeded: verify the client's binding,
server cast, mana and buff results. The live pet Run! shortcut remains unverified.

`client_use_item` calls `X2Bag:UseItemByType` for a scroll actually present in inventory, preserving
the client's normal skill cast, placement checks and server ownership checks. `accepted: true`
means the client accepted the request; wait for casting/portal completion and verify
`client_vehicle.has_vehicle` and the expected `prefab_unit` entity. Using an active vehicle's
scroll requests dismissal. `client_vehicle` reports driver/seat state, speed, turning and car
gear/brake telemetry; `action: "discard"` requests dismount, whose completion is asynchronous.
`client_chat` sends normal Say messages, including GM commands subject to server permissions.
`client_action` executes a validated UI hotkey action. Continuous movement uses `client_input`.
`client_mode_actions` reads the stock mode action bar, including fishing rod actions, native
tooltips, availability, cooldowns, active native hotkeys and staged option binding rows.
Each slot also reports `bounds: {x, y, width, height}` in client pixels from the verified
stock widget `GetEffectiveOffset()` and `GetEffectiveExtent()` getters, including UI scaling, for locating a normal `client_pointer`
click after inspecting the current UI.
Empty staged rows do not mean the native action has no active hotkey. It reads at most ten slots and
does not cast or change shortcuts. Check these bindings before submitting a fishing response
through `client_input`; verify the resulting cast and fish reaction on the server.
`client_mode_input` takes a one-based active mode `slot` from 1 to 10 and reads
that button's current `canUse`, bound skill tooltip and `GetHotKey()`. It rejects
missing, unavailable, unbound or unsupported bindings, then presses the observed
key through the same native CryInput path as `client_input`. Letter/number-row
keys and `space` support case-insensitive `ctrl`/`shift`/`alt` chords, including
stock `CTRL-8`. Optional `duration_ms` is bounded to 1–10000 ms (default 100).
It never clicks, rebinds or enables buttons. The result includes `slot`, `hotkey`,
`skill_id`, available `skill_name`, `submitted` and the native input response.
The slot can change between reading its binding and pressing the key; submission
does not prove the server completed the cast. Verify the resulting skill and
fish state using native telemetry and server observations.
`client_unit_state` reads `target: "player"` or `target: "target"` (the default) through
the stock `X2Unit` getters. An absent target returns `present: false` without
fabricated health, ID or distance values. Present units include `name`, native
`health` and `max_health`, and available `unit_id`/`distance` values from
`X2Unit:GetUnitId(unit)` and `X2Unit:UnitDistance(unit)`. Each getter takes exactly
one unit token; IDs retain their native opaque value. Stock Lua treats distance
as a numeric visibility measure but does not establish its geometry or units;
the tool reports the native result without conversion or a zero fallback.
`buffs` and `debuffs` are keyed by their one-based visible indices.
Each entry contains its `index` and available stock `buff_id`/`path` fields. The native
counts are retained as `buff_count`/`debuff_count`, while each collection reads at most
32 entries using `UnitBuffCount`/`UnitDeBuffCount` and `UnitBuff`/`UnitDeBuff`.
Hidden server buffs may be absent from these visible lists. The tool requires an in-world
character and does not select a target or cast a skill.
`client_target` selects a unit using its native opaque `unit_id` and a required
`expected_name`. The ID contains 1–16 hexadecimal digits, without `0x`, and must be
nonzero. The tool first checks `X2Unit:GetUnitInfoById(id).name`, sets the watch target
with `X2Unit:SetWatchTarget(id)`, checks its ID, then calls
`X2Unit:TargetUnit("watchtarget")`. It confirms the resulting target ID and name
before returning the same native telemetry as `client_unit_state`, plus `submitted`
and `target_id`. Leading zeros and letter case are ignored when comparing IDs;
comparison uses strings so large opaque values do not lose precision. This path
was verified on the original 1.2 client with an isolated Cottage target. Engine
entity IDs returned by `client_entities` are a different identifier and must not
be substituted. The tool changes the watch target and current target, and does
not cast or issue commands. Verify its confirmed result before a target-dependent
GM test.
`client_loot` reads the current native loot bag with `count` and at most 32 one-based
`items`, retaining available stock name, count, template, grade, money and AA-point fields.
It also reports effective `take_all_bounds` for the existing Take All widget. Inspect a
screenshot before clicking: the button may be hidden during timed loot notifications.
The tool does not open a corpse or take items; use normal interaction and pointer input.
`client_pointer` moves or clicks in client pixels through the guarded 1.2 mouse handler:
`action: "click"` remains a left-click and `action: "right_click"` sends a right-click.
The mouse-lock mode is rejected before position events because that native path can move the
desktop cursor. Clicks position first and wait at least 50 ms for the native Update before
pressing, then hold for 100 ms and release on the driver's independent tick; repeated
clicks while held are rejected. `client_input` with `key: "release"` also releases the mouse.
Stock Map clicks and native mouse coordinates have been checked with hidden rendering.
Allow the client to render the resulting UI before taking a screenshot; input acceptance
alone does not establish that a ground-targeted cast or server interaction completed.

`client_entities` returns bounded nearby entity transforms and terrain heights in **client-local
coordinates**. The seamless map origin can change; these values must not be passed directly to
GM commands, which use global coordinates. `client_perf` with `action: "profile"` resets and
records up to 2,048 DXGI Present intervals; `status` reads them and `off` stops profiling. FPS,
mean, p95, p99 and maximum describe the rendered test, including its polling workload. Capture
screenshots after stopping profiling to exclude screenshot readback. Profiling defaults off.

For an MCP host that accepts a `mcpServers` configuration, use these machine-local paths:

```json
{
  "mcpServers": {
    "aaemu-client": {
      "command": "D:\\aaemu-target\\main\\release\\aaemu-clientdriver.exe",
      "args": [
        "mcp", "--dll", "D:\\aaemu-target\\main\\aadriver\\aadriver.dll",
        "--client", "D:\\AA1.2\\bin32\\archeage.exe",
        "--login", "127.0.0.1:21237", "--user", "clienttest",
        "--password-file", "D:\\aaemu-live\\cfg\\client-test-password.txt",
        "--artifact-dir", "D:\\aaemu-live\\shots\\mcp"
      ]
    }
  }
}
```

The launcher defaults to the same folder as the driver controller; override with `--launcher`.
Screenshots are saved as uniquely named PNG artifacts and returned as MCP image content.
Lua uses the native client's API and can change gameplay state; validate the current stage
before an action. At server select (stage 3), `GetWorldListInfo()` lists available worlds and
`X2World:EnterWorld(1,0)` connects to the first world. Once stage 4 is reached, the character
count API is valid and the selected character's Start button can be invoked with:

```lua
local b = characterSelectWindow.bottomPanel.startButton
b:OnClick('LeftButton')
```

In-world, the lobby stage resets to 0; confirm `X2Unit:UnitName('player')` is nonempty before
reading player state. Calling character-count APIs before a world
connection, or repeating a stage-specific action after leaving that stage, can fault inside
the native client. The driver reports Lua errors and native exceptions as tool errors.

### Reproduce the startup comparison and live smoke test

From the repo root, with the private login/game stack already running and no client open:

```powershell
python -I Scripts/client-smoke.py `
  --driver-exe D:\aaemu-target\main\release\aaemu-clientdriver.exe `
  --dll D:\aaemu-target\main\aadriver\aadriver.dll `
  --client D:\AA1.2\bin32\archeage.exe `
  --password-file D:\aaemu-live\cfg\client-test-password.txt `
  --login 127.0.0.1:21237 --output-dir D:\aaemu-live\client-bench\ab `
  --compare --hidden --trials 2
```

The script alternates normal and fast launches with the same render/window settings, measures
time to stage 3, captures images through MCP, checks Lua errors and stop ownership, and stops
only its own client. It saves `measurements.json` and screenshots. Omit `--compare` and add
`--enter-world` to also capture character selection and the selected test character in-world.
It does not create characters or reset databases.

## 8. Pak read-ahead and world loading

Pak optimization, enabled by default on `aaemu-launcher`, applies a reversible **in-memory** change to the known 1.2
`CrySystem.dll` (timestamp `0x543CB856`, image size `0x169000`). It reduces the pak reader's
read-ahead floor from 1 MB to 64 KB. The original 1 MB per-thread buffer, file table,
sector alignment, large-file chunking, seek/EOF behavior and asset contents stay intact.
The driver validates the instruction and surrounding call bytes at RVA `0x14CC4` before
changing the immediate, flushes the instruction cache, and restores it on driver detach.
An unsupported build returns an error instead of applying the change. Client files on disk
are never patched.

Binary analysis of `Pack::Read` (RVA `0x14B10`) and its chunk calculator (`0x130D0`) found
that every cache miss requested at least 1 MB, even for tiny XML, material and model reads.
The calculator already caps reads to the buffer capacity and increases them to satisfy a
larger request, so the change reduces excess traffic while retaining that code path.
The pak's data-read path copies stored bytes; this change does not replace a decompressor.
Windows also caches these reads, so warm-cache timings do not measure cold-disk throughput
([Windows file caching](https://learn.microsoft.com/en-us/windows/win32/fileio/file-caching)).

```powershell
D:\aaemu-target\main\release\aaemu-launcher.exe --client D:\AA1.2\bin32\archeage.exe `
  --login 127.0.0.1:21237 --user clienttest `
  --password-file D:\aaemu-live\cfg\client-test-password.txt `
  --hidden

$driver = 'D:\aaemu-target\main\release\aaemu-clientdriver.exe'
& $driver pak optimize      # enable 64 KB in an already attached client
& $driver pak baseline      # restore 1 MB
& $driver pak profile       # start/reset raw game_pak I/O measurements
& $driver pak status        # JSON counters and current read-ahead
& $driver pak trace         # bounded CSV: offset, bytes, microseconds, thread
& $driver pak off           # stop profiling (keeps the selected read-ahead)
& $driver pak verify        # actual engine byte parity, boundary reads, seeks and EOF
```

The MCP equivalent is `client_pak` with `action` and optional `pid`. `client_launch` enables
the optimization by default; set `fast_pak: false` for the baseline. Profiling is opt-in,
records at most 100,000 reads, and reports truncation. Read time is the sum of successful
synchronous raw read calls, including reads from worker threads; it excludes seeks,
asset parsing, GPU work and network waits. It is not the elapsed world-entry time.

`verify` compares three real pak assets under both profiles through the mounted engine
file system, checking bytes, output bounds, partial reads, sector/64 KB/1 MB boundaries,
backward seeks and EOF. It restores the prior profile afterward. This is a diagnostic
action and should be run separately from timed comparisons.

To measure world entry with identical fast-start and hidden-window settings:

```powershell
python -I Scripts/client-smoke.py `
  --driver-exe D:\aaemu-target\main\release\aaemu-clientdriver.exe `
  --dll D:\aaemu-target\main\aadriver\aadriver.dll `
  --client D:\AA1.2\bin32\archeage.exe `
  --password-file D:\aaemu-live\cfg\client-test-password.txt `
  --login 127.0.0.1:21237 --output-dir D:\aaemu-live\client-bench\pak-ab `
  --compare-pak --enter-world --hidden --pak-profile --trials 3
```

The harness times the selected character's Start action to stage 0 with a nonempty player
name, before the fixed HUD screenshot delay. It also records server/character selection
timings, preserves each client log, and saves pak traces and world screenshots. To check
byte parity separately, omit `--compare-pak` and add `--fast-pak --verify-pak`.

Three alternating pairs on October 11, 2026, entering `w_solzreed_3` with the same existing
character and graphics settings, produced these medians:

| Measurement | Original 1 MB | 64 KB read-ahead |
| --- | ---: | ---: |
| Raw pak data read during world entry | 8.189 GB | 0.892 GB |
| Sum of raw read calls | 866.465 ms | 167.595 ms |
| Start action to in-world player | 11.735 s | 11.406 s |

This is **89.1% less pak traffic**, **80.7% less measured read-call time**, and **2.8% faster
world entry** in warm-cache runs. Each matched pair improved world entry (11.625→11.297,
11.891→11.640, 11.735→11.406 seconds). Asset initialization and the other loading stages
still run normally; cold-disk gains were not measured. Saved evidence on the reference
machine is `D:\aaemu-live\client-bench\pak-ab-final\measurements.json`, with per-run CSV,
logs and screenshots alongside it. A separate `--verify-pak` run passed 21 checks across
the three assets, and all six comparison runs entered and rendered the world.

## 9. Smooth startup scheduling

Smooth startup, enabled by default, removes the CPU-clock calibration's brief priority escalation without
skipping its timing calculation. Binary-derived CrySystem RVA `23740` saves the process
and thread priorities, requests `REALTIME_PRIORITY_CLASS` and
`THREAD_PRIORITY_TIME_CRITICAL`, measures QPC/TSC across a 100 ms sleep, then restores the
saved priorities. On the reference machine the process was observed at **High** priority
for roughly 0.1–0.2 seconds during startup. Such escalation can interfere with desktop
input and other processes ([Windows scheduling priorities](https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities)).

The native driver changes only those two immediate values to `NORMAL_PRIORITY_CLASS`
and `THREAD_PRIORITY_NORMAL`. The patch checks the exact DLL build and surrounding
instructions, including relocated IAT addresses, uses aligned atomic halfword stores,
and restores both values on detach. It is applied before the script environment exists.
Game, audio, streaming and rendering thread priorities elsewhere are unaffected. Native
builds run fixture tests for relocation, foreign-byte rejection, rollback and restoration.
The capture hook also uses a WARP software probe to discover DXGI's shared vtable, avoiding
creation of a second hardware GPU device during startup. Actual frames still come from
the game's GPU device.

Use `--no-smooth-start` to select the original calibration in the normal launcher.
MCP `smooth_start: false` selects the original
calibration for comparison. `client_status`/`ping` report `smooth=1` when the patch was
applied. `Scripts/live-stack.sh client` uses the same defaults (override its DLL path
with `AAEMU_DRIVER_DLL`). Direct launcher use needs no optimization flags:

```powershell
D:\aaemu-target\main\release\aaemu-launcher.exe --client D:\AA1.2\bin32\archeage.exe `
  --login 127.0.0.1:21237 --user clienttest `
  --password-file D:\aaemu-live\cfg\client-test-password.txt
```

Two alternating pairs on October 11, 2026 reproduced High priority in both baselines;
both smooth launches stayed at Normal. Median warm startup was 22.390 vs 22.453 seconds;
the change targets desktop scheduling, with no meaningful startup-time difference.
Separate engine logs estimated the same 4.2 GHz clock under both profiles. Hidden
screenshots remained valid with the WARP probe. These checks establish removal of the
priority spike. A fresh smoke run also entered the world, rendered its HUD and passed all
21 pak byte checks; the visible launch helper reached server select with `smooth=1`.
The full `cargo xtask ci` suite and native patch fixture tests passed. The measurements
do not prove that every desktop/audio hiccup has the same cause.
Evidence is in `D:\aaemu-live\client-bench\hiccup\matched\measurements.json` and the
per-run priority samples. Reproduce with:

```powershell
python -I Scripts/client-smoke.py `
  --driver-exe D:\aaemu-target\main\release\aaemu-clientdriver.exe `
  --dll D:\aaemu-target\main\aadriver\aadriver.dll `
  --client D:\AA1.2\bin32\archeage.exe `
  --password-file D:\aaemu-live\cfg\client-test-password.txt `
  --login 127.0.0.1:21237 --output-dir D:\aaemu-live\client-bench\smooth-ab `
  --compare-smooth --fast-pak --hidden --trials 2
```

## 10. Vehicle tests through the real 1.2 client

`Scripts/client-vehicle-smoke.py` uses the MCP tools from §7 to exercise acceleration, steering,
coasting or the car brake, reverse, dismount and scroll dismissal. It saves `vehicle.json` with
sampled transforms, speeds, seat state and frame intervals, plus a rendered `vehicle.png`.
Failures save `failure.png`; test inputs release automatically and profiling is stopped on exit.

Use a disposable test character with an owned vehicle scroll, clear driving space or deep water,
and a client already in the world. The summon option calls the ordinary item skill and waits for
casting and portal completion. Boarding uses CryInput interaction; choose the approach key and
duration for the current camera and placement. If that approach misses a ship's helm, board it
normally and rerun without `--summon-item`, using `--dismiss-item` to test cleanup afterward.
Vehicle descriptions distinguish templates; saved vehicle names can differ from those descriptions.

```powershell
python -I Scripts/client-vehicle-smoke.py `
  --driver-exe D:\aaemu-target\main\release\aaemu-clientdriver.exe `
  --pid CLIENT_PID --kind car --expected-vehicle 'Comet Speedster' `
  --summon-item 26874 --output-dir D:\aaemu-live\vehicle-test\car

# After boarding the helm of an owned Harpoon Clipper:
python -I Scripts/client-vehicle-smoke.py `
  --driver-exe D:\aaemu-target\main\release\aaemu-clientdriver.exe `
  --pid CLIENT_PID --kind boat --expected-vehicle 'Harpoon Clipper' `
  --dismiss-item 15816 --settle-seconds 10 --output-dir D:\aaemu-live\vehicle-test\clipper
```

The harness checks finite transforms, attachment to the driver seat, forward movement, a heading
change, braking/reverse response, and disappearance after dismissal. Obstacles can legitimately
prevent a car from reversing; inspect the failure screenshot and use a clear fixture. Client-local
entity coordinates can shift with seamless map origins and are unsuitable for global GM moves.
Frame results include hidden rendering and MCP polling; they are diagnostics, not an FPS benchmark.
`--settle-seconds` records idle transforms before controls and after releasing reverse, including
root-height ranges for checking draft and settling. It does not assume a particular world water level.

The clipper's server buoyancy now uses the two `buoy*` brushes in the 1.2 normal prefab, with
their CGF geometry, local rotations and scales, instead of the generic fallback cylinder.
Rowboats retain their authored tube parameters because their prefab has no buoy brushes.
The gated `real_clipper_prefab_buoyancy_draft` test loads the actual client pack and compact data,
settles for 60 seconds, and checks driven motion at two headings. In flat water at Z=100, the
authored clipper averaged root Z=99.826 versus 99.607 with the fallback. Root height describes
the prefab pivot; it is not a hull waterline measurement. Live idle captures include waves.

Final real-client runs on October 11, 2026 passed for these representative templates. Each used
the owned scroll's normal item path for summon/dismissal and the ordinary driver interaction.
The clipper was boarded at the observed helm before running the harness with `--dismiss-item`.

| Vehicle | Scroll template | Peak during acceleration | Reverse minimum | Result |
|---|---:|---:|---:|---|
| Comet Speedster | 26874 | 13.80 m/s | -0.74 m/s | Steering, brake, reverse and dismissal passed |
| Rowboat | 15817 | 2.58 m/s | -2.49 m/s | Steering, coasting, reverse and dismissal passed |
| Harpoon Clipper | 15816 | 2.66 m/s | -2.67 m/s | Steering, coasting, reverse, settling and dismissal passed |

These are short control tests, not maximum-speed measurements or coverage of every vehicle.
Car movement remains client-authoritative. Both boats crossed sectors without the former
out-of-bounds driver transforms; normal dismount no longer produced origin-jump speed reports.
The clipper's ten-second idle mean root Z was 99.819 in this fixture (water level 100), and its
rendered hull met the water. Screenshots and JSON are under
`D:\aaemu-live\vehicle-test\{car,rowboat,clipper}-seat-final` on the reference machine.
The full `cargo xtask ci` suite, native input/startup fixtures and real-data draft test passed.

## 11. Multiple clients for multiplayer tests

MCP `client_launch` accepts optional `user` and `password_file` arguments for each launch.
Omitting them uses the session's `--user` and `--password-file` defaults. Passwords are passed
by filename; the MCP schema does not accept plaintext passwords. Use distinct accounts:
the session rejects a second active launch for the same account. The result includes
`structuredContent.pid` and `structuredContent.user`; supply that PID to subsequent tools.
PID-less tools reject ambiguity when more than one client is running. `client_list` reports
session ownership and account names for its own launches. `client_stop` retains the original
process handle and refuses clients launched by another session, including after PID reuse.

Each launcher ticket now has a unique mapping and event name. The client consumes their
inherited handles through `-handle`, so a second launch cannot overwrite the earlier ticket
or mistake its signalled event for authentication completion. Concurrent-ticket tests verify
the two mappings retain distinct bytes and unsignalled events.

The 1.2 loader itself supports a second instance: on its existing `ArcheAge` mutex it asks
“Do you want to start another one?” Direct launcher users can add `--multi-client` to accept
that confirmation automatically; MCP supplies it for its launches. The injected driver
accepts only its own process's dialog, checks the r208022 loader PE timestamp and image size,
the exact title and prompt, and both Yes/No controls. It submits the normal Yes action.
No mutex or client executable bytes are patched, and the client folders remain read-only.

The smoke harness uses disposable `multitest_` accounts and named characters, preserves
pre-existing clients, and stops only the clients owned by its MCP session. `--create-missing`
creates an empty test account's character through the original 1.2 race/customization/ability
form; omit it on repeat runs. Each account needs its matching named test character, in the
same area with room to move. The harness records mutual visibility, movement observed by
the other client, Say chat received in both directions, family title/owner changes, PID
ambiguity and ownership refusals, plus a screenshot for each process. Test characters remain
saved for another run; it does not use or move `clienttest`/Cosmic.

The default interaction uses a family containing only the two disposable characters. On the
first run it invites and joins through the original native family confirmation; later runs
verify the same family, change the other member's title and transfer ownership. It refuses
to modify a family with additional characters. Select `--interaction trade` for an empty
trade request/cancel with eligible characters. The observed stock Trion r208022 policy
requires both partners to reach level 10. The native RequestTrade path checks this before
sending `CSCanStartTrade`; level-1 fixtures therefore cannot test that server flow. The
harness reports this requirement and never changes character levels automatically.

```powershell
python -I Scripts/client-multiplayer-smoke.py `
  --driver-exe D:\aaemu-target\main\release\aaemu-clientdriver.exe `
  --launcher D:\aaemu-target\main\release\aaemu-launcher.exe `
  --dll D:\aaemu-target\main\aadriver\aadriver.dll `
  --client D:\AA1.2\bin32\archeage.exe `
  --password-file D:\aaemu-live\cfg\client-test-password.txt `
  --login 127.0.0.1:21237 --create-missing `
  --output-dir D:\aaemu-live\feature-audit\multiclient
```

Use `--password-file-b` when the second account has a different password. Credentials and
driver pipes are separate per process; the stock clients still share
`Documents\ArcheAge` logs and settings. Shared client logs can overwrite or interleave and
are unsuitable as per-process evidence. Use PID-specific driver telemetry/screenshots and
server logs instead. This harness does not isolate Windows user profiles or benchmark FPS.

## 12. Compressed pack testing

Run `Scripts/build-client-pak.ps1` to build the x86 Rust bridge, native driver and
64-bit CLI tools. The bridge's C ABI exchanges opaque handles and caller-owned byte
buffers; its allocator remains inside the DLL. The supported client DLL is checked
against its r208022 PE timestamp and image size before installing pack hooks.

The launcher passes `--pak-manifest` as the driver's private startup argument. The
driver prepares silent audio first, loads the bridge and the client's stock
`CrySystem.dll` while the main thread is suspended, then redirects that module's
pack-file imports. The stock ANSI mount first calls the legacy CRT `_stat32i64`;
that import and the wide mount's `GetFileAttributesW` report a read-only regular
virtual file with the original 64-bit size. The CRT structure's 48-byte layout is
checked against the shipped x86 runtime. Other files continue through the original
functions.
Virtual handles use independent cursors, 64-bit seeks, ordinary EOF behavior and
read-only writes. Rust positional I/O and decoding run outside the shared cache
lock, allowing independent client loader threads to read concurrently.

MCP `client_launch` accepts an optional absolute `pak_manifest` path. Use a separate
client copy containing `bin32` and `game` but no original `game_pak` to establish that
the compressed path can actually boot, authenticate and load a world. Supply a
disposable test account and character. `client_status` reports `virtual_pak=1`, the
original logical pack size, and virtual open/read counters; save those with the
rendered screenshot and normal server enter-world evidence.

Run `aaemu-pak verify` before live tests. It checks complete blob hashes, all decoded
chunks and the logical stream SHA-256. Native fixtures check cursor, seek, EOF,
write-denial, legacy metadata layout and foreign-file forwarding; Rust tests cover concurrency, malformed
manifests, update corruption, wrong bases, replay and OS writer-lock recovery after
process termination. Windows update tests also check both directions of the
client/update manifest-sharing exclusion. Do not replace a live client's manifest.

The pack crate's Windows tests also run as 32-bit binaries, matching the bridge's
architecture. Update tests bound receipt reads and blob hashing/copies even when
package files change after preverification; failures preserve the installed
manifest and do not publish an invalid blob.

The complete local 1.2 pack test decoded 24,877,137,920 bytes with SHA-256
`c7fa0c854fceb8ffaf6f2024c1b05f61a25f07ba2c1c5c1e0f7abe49daea1f34`, matching the
original file independently. The compressed blob plus manifest occupies
12,859,559,617 bytes (48.3% less). A disposable copy without `game_pak` reached the
world, rendered, moved and passed all 21 client reader checks; engine sound and
music remained disabled. Automatic `game.aacm` discovery passed repeated launches.

Three sequential trials per pack, using the same disposable character, hidden
rendering and fast/smooth/silent defaults, gave these medians on this machine:

| Pack | Server-selection startup | Enter-world loading |
|---|---:|---:|
| Original pack | 21.875 s | 11.125 s |
| Compressed 256 KB chunks | 23.141 s | 13.922 s |
| Compressed with traced chunks stored raw | 23.328 s | 12.157 s |

These are local warm-cache measurements, with no controlled cache flush or
cross-machine comparison. Compression saved storage but increased warm world
loading by 25.1%; do not infer a speed improvement from file size. To experiment
with avoiding decompression of traced chunks, run `aaemu-pak profile BASE_MANIFEST
READ_TRACE_CSV PATCH_DIRECTORY`, then apply that package offline. It preserves the
logical hash, reuses cold chunks, and adds raw storage while retaining old blobs.
Test areas beyond the trace before choosing a distribution profile.
The recorded profile added 1,184,104,448 bytes, for 14,043,664,105 bytes including
the manifest (43.5% below the original). Its median world load improved 12.7%
over full compression but remained 9.3% slower than the original warm pack.
A decoder-context reuse experiment did not improve these measurements reliably
and was removed. Subsequent comparisons should identify the driver build and
record cache conditions.

The real update exclusion check launched an owned compressed client, attempted a
valid profile package, and received Windows sharing error 32; the installed
manifest SHA stayed unchanged. Closing that client permits normal verified update
application. Root-relative SHA filenames and receipt bounds reject arbitrary file
paths. Receipts protect integrity/base identity, not publisher authenticity.
