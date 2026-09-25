# GF-W12 faction-scoring metadata groundwork

## Scope

This slice adds **read-only typed catalogs** for faction competitions and zone scores plus packet
shape tests. It intentionally does **not** implement scoring, ranking, rewards, persistence, level
transitions, packet senders, or a World-to-Zone state bridge.

The loader is [FactionScoringGameData.cs](../AAEmu.Game/GameData/FactionScoringGameData.cs). It is a
normal `IGameDataLoader`, so a missing or malformed relationship fails during game-data startup
instead of being converted to a zero/default value.

## Shipped content covered

The shipped 10.0.2.13 compact catalog contains:

| Table | Rows | Catalog role |
|---|---:|---|
| `enum_faction_competition_reset_state_kinds` | 3 | named reset-state catalog |
| `faction_competitions` | 8 | point rules, required points, detail and reset metadata |
| `faction_competition_npc_infos` | 108 | competition → NPC links |
| `faction_competition_quest_infos` | 14 | competition → quest-context links |
| `zone_score_contents` | 9 | zone-group score containers |
| `zone_score_kinds` | 16 | score kind/UI/reset metadata |
| `zone_score_levels` | 82 | level thresholds, including the shipped level-zero base rows |
| `zone_score_kind_rank_details` | 2 | kind → rank-detail links |

The two duplicate competition/NPC link rows are retained as separate link rows; the loader does not
silently collapse content rows. The loader validates competition parents, NPC rows, quest-context
rows, zone-score parents, kind levels, level-number uniqueness, and non-zero buff references. Buff
id `0` remains the shipped “no buff” value; `faction_competitions.force_stop_tower_def_id` keeps
both shipped null and zero forms rather than normalizing them; `zone_score_contents.quest_id` is
preserved without inventing a quest-table relationship that the compact schema does not define here.

## Packet contracts pinned

The tests in
[FactionScoringPacketContractTests.cs](../AAEmu.UnitTests/Game/Core/Packets/G2C/FactionScoringPacketContractTests.cs)
pin the existing SC contracts and the previously missing list shape:

| Packet | Opcode | Body |
|---|---:|---|
| `SCFactionCompetitionUpdatePointPacket` | `0x338` | `ushort kind`, `uint kindId`, `s32 pointDelta` |
| `SCZoneScoreListPacket` | `0x34F` | `s32 count`, then `count × { uint kind, s32 score }`; this metadata-only packet imposes no list bound |
| `SCZoneScoreUpdatePacket` | `0x350` | `uint kind`, `s32 scoreDelta` |
| `SCZoneScoreResetPacket` | `0x351` | `uint kind` |

These are contract-only classes. No gameplay code constructs or sends them yet.

## Runtime work still blocked

The following remain deliberately unimplemented until their ownership and evidence are settled:

- which side owns faction competition points and zone-score state under Zone Authority;
- the meaning and source of the two field selectors in the faction-competition update packet;
- when NPC kills and quest completions contribute, and how a winner/reset state is selected;
- level-threshold application, reward delivery, and restart persistence;
- the World↔Zone bridge and any state snapshot/replay contract;
- the special-effect paths for faction-competition point changes and zone-score changes;
- a live client trace proving the update/list/reset packets reach the intended UI.

Until those questions are answered, the catalogs are metadata only. They must not be used to award
points, advance levels, or claim that scoring/persistence is complete.
