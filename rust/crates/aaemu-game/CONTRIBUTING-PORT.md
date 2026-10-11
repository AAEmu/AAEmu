# Porting guide for `aaemu-game` (phase 3+)

This crate is a faithful port of `AAEmu.Game` for the **1.2 client** (`client_12_r208022`).
The reference is the C# code base, which was removed from the repository in `a2ab80abd`; read it
from the git history (`git show a2ab80abd^:AAEmu.Game/<path>`, or a worktree of `a2ab80abd^`). Wire
formats must match the 1.2 C# byte for byte. `PORTING.md` holds the status table, deviations and the C#
bugs we mirror. This file explains *where code goes* and *how to use the shared object model*
so several subsystems can be ported in parallel without merge conflicts.

Build output goes to `D:` through `rust/.cargo/config.toml` (local, git-ignored). Never point
`target-dir` back to `C:`.

## 1. Module layout

```
src/
  main.rs            startup (ManagerOrchestrator order) — calls systems::start_all
  server.rs          GameServer: config, db, game_data, registry, phase-1/2 managers,
                     `systems` (phase-3 managers + hooks), view_ctx()
  config.rs          AppConfiguration
  data/              game data (compact.sqlite3, Data/*.json, client files) — GameData
  models/            C# `Models/Game/*` data types
    unit.rs          UnitBase (GameObject/BaseUnit/Unit members), traits, bonuses, cooldowns,
                     skill/buff modifier caches, combat buffs, UnitAttribute & friends
    transform.rs     Transform, PositionAndRotation, Quaternion (parent composition)
    character.rs     Character (players)      npc.rs       Npc (+ Portal)
    doodad.rs        Doodad                   gimmick.rs   Gimmick
    slave.rs mate.rs house.rs transfer.rs shipyard.rs
    buff.rs          Buffs bookkeeping + BuffEvent + NetBuff format
  world/
    object.rs        Entity<D>, WorldObject, ObjectCore, *Ref handles, locking rules
    mod.rs           WorldManager, WorldInstance (object tables, regions, visibility), Player
    visibility.rs    per-kind AddVisibleObject / RemoveVisibleObject
    unit_state.rs    SCUnitStatePacket for every BaseUnitType (+ SCCharacterState)
    session.rs       player lifecycle (select / spawn / move / chat / leave)
  managers/          phase-1/2 managers (account, character, chat, id, item, save, ...)
  packets/           phase-1/2 packets: c2g*.rs (C->S), g2c.rs / world.rs (S->C),
                     world_objects.rs (object visibility S->C), proxy.rs (level 2)
  systems/           phase-3 subsystems — one directory each:
    mod.rs           the ONLY central lists (data, managers, packets, start, save, hooks)
    skills/ npcs/ economy/ social/ quests/
  stream/ login/ net/  stream server, login link, network core
  test_support.rs    flow-test helpers (server without MySQL, clients, characters)
```

### Where a new subsystem puts its code

Everything of subsystem `X` lives under `src/systems/X/`:

| What | Where | Wiring (already done) |
|---|---|---|
| C->S packets (`Core/Packets/C2G`) | `systems/X/packets.rs` (or several files) | `systems::X::register(reg)` |
| S->C packets (`Core/Packets/G2C`) | `systems/X/packets.rs` | – |
| Game-data tables (`*Manager.Load` from compact.sqlite3 / json / client files) | `systems/X/data.rs`, fields of `systems::X::Data` | `Data::load(&DataLoadCtx)` → `server.game_data.systems.X` |
| Managers (runtime state) | `systems/X/<manager>.rs`, fields of `systems::X::System` | `server.systems.X` |
| Startup (DB loads, timers, hook installation) | `System::start(&Arc<GameServer>)` | `systems::start_all` (from `main.rs`) |
| Persistence in `SaveManager.DoSave` | `System::save(server, tx, stage)` | `systems::save_stage` (houses/mails: `BeforeItems`, auction/crimes: `AfterItems`, slaves: `AfterCharacters`) |
| Behaviour other modules call | implement `systems::UnitHooks` (or add a slot to `systems::Hooks`) | install in `System::start` |
| New model types | `models/<type>.rs` if shared by several subsystems, else inside `systems/X/` | – |

Use the existing types where the C# has them: `opcodes::{cs, sc}` (complete 1.2 tables — never
edit them), `managers::id::IdManagers` (every C# id manager is already there:
`server.ids.object`, `.doodad`, `.housing`, `.mail`, `.team`, `.tl`, ...),
`managers::name::NameManager`, `managers::item::ItemManager`, `data::skills::SkillData`
(phase-2 subset: the skills subsystem extends it).

### Registering packets

```rust
// systems/X/packets.rs
pub struct CSFooPacket { pub a: u32 }
impl ClientPacket for CSFooPacket {
    const NAME: &'static str = "CSFooPacket";
    fn read(s: &mut PacketStream) -> Result<Self> { Ok(Self { a: s.read_u32()? }) }
    fn handle<'a>(self, server: &'a GameServer, conn: &'a mut GameConnection)
        -> BoxFuture<'a, Result<()>> { Box::pin(async move { /* Execute() */ Ok(()) }) }
}

// systems/X/mod.rs
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<packets::CSFooPacket>(LEVEL_PLAIN, cs::CSFooPacket);
}
```

S->C packets: a struct + `packet_def!(Name, level = LEVEL_PLAIN, opcode = sc::Name)` +
`impl Marshal` (see `packets/world_objects.rs`). A packet whose C# `Write` reads a live
object is built while holding that object's lock and sent as `raw_body_packet!` (body bytes).
`packets::tests::no_duplicate_registrations` fails if two modules register the same opcode.

## 2. The object model

### Handles and data

| C# | Rust handle (`world::object`) | data (`models`) |
|---|---|---|
| `Character` | `PlayerRef = Arc<Player>` (`p.data()` → `Character`, `p.send(..)`) | `Character` |
| `Npc` / `Portal` | `NpcRef = Arc<Entity<Npc>>` | `Npc` (`portal: Option<PortalInfo>`) |
| `Doodad` | `DoodadRef` | `Doodad` |
| `Slave`, `Mate`, `House`, `Transfer`, `Shipyard`, `Gimmick` | `SlaveRef`, `MateRef`, `HouseRef`, `TransferRef`, `ShipyardRef`, `GimmickRef` | same names |
| `GameObject` (anything in a region) | `WorldObject` (enum of the above) | – |

* A handle keeps the immutable `obj_id` and an `ObjectCore` (region, `IsVisible`, transform
  children) that only the world code mutates. `Entity::new(obj_id, data)` creates one; the obj
  id comes from `server.ids.object.get_next_id()`.
* The C# members are the data behind one `Mutex`: `npc.data()` / `player.data()`.
* Every unit kind embeds `pub unit: UnitBase` (the `GameObject`/`BaseUnit`/`Unit` members: name,
  template id, level, hp/mp/max, transform, faction, expedition, model, scale, buffs, modifier
  caches, combat buffs, bonuses, cooldowns, combat flags, current target, owner, tl id, ...) and
  implements `UnitData` via `impl_unit_data!`. `Doodad` is a `BaseUnit` only (`BaseUnitData`).
  C# virtual properties that read a template (`Scale`, `RaceGender`, `MaxHp`, `AnimActionId`)
  are stored fields filled by the code that creates / updates the object.
* Generic access without matching on the kind:
  `obj.with_unit(|u: &mut dyn UnitData| u.unit_mut().hp -= 10)` (None for doodads),
  `obj.with_base_unit(|b| b.buffs_mut()...)`, `obj.transform()`, `obj.with_transform(..)`,
  `obj.kind()`, `obj.is_unit()`, `obj.as_npc()`, ...; `WorldObject::from(&npc_ref)` converts
  any handle (all world APIs take `impl Into<WorldObject>`).

### World API (`world::WorldInstance`, `world::WorldManager`)

```rust
let world = server.world.world(instance_id)?;          // WorldManager.GetWorld
let ctx = server.view_ctx();                            // game data + names + now
let npc = Entity::new(server.ids.object.get_next_id()?, Npc::new(template_id));
npc.data().unit.transform.set_position(x, y, z, 0.0, 0.0, yaw);
let zone_changes = world.spawn(&npc, &ctx);             // GameObject.Spawn (AddObject + Show)
session::handle_zone_changes(&server, zone_changes);    // dispatch OnZoneChange afterwards
npc.data().unit.transform.set_xyz(nx, ny, nz);          // move ...
world.add_visible_object(&npc, &ctx);                   // ... then update regions / visibility
world.delete(&npc);                                     // GameObject.Delete (Hide + RemoveObject)
```

* Lookups: `world.object(id)`, `unit(id)`, `npc(id)`, `doodad(id)`, `slave(id)`, `mate`,
  `house`, `transfer`, `shipyard`, `gimmick`, `character_by_obj_id`, `all_npcs()`,
  `all_doodads()`, `all_of_kind(kind)`, `npc_by_template_id`, `doodad_by_db_id`,
  `doodads_by_house_db_id`; across instances `server.world.object(id)`,
  `server.world.world_of(&obj)`.
* Neighbourhood (`GetAround<T>`): `world.objects_around(&obj)`, `world.players_around(&obj)`,
  `world.around(&obj, |o| o.as_npc().cloned())`; per region `objects_in_region((x, y))`,
  per cell `objects_in_cell(x, y)`.
* Broadcast (`GameObject.BroadcastPacket`): `world::broadcast(&world, &obj, &packet, self)`.
* Show / hide (`IsVisible`): `world.show(&obj, &ctx)`, `world.hide(&obj)`.
* Parent / children (`Transform.Parent`, `Children`): `world.attach_child(&parent, &child)`
  makes the child's transform local to the parent; `world.world_pos_rot(&obj)` composes the
  world position (`Transform.World`), `world.region_of(&obj)` (`GetRegion`, uses the root),
  `world.detach_child(&child)`. Children follow the parent's visibility and region moves.
* Visibility packets are complete for every kind (`world/visibility.rs`): `SCUnitStatePacket`
  (+ points / slave / mate / house / shipyard state, attached units) for units,
  `SCDoodadCreated/sCreated/Removed/sRemoved` for doodads, `SCGimmicks*` for gimmicks,
  `SCUnitsRemoved`, `SCTargetChanged` when a viewer's target disappears. Region batches,
  removal filters and C# quirks (double removal in the own sector, children shown twice on
  first spawn, house doodad removal offset bug) are mirrored and documented in `PORTING.md`.
  NPC AI activation follows the C#: `npc.ai_should_tick` is set / cleared when players arrive /
  leave (only when `has_ai`).

### Locking rules (deadlock freedom)

1. Order: instance grid → one object's data → `ObjectCore` leaf locks.
2. Hold **at most one** object data lock at a time. Copy what you need, drop the guard, then
   lock the next object.
3. Never call into the world (`spawn`, `show`, `add_visible_object`, `around`, `broadcast`,
   lookups) while holding an object's data lock. `let hp = npc.data().unit.hp;` then act.
4. Packets that need the object's state are built while holding its lock and sent after
   (sending never blocks).

### Buffs, effects, stats (skills subsystem foundation)

* `UnitBase::buffs: Buffs` keeps the C# bookkeeping (indices, stack rules, `GetAllBuffs`,
  `CheckBuff(s)`, `RemoveBuff` / `RemoveEffect(index | template+skill)` / `RemoveAllEffects`,
  persistence rules, `NetBuff` encoding). Each effect that is started / refreshed / removed queues
  a `BuffEvent`.
* After mutating buffs and releasing the lock, call
  `systems::dispatch_buff_events(&server, &obj)`: it drains the events into
  `server.systems.hooks.unit` (`systems::UnitHooks::on_buff_event`). The skills subsystem
  installs its `UnitHooks` in `System::start` and implements `BuffTemplate.Start/Dispel`,
  modifier caches (`unit.skill_modifiers` / `buff_modifiers.add_modifiers(rows)`),
  `combat_buffs`, bonuses, buff packets, timers. `UnitHooks::on_unit_zone_change` is the
  `Unit.OnZoneChange` (zone group buffs) hook.
* Stats: `unit.bonuses` (`AddBonus`/`RemoveBonus`/dynamic bonuses) and
  `unit.calculate_with_bonuses(value, UnitAttribute::X, now_ms)` port `CalculateWithBonuses`;
  `UnitCooldowns` (`add_cooldown` / `check_cooldown` / `remove_cooldown`) ports the skill
  cooldown store. Character `MaxHp/MaxMp` come from `models::stats` (cached into
  `unit.max_hp/max_mp`); NPC formulas belong to the NPC subsystem.

## 3. Rules for shared files

Shared files are edited **additively** and **only where marked**:

* `models/character.rs` / `models/unit.rs`: new fields go *below your subsystem's marker*
  (`// ---- phase 3 / skills: ...`) in the struct and the matching marker in the constructor.
  Never reorder or re-wrap existing fields.
* `systems/mod.rs`: the central lists are complete for the five subsystems; edit only to add a
  new hook slot (`Hooks`) or a new subsystem.
* `server.rs`, `main.rs`, `data/mod.rs`, `packets/mod.rs`, `managers/id.rs`, `opcodes.rs`:
  do not edit (everything is reachable through `systems::*`). If you really need to, keep the
  change to a single added line and explain it in the commit.
* `world/*`, `models/{transform,unit,buff,npc,doodad,...}.rs`: fix bugs and add methods
  freely, but do not change existing signatures; add new behaviour in your subsystem and call
  the public API. Kind-specific behaviour belongs to the subsystem that owns the kind
  (`npcs`: Npc / Doodad / Gimmick; skills: buffs and combat on `UnitBase`).
* `PORTING.md`: update only your subsystem's section ("Phase 3 subsystems") and append to the
  deviation / mirrored-bug lists under a `systems::X` bullet prefix.
* Run `cargo fmt --all` before committing; formatting-only changes to other people's files are
  not allowed (they cause conflicts).

## 4. Testing conventions

* **Byte-exact packet tests** for every S->C packet you add: spell out the expected bytes from
  the C# `Write` with `PacketStream` primitives (or literal byte arrays) and compare with
  `net::packet::encode(&packet)` / the body. Examples: `packets/world_objects.rs`,
  `world/unit_state_tests.rs`, `models/house.rs`. C->S packets: decode a literal body.
* **Real compact DB tests** are `#[ignore]`d and gated by the env var `AAEMU_COMPACT_DB`
  (path to `compact.sqlite3`; the repo copy is `rust/crates/aaemu-game/app/Data/compact.sqlite3`):
  `AAEMU_COMPACT_DB=... cargo test -p aaemu-game real_compact_db -- --ignored`. The test in
  `data/mod.rs` already runs every `systems::*::Data::load`; add assertions for your tables
  there or in a gated test of your own (`if std::env::var("AAEMU_COMPACT_DB").is_err() { return; }`).
* **Flow tests** run whole packet flows without MySQL: `test_support::test_server()`,
  `TestClient` (inspect the frames a connection receives: `drain()` / `drain_ops()`),
  `test_character`, `spawn_player`; `#[tokio::test]` (the lazy pool needs a runtime). See
  `object_flow_tests.rs` (player sees NPC / doodad appear and disappear) and
  `world_flow_tests.rs`.
* `cargo build --workspace --all-targets`, `cargo test --workspace`,
  `cargo clippy --workspace --all-targets` (**0 warnings**) and `cargo fmt --all` must pass
  before every commit. Commit messages end with the `Co-Authored-By` trailer.
