# Client feature gaps — 1.2 client (`r208022`) vs. the Rust game server

An audit of what the **ArcheAge 1.2 client supports** that the **Rust game server does not
implement**. Written against the target client in `D:\AA1.2` and the servers in
`rust/crates/aaemu-{game,login}`. Method, reproduction commands and caveats are at the end.

## Summary

The server's **packet plumbing is complete**; its **feature coverage is not**.

| Measure | Result |
|---|---|
| C→S: distinct 1.2 wire opcodes with a registered handler | **244 / 244** — complete, enforced by `packets::tests::every_supported_1_2_inbound_opcode_has_a_handler` |
| S→C: opcodes declared in the 1.2 `SCOffsets` table | 517 |
| S→C: opcodes with a Rust packet type | 402 |
| S→C: opcodes with **no Rust packet type at all** | **115** (19 are `SCUnk*` placeholders) |
| S→C: packet types that exist but are **never constructed** (dead code) | **73** |

Both S→C figures are produced by two independent tools that agree exactly on the 115-name
set: `sc_coverage.py` (macro/regex scan of the 1.2 opcode table) and `sc_usage.py`
(occurrence analysis that ignores the opcode table as a use site and skips macro bodies and
test modules).

So the gap is not "missing opcodes" — it is **features whose client half exists and whose
server half was never written**. Findings are ordered by player impact.

## Findings

### 1. Achievements — client panel exists, server has no implementation

`X2Achievement` · 8 UI scripts (`achievement/*.alb`) · data shipped: `achievements` **1 743
rows**, `achievement_objectives` **3 558 rows**, `pre_completed_achievements` **94 rows**.

The server loads all three tables (`systems/social/achievement.rs`,
`AchievementGameData::load`) and has byte-exact packet types for the whole family — but the
code is explicit that nothing drives them:

> `systems/social/packets/misc.rs:252`
> `// ---- Achievements (no achievement manager in the 1.2 C#: none of these is sent) ----`

All five packets are in the verified **defined-but-never-sent** set:
`SCAchievementsPacket` (0x1fc), `SCAchievementChangedPacket` (0x1fd),
`SCAchievementCompletedPacket` (0x1fe), `SCAchievementResetedPacket` (0x1ff),
`SCAchievementItemSentPacket` (0x200).

The loader also documents its own limit — *"The 1.2 C# has no achievement manager (the types
are only the DB / packet containers)"* — and its real-data test asserts
`d.achievements.values().all(|a| !a.is_active)`, i.e. **every row in the shipped data is
inactive** because the `is_active`/`complete_or` decision is unimplemented.

Player-visible effect: the achievement window opens and stays empty; nothing is ever
progressed, completed or rewarded.

**Reference implementation available.** The 10.x zone server implements the feature —
`ClientAchievementManager`, `AchievementCategoryDesc`, `AchievementObjectiveDesc`,
`PreCompletedAchievementDesc`, `GetAchievementMainList` / `GetAchievementSubList` /
`GetAchievementInfo` / `AddTracingAchievement` / `RemoveTracingAchievement`, and the same
five `SCAchievement*Packet` names — in `Bin64/decompiled/x2game-dev_dedicate.c`. That is the
porting oracle for this finding.

### 2. Ranking boards — opcodes declared, no packet type, no data loaded

`X2Rank` · 8 UI scripts (`ranking/*.alb`). The client data ships complete rank definitions:

| table | rows | content |
|---|---|---|
| `ranks` | 4 | arena dominator (score / wins), biggest fish by length, biggest fish by weight, car-race best time |
| `rank_scopes` | 40 | rank tiers (`고급` / `신화` / `전설` … with score ranges) |
| `rank_rewards`, `rank_reward_links`, `rank_scope_links` | 40 each | reward definitions |

Server state:

* `SCRankCharacterPacket` (0x1fa), `SCRankSnapshotPacket` (0x1fb) and
  `SCRankRewardMailPacket` (0x1f9) are **opcode constants with no Rust packet type**.
* `SCRankAlarmPacket` (0x205) has a type but is **defined-but-never-sent**.
* `CSRankCharacterPacket` (0x12f) is a **log-only stub**
  (`systems/services/character_packets.rs:255`).
* **No Rust code reads** `ranks`, `rank_scopes`, `rank_rewards`, `rank_scope_links` or
  `rank_reward_links` — the tables are never even loaded.

Player-visible effect: opening a ranking board does nothing.

Note: 10.x **removed** this feature (`SCRankCharacterPacket` / `SCRacingPacket` do not appear
in the 10.x decompile), so there is **no later reference implementation** — it must be derived
from the 1.2 data and the client UI.

### 3. Racing / time trials — data and client UI exist, nothing server-side

`X2Racing` · 1 UI script (`racing/common.alb`). Client data ships two tracks:

```
race_tracks: (1, 'test_신기루섬',  zone 108, loop 300000, …)
             (2, '신기루섬 경주', zone 183, loop 300000, record_min 10000,
                 doodad 7369, wait 60000, ready 60000, start 5000, doodad_group 81, …)
race_track_shapes: 14 rows
```

`SCRaceCondPacket` (0x1f6), `SCRacingPacket` (0x1f7) and `SCRacingResultPacket` (0x1f8) are
**opcode constants with no Rust packet type and no handler**. As with ranking, 10.x dropped
racing, so there is no reference implementation.

### 4. Second password — client prompts, server answers nothing

`X2Security` · 4 UI scripts (`secondpassword/*.alb`), exposing `IsSecondPasswordCreated` /
`IsSecondPasswordLocked` / `CreateSecondPassword` / `CheckSecondPassword` /
`StartSecondPasswordCreation` and an unlock-remain-time alert.

Server state:

* `CSSetupSecondPassword` (0x140) and `CSRequestSecondPasswordKeyTablesPacket` (0x125) are
  **log-only stubs** (`systems/services/character_packets.rs:265,269`).
* **All eight responses are opcode constants with no Rust packet type**: 0x1e3–0x1ea
  (`SCSecondPassSetted`, `…KeyTables`, `…Changed`, `…Checked`, `…Cleared`,
  `…CheckOverFailed`, `…AccountLocked`, `…Created`).

Player-visible effect: the second-password UI cannot complete a single flow.

**Reference implementation available.** 10.x implements the whole feature —
`CheckSecondPassword`, `CreateSecondPassword`, `ChangeSecondPassword`, `ClearSecondPassword`,
`IsSecondPasswordCreated/Passed/Locked`, `GetSecondPasswordUnlockTime` /
`…UnlockRemainTime` / `…FailedCount` / `…ClearReserveTime` and the same `SCSecondPass*Packet`
names (plus `SCSecondPassReservedClearTimePacket`) in `x2game-dev_dedicate.c`.

### 5. Event calendar / schedule items — data loaded, packet never sent

`hud/main_menu_bar/timer.alb` calls `GetScheduleItemList`, `GetScheduleItemInfo`,
`CreateScheduleItemTimer` and **`TakeScheduleItem`** — a player-facing "claim this event
reward" action.

| table | rows |
|---|---|
| `game_schedules` | 241 |
| `game_schedule_spawners` | 174 |
| `game_schedule_doodads` | 119 |
| `game_schedule_quests` | 96 |
| `schedule_items` | 32 (with `icon_path`, `enable_key_string`, `disable_key_string`, `label_key_string`, `tool_tip`, `active_take`, `on_air`, `show_wherever`, `show_whenever`) |

The schedule **engine** is implemented (`systems/npcs/schedule.rs` — cron windows, per-spawner
and per-doodad period status), so scheduled spawns do happen. But `schedule_items` is **not
loaded**, and `SCScheduleItemUpdatePacket` (0x1eb) is a verified **defined-but-never-sent**
packet: the struct and its byte-exact test exist, nothing ever populates `schedule_items`.

Player-visible effect: the timer/event bar never lists events, and `TakeScheduleItem` has
nothing to claim.

**Reference implementation available, but the wire format changed.** 10.x replaced this with
the `SCGameSchedule*` family (`SCGameScheduleControlPacket`, `SCGameScheduleSinglePacket`,
`CSGameSchedulePacket`, `ClientGameScheduleManager`, `LoadScheduleItemDescs`). Use it for the
semantics (windows, on-air state, claim rules), not the 1.2 packet layout.

### 6. Other confirmed gaps

Each row is verified by the coverage tools or by direct inspection; "no type" means the opcode
has no Rust packet type at all, "never sent" means the type exists but nothing constructs it.

| Feature | Client evidence | Server state |
|---|---|---|
| Character edit / rename from the lobby | `CSEditCharacterPacket` | **no type** for `SCEditCharacterResponsePacket` (0x03a); the C→S side is a log-only stub (`packets/c2g_misc.rs`: *"is not implemented!"*) |
| Quest log list | quest journal UI | **no type** for `SCQuestListPacket` (0x065); only `SCQuestContextUpdatedPacket` / `SCQuestsPacket` exist |
| Unit equipment appearance | equipment UI | **no type** for `SCUnitEquipmentIdsPacket` (0x095) |
| Buff duration updates | buff bar | **no type** for `SCBuffUpdatedPacket` (0x0b9) |
| Bulk unit movement | `CSMoveUnitPacket` batching | **no type** for `SCUnitMovementsPacket` (0x06b); the body writer exists (`models/movement.rs:707`) but is **only called from tests** |
| Specialty / trade goods | `specialty/*.alb` | **no type** for `SCSpecialtyGoodsPacket` (0x09a) / `SCSpecialtyRecordsPacket` (0x09b) |
| Jury verdict counter | `usertrial/*.alb` (17 scripts) | `SCChangeJuryOkCountPacket` (0x179) **is implemented and sent**; `SCChangeJuryVerdictCountPacket` (0x17a) is **never sent** |
| Bounty system | `usertrial/bounty_bullet.alb`, `setup_bounty.alb` | **no type** for `SCSetBountyPermittedPacket` (0x1da), `SCSetBountyDonePacket` (0x1db), `SCBountyPaidPacket` (0x1dc) |
| Cooldown list | action bar / cooldown UI | `SCCooldownsPacket` (0x04a) **never sent** (documented: *"always empty in the C#"*) |
| Skill tree progress | `skill/*.alb` | `SCAbilityExpChangedPacket` (0x0ff) and `SCSkillUpgradedPacket` (0x106) **never sent** |
| Anti-addiction / playtime | session UI | `SCPlaytimePacket` (0x059) **never sent** |
| Player search list | `relationship/*.alb` | `SCSearchListPacket` (0x04f) **never sent** |
| Mining amount feedback | mining | `SCMineAmountPacket` (0x124) **never sent** |
| Knockback replication | combat | `SCKnockBackUnitPacket` (0x074) **never sent** |
| Faction / family / expedition management | `expedition/*.alb`, `territory/*.alb` | the densest dead cluster: **15 packet types defined but never sent**, covering all faction rename / immigration / relation results, faction owner & independence, family titles & name changes, and expedition rename / sponsor UI |
| Nation tax & monument | `nation/*.alb` | `SCNationalTaxRatePacket` (0x021), `SCNationalMonumentChangedPacket` (0x022) **never sent** |
| Castle siege / dominion status | `expedition/siege_schedule_icons*.alb` | **no type** for `SCSiegeState/Declared/Reinforce/Member/AlertPacket`; `SCDominionOwnerChangedPacket` (0x01f) **no type**; `SCSiegeReinforcePacket` / `SCDominionDeletedPacket` / `SCDominionTaxBalancedPacket` defined but **never sent** |
| Mail box live updates | `mailbox/*.alb` (26 scripts) | `SCMailReceiverOpenedPacket` (0x11c), `SCMailRemovedPacket` (0x123) **never sent** (both marked "unused in C#") |
| Crafting failure feedback | `crafting/*.alb` | `SCCraftFailedPacket` (0x1bf) **never sent** |
| Looting state | `loot/*.alb` | `SCUnitLootingStatePacket` (0x0f7) **never sent** |
| Housing build/farm/tax live updates | `housing/*.alb` | `SCHouseBuildPayChangedPacket` (0x0bf), `SCHouseFarmPacket` (0x0c3), `SCHouseOwnerNameChangedPacket` (0x0cb) **never sent** |
| Portal / teleport replication | `portal/*.alb` | `SCPortalDeletedPacket` (0x05e), `SCUnitPortalUsedPacket` (0x060) **never sent**; `SCNaviTeleportPacket` (0x15c) and `SCAboxTeleportPacket` (0x1a2) defined but **never sent** |
| Combat relationship display | faction/PvP UI | `SCCvFCombatRelationshipPacket` (0x087) / `SCFvFCombatRelationshipPacket` (0x088) **never sent** |
| In-game cash shop (ICS) | `ingameshop/*.alb` | `SCICSSyncGoodPacket` (0x1d4), `SCICSExchangeRatioPacket` (0x1d7), `SCPremiumPointChangedPacket` (0x202) **never sent**; the AA-point wallet is a documented deliberate gap (*"The 1.2 AA-point wallet is not implemented"*, `systems/economy/cash_shop.rs:944`) |
| Dungeon channel select | `indunchannelselect/*.alb` (7 scripts) | partially present: `select_channel` is loaded and used for instance allocation (`systems/instances/indun.rs:192`), but there is **no packet type for a channel-select response** |
| Client-driven instances | `clientdrivenindun/*.alb` (5 scripts) | partially present: `client_driven` is loaded (`systems/instances/data.rs:43`) but no packet or flow acts on it |
| Emblem / UCC name, doodad UCC | `X2Ucc`, stamp UI | **no type** for `SCEmblemUploadedPacket` (0x1a8), `SCEmblemDownloadedPacket` (0x1a9), `SCUccCharacterNameLoadedPacket` (0x15b), `SCUccCheckecPacket` (0x1ae), `SCDoodadUccStringPacket` / `SCDoodadUccDataPacket` |
| Demo mode | `client12` demo data (`demos`, `demo_chars`, `demo_equips`, …) | **no type** for `SCShowDemoModeUiPacket` (0x1b8), `SCDemoCharResetItemPacket`, `SCDemoCharResetLocPacket`, `SCDemoResetActionSlotPacket`; the demo tables are empty in the 1.2 compact DB |

## Why the existing audit could not see this

`rust/PORT-AUDIT.md` reports *"S->C packet classes without a Rust packet: 25 → 0"*. That result
is real, but it only ever compared **C# against Rust**: both the C# registry fixture
(`tests/fixtures/csharp_game_network_registry.tsv`) and the audit's expected set are derived
from the *former C# emulator*. Any opcode the 1.2 client supports that the C# never wrote is
invisible to it.

`SCRankCharacterPacket` is the clean example: it is in the 1.2 `SCOffsets` table that
`opcodes.rs` was generated from, it has no Rust type, and no C#-centric check will ever flag
it. `rust/tools/opcode_coverage/sc_coverage.py` and `sc_usage.py` close that blind spot by
iterating the 1.2 opcode table instead of the C# registry.

Suggestion: ratchet the "opcodes without a type" and "defined but never sent" counts in CI
once the lists below are triaged.

## Caveats

* **115 type-less opcodes is not 115 features.** The set mixes real gaps with emulator-only
  vocabulary no 1.2 client expects: 19 `SCUnkPacket*` / `CDominion*` placeholders plus GM/debug
  dump packets (`SCGmDump*`, `SCAiDebugPacket`, `SCDump*`) and demo-mode packets.
* **"244/244 handled" means a handler is registered, not that the feature works.** Several
  handlers are deliberate log-only stubs mirroring the 1.2 C# — `CSRankCharacterPacket`,
  `CSSetupSecondPassword`, `CSRequestSecondPasswordKeyTablesPacket`, `CSEditCharacterPacket`,
  `CSTurretStatePacket` — and those are called out individually above.
* **"Defined but never sent" is a static approximation.** The 73-row set was produced by
  textual analysis with no call-graph: a packet built only inside unreachable code still counts
  as sent, and a hand-assembled raw byte buffer would be missed (no such case exists). Eight
  borderline cases were inspected by hand and are genuine sends.
* The `never_sent` bucket emitted by `sc_coverage.py` alone is a weaker grep heuristic and
  produces false positives; prefer `sc_usage.py`'s classification, which ignores the opcode
  table as a use site, skips macro bodies and test modules, and was cross-checked by printing
  every non-test occurrence of each of the 73 packets.

## Method and reproduction

### The client's code cannot be statically analysed; its data can

Ghidra 12.1.4 headless **does work** on this machine (see `ghidra-work/README.md`), but
`bin32/x2game.dll` is **packed/encrypted**: `.text` is 9.7 MB at **7.88 bits/byte** entropy
(normal x86 is 6.0–6.5), and a full default analysis finds only **135 functions / 44,816
instructions** in 94 s — the packer stub, not the game. The same is true of `crynetwork.dll`,
`cryphysics.dll` and `archeage.exe`, and it matches `rust/docs/client/LUA-DRIVER.md:64`.
Unpacking (or dumping from a live process) is a prerequisite for static analysis of the 1.2
client's **code**.

What *is* readable is the client's **data**: the 24.9 GB `game_pak` is not encrypted, and its
compiled-Lua UI scripts keep their string constants — engine API names, UI callbacks, locale
keys, packet field names. That yields a client-side feature inventory with no disassembly at
all. The *10.x* client DLLs, by contrast, are **not** packed (`.text` entropy 6.3), and the
10.x zone server is already fully decompiled — which is why the porting oracles above come
from there.

### Commands

```sh
cd rust/tools/opcode_coverage

# 1. C->S coverage: 244/244 opcodes have a registered handler
python coverage.py

# 2. S->C coverage: which 1.2 server->client opcodes have no Rust packet type
python sc_coverage.py --json sc_coverage.json

# 3. S->C classification: sent / defined-but-never-sent (the 73-row set)
python sc_usage.py            # -> sc_sent.json, sc_sent.md

# 4. Client feature inventory: extract the compiled-Lua UI out of the 1.2 game_pak
python gamepak.py list 'D:\AA1.2\game_pak' '^game/scriptsbin/x2ui/.*\.alb$' --out x2ui-names.txt
python gamepak.py get  'D:\AA1.2\game_pak' client12-x2ui --out x2ui-names.txt
python alb_symbols.py client12-x2ui/game/scriptsbin/x2ui --out client12-x2ui-symbols.txt
python alb_api.py     client12-x2ui/game/scriptsbin/x2ui --out client12-x2-api.tsv
```

Client data counts quoted above come from `rust/crates/aaemu-game/app/Data/compact.sqlite3`
(read-only reference data); client feature names come from `client12-x2ui-symbols.txt`.
Reference binaries: `Bin64/decompiled/x2game-dev_dedicate.c` (10.x zone server, 3 291 392
lines) and `D:\Tools\ghidra_12.1.4_PUBLIC` for the analysis attempt.

## Suggested order of work

1. **Achievements** — most data shipped, largest player-visible hole, and the only one of the
   big four with both a complete client UI and a 10.x reference implementation.
2. **Event calendar / schedule items** — the engine already runs; this is mostly a data loader
   plus one packet.
3. **Second password** — self-contained, 10.x reference available, security-relevant.
4. **Ranking** — data and client UI shipped, no reference; needs design from the 1.2 data.
5. **Triage the remaining 115 type-less and 73 never-sent opcodes** by feature area. The
   densest clusters are faction/family/expedition (15 never-sent), the `packets/g2c_misc.rs`
   "no caller yet" module (19), and economy/mail/cash-shop (8).
