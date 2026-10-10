# Economy port — rules for helper agents (temporary file, removed before the final merge)

You are porting part of the AAEmu 1.2 C# economy (`AAEmu.Game/`, next to `rust/` in your worktree)
to Rust, byte-exact. Read first: `rust/crates/aaemu-game/CONTRIBUTING-PORT.md`, then this file.

## Your worktree
* Your own git worktree + branch (given in your task). Commit there only (messages end with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`). Never push, never touch other
  worktrees. Never read C# from `C:\Users\messe\Documents\GitHub\AAEmu` (wrong version); use the
  `AAEmu.Game/` in your worktree. Do not modify C#.
* `rust/.cargo/config.toml` points the target dir at `D:/aaemu-target/econ-<name>` (pre-built).
  Keep it. C: is nearly full.
* Real data (read-only): `C:\Users\messe\Documents\GitHub\AAEmu-rust-1.2\AAEmu.Game\Data\compact.sqlite3`
  (set `AAEMU_COMPACT_DB` to it for gated tests).
* Done means: `cargo build -p aaemu-game --all-targets`, `cargo test -p aaemu-game`,
  `cargo clippy -p aaemu-game --all-targets` (0 warnings), `cargo fmt --all` (only your files may
  change; check `git status`) — all from `rust/`. Run long cargo commands with a generous timeout.

## Where code goes
* Everything new lives in `src/systems/economy/<your files>.rs`. In `systems/economy/mod.rs` add only:
  your `pub mod` lines, your fields in `System` (managers) and in `data::Data` (tables, or a
  sub-struct field like `pub mail: mail_data::MailData`), your `xxx::register(reg)` call in
  `register`, your calls in `System::start` / `System::save`. Keep these edits minimal (other
  helpers edit the same lists; the coordinator merges).
* Do NOT edit: `server.rs`, `main.rs`, `data/mod.rs`, `packets/mod.rs`, `managers/id.rs`,
  `opcodes.rs`, other subsystems' directories. `models/container.rs`, `models/item.rs`,
  `models/item_task.rs`, `data/items.rs`, `managers/item.rs`, `systems/economy/{inventory,fx,hooks,
  item_handlers,item_packets}.rs` belong to the coordinator: if you need something there, prefer an
  extra `impl ItemContainer { ... }` / free functions in your own file (inherent impls may live in
  any module of the crate). Only the `data` helper may extend `data/items.rs`, `models/item.rs` and
  `managers/item.rs`. A tiny additive change elsewhere is acceptable if unavoidable — say so in
  your report.
* Character fields: avoid them; keep per-character economy state in your manager keyed by
  character id. If unavoidable, add below `// ---- phase 3 / economy` in `models/character.rs`.

## Foundation API (already ported, use it)
* `models::item_task`: `task_type::*` (ItemTaskType), `ItemTask` (all subclasses, snapshots),
  `ItemFx`/`ItemFxList` (deferred effects: `fx.tasks(owner_char_id, task, vec![...])`,
  `fx.send(owner_char_id, packet)`).
* `models::container::ItemContainer` ops (record effects into `ItemFxList`/`ItemEnv`):
  `add_item` (AddOrMoveExistingItem of a free item), `move_item_from(src, ...)`,
  `relocate_item`, `place`, `remove_item_task` (RemoveItem), `remove_item` (raw remove, no
  effects), `consume_item`, `acquire_default_item(_ex)`, `space_left_for_template/item`,
  `apply_bind_rules_task`, `wipe`, `renumber_slots`, `get_item_by_id(_mut)`, `get_item_by_slot(_mut)`,
  `items_by_template`, `get_unused_slot`, `free_slot_count`, `can_accept`.
  `ItemEnv` = templates + `create()` (ItemManager.Create with a new id) + clock + `fx`.
* `systems::economy::inventory`:
  - `ItemTxn::begin(server)`: takes the economy operation lock, gives `txn.env`; on drop it releases
    the lock and flushes the effects in order (packets to owners by character id, quest hooks,
    equipment broadcasts, ReleaseId, ...). Every item operation must run inside one. Never begin a
    txn while holding a character or container lock; never hold a character lock while locking a
    container... (order: txn → one character data lock at a time / container leaf locks; copy what
    you need and drop guards quickly). `lock_pair(a, b)` locks two containers safely.
  - `Out::Direct` / `txn.out()` (`Out::Queue`): where character packets go. Inside a txn ALWAYS
    queue (`txn.env.fx.send(char_id, packet)` or `txn.out()`), otherwise packet order vs. item tasks
    breaks.
  - `Inv::of(&player)`: the player's containers (`bag()`, `equipment()`, `warehouse()`, `get(slot_type)`,
    `item_by_id`, `holder_of`, `holder_of_any`, `item_at`, `check_items`, `items_count`, ...).
  - `consume_item`, `try_add_new_item`, `try_equip_new_backpack`, `take_off_backpack`,
    `split_or_move_item(_ex)`, `expand_slot`, `coffer_items`, `change_money(out, ...)`,
    `add_money`, `subtract_money`, `change_game_points`, `change_labor`, `send_error_message(_to)`,
    `item_use`, `on_item_manually_destroyed`, `is_auto_equip_trade_pack`, `server_env`.
* `systems::economy::error_message::*` — every `ErrorMessageType` as an i16 const.
* `systems::economy::item_packets` — `SCItemTaskSuccessPacket`, `SCSyncItemLifespanPacket`,
  `SCUnitEquipmentsChangedPacket`, `SCInvenExpandedPacket`, `SCGamePointChangedPacket`,
  `SCCharacterLaborPowerChangedPacket`.
* `systems::economy::item_handlers::sync_client_packet!(Type)` — implements `ClientPacket` for a
  type with inherent `fn read_body(&mut PacketStream) -> Result<Self>` and
  `fn run(self, &GameServer, &mut GameConnection)`; `item_handlers::active(conn)` = ActiveChar.
  Async handlers (DB access) implement `ClientPacket` by hand.
* `systems::economy::hooks` — hook traits other subsystems install
  (`server.systems.economy.hooks.{quests,gear,progression,npcs,summons}`). If you need behaviour
  owned by another subsystem (skills, npcs, quests, social, housing...), add a NEW narrow trait +
  slot in your own file (e.g. `pub struct MailHooks` field inside your manager) with permissive
  defaults, and list it in your report. Do not edit `hooks.rs` (coordinator file) — unless adding a
  method to `NpcEconomyHooks::shop_info` fields is all you need; then say so.
* Tests: see `systems/economy/tests.rs` (`world()`, `give()`, `item_at()`, `task_body()`,
  `run::<Packet>(&mut w, 0|1, body)`), `test_support.rs`. Server without MySQL.
* DB: `server.db` is a `sqlx::MySqlPool`; persistence goes through `System::save(server, tx,
  stage)` in the C# `SaveManager` order (`SaveStage::BeforeItems` = houses+mails, `AfterItems` =
  auction). Loads in `System::start`. Mirror the C# SQL exactly (tables in `AAEmu.Game/` SQL
  files / `SQL/` folder at the repo root).

## Fidelity rules
* Byte-exact S->C packets (a test per packet spelling out the C# `Write`), C->S decoding tests.
* Mirror C# behaviour incl. quirks; when you must deviate (e.g. C# would throw / alias objects),
  document it as a `systems::economy` bullet in your report (the coordinator writes PORTING.md).
* No skeletons: port the logic. If a piece truly depends on an unported subsystem, define the
  narrow hook and implement everything around it.

## Report (your final message)
Ported areas (C# file -> Rust file), stubs/hooks needed (trait + method + who should implement),
shared-file edits, C# bugs mirrored, deviations, real-data validation numbers, test count, and the
commit hash(es) on your branch.
