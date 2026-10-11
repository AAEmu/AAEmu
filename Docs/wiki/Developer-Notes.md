# Developer Notes

- Audience: Contributors
- Last verified against: `develop` on October 10, 2026
- Prerequisites: None

## Architecture note: the Rust port

The login server, game server and tools were ported from C# (.NET) to Rust,
and the C# projects have since been removed from the repository (they remain in
the git history). All code lives in the Cargo workspace in `rust/`:

| Crate | Purpose |
| --- | --- |
| `aaemu-commons` | Shared network primitives, MySQL helpers and SQL updater, AAPak, utilities |
| `aaemu-login` | Login server |
| `aaemu-game` | Game server |
| `aaemu-physics` | Port of the client's CryPhysics (collision worlds, ships); configured by `Configurations/Physics.json` |
| `aaemu-launcher` | Starts the 1.2 client with a login ticket (replaces the public AAEmu Launcher) |
| `aaemu-clientdriver` | Lua driver for the real client (injected DLL + CLI), see `rust/docs/client/LUA-DRIVER.md` |
| `aaemu-testclient` | Headless 1.2 client bot for live tests, parity checks and load tests |
| `aaemu-tool-worldconverter`, `aaemu-tool-updatesfortransform` | Offline tools |
| `xtask` | `cargo xtask ci`, `dev` and `publish` |

What this means for contributors:

- Build with `cargo build --release`, check with `cargo xtask ci` (fmt,
  clippy with warnings as errors, tests).
- Game startup order is explicit code in `aaemu-game/src/main.rs` and
  `systems::start_all` instead of the former DI container and parallel manager
  orchestrator (PRs `#1363` / `#1366` in the C# code).
- Game subsystems live in `aaemu-game/src/systems/<name>/` and talk to each
  other through hook traits; see
  [`CONTRIBUTING-PORT.md`](https://github.com/AAEmu/AAEmu/blob/develop/rust/crates/aaemu-game/CONTRIBUTING-PORT.md).
- [`rust/PORT-AUDIT.md`](https://github.com/AAEmu/AAEmu/blob/develop/rust/PORT-AUDIT.md)
  tracks what has been ported from the C# code.
- [`rust/LIVE-TESTING.md`](https://github.com/AAEmu/AAEmu/blob/develop/rust/LIVE-TESTING.md)
  explains running the servers against real data with `aaemu-testclient` and
  starting the real client with `aaemu-launcher`.
- [`rust/docs/client/LUA-DRIVER.md`](https://github.com/AAEmu/AAEmu/blob/develop/rust/docs/client/LUA-DRIVER.md)
  describes driving the real client through Lua (`aaemu-clientdriver`).
- [`rust/docs/physics/`](https://github.com/AAEmu/AAEmu/tree/develop/rust/docs/physics)
  documents the CryPhysics port; the game crate's `PORTING.md` section
  "systems::ai collision" explains how the zone physics uses it.
- [`Docs/networking.md`](https://github.com/AAEmu/AAEmu/blob/develop/Docs/networking.md)
  describes the network layer (listeners, framing, connection tasks).

Operational impact for setup docs:

- No .NET SDK; install Rust via rustup.
- Runtime files moved to `rust/crates/aaemu-login/app/` and
  `rust/crates/aaemu-game/app/`.
- Logging is configured with `RUST_LOG` instead of `NLog.config`.

## Related

- [Home](Home)
- [Local Development Guide](Local-Development-Guide)
- [Components](Components)
