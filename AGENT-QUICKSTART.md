# Agent quickstart: run the stack and the real client

Short handoff for agents (and humans) working on this machine. Details live in
[`rust/LIVE-TESTING.md`](rust/LIVE-TESTING.md); conventions in [`AGENTS.md`](AGENTS.md).
Both clients, the 10.x zone reference binaries, IDA databases and all reverse-engineering notes:
[`REVERSING.md`](REVERSING.md).

## TL;DR

```bash
Scripts/live-stack.sh build    # `live` profile build of all binaries (~12 min cold, ~2.5 min after an edit)
Scripts/live-stack.sh sync     # copy rust/crates/*/app configs + data into the runtime dirs
Scripts/live-stack.sh start    # private MySQL + login + game; returns when the game registered (~1 min)
Scripts/live-stack.sh client   # starts the real 1.2 client, already logged in (ticket handoff)
Scripts/live-stack.sh status   # what is up + last log lines
Scripts/live-stack.sh shot     # screenshot -> D:/aaemu-live/shots/now.png (open it to see the client)
Scripts/live-stack.sh stop     # client, both servers, private MySQL
```

After a schema change or when the databases are in a bad state: `stop`, then `db-reset`, then
`start` (this wipes the throwaway test accounts and characters).

## Where things are (this machine)

| What | Path |
|---|---|
| Repo (Rust only, branch `rust-rewrite`) | `C:\Users\messe\Documents\GitHub\AAEmu-rust-1.2` — pushed to the user's fork `https://github.com/NickMesser/AAEmu` (remote `fork`). Never push to `origin`/`upstream` (AAEmu/AAEmu). |
| Cargo build output | `D:\aaemu-target\main` (set in the untracked `rust/.cargo/config.toml`) |
| Runtime dirs, logs, MySQL data | `D:\aaemu-live\{rs-login,rs-game,logs,mysql,shots}` |
| Secrets (DB password, test account password) | `D:\aaemu-live\cfg\` and the runtime `Config.Local.json` files — never print or commit them |
| 1.2 client (read-only) | `D:\AA1.2` (`bin32\archeage.exe`, `game_pak`) |
| Server game DB | `rust/crates/aaemu-game/app/Data/compact.sqlite3` (git-ignored; source archive: `C:\Users\messe\Documents\MEGA downloads\ArcheAge_Server_Compact_r208088_v1.2.4.13_update_2026-01-23.7z`) |
| 10.x reference client / zone server (read-only) | `D:\ArcheAge Returns 10.0.2.13 - 8yx - r575 - 2026-06-18` |
| Reverse-engineering outputs | `D:\aa-re\` (CryPhysics, zone, client12) |

## Ports (live stack)

| Service | Address |
|---|---|
| Private MySQL | `127.0.0.1:3317` (root without password, loopback only) |
| Login (client) / internal | `127.0.0.1:21237` / `21234` |
| Game / stream / web API | `127.0.0.1:21239` / `21250` / `21280` |

## The real client

- `client` runs `aaemu-launcher`, which hands the login ticket to `archeage.exe` (Trion 1.2
  protocol: RC4 ticket in shared memory, `-handle MAP:EVENT`). Nothing is typed into the game.
  Account `clienttest` is auto-created (`AutoAccount`).
- The client loads ~60–80 s, then shows **Select Server** (`LiveTest`). It goes fullscreen
  1920×1080. Left idle at login it disconnects after 1–2.5 min, so act promptly.
- To drive it: screenshots (`shot`) + clicks/keys via PowerShell (`SetCursorPos`/`mouse_event`,
  `SendKeys`), or run Lua inside it with the client driver
  ([`rust/docs/client/LUA-DRIVER.md`](rust/docs/client/LUA-DRIVER.md):
  `aaemu-clientdriver inject`, then `lua "<expr>"`).
- Confirm each step on the server side: `rs-login.log` (`clienttest connected.`), `rs-game.log`
  (X2EnterWorld, CSListCharacter, CSSelectCharacter, CSMoveUnit, chat).

## Rules that bite

- **C: is nearly full.** Build output, logs and data go on D: (`target-dir` in the untracked
  `rust/.cargo/config.toml`, which also sets `jobs = 2` and the `rust-lld` linker); delete
  per-agent target dirs when done. The live stack runs the `live` profile (opt-level 1,
  incremental) from `D:\aaemu-target\main\live`; `AAEMU_PROFILE=release` switches the script back.
- **The user's MySQL on port 3306 is off-limits.** Only use the private instance on 3317.
- **`D:\AA1.2` and the 10.x folder are read-only.** The 10.x `dedicatedserver.exe` writes its log
  into the user's real `Documents\ArcheAge` — don't run it casually.
- **Don't type passwords into any app UI**; use the launcher's ticket (`--password-file`).
- Long `cargo test` runs: use `--release` for `aaemu-physics`; real-data tests are `#[ignore]`d and
  need `AAEMU_COMPACT_DB=rust/crates/aaemu-game/app/Data/compact.sqlite3` (`-- --ignored`).
- CryPhysics oracle tests need `AAEMU_PHYS_ORACLE` (the real DLL harness, see
  `rust/crates/aaemu-physics/README.md`); without it they replay checked-in goldens.

## Known state (2026-10-11)

- Rust port complete (C# removed in `a2ab80abd`); CryPhysics ported bit-exact and integrated
  (`Configurations/Physics.json`, see `rust/crates/aaemu-game/PORTING.md` → systems::ai collision).
- Verified: real 1.2 client logs in, creates a character, walks, chats, logs out on the Rust
  servers; packet parity with the old C# servers on scripted scenarios.
- Open: navmesh changes left 5/16 instance route tests finding routes (unverified); ship collision
  damage thresholds are stand-ins; Linux/Docker only covered by CI; see `PORTING.md` deviations.
