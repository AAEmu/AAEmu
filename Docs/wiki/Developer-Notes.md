# Developer Notes

- Audience: Contributors
- Last verified against: `rust-rewrite` on October 14, 2026
- Prerequisites: None

## New character skills and hotbar

Character creation uses the selected ability from `CSCreateCharacterPacket`. Starting spells
come from `compact.sqlite3`'s `skills` rows marked visible, auto-learned, requiring learning,
and available at ability level 1. Shared shortcuts come from `default_skills` entries whose
`add_to_slot` flag is set; starting supplies use `character_supplies`.

The selected ability's first starting spell appears on key 1. Its displaced shared action moves
to a free slot, preserving the other utility and supply shortcuts. Sorcery starts with Flamebolt
(10752); Battlerage starts with Triple Slash (18132). Melee Attack, Shoot Arrow and Kick are
shared general skills, even though Kick uses a Battlerage-style icon. Saved hotbars retain the
player's arrangement when logging in.

## Mirage Isle portals

Mirage Isle entrance doodads (template 4894) were reconciled to the original 1.2
`map_data/doodad_map/*.dat` map markers: ten inherited extra placements were removed, two
positions corrected and five missing records restored. The 109 portals now match the markers
one-to-one within 0.03 m. Authored rotations are unchanged; restored records use the default
zero rotation because the markers carry only positions. Verify against the real assets from
`rust/` with the 1.2 `game_pak`:

```sh
AAEMU_CLIENT_PAK=<path-to-1.2-game_pak> cargo test -p aaemu-game --test mirage_portal_spawns -- --ignored
```

## NPC movement and respawn

Combat chase retains its route while the target is stationary and refreshes it when the
target moves. Walking NPCs reach path corners using horizontal distance and stand on the
collision floor, so differences between navigation heights and terrain do not stall them.
Attack range is checked against the target separately from waypoint arrival.

Walking floor queries start from the NPC's current level and accept only a surface within
the 2 m step-up allowance; a collision floor at Z = 0 is valid, while terrain above a cave
roof is not. When nothing reachable is found the current height is kept, so cave and
building NPCs stay in their cavity instead of snapping to the surface above them. Real 1.2
asset checks cover the Bleakbreath Cave Mistfiend points (terrain 91–145 m above the
authored positions). Flying and idle NPCs keep their documented behavior.

Flying NPCs (`CanFly`) fight and return in 3D: an attacked flyer descends toward its attacker
down to attack range, and after it loses the target it climbs back to its spawn altitude
before leaving the return state. Skill min / max range is measured in 3D too, so a flyer
cannot cast at a victim far below it. Return completion and the return timeout teleport
measure the full 3D distance, so a flyer directly above or below its home is not treated as
arrived.

NPC respawn uses the spawner's authored delay and counts living NPCs towards population
limits. Ordinary spawners keep the authored `Count` / `min_population` baseline (default 1)
and grow with nearby players up to `max_population` or a positive `suspend_spawn_count`;
capped member selection uses the authored weights. Corpses do not count, and alternate
spawners sharing the same caps are included, so hard limits hold across clones. Pending
slots keep their individual respawn deadlines, so staggered deaths respawn on their own
schedule. A lootable corpse can remain while its replacement spawns. Unlooted items extend
corpse lifetime by five minutes; taking the final item restores the normal corpse deadline
or, if that deadline already passed, allows two more seconds before removal. The one-second
cleanup tick reads this updated deadline.

Spawn entries are deduplicated per NPC template and position including Z (2 m per
coordinate), so cave and building floors remain distinct. A fallback spawner generated for
an NPC without authored spawner IDs keeps its first authored placement instead of dropping
it. Focused back-end tests cover these corrections, and a real 1.2 client logged into the
Bleakbreath Cave interior (GM teleports, no spawn commands) showed the Mistfiend and Wisp
in screenshots; telemetry reported all six Mistfiend entities at cave heights below the
terrain, plus a Mist Skeleton. This does not establish retail population density or
validate every cave.

Directly deleting an NPC (`manager::delete`, used by skill effects, dungeon actions and spawn
cleanup) releases the exact NPC's shared spawner membership under the spawners' reentrant
serialization lock, so the population gates stop counting the deleted object and no replacement
can be filled while the removal is still in progress. Membership is matched by object identity:
a recycled object id in any instance, or an identical member of another placement, is never
touched, and deleting an already removed NPC is harmless. `despawn_with_respawn` performs the
delete, the reserved respawn slot and its population credit as one atomic step. The authored
corpse lifetime and each pending slot's individual respawn deadline are unchanged.

Ordinary NPC spawners are indexed per world instance and 64 m region and queried only around
present players. The client's 2-region visibility radius is unchanged;
`World.NpcPrewarmRegionMargin` (default `1`, clamped to 0–4) adds that many rings beyond it for
prewarming, so the default prepares the 7×7 regions around each player. Spawners whose authored
3D `TestRadiusPc` / `TestRadiusNpc` reaches further keep their full trigger through a grouped
coarse index (radius-sized cells, nine probed around a player, then the exact sphere), so
prewarming never narrows them. `World.NpcSpawnTickMs` (default 250, `0` falls back to 250,
clamped to 50–60 000 ms) sets the pass interval, and `World.NpcSpawnsPerTick` (default 64, `0`
treated as 1) bounds how many NPCs one pass creates across all instances. A pass fills
client-visible neighbourhoods before prewarm candidates, nearest first; each queue continues
from its cursor next pass and the starting instance rotates, so a small budget cannot starve
later candidates.

The candidate queues are rebuilt only when the instance's player-activity revision, the spawner
membership or the margin changes: the revision is published on player arrival, movement
(including within one region) and departure, so proximity caches follow immediately, and an
unchanged pass reuses the cached queues instead of walking the full spawner table. Membership
changes also clear the count cache anchored to a spawner's first NPC. A player of another
instance at the same coordinates never activates a spawner, and a placement whose member list is
still empty stays queued and is retried once it is initialized, without waiting for movement or
a reload.

Prepared placements are tracked in a warmed registry. When the last player leaves their warm
set, the existing NPCs keep their state while `World.NpcWarmRetentionSeconds` (default 60
seconds; `0` permits reclaiming on the first eligible pass) counts down, and a returning player
restarts the countdown. After it expires, cold NPCs are reclaimed per live NPC under a separate
bounded per-pass budget; corpses and queued respawn deadlines keep their own timers, and the
population reservation credit is unchanged. Ordinary nonhostile quest givers / reporters and
service roles (merchant, banker, skill trainer, auctioneer, priest, blacksmith, teleporter,
ability changer, stabler, trader, repairman, specialty, expedition, look converter) stay
resident after their first preparation while `World.NpcResidentServiceNpcs` (default true) is
enabled. A mixed placement decides per live NPC, so its residents stay while the other members
expire, and runtime-owned or creator-only actors are never residents. Authored spawn schedules
and management despawns still win. Casting, plots and skill controllers, persistent or
`ActivateAiAlways` AI work and actual combat against a live opponent inside the authored return
leash protect an NPC from reclamation; the persistent-work check includes the same finite work
(an active command after its queue drained, spawn settling, return home).

The AI manager's ~100 ms tick visits only the AIs selected for it: NPCs in the 2-region
neighborhood around active players (their region plus two rings), plus AIs with persistent
paths, follow targets, queued commands or finite work still to finish (an active command even
after its queue drained, spawn settling on the way to Idle, returning home, or a combat
behavior with aggro entries still to clear), AIs with calls pending, newly added AIs for a
20-tick initialization grace, and NPCs whose template sets the authored `ActivateAiAlways`
flag. A settled Idle / Roaming / DoNothing / Alert AI without such work sleeps after that
grace. The selection is deduplicated and visited in monotonic `AddAi` order, so a replaced
object id cannot mix a stale handle into it, and distant idle AIs are neither locked nor
visited. The manager's wake / maintenance classification, the tick guard and the cold-NPC
reclamation check share this predicate, so a selected AI is never skipped and a
mid-initialization / mid-return / mid-command NPC is never distance-reclaimed. A maintenance
sweep every 50 ticks drops stale owners and NPCs deleted without the normal hook.
`World.AiWorkerThreads` (default `1`, clamped to 1–4) runs one tick's plan as at most that many
contiguous chunk jobs on the existing Tokio blocking pool: the default keeps the strict global
`AddAi` execution order, while higher values let the chunks run in parallel. Batches are awaited
and serialized under their own supervisor, so a cancelled caller cannot release a batch early;
no separate process or thread pool is used.

Focused back-end tests cover the activation / retention pass and the player-activity revision
(`spawn_tests.rs`, `world/player_activity_tests.rs`); from `rust/`, the
`cargo test -p aaemu-game activation_` filter runs the activation tests.

The NPC residency round-trip has a `testclient` scenario (`rust/crates/aaemu-testclient`). It
needs a fresh disposable character whose configured `quest_id` / `quest_npc_template` (for
example the Nuian starter 330 / 3597) can accept the quest: the bot starts it at the giver,
moves the default 2048 m away for 70 s, returns to the same giver, and checks the re-announced
NPC and the active-quest snapshot after a character re-select. From `rust/`:

```sh
cargo run --release -p aaemu-testclient -- run --config testclient.local.json --scenario npc-return --out npc-return.jsonl
```

`npc_return_distance` and `npc_return_away_secs` come from `testclient.local.json`, or use the
`--npc-return-distance` / `--npc-return-away-secs` flags. The scenario's behavior and wire
format are covered by focused `testclient` tests. A live run on a fresh disposable character
(`Npcbeta`) exited 0: quest 330 was accepted from giver template 3597, the character stayed
2048 m away for 70 s, returned to the same giver object and re-selected with the quest still
active (`SCQuests` status 3) before a clean logout. The natural cave `load` scenario also exited
0 (character `Recentcheck`, world 0 / zone 156 at 22848.135, 9444.387, 592.701): six natural
template-4227 Mistfiends at their authored positions with 2381 HP and 17 nearby NPCs, with no
force-spawned or manually placed NPCs.

Scheduled event NPCs follow their event schedule through the whole ordinary spawn lifecycle.
A fallback spawner generated for an unscheduled clone stays inactive when any candidate
spawner of the same placement has schedule rows, so it cannot spawn the event NPC outside the
event. An in-progress event keeps its NPCs; an ended or not-yet-started event removes them
through the ordinary despawn and respawn cleanup instead of being torn down while active.
Dated event rows do not become recurring: the shipped Vile Gourdhead Halloween window
(October 16-30 2013; NPC 14312, schedules 53/54/55) is expired and is not retargeted into a
yearly event.

## Housing behavior

Housing uses the original 1.2 placement polygons and database category rules. Plot ownership is
checked per world instance, including unfinished houses. Private, family and expedition
permissions also apply to planting and crafting benches. Beginner land marked as nonrenewable
expires instead of accepting weekly tax payments.

Construction consumes the selected inventory design and the complete payment together. Sales
and demolition prepare all return mail before charging or removing property. Furniture recovery
keeps the placed object when permission is denied or the recipient's inventory cannot accept it;
other players also need the owner's recovery toggle enabled.

New renewable houses receive the prepaid tax period plus seven days of grace before removal.
Each later payment covers one configured period. Crops and dropped packs preserve their world
placement when attached to a house and save the corresponding local coordinates for reload.

Housing save changes remain pending until the database transaction commits. Normal-state house
prefab geometry supplies server collision for construction stages and completed floors, walls
and roofs, including authored static animated objects in castle lodestones. Original client
checks still govern model-specific placement slopes and footprints.

Building damage persists in `housings.hp`; legacy NULL values retain authored construction
health. Damage stages use the original prefab geometry. Lodestone CGA/ANM root animation also
updates its collision proxies, including the settled static geometry after destruction.
Server and client animation phases are initialized independently; absolute phase synchronization
has not been established.

Castle ownership belongs to the expedition in `dominions`, separate from ordinary house owners.
Initial claim uses the original Purified Archeum pack and guild-leader entitlement. A registered
attacker may capture a destroyed lodestone through the original five-minute Convert channel;
movement, death, disconnect or changed authority cancels it. Castle forts follow the ruling
guild, while ordinary properties retain their owners. Fort designs are limited to the nine
original independently itemized designs; native slope/mirror variant selection is unverified.

Sieges are explicitly scheduled with `/dominion schedule <zoneGroupId> <attackerExpeditionId>
<startDelaySeconds>`, using the original zone's duration. `/dominion cancel` ends the schedule,
and `/dominion register` remains a GM roster override. Players use `/joinsiege [zoneGroupId]`
and `/leavesiege [zoneGroupId]`; omitting the zone uses their current province. Registration
assigns the side from the scheduled guilds and requires the current guild rank's Siege
Participation Open (`JoinSiege`) permission. All shipped ranks permit joining by default.
Players cannot enroll another character or administer the schedule. Participant buffs expose
the original five-second headquarters teleport skills.
Bind an already spawned original headquarters with `/dominion headquarters <zoneGroupId>
attacker|defender <doodadObjectId>`; offense template 4307 must be at least 250 meters from the
lodestone, and defense uses template 3410. Bindings require restoration after server restart.
Temple revival for registered participants uses a validated current headquarters when present.
The original SiegePeriod doodad predicate is unresolved and fails closed. GM scheduling and
player registration are the chosen server policy; historical auction calendars are not used.
Commander appointments and reinforcement eligibility are not inferred from dated calendars.

Property, treasury, mail, certificates and wallets share a prepared save snapshot and database
transaction. Logout writes the same financial state, and failed departures remain available for
retry before character selection. Shutdown waits for ongoing saves before its final flush.

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

The 1.2 feature audit in `rust/tools/port_audit/features.py` inventories supported inbound
packets and database table usage separately from C# port coverage. Supply a read-only
`compact.sqlite3` with `--compact-db` and save the JSON report outside the repository with
`--output`. The game crate's `PORTING.md` records reference functions, tested fixes and
remaining gaps; packet registration alone does not establish that a feature works.

The world server enforces buff-dependent skill restrictions, reads explicit quest starter
item mappings, evaluates manual timed buff modifiers, and runs group-item quest objectives.
Owned vehicle renames, priest purchases and trade refusals use existing 1.2 packets. The
audit also adds ownership/permission checks to equipment, mail, containers, auctions,
housing and movement, and prevents duplicate battlefield enrollment. These changes require
no separate zone process or database migration.

Combat casts also enforce life, swimming, stun, walking, range and angle restrictions before
changing cast state. Configured obstruction checks use the collision service. Direct channel
skills spend periodic mana, apply their configured effects and clean up on interruption;
buff events follow the affected unit. Cone targeting, guaranteed-hit effects, buff stealing,
aggro changes, percentage recovery and server cooldown resets have regression coverage.
The skills section of `rust/crates/aaemu-game/PORTING.md` distinguishes these tested repairs
from remaining placeholders and missing buff definitions in the original 1.2 data.

## Crime, trials and prison

Reported crime evidence adds crime and infamy points. At 50 crime points a character becomes
wanted; the combat arrest path sends a defeated wanted character to the faction's court
holding cell. The defendant can accept the default sentence or request a trial. Court seats
and judges are linked by the `justice.<court>.*` spawn entries, and court/prison destinations
come from `rust/crates/aaemu-game/app/Configurations/Justice.json`.

Eligible players with jury points receive invitations for their faction's court. Jurors
review the criminal record, hear the defendant's closing statement and choose acquittal or
20%, 50%, 80%, 100% or 120% of the calculated sentence. The most common choice wins; tied
choices follow jury seat order. A verdict timeout without votes uses the 50% choice, while
a trial without any active jurors uses the default sentence. Guilty verdicts send the
defendant to prison; acquittal clears the current crime points and reduces infamy.

Disconnecting during a trial closes the abandoned case and retains the pending default
sentence for the next login (at least one minute, the pending-sentence field's precision).
Prisoner buffs save the remaining sentence, including the final
seconds, and resume it on reconnect; offline time does not serve the sentence. The normal
buff timer ends the prisoner state. Jurors return to their saved location when the case
closes; escaped jurors follow `Justice.AllowJuryEscape`. Only jurors who submit a verdict
receive attendance credit. These repairs reuse
the existing character and active-buff tables and require no database migration.

## Doodad interactions and event content

Placement validates the owned source item against the requested doodad, checks its
position and zone restrictions, and spends labor only after placement succeeds.
Stackable items consume one unit. Single items move to the system container so
recovery retains their item identity and custom data. Coin loot credits once.

Doodad phase operations are serialized per object. Timers retain the original
object and world identity, and time-of-day changes execute the new phase's functions
and replace its old timer. The first Normal phase is used when a template has no
Start phase; this restores the authored waiting phase of Castaway Shipwreck 6906.
Successful interactions honor `act_count`; partial counts persist through the
`2026-10-12_aaemu_game_doodad_interactions.sql` update. On-change saves and deletes
use an ordered writer, which the regular save lifecycle drains. Loading restores
the position and counters before enabling saves, and initializes phases after
spawning. Harvesting and recovery test line of sight above the ground anchor and
exclude the target's own collider; intervening walls still block interaction.

Logic families route true and false pulses within the same instance, zone and
ownership domain. Client-created wires require owned nearby doodads. Immediate
logic gates, family subscribers and pulse transitions run on the server; delayed
logic descriptors 2 and 3 remain unsupported. The placed 1.2 logic templates use
descriptor 1. Prefab flow graphs run on the client from phase and visibility packets.
Doodad gimmick functions create the authored gimmick with its offsets and motion.

`DoodadFuncSpawnMgmt` uses an explicit `SpawnGroupId` on NPC spawn entries, scoped
by its authored zone key. This group is separate from `NpcSpawnerIds`. Tagged
entries stay inactive until enabled by their management doodad. The shipped spawn
files omit memberships for groups 73–76, so those management functions report
missing content. Restore authored memberships before relying on those sequences.

The 1.2 catalog lacks templates 8314, 8324, 9908, 9909, 9910, 9929, 9930 and 11934,
referenced by 39 Howling Abyss/Nachashgar spawn entries. These entries are skipped
with diagnostics; supplying newer client definitions does not establish 1.2
compatibility. Tower defense preflight also requires authored positions for every
NPC and doodad wave target.

The 1.2 client verification covered Hasla's automatic 18:00 start, all thirteen
wave announcements and operator cleanup. Later waves were advanced with operator
commands to exercise each authored stage. Public-farm planting consumed one seed
from a stack, retained the planted crop across server restarts, and harvesting
produced four Potatoes and a Yellow Potato for one labor point. Separate coin
interactions increased the displayed wallet by exactly the announced payout.
The isolated verification stack used accelerated crop growth and god mode for
the low-level test character; these are not production requirements.

## Related

- [Home](Home)
- [Local Development Guide](Local-Development-Guide)
- [Components](Components)
