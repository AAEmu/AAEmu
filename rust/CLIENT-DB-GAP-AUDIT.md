# Feature-gap audit — Rust server vs. 1.2 client + databases (2026-10-14)

Independent audit (no OpenCode workers; direct inspection + fresh tool runs on `aa90a8c3e`
plus the uncommitted battlefield/spawner work in the tree). It **supersedes the stale parts
of `CLIENT-FEATURE-GAPS.md`** (2026-10-13): several of that document's headline gaps were
closed or half-closed within a day, and it did not cover unused DB tables, hardcoded
values, wrong packets, the MySQL schema, the login server, quests, or physics/geodata.

## Verdict in one paragraph

The C#→Rust port is faithful and the wire plumbing is complete inbound (244/244), but the
server is **not feature-complete against its own client and databases**: 108 of 517 S→C
opcodes cannot be encoded at all, 73 more packet types are never sent, 10 real C→S packets
are log-only stubs, ~60 server-relevant `compact.sqlite3` tables are never read, three
finished subsystems (achievements, ranking, schedule-item claims) are built and tested but
have **no live entry point**, every account is silently premium-until-2030, and a long tail
of C# bugs is mirrored on purpose. Nothing here needs the packed 1.2 DLL: Ghidra was run
live and confirms static analysis of the 1.2 client code is blocked (135 functions), so the
evidence base is the client *data* (`game_pak` Lua UI, `compact.sqlite3`), the opcode
tables, and the unpacked 10.x zone decompile as oracle.

## 1. Packets

Fresh runs (2026-10-14): `coverage.py`, `sc_coverage.py`, `sc_usage.py` in
`rust/tools/opcode_coverage/`.

| Measure | Oct 13 doc | Now |
|---|---|---|
| C→S wire opcodes with handler | 244/244 | **244/244** (unchanged) |
| S→C opcodes (1.2 `SCOffsets`) | 517 | 517 |
| S→C with Rust packet type | 402 | **409** |
| S→C with **no type at all** | 115 | **108** |
| S→C types defined but **never sent** | 73 | **73** (membership changed) |
| S→C types constructed in non-test code | 333 | **340** |

Caveat on the tools: `sc_coverage.py`'s "163 typed but no Marshal" line is a regex false
positive (packets declared via macros in `systems/social/packets/*` are counted as
Marshal-less even when sent); trust `sc_usage.py` for sent/never-sent.

### 1.1 Missing S→C packet types (108) — by feature

Excluding ~19 `SCUnk*` placeholders and GM/debug/demo vocabulary
(`SCGmDump*` ×5, `SCDump*`, `SCAiDebugPacket`, `SCShowDemo*`/`SCDemo*` ×4,
`SCHSRequestPacket`, `SCPlotProcessingTimePacket`), the real gaps cluster as:

- **Second password (entire feature):** `SCSecondPass*` 0x1e3–0x1ea (8 packets, no types).
- **Racing:** `SCRaceCond/Racing/RacingResult` 0x1f6–0x1f8.
- **Moulds:** `SCMouldList/Asked/Taken` 0x1ee–0x1f0 (the `moulds*` tables are empty in 1.2
  data, so this may be legitimately dead content — verify before building).
- **Scheduled events:** `SCScheduledEventStarted/Ended` 0x1ec–0x1ed, `SCScheduleItemSent` 0x151.
- **NPC interaction/friendship:** `SCNpcInteractionEndedByZone` 0x07c,
  `SCWorldInteractionSkillList/Canceled` 0x07e–0x07f, `SCNpcInteractionStatusChanged` 0x080,
  `SCNpcFriendshipList/Changed` 0x082–0x083 (pairs with unused `npc_interactions*` tables, §2).
- **Items:** `SCItemTaskNotify` 0x091, `SCItemDetailUpdated` 0x093,
  `SCUnitEquipmentIds` 0x095, `SCItemAcquisition` 0x097, `SCCraftItemUnlock` 0x0f5,
  `SCMateEquipmentExpired` 0x127.
- **Specialty/trade:** `SCSpecialtyGoods` 0x09a, `SCSpecialtyRecords` 0x09b.
- **Combat/movement:** `SCUnitMovements` 0x06b (bulk movement; body writer exists in
  `models/movement.rs` but is test-only), `SCCastingDelayed` 0x0a6, `SCUnitTracked` 0x0a9,
  `SCBombUpdated` 0x0aa, `SCBuffUpdated` 0x0b9, `SCUnitLocation` 0x1c7.
- **Quests:** `SCQuestList` 0x065, `SCQuestContextFailed` 0x146, `SCQuestUnitReqFailed` 0x148,
  `SCDoodadCompleteQuest` 0x14d, `SCQuestMailSent` 0x150, `SCShowQuestArea` 0x1af.
- **Doodads/gimmicks/UCC:** `SCDoodadPuzzleScene` 0x110, `SCDoodadPhaseMsg` 0x157,
  `SCDoodadUccString/Data` 0x158/0x15a, `SCUccCharacterNameLoaded` 0x15b,
  `SCEmblemUploaded/Downloaded` 0x1a8–0x1a9, `SCUccCheckec` 0x1ae,
  `SCGimmickResetJoints/Grasped` 0x140–0x141, `SCDoodadReqBattleField` 0x1be.
- **Battlefields:** `SCInstantGameState` 0x18d, `SCInstantGameInviteTimeout` 0x18f
  (relevant to the in-flight arena work).
- **Social/dominion:** `SCNationShowRenameUI` 0x010, `SCEditCharacterResponse` 0x03a,
  `SCAreaTeamMessage` 0x155, `SCInvitationCanceled` 0x1dd, `CDominionStartUnk` 0x024,
  `SCDominionEndUnk` 0x025.
- **Misc client services:** `SCChatFailed` 0x0d1, `SCChatLocalizedMessage` 0x0d2,
  `SCAuctionMessage` 0x133, `SCNpcSpawner` 0x135, `SCPlaySequence` 0x13b,
  `SCWorldRayCastingResult/AimPoint/AABB` 0x142–0x144, `SCSoundAreaEvent` 0x152,
  `SCDelayedTaskOnInGameNotify` 0x1a3, `SCRestrictInfo` 0x1c8, `SCIsUnitInFarm` 0x1c9,
  `SCNotifyUIMessage` 0x1cc, `SCGmCommand` 0x199, `CSQuestAcceptConditional` 0x14a
  (a CS-named constant sitting in the SC table).
- **Siege state family** remains typeless per the Oct 13 doc (`SCSiegeState/Declared/
  Member/Alert` etc. — several share names with the dominion work in
  `systems/housing/dominion/`; recheck name-by-name before assuming).

### 1.2 Defined but never sent (73)

Densest clusters (fresh `sc_usage.py`): faction/family/expedition management (~17:
all rename/immigrate/relation/owner/independence results, family title/name, sponsor),
`packets/g2c_misc.rs` "no caller yet" set (cooldowns `SCCooldownsPacket`, playtime,
search list, knockback, mine amount, CvF/FvF combat relationship, idle kick, world
message…), housing live updates (build-pay, farm, owner-name), portal replication
(deleted/used/navi/abox teleport), mail live updates (receiver-opened, removed),
craft-failed feedback, looting state, ability-exp/skill-upgraded, jury verdict count,
trial canceled, bot-trial pair, bounty trio (types now exist in `trial.rs` — still unsent),
cash-shop exchange ratio + premium-point-changed, `SCHackGuardRetAddrsRequest`,
`SCInstantGameRemainTime` (its C# comment is literally "TODO: Check offset!!"),
`SCRankRewardMail`/`SCRankCharacter`/`SCRankAlarm`, `SCScheduleItemUpdate`,
`SCAchievementReseted`, `SCSiegeReinforce`, dominion deleted/tax-balanced,
national tax/monument, character renamed, unit bounty money, unit offline,
escape-slave, slave-equipment-changed, ask-to-join-team-area pair, spam-reported,
add-action-point, character-bound, unknown (0x0a0), mileage-changed,
unlock-currency-slot, unit-GM-mode-changed, world-queue.

### 1.3 Log-only C→S stubs (handler registered, feature absent)

Registered (count toward 244/244) but only log: `CSEditCharacterPacket` (body not even
read — lobby character edit/rename is dead; `packets/c2g_misc.rs:45`),
`CSTurretStatePacket` (ship turret aim is dropped), `CSRankCharacterPacket`,
`CSSetupSecondPassword`, `CSRequestSecondPasswordKeyTablesPacket`, `CSConsoleCmdUsedPacket`,
`CSCriminalLockedPacket`, `CSEditorGameModePacket`,
`CSRequestPermissionToPlayCinemaForDirectingMode`, `CSResturnAddrsPacket` (HackShield
return-address report; pairs with unsent `SCHackGuardRetAddrsRequestPacket`).

Defined but deliberately **not registered** (no 1.2 opcode, `0xfff`, mirroring C#):
`CSResetQuestContext`, `CSAcceptCheatQuestContext`, `CSSearchList`,
`CSChangeExpeditionSponsor`, `CSChangeSlaveTarget`, `CSSetTeamOfficer`,
`CSUpdateNationalTaxRate`, plus economy's `CSSetCraftingPay`, `CSChangeItemLook`,
`CSSpecialtyRecordLoad`, `CSBuySpecialtyItem`, `CSReturnMail`. 14 C→S constants in
`opcodes.rs` have no 1.2 wire opcode at all. These are correct to leave dead unless
packet captures prove otherwise — but they should stay labelled as such, which they are.

### 1.4 Wrong / unverified packets

- **Level-1 framing is unauthenticated:** server writes hash/count 0/0 and never verifies
  the client's (`net/packet.rs`, C# "TODO: verify 1.2 crc"). Any non-1/2 level kills the
  connection (C# throws). There is no stream encryption in 1.2 (correct).
- **Empty faction lists use wrong type ids** `0x007`/`0x008` (hard-coded in C#; in the 1.2
  table those are `SCTrionConfigPacket`/`SCFactionListPacket`) — mirrored
  (`packets/world.rs`, PORTING.md "C# bugs noticed").
- **`SCUnitStatePacket.Write` side-effects mirrored:** adds buffs 8000011/8000012
  ("Wrong place"); an exception mid-`Write` truncates the packet (swallowed by the C#
  marshaler). Both reproduced deliberately.
- **Duplicate visibility packets mirrored:** `RemoveVisibleObject` sends
  `SCUnitsRemoved` twice to same-sector players; first placement shows children twice;
  gimmick ids appear in both `SCUnitsRemoved` and `SCGimmicksRemoved`.
- **Approximate fields:** character list writes `hp * 100` ("TODO: precise health?",
  `packets/g2c_misc.rs:373`), `leave_time` for `lastWorldLeaveTime` (:382),
  `languageType = 0` (`packets/world.rs:712`).
- **Unverified error paths:** craft failure `SCErrorMsg` pairs marked "TODO not verified"
  ×4 (`systems/economy/craft.rs:319,411,529,544`); one "TODO verified" beside them shows
  the rest were never capture-checked. `SCCraftFailedPacket` itself is never sent.
- **Assumed packet-size caps:** item-task counts written as `u8` with "TODO max 30/28"
  (`item_packets.rs`, `trade.rs`) — truncation behavior unverified.
- **Compressed packets (DD04)** are emitted as *stored* (uncompressed) deflate blocks;
  valid, but not what a retail server sends (size/bandwidth only).
- **`SCInstantGameRemainTimePacket` layout is explicitly unchecked** in the source C#
  ("TODO: Check offset!!", `systems/instances/packets.rs:622`) and the packet is unsent.
- Login: two `None => fail // TODO` auth branches and a hard-coded max packet size note
  (`aaemu-login/src/network/login.rs:340,355,767`); otherwise the login crate has no
  stub handlers.

## 2. `compact.sqlite3` tables the server never reads

Method: all 679 tables enumerated read-only; a table counts as *used* only if its name
appears in a Rust source (loaders select by literal name). Result: **474 used, 205 never
named** (166 of the 205 are non-empty; 39 are empty in the shipped DB — demo/mould/doodad-
modifier families, `premium_points`, `merchant_price_ratios`, `mine_jewel_rates`, etc.,
which are correctly ignorable).

Most of the 166 are legitimately **client-only presentation data** the server should not
load: `item_assets` (21 276), `item_armor_assets` (18 003), `hair_colors` (14 751),
`sound_pack_items`/`sounds`/`fx_*` (~25k rows), `icons`, `ui_texts`, `armor_assets`,
`quest_chat_bubbles`/`quest_*_texts` (client dialogue text), book contents,
`schema_migrations` (3 148; tooling metadata), cinemas/captions, model/anim bindings.

The server-relevant remainder — gameplay rules the client ships but the server either
hardcodes, approximates, or lacks the feature for:

| Area | Unused tables (rows) | Consequence |
|---|---|---|
| Buffs/CC | `buff_breakers` 661, `buff_mount_skills` 64, `buff_unit_modifiers` 17, `aoe_diminishings` 10 | No data-driven CC-break/diminishing/mount-buff rules |
| NPC AI/groups | `npc_aggro_links` 643, `aggro_links` 130, `npc_groups` 158, `npc_group_members` 571, `quest_monster_groups` 594, `ai_files` 15 | Linked-aggro and group membership not data-driven (quest monster-group hunts exist — check they don't need `quest_monster_groups` proper) |
| NPC interaction | `npc_interactions` 114, `npc_interaction_sets` 111, `npc_nicknames` 88 | Pairs with typeless interaction/friendship packets (§1.1) |
| Items | `item_recipes` 2 816, `craft_packs` 150, `merchant_packs` 263, `item_conv_rpacks/ppack_members/rpack_members` ~923, `item_sockets` 123 + `item_socket_num_limits` 372 + `item_socket_level_limits` 108, `item_proc_bindings` 51, `item_dyeings` 52, `item_open_papers` 551, `item_cap_scale_forbids` 1 340, `item_tools` 130, `item_groups` 29, `item_bags` 5, `tagged_items` 1 066 | Crafting/store/socketing/conversion run on other tables or hardcoded subsets; each needs a per-table verdict (some may be client-UI mirrors of `crafts*`) |
| Enchanting | `physical_enchant_abilities` 86 | — |
| Units/characters | `unit_attribute_limits` 21, `character_p_stat_limits` 5, `resurrection_waiting_times` 10, `allow_to_equip_slots` 35, `allow_to_equip_slaves` 16, `equip_slot_groups` 24 + maps 48 | Attribute caps / respawn waits / equip rules hardcoded or uncapped |
| Mates/slaves | `mate_equip_pack_groups` 191, `mate_equip_packs/items/slot_packs` ~82, `slave_equip_slots` 92, `slave_customizing*` ~49, `slave_initial_item_packs` 10, `item_slave_equipments` 43, `item_shipyards` 13, `shipyard_rewards` 9 | Vehicle customization/equip packs partial (`SlaveCustomize` flag exists) |
| Siege/dominion | `siege_items` 13, `siege_settings` 11, `siege_ticket_offense_prices` 10 | Siege tuning not loaded |
| Battlefields | `game_score_rules` 35, `game_rule_events` 9, `battle_field_buffs` 11 | Directly relevant to the in-flight arena work; today scoring lives in `battlefields.json` + code |
| Racing | `race_tracks` 2, `race_track_shapes` 14 | Feature absent (§1.1) |
| Trade/economy | `specialty_bundles` 30, `auction_a/b/c_categories` 122 | Auction category tree is client-rendered; verify server search doesn't need it |
| Chat/names | `allowed_name_chars` 2 352, `blocked_texts` 24, `replace_chat*` ~126, `chat_spam_rules` 1 + details 30, `slash_commands` 171, `slash_functions` 47, `chat_commands` 15, `ignore_texts` 7, `grammar_tags` 29 | Name validation uses a config regex; profanity/spam/slash data unused |
| Premium/world | `premium_benefits` 2, `premium_configs` 1, `pcbang_buffs` 5, `content_configs` 53, `world_spec_configs` 1, `world_var_defaults` 1, `world_groups` 6, `zone_climates` 10, `zone_group_banned_tags` 333 | Premium is hardcoded instead (§4); world/content toggles unread |
| Housing | `housing_deco_limits` 12, `doodad_bundles*` 6, `blocked_child_doodads` 2 | Deco limits not enforced from data |
| Books/UCC | `books` path exists via other tables; `ucc_applicables` 128, `emblem_patterns` 57 | UCC applicability not checked from data |
| Quests (small) | `quest_categories` 92, `quest_cameras` 51, `quest_act_obj_aliases` 2 746 (likely editor-only), `quest_mails*` ~3, `quest_tasks` 1 | Category used by achievements via quests themselves; verify |
| Schedule/faction misc | `faction_chat_regions` 4, `default_inventory_tabs*` 14, `default_action_bar_actions` 3, `deco_actability_groups` 6, `item_secure_exceptions` 1, `ddcms_merge_protect_infos` 12, `doodad_func_buy_fish_model_items` 61, `wi_details` 60, `tooltip_skill_effects` 136 (client), `skill_synergy_icons` 130 (client) | Mixed; per-table verdict needed |

Second-order finding: a further **91 tables are named in code but never appear after
`FROM`/`JOIN`** — mostly `quest_act_*` detail tables loaded through generic helpers
(legit), but the list also contains `constants`, `premium_grades`, `equip_item_sets`,
`projectiles`, `prefab_models`, `tags`, `sub_zones`, `return_points`, `books`, `climates`.
Each deserves a one-line verdict (loaded-via-helper vs. mentioned-only-in-comments/tests).
This audit did not prove per-table consumer reachability for the 474 "used" tables;
loaded-but-never-consumed is its own class (see §3 for three proven cases at feature level).

Also from PORTING.md (skills audit): the shipped DB itself is inconsistent — **33 unique
buff templates referenced by 35 `BuffEffect` definitions do not exist** in either the
client-extracted or server DB (15 126 skill ids, 9 816 buff ids otherwise agree). The
loader warns; no replacements are fabricated. Those 35 effects can never work as authored.

## 3. Built but unwired (the Oct 13 document is already stale here)

Three subsystems landed after `CLIENT-FEATURE-GAPS.md` called them missing. All three now
have data loaders, pure runtimes, persistence and tests — and **no live caller**:

1. **Achievements.** `achievement.rs` now loads `is_active`/`complete_or` correctly via
   `bool_str` (the C# `GetBoolean`-on-text bug is *not* mirrored here);
   `achievement_manager/runtime/persistence/rewards/hooks` exist; rows load/save with the
   character (`db_character.rs:602` — whose comment still says "the gameplay events that
   report into them are a later phase"). But `initial_snapshot`/`back_credit_values` and
   every record producer are only called from tests; nothing outside
   `systems/social/achievement*` reports progress. `SCAchievementResetedPacket` is the one
   remaining never-sent packet of the family. Player effect unchanged from Oct 13 (empty
   panel), but the remaining work is now *wiring* (~55 `char_records` kinds;
   `rust/docs/achievement-hook-audit.txt` maps them), not building.
2. **Ranking.** `ranking_data.rs` loads `ranks/rank_scopes/rank_rewards` + links;
   `ranking_packets.rs` defines the wire types; `ranking_runtime.rs` builds standings,
   personal rows and snapshots. `build_snapshot`/`build_personal_rows` have **zero
   non-test callers**, `CSRankCharacterPacket` is still log-only, and
   `SCRankCharacter/RewardMail/Alarm` are never sent. The snapshot scope figures are sent
   empty by design ("raw meaning unresolved, none is invented"). The racing board is
   explicitly excluded from the runtime.
3. **Schedule items (event timer bar).** `schedule_items` (32 rows) is loaded, account
   progress persists to MySQL `account_schedule_items`, and
   `AccountScheduleProgress::build_update_rows` composes the HUD rows — again test-only.
   `SCScheduleItemUpdatePacket` (0x1eb) is still never constructed outside tests, and no
   claim path (`TakeScheduleItem`) is wired. The underlying spawner/doodad schedule engine
   (`game_schedules*`) does run.

Same pattern, smaller: `SCUnitMovements` body writer (test-only), dungeon
`client_driven`/`select_channel` data loaded with no packet flow (Oct 13 §6, still true).

## 4. Hardcoded / assumed values

- **Every account is premium until 2030.** `AccountPayment::default()`
  (`net/connection.rs:38-56`, C# TODO "load real payment info") is never replaced with
  real data; `connection_payment()` literally returns the default. Consequences: premium
  labor cap 5 000 for everyone (`timed_rewards.rs:22`), premium-gated schedule items,
  quest labor-margin checks. `premium_benefits/configs/grades` tables unread (§2). The
  AA-point wallet is explicitly unimplemented (`cash_shop.rs:944`), and
  `SCPremiumPointChangedPacket`/`SCICSExchangeRatioPacket` are never sent.
- **Feature flags are a hardcoded 11-byte blob** (`data/features.rs:42` `DEFAULT_FSET`
  from C#) with explicit overrides: `Secondpass`/`IngameshopSecondpass`/`ItemSecure`/
  `SensitiveOperation` **off**, `DwarfWarborn` **off**. The client gates UI on these bits,
  so several "missing features" are also switched off client-side by our own fset —
  enabling the server half without flipping the bit (or vice versa) does nothing.
- **Unplayable races:** Fairy/Dwarf/Returned/Warborn map to 0 ("not implemented",
  `systems/npcs/npc/manager.rs:255`), consistent with the fset bit above.
- **Login limits are constants:** `MAX_CHARACTERS_PER_ACCOUNT = 6`,
  `ADDITIONAL_CHARACTERS_PER_SERVER = 0`, `IS_PRE_SELECT_CHARACTER_PERIOD = false`
  (`aaemu-login …/login.rs:526`, "TODO: Make configurable" ×3). ARS phone 2FA is a stub
  (`two_factor.rs:318`, no `IArsPhoneProvider` in C# either).
- **Store hack:** four hard-coded template ids `[17663, 17662, 28051, 24581]`
  (`HACK_TEMPLATES`, `store_data.rs:118`, mirrored from C# ItemManager), plus the
  hard-coded UCC/music/treasure item templates in `data/items.rs`.
- **Physics/AI assumptions:** `HEURISTIC_FLOW_SIGN_HACK = -1.0` (water flow sign,
  `systems/ai/physics/water.rs:253`); fear/wandering speed is the **7 m/s fallback**
  because actor model stances are not loaded; quest distance checks use **model size 0**
  (no model data); geodata zone pick is "first match" ("TODO: pick the actually correct
  zone"); ship hulls fall back to a mass-box when authored physics parts are unavailable.
- **Unverified formulas:** specialty/trade-pack price formula ("TODO: Verify this
  formula", `economy/bridges.rs:640`), negotiation bonus = 0, labor-by-skill-level not
  calculated, global FeatureSet inside specialty is a fresh default ("TODO: implement a
  global fsets"); several trade-cancel reasons are literal `0 // TODO - reason?`.
- **Config-section gaps:** specialty/mail delays use C# defaults because there is **no
  `Specialty` config section yet**; `ExpeditionConfig` falls back to shipped JSON;
  proxy test URLs are hard-coded ("TODO - config files").
- **Delete-character wipe is partial:** `fullWipe` unimplemented
  (`managers/character.rs:427`); `DeleteCharacterAssets` (houses/guild/family/mail
  returns) is a later phase and sends no guild/family packets to the lobby character.
- **Assumed-by-construction:** session ids are a counter (not endpoint hash); mail dates
  are whole seconds; auction/mail enumeration orders are chosen to mimic C# dictionary
  order; unknown faction ids write mother-id 0; a missing unit formula evaluates to 0
  (C# throws); `save_rule_id`/`real_time` default when columns are absent.
- Scale of the known-unknowns: **~143 `TODO` strings** across the workspace sources,
  12 "not implemented" sites. The biggest functional TODO clusters are economy
  (trade/craft/specialty), doodad funcs, housing ops, and instant games.

## 5. Mirrored C# bugs (wrong on purpose — know the list)

`PORTING.md` "Known deviations" + "C# bugs noticed (mirrored, not fixed)" is the register;
the fidelity policy is to match the original unless the 1.2 client disagrees. The
player-visible ones worth re-deciding explicitly:

- Text-boolean class: C# `GetBoolean` on `'t'`/`'f'` columns loads `false`. Rust loaders
  mostly fixed this (`bool_str`, 417 uses; achievements fixed), but PORTING still lists
  craft/backpack/weapon booleans in economy as false-for-`'t'` — audit the remaining
  numeric readers column-by-column.
- Social: kicker leaves own chat channels on `AskRiskyTeam`; joiners never join party
  chat (but can speak); family invitations always carry family id 1; `CSAddFriend`
  accepts yourself; disbanded guilds stay listed; expedition owner-name not updated.
- World: double remove/show packets (§1.4); sector order depends on swap-remove;
  hard disconnects never leave chat channels; mail count at character select is always 0
  (C# counts before registration); `House.RemoveVisibleObject` chunk bug drops doodads
  beyond the first 400 with a logged error.
- Items/economy: stacked-item DB rows survive merges (dup on next load); split items get
  owner 0 / wrong previous-slot events; loot dice pick the lowest roll; looting leaks an
  item id; `ItemProc ignoreRoll` never procs; trade completes when only one side locked
  and the whole stack changes hands (amount only checked); negative money put-ups
  accepted; cash-shop loyalty charged undiscounted; auction "ExpireDate" sorts by
  PostDate and buyout sends no bid packet. (Several economy exploits *were* fixed
  deliberately — ownership checks on mail/coffer/auction, trade cleanup — see PORTING
  deviations; the line between "mirrored" and "fixed" is per-case, not principled.)
- Quests: `QuestActObjZoneMonsterHunt` (non-score) can never complete (`count > Count`
  required, objective capped at `Count`); unused act types log "not implemented" and do
  nothing; daily reset at 00:00 UTC (C# used server-local time).
- Instances (C# originals, several since fixed in Rust — PORTING records which):
  double-subscribed indun events (kill chains ×4), entry item charged twice, instance
  zone chat never left, `InstantGame` corps cleanup keyed by 0/1 instead of faction ids,
  `AddScore` always `Corps1`/`score2 = 0`, battlefield entry kicking cross-faction guild
  members via `SetFaction`. The uncommitted arena work should be checked against this
  list line by line.
- NPC spawns: inverted "spawn all" schedule check, `DoRandomSpawn` double-spawn + obj-id
  leak, gimmick rotations left in degrees, NPCs never despawn when players leave —
  partially superseded by the Oct 13–14 spawner work (populations, schedules, cave
  heights); re-verify each bullet before quoting it.

## 6. Quests (fresh `quest_audit` run)

4 876 contexts → 4 775 loaded quests (101 tutorials excluded by design), 17 720 loaded
components, 19 047 loaded acts of 26 886 (7 607 orphan acts belong to unloaded/orphan
components — shipped-data dead content, not server loss). Validation: 4 034
static-checks-only, **622 needs-review, 10 blocked**, 109 auxiliary. Standing findings:
component-accept always-true (263 quests), interaction ignores action/phase (271),
missing act target (43), authored NPC actions unverified (32), requirement always-true
(21), missing required quest (13), no-components (10 = the blocked set), unknown
requirement passes (8). Twelve finding classes were *resolved* by the October quest
audit (timer isolation/deadlines, NPC/doodad report isolation, zone-kill filtering,
aggro credit, escort death…); `Docs/wiki/Quest-Audit.md` tracks the rest. Static audit
only — it does not certify playability, and no live quest-lifecycle execution backs the
4 034 "passes static checks" quests.

## 7. MySQL schema (`SQL/`)

43 game tables + updates. Two schema tables are **never referenced by any Rust code**:
`cash_shop_item` (superseded by the `ics_*` trio) and `mails_items` (mail attachments
live elsewhere) — dead schema that will mislead anyone reading the DB first. Everything
else is referenced at least by name. Newer feature tables exist and are used:
`account_schedule_items`, `character_achievements`, `character_records`, `dominions*` —
note the achievement/schedule tables persist data for features whose live entry points
don't exist yet (§3), so rows will simply never be written by current builds. There is
no MySQL home for: second-password state, ranking scores/snapshots (runtime is
in-memory/test-only today), AA-point wallet, bounty state — building those features
needs `SQL/updates/…` + base-file patches per the repo workflow.

## 8. Client files, physics, geodata

- **Static collision is now loaded** (terrain + `brush.dat`/`vegetation.dat` via CGF
  `MeshPhysicsData`, config-gated `Geometry = Terrain/Brushes/Full`) — the gap list in
  `rust/docs/physics/ZONE-PHYSICS-USAGE.md` §2.7 is **stale**. Still missing per that doc
  and current code: terrain surface types (`cover.ctc` → surface-id queries return −1),
  visareas/indoor geometry, terrain holes, `entities.xml` level areas/static gimmicks,
  wheeled-vehicle physics for land vehicles (client-driven today), shared-render-mesh
  CGF blobs (341 corpus models, `aaemu-physics` README), physics worker threads.
- **Geodata reader defects** (ZONE doc §"Rust ai::geodata gaps", spot status unverified
  against current code): link byte +42 compared `== 1` instead of bit-test, nav-modifier
  header misread (`altered` always true), no cross-sector graph, no triangle height
  interpolation, pass radius/cost/flags/nav-type ignored, no physics walkability.
  PORTING's geodata section says parts were since made engine-faithful — reconcile the
  two documents.
- **Data-format drift risk:** navmesh areas v21 (1.2) vs v22 (10.x) — the reader rejects
  v > 21, correct for 1.2, but any 10.x-sourced data must be version-checked per file.
- **41 server-only DB tables** that the 10.x zone server needs are absent from all client
  data (its `game.sqlite3` is encrypted), which is why `dedicatedserver.exe` cannot be
  used as a live oracle — only its decompiled code can.

## 9. Ghidra status (run live for this audit)

Ghidra 12.1.4 headless works via `ghidra-work/ghidra-headless.cmd`. Re-running
`CountFunctions.java` against the saved `proj-aa12` project reproduced the recorded
result exactly: **packed `x2game.dll` (1.2): 135 functions, 3 named, 12 thunks,
2 345 symbols** — the packer stub only. `.text` entropy 7.88 bits/byte over 9.7 MB
confirms encryption at rest; the same holds for `crynetwork.dll`, `archeage.exe` and
the packed `cryphysics.dll`.

**Unpack — done later the same day (supersedes the "blocked" framing above).** A
runtime dump from the live client already existed at
`D:\aa-re\regrade\db-auth\runtime\` (`dump_runtime_modules.py`, `ReadProcessMemory`
on `archeage.exe`): `x2game.text.bin` measures 6.70 bits/byte, i.e. real decrypted
code. The protection is in-place section encryption with normal section names, 755
imports/exports intact, image loaded at its preferred base `0x38ff0000`, so the
dumped `.text`/`.rdata` were spliced back into a copy of the original
(`D:\aa-re\client12\rebuild_unpacked.py`) producing
**`D:\aa-re\client12\x2game_unpacked.dll`**. A full headless Ghidra analysis
(`ghidra-work/proj-aa12u`) then succeeded with **65 203 functions (18 756 named,
159 thunks)** — the 1.2 client logic is now statically readable, including its own
SQL loader strings (`SELECT … FROM doodad_func_mould_items`) and the PDB path
`C:\work\na_hotfix\trunk\dev32\x2game.pdb`. Limits: `.data` was not part of the
runtime dump (globals/RTTI type descriptors referenced but valueless — Ghidra logs
TypeDescriptor warnings), and no public unpacked copy was found anywhere (web/GitHub
searches return only generic VMProtect material; this packer carries no VMProtect or
Themida marks). The same live-dump recipe can recover `.data` and the other packed
1.2 DLLs whenever needed. Native cross-check from the unpacked strings: the 1.2
client has 646 `Load*Descs` loaders naming 666 distinct `FROM` tables, of which
**194 are never named in the Rust sources** — independent confirmation of §2
(the 205-by-enumeration set overlaps almost exactly).

Pak side, for completeness: the upstream public editor
([ZeromusXYZ/AAEmu-Packer](https://github.com/ZeromusXYZ/AAEmu-Packer), `AAPakCLI`)
was cloned and built from source in this audit; its read-only mode was verified
hash-stable on a throwaway pak before listing the real 1.2 `game_pak`
(218 066 files, 5 deleted/extra entries; size/mtime unchanged).

Remaining oracles: the unpacked 1.2 `cryphysics` copy
(`D:\aa-re\cryphysics\work_x86\`, IDA `.i64` present) and the unpacked 10.x binaries
(`.text` entropy 6.3), whose zone server is already fully decompiled
(`Bin64\decompiled\x2game-dev_dedicate.c`, ~89k functions — verified present).
Practical oracle map per gap, updated: packet layouts/feature logic → **1.2 unpacked
`x2game.dll` first**, 10.x zone second (same packet names for achievements + second
password); ranking + racing → 1.2 unpacked (removed in 10.x); schedule items → 1.2
unpacked (10.x changed the wire format to `SCGameSchedule*`); physics → 10.x zone
physics (same CryPhysics generation).

## 10. Suggested order of work

1. **Wire the three finished subsystems** (§3): achievement producers + entry snapshot,
   ranking request path (`CSRankCharacterPacket` → snapshot/personal rows; needs a
   score-persistence decision), schedule-item send + claim. Highest value per hour —
   the engines and tests already exist.
2. **Second password** (feature flag first, then 8 packets + 2 stubs; 10.x oracle;
   needs a MySQL table). Security-relevant and self-contained.
3. **Per-table verdicts for §2's server-relevant set**, starting with
   `aoe_diminishings`, `buff_breakers`, `npc_aggro_links`/`npc_groups*`,
   `resurrection_waiting_times`, `unit_attribute_limits`, and the battlefield trio
   (`game_score_rules`, `game_rule_events`, `battle_field_buffs`) while arena work is
   in flight.
4. **Premium/payment truth:** load real `AccountPayment` (or document the emulator
   choice in config), decide the AA-point wallet, and flip fset bits only with their
   server halves.
5. **Triage the 108 + 73 packet lists by feature** and ratchet both counts in CI
   (`sc_usage.py` output), so the C#-centric PORT-AUDIT blind spot ("0 missing") can't
   hide regressions again.
6. **Quest live-execution sampling** for the 622 needs-review quests, and reconcile
   the stale documents this audit found (`CLIENT-FEATURE-GAPS.md`,
   `ZONE-PHYSICS-USAGE.md` §2.7/geodata, `achievement-hook-audit.txt` "untracked WIP"
   header, PORTING spawner bullets).

## Method and limits

Fresh, read-only: opcode tools (above), `quest_audit/audit.py` against
`compact.sqlite3`, full 679-table enumeration with row counts, text searches over
`rust/crates/*/src`, `PORTING.md`/`CLIENT-FEATURE-GAPS.md`/`ZONE-PHYSICS-USAGE.md`
cross-checked against code, `SQL/` schema diff, and the Ghidra headless run in §9.
Limits: "table used" = name appears in a source file (overstates use; §2); "packet
sent" = constructed in non-test code (a builder only called from tests still counts as
constructed when the builder itself is non-test code — §3 was verified by caller
search, other features were not); no live server or client was run; the uncommitted
battlefield/spawner changes in the tree were read as-is and may shift §1/§2 battlefield
rows. Scratch scripts used for counting were deleted after the run; all numbers above
are reproducible with the named tools.
