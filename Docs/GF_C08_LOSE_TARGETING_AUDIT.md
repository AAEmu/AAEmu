# GF-C08 LoseTargeting — content audit and runtime target selection

## Content audit

The content slice validates the typed `special_effects` type-146 rows and records which content link
tables name them. `LoseTargetingContentAudit.Inspect` runs after skill content loading and emits one
bounded summary; duplicate rows, null required fields, or a link naming an unknown type-146 row fail
loudly. A count discrepancy is reported and left visible.

## Reviewed matrix

| Measure | Reviewed baseline | Meaning |
| --- | ---: | --- |
| Content rows | 65 | `special_effects` rows whose typed type is `LoseTargetingTheTarget` |
| Linked rows | 50 | Server-side content links in the reviewed matrix |
| Unlinked gap | 15 | Content rows with no reviewed server-side link |
| Link sources | skill, buff trigger, buff tick | The server loaders that can resolve an effect id |
| Plot links | reported separately | Plot ownership is not treated as a World target path |

A local re-audit of the available compact content produced 65 type-146 rows and 48 server-linked rows
under the documented `effects`-mapping join, with plot-effect rows as a separate source covering all 65.
The available content therefore does not reproduce the reviewed 50-linked figure; the discrepancy is
recorded rather than silently normalized, and the link definition still has to be reconciled.

## Runtime target selection

`LoseTargetingTheTarget` does two separate things to the unit it lands on:

1. it drops that unit's own current target, and
2. it drops the target of every unit that can no longer **hold it** — the reversed lookup.

The reversed half is what the content decides. Of the 65 type-146 rows, 6 are skill effects (the
self-cast Mirror Warp family, `target_type_id` 0 with a single-target area) and only exercise (1). The
other 42 are buff-trigger rows, and their own buffs say who may not keep targeting their owner:

| Buff targeting columns on the owner | Type-146 rows | Reversed selection |
| --- | ---: | --- |
| `impossible_targeting` set, `targeting_relation_id` hostile | 23 | narrowed to the hostile holders |
| `impossible_targeting` set, `targeting_relation_id` others | 2 | narrowed to the other holders |
| `impossible_targeting` set, `targeting_relation_id` unset | 1 | every holder |
| `impossible_targeting` clear | 16 | empty — nothing is barred |

Two of the 23 hostile rows also set `targeting_use_origin_source`, so their relation is measured from
the buff's caster rather than from the owner. That is the second axis of the filter.

So 26 of the 65 rows select a narrowed reversed target set, 6 clear the caster's own target only, and
33 select nobody. `LoseTargetingRules` reads those columns off the owner's active buffs at cast time;
it never widens to "everybody currently targeting it", because a relation id this build cannot read is
logged and skipped rather than treated as the widest bar.

`impossible_change_targeting` is deliberately not read. It restricts the affected unit's own ability to
change target, which is what (1) already does, and it says nothing about who else may hold it.

`value1` is 4 on 59 rows and 0 on the other six, and `value2` is 1 on three. Neither correlates with
the buff's targeting columns, so neither is read.

## Under zone authority

The World mirror is not authoritative for targeting under `WorldIntegration.ZoneAuthority`. Each clear
is relayed to the Zone as a zero target id and the World mirror is left to the Zone's response, exactly
as the direct half has always done. The reversed enumeration is scoped to the units the World instance
holds; a unit in no known instance has no enumerable holders and only its own target is cleared.

## Tests

`AAEmu.UnitTests/Game/Models/Game/Skills/Effects/LoseTargetingRulesTests.cs` covers the catalog
resolution, the narrowed selection with holders that must survive it, the origin-source axis, the
world-instance enumeration, and the zone relay. `LoseTargetingContentAuditTests.cs` covers the content
matrix and its failure cases.
