# Client Downloads

- Audience: Contributors, players, and testers
- Last verified against: working tree on October 11, 2026
- Prerequisites: Internet access and enough disk space for client files

Our current `develop` branch requires client version 1.2 (revision `208022`).
Download links are listed below.

Besides connecting to the server, the client data is also needed for server
configuration.

## For AAEmu version 1.2

### Client v1.2 (`r208022`)

1. [MEGA](https://mega.nz/folder/GnwjQCrZ#WNWzX_lDvkzCqoTtt7I42Q) -
   `ArcheAge 1.2.4.13 (r208022) for AAEmu.7z`
1. [MEGA][client-download-mega-patched] -
   `Trion_1.2_(r208022)_BROC_FULL_PATCHED_CLIENT.zip`
1. [Google][client-download-google] - `aaemu client.zip` (multiple files)

### Launcher (`aaemu-launcher`)

The 1.2 client does not ask for a user name or password itself: a launcher
hands it a login ticket. AAEmu ships its own launcher in the Cargo workspace
(`rust/crates/aaemu-launcher`), so nothing else has to be downloaded. It runs
on Windows (where the client runs).

Build the native startup driver as described below before the first launch.

```sh
cd rust
cargo build --release -p aaemu-launcher
cargo run --release -p aaemu-launcher -- --client "C:\ArcheAge\bin32\archeage.exe" --login 127.0.0.1:1237 --user myname
```

- `--client`: the client executable `bin32\archeage.exe` inside the extracted
  client folder (or set the `AAEMU_CLIENT` environment variable). The client
  folder is only read; nothing in it has to be changed.
- `--login`: the login server's public address and port (default
  `127.0.0.1:1237`).
- `--user`: the account name. With `AutoAccount` (on by default) the login
  server creates the account on the first login.
- Password: the launcher asks for it on the console, or use
  `--password PW`, `--password-file FILE` (first line of the file) or the
  `AAEMU_PASSWORD` environment variable.
- `--lang` selects the client locale (default `en_us`); `--nosplash`,
  `--arg X` (extra client argument), `--wait` (stay until the client exits)
  and `--dry-run` (print the command line only) are optional; `--help` lists
  everything.

Client launches are silent by default. Add `--sound` to enable sound and music;
`--no-sound` restores silence. The launcher injects the driver before resuming the
client. Silent mode mutes only that client's audio sessions and maintains the engine
sound/music controls across profile resets. Sound mode unmutes each new client
session once and preserves later manual mute choices and volume preferences.
MCP `client_launch` uses the same default; pass `sound: true` to enable audio.

The built binary is `rust/target/release/aaemu-launcher.exe` (or in your Cargo
target directory); a published build has it in `Tools/`. The ticket protocol is
described in
[`rust/LIVE-TESTING.md`](https://github.com/AAEmu/AAEmu/blob/develop/rust/LIVE-TESTING.md#6-the-real-12-client-aaemu-launcher).

The public AAEmu Launcher (Trion 1.2 login type) also works as an alternative,
but it is not needed.

### Faster startup and automated client tests

Normal launcher runs enable fast startup, smooth startup and optimized pak loading
by default. They use the `aaemu-clientdriver` DLL, which skips timed login logo stages
through the client's own Lua API. Build its x86 native parts with MSVC using
`rust/crates/aaemu-clientdriver/native/build.bat`, then build `aaemu-clientdriver`
and `aaemu-launcher` with Cargo. The launcher finds `aadriver.dll` beside its executable,
in an adjacent `aadriver` folder, or in the Cargo target directory's `aadriver` folder.
Source builds also check the native build script's default `native/out` folder.
Use `--driver-dll PATH` or `AADRIVER_DLL` to override that location.

Pak read-ahead is reduced from 1 MB to 64 KB through a guarded in-memory
change on the supported 1.2 build. This avoids excess reads for small assets during loading.
The installation and `game_pak` remain read-only.
Smooth startup keeps the startup CPU-clock calibration at normal scheduling
priority. It preserves the calculation while removing the engine's brief request for
real-time process and thread priorities. The screenshot hook uses a software probe to
avoid initializing an extra GPU device. These defaults also apply through
`Scripts/live-stack.sh client` and MCP launch.

Use `--no-fast-start`, `--no-smooth-start` or `--no-fast-pak` to disable individual
optimizations for comparison. The startup audio policy still requires the DLL when
all three optimizations are disabled. A missing or outdated driver causes launch
to fail before the client resumes.
The normal launcher keeps the game window visible.

Add `--hidden` to render with a hidden window. The driver can capture the client's
DX10/DX11 back buffer as a PNG while hidden, and expose launch, Lua, status,
screenshots and window controls through `aaemu-clientdriver mcp` over stdio.
This mode still needs Windows, a graphics device and a desktop session. The
`aaemu-testclient` protocol bot supports tests without rendering.

The real-client MCP also provides timed keyboard input, normal inventory item casts,
vehicle/driver state, nearby entity telemetry and frame timing. Inputs go through the
client's engine and release automatically; hidden tests send no desktop keystrokes.
`Scripts/client-vehicle-smoke.py` exercises acceleration, steering, braking/reverse and
optional summon/dismiss cycles, saving JSON telemetry and a rendered screenshot.
Use a test character in open terrain or deep water. Client entity coordinates are
relative to the current seamless map origin, while GM commands use global coordinates.

Real 1.2 tests cover the Comet Speedster, Rowboat and Harpoon Clipper through their normal
scroll summons, driver controls and dismissal. The server fixes include physics tick timing,
clipper prefab buoyancy, ship passenger transforms and a dismissal deadlock. Reproduction
steps and the limits of those tests are in the driver's
[vehicle testing section](https://github.com/AAEmu/AAEmu/blob/develop/rust/docs/client/LUA-DRIVER.md#10-vehicle-tests-through-the-real-12-client).

Build commands, MCP connection settings and the repeatable startup comparison
are in [`rust/docs/client/LUA-DRIVER.md`](https://github.com/AAEmu/AAEmu/blob/develop/rust/docs/client/LUA-DRIVER.md#7-fast-startup-hidden-rendering-and-mcp).

### Extra client archives

- [MEGA client directory (multiple versions)](https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg)

## Related

- [Home](Home)
- [Installation & Setup](Installation-&-Setup)
- [Local Development Guide](Local-Development-Guide)

[client-download-google]: https://drive.google.com/drive/folders/1_pIBVHIm1YFal-nteGaVuXjTv3Yrsv4Q
[client-download-mega-patched]: https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg/file/qyAVQY4I
