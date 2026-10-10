# Developer Notes

- Audience: Contributors
- Last verified against: `develop` on October 10, 2026
- Prerequisites: None

## Architecture note: the Rust port

The login server, game server and tools were ported from C# (.NET) to Rust.
The code lives in the Cargo workspace in `rust/`:

| Crate | Purpose |
| --- | --- |
| `aaemu-commons` | Shared network primitives, MySQL helpers and SQL updater, AAPak, utilities |
| `aaemu-login` | Login server |
| `aaemu-game` | Game server |
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
  explains running the servers against real data with `aaemu-testclient`.

Operational impact for setup docs:

- No .NET SDK; install Rust via rustup.
- Runtime files moved to `rust/crates/aaemu-login/app/` and
  `rust/crates/aaemu-game/app/`.
- Logging is configured with `RUST_LOG` instead of `NLog.config`.

## Related

- [Home](Home)
- [Local Development Guide](Local-Development-Guide)
- [Components](Components)
