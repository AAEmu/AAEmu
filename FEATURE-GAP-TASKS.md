# Feature-gap task list — server vs. 1.2 client (prioritized)

Single ordered task list derived from [`rust/CLIENT-DB-GAP-AUDIT.md`](rust/CLIENT-DB-GAP-AUDIT.md)
(2026-10-14). That audit holds the evidence; this file holds the work. Check items off here
as they land and record deviations in `rust/crates/aaemu-game/PORTING.md` per repo policy.

Primary oracle for packet layouts and client logic is now the **unpacked 1.2 `x2game.dll`**
in Ghidra — see the Ghidra section of [`REVERSING.md`](REVERSING.md#tools) for install,
project locations (`ghidra-work/proj-aa12u`, 65 203 functions) and the unpack recipe.
Secondary oracle: the 10.x zone decompile (`Bin64/decompiled/x2game-dev_dedicate.c`).

Priorities: **P0** = finished code that only needs wiring, or cheap guardrails.
**P1** = complete client features with a working oracle. **P2** = rules the shipped data
defines but the server ignores. **P3** = long tail, cleanup, and verify-before-building.
Effort: S ≤ a day, M ≈ days, L ≈ a week or more. Every task ships with the repo's
standard gates (byte-exact packet tests, flow tests, `cargo xtask ci`, live-client check).

---

## P0 — wire what's already built

- [ ] **1. Achievements: live producers + entry snapshot** (M)
  Call `achievement_hooks::initial_snapshot` on character select; add record producers for
  the ~55 `char_records` kinds per `rust/docs/achievement-hook-audit.txt`
  (`achievement_manager::back_credit_values` already covers state-like kinds); send
  `SCAchievements/Changed/Completed/ItemSent`; implement or deliberately drop
  `SCAchievementResetedPacket` (0x1ff, only unsent family member). Done when: panel lists
  active achievements, a kill/level/quest event progresses one live, reward item arrives.
- [ ] **2. Ranking: request path + persistence** (M)
  Replace log-only `CSRankCharacterPacket` (`systems/services/character_packets.rs:255`)
  with a handler that sends `ranking_runtime::build_personal_rows`/`build_snapshot`;
  decide score persistence (today in-memory/test-only — needs `SQL/updates/…` + base patch
  or an explicit volatile design); record arena and fishing scores at their events; send
  `SCRankRewardMailPacket`/`SCRankAlarmPacket`. Client strings to mine first:
  `RequestRankSnapshots`, `MAX_RANK_SNAPSHOT`, `GetRankInfo` in proj-aa12u.
  Done when: board opens with standings, own row shows, reward mail after a period closes.
- [ ] **3. Schedule items: send + claim** (S)
  Send `SCScheduleItemUpdatePacket` (0x1eb) built from `build_update_rows` on login and on
  window changes; wire the claim path (`TakeScheduleItem` — find its CS packet/handler in
  proj-aa12u) through `ScheduleItemManager` (persistence to `account_schedule_items`
  already works). Done when: timer bar lists the 32 authored items and a claim gives the
  item exactly once per window.
- [ ] **4. CI ratchet for packet coverage** (S)
  Land `rust/tools/opcode_coverage/sc_usage.py` counts as a checked baseline
  (today: 108 typeless, 73 never-sent, 340 sent) so the C#-centric PORT-AUDIT's
  "0 missing" can't hide regressions; fail CI when either gap count grows.

## P1 — dead client features, oracle available

- [ ] **5. Second password, end to end** (M)
  Flip fset `Feature::Secondpass`/`IngameshopSecondpass` (`data/features.rs:70-71`) only
  together with the server half: implement `CSSetupSecondPassword` +
  `CSRequestSecondPasswordKeyTablesPacket` (today log-only), add the 8 typeless
  `SCSecondPass*` packets (0x1e3–0x1ea), add MySQL state (hash, fail count, lock/unlock
  times). Layouts/semantics: proj-aa12u (`CreateSecondPassword`, `CheckSecondPassword`,
  `GetSecondPasswordUnlockRemainTime`, …) corroborated by the 10.x zone implementation.
- [ ] **6. Recover `x2game.dll` `.data` (RE enabler)** (S)
  Re-run the live dump (`D:\aa-re\regrade\db-auth\runtime\dump_runtime_modules.py`,
  update PID, add `.data`) against a client started via `Scripts/live-stack.sh`; splice
  into `x2game_unpacked.dll` with `D:\aa-re\client12\rebuild_unpacked.py`; re-analyse in
  proj-aa12u. Unblocks RTTI/vtable naming for every task below. Optionally dump the other
  packed 1.2 DLLs (`crynetwork.dll`, `archeage.exe`) the same way.
- [ ] **7. Packet-layout recovery pass for the 108 typeless SC opcodes** (M)
  In proj-aa12u, locate each client handler (start from the SCOffsets dispatch table and
  the strings already extracted, e.g. `OnMouldList`, `RACING_RESULT`), and record field
  layouts in `rust/CLIENT-DB-GAP-AUDIT.md` §1.1 or a sibling notes file. No server code in
  this task — it de-risks tasks 5, 8, 10, 12, 15, 17 and the P3 triage.
- [ ] **8. Finish battlefields with the in-flight arena work** (M)
  Add `SCInstantGameStatePacket` (0x18d) + `SCInstantGameInviteTimeoutPacket` (0x18f)
  types, verify `SCInstantGameRemainTimePacket`'s unchecked offset against proj-aa12u and
  send it; load `game_score_rules` (35), `game_rule_events` (9), `battle_field_buffs` (11)
  instead of code/`battlefields.json` scoring; check the arena diff line-by-line against
  the mirrored-C#-bug list (double entry charge, corps cleanup keyed 0/1, `AddScore`
  hardcoding). Coordinate with the uncommitted `systems/instances/*` changes — one owner.
- [ ] **9. Quest blockers** (S–M)
  Fix or formally mirror-decide `QuestActObjZoneMonsterHunt` never-completes
  (`count > Count` vs capped objective); clear the 10 blocked/no-component quests and the
  43 missing-act-target cases from `quest_audit`; then sample-run the 622 needs-review
  quests live (static audit does not certify playability). Track in
  `Docs/wiki/Quest-Audit.md`.
- [ ] **10. Specialty / trade-pack completion** (M)
  Add `SCSpecialtyGoodsPacket`/`SCSpecialtyRecordsPacket` (0x09a–0x09b), load
  `specialty_bundles`, verify the price formula marked "TODO: Verify this formula"
  (`economy/bridges.rs:640`) against proj-aa12u, implement negotiation bonus and
  labor-by-skill-level, and add the missing `Specialty` config section (C# defaults are
  currently baked in).

## P2 — shipped rules the server ignores

- [ ] **11. CC/buff rules from data** (M): load `aoe_diminishings` (10),
  `buff_breakers` (661, client loader `LoadBuffBreakersRelation`), `buff_mount_skills`
  (64), `buff_unit_modifiers` (17) in `systems/skills/data.rs`; apply diminishing/break
  rules in the buff engine with tests per category.
- [ ] **12. NPC groups, linked aggro, interaction/friendship** (M–L): load
  `npc_aggro_links` (643) + `aggro_links` (130), `npc_groups`/`npc_group_members`
  (158/571), `ai_files` (15); add the typeless interaction packets
  (`SCNpcInteraction*`, `SCWorldInteraction*`, `SCNpcFriendship*`, 0x07c–0x083) using
  `npc_interactions`/`npc_interaction_sets` (114/111). Oracle: proj-aa12u loaders
  `LoadAggroLinkDescs`, `LoadAiFileDescs`.
- [ ] **13. Character limits from data** (S): `resurrection_waiting_times` (10) replaces
  hardcoded respawn waits; enforce `unit_attribute_limits` (21) and
  `character_p_stat_limits` (5); check `allow_to_equip_slots/slaves` against equip code.
- [ ] **14. Faction/family/expedition dead-packet cluster (~17)** (M): send the
  rename/immigrate/relation/owner/independence results, family title/name, sponsor —
  or prove from proj-aa12u that the 1.2 client never awaits them and delete the types.
  Includes wiring `CSChangeExpeditionSponsor`/`CSUpdateNationalTaxRate` only if a real
  1.2 opcode exists (today `0xfff`, unregistered — verify in proj-aa12u dispatch table).
- [ ] **15. Economy feedback packets** (M): send `SCCraftFailedPacket` and verify the four
  "TODO not verified" craft error pairs against proj-aa12u; add/send
  `SCItemDetailUpdated`, `SCItemAcquisition`, `SCItemTaskNotify`, `SCCraftItemUnlock`,
  `SCMateEquipmentExpired`, `SCMineAmount`, mail live updates
  (`SCMailReceiverOpened`/`SCMailRemoved`), `SCUnitLootingState`.
- [ ] **16. Housing live updates** (S–M): send `SCHouseBuildPayChanged`, `SCHouseFarm`,
  `SCHouseOwnerNameChanged`; enforce `housing_deco_limits` (12).
- [ ] **17. Racing / time trials** (L): implement from 1.2 data (`race_tracks` 2,
  `race_track_shapes` 14) + typeless `SCRaceCond/Racing/RacingResult` (0x1f6–0x1f8).
  No 10.x oracle (feature removed there) — proj-aa12u is the only reference
  (`X2Racing`, `RACING_STATE/CONDITION/TEXT`). Includes the racing ranking board
  deliberately excluded from `ranking_runtime`.
- [ ] **18. Real payment/premium state** (M): load `AccountPayment` instead of
  premium-until-2030 for everyone (`net/connection.rs:38`, `services/accounts.rs:168`);
  read `premium_benefits/configs/grades`; decide the AA-point wallet
  (`cash_shop.rs:944` "not implemented") and send `SCPremiumPointChanged`/
  `SCICSExchangeRatio` or gate them off in the fset (`AaPoint`, `BmMileage` bits).

## P3 — long tail and hygiene

- [ ] **19. Verify-before-building set** (S each): moulds (`SCMould*` packets exist in the
  client — `OnMouldTaken` — but `moulds*` tables are empty in 1.2 data; build only if
  authored content exists elsewhere), demo mode (tables empty — likely classify
  won't-fix), `SCGmDump*`/`SCDump*`/`SCAiDebug` (GM/debug; classify), indun
  channel-select + client-driven-indun packet flows (data already loaded, unused).
- [ ] **20. Remaining packet triage** (M, can split): UCC/emblem family
  (`SCEmblemUploaded/Downloaded`, `SCUcc*`, `SCDoodadUcc*`), gimmick joints
  (`SCGimmickResetJoints/Grasped`), movement/combat (`SCUnitMovements` — writer exists,
  test-only; `SCCastingDelayed`, `SCBuffUpdated`, `SCKnockBackUnit`), portals
  (`SCPortalDeleted/Used`, `SCNaviTeleport`, `SCAboxTeleport`), jury verdict count +
  bounty senders (types exist), `SCPlaytime`, `SCSearchList`, `SCCooldowns`,
  `SCAbilityExpChanged`/`SCSkillUpgraded`, scheduled-event started/ended (0x1ec–0x1ed).
  Close each as implemented or documented won't-fix so the 108+73 lists shrink to a
  deliberate remainder (feeds task 4's ratchet).
- [ ] **21. Items data verdicts** (M): per-table keep/load/ignore decision for
  `item_recipes` (2 816), `item_conv_*` (~923), socket limits (`item_sockets` +
  `item_socket_num/level_limits`), `item_proc_bindings`, `item_dyeings`,
  `item_open_papers`, `item_cap_scale_forbids`, `tagged_items`, `craft_packs`,
  `merchant_packs`, mate/slave equip-pack tables, `physical_enchant_abilities` —
  several may be client-UI mirrors; proj-aa12u loader names settle which are rules.
- [ ] **22. Chat/name data** (S): use or drop `allowed_name_chars`, `blocked_texts`,
  `chat_spam_rules`(+details), `replace_chat*`, `slash_commands/functions`.
- [ ] **23. Client-file/physics remainder** (M–L): terrain surface types (`cover.ctc`,
  today surface-id = −1), visareas/indoor geometry, terrain holes, wheeled-vehicle
  physics; reconcile `ZONE-PHYSICS-USAGE.md` §2.7/geodata gaps with current code
  (brush/vegetation loading already landed — the doc is stale).
- [ ] **24. Mirrored-bug re-decisions** (S each): trade completes one-sided/whole-stack,
  loot dice lowest-roll, stacked-item DB-row duplication, auction ExpireDate sort,
  mail count always 0 at select, double visibility packets. Each: keep mirrored
  (document why) or fix with a regression test; list lives in
  `PORTING.md` "C# bugs noticed".
- [ ] **25. Schema and doc hygiene** (S): drop or use dead MySQL tables `cash_shop_item`
  and `mails_items` (`SQL/updates/…` + base files); mark `CLIENT-FEATURE-GAPS.md`
  superseded by the Oct 14 audit; refresh stale headers
  (`achievement-hook-audit.txt` "untracked WIP", `db_character.rs:602` "later phase"
  comment once task 1 lands); login limits (`MAX_CHARACTERS_PER_ACCOUNT = 6` etc.)
  become config.
