# GF-C08 LoseTargeting content audit

## Scope

This slice is deliberately **audit-only**. It validates the typed `special_effects` type-146 rows and
records which content link tables name them. It does not enumerate units, choose a target, interpret
`value1`/`value2`, or change `LoseTargetingTheTarget` runtime behavior.

The target-selection and relation-filter semantics required to complete GF-C08 remain unproven. The
existing action therefore stays limited to the unit on which the effect lands.

## Reviewed matrix

| Measure | Reviewed baseline | Meaning |
| --- | ---: | --- |
| Content rows | 65 | `special_effects` rows whose typed type is `LoseTargetingTheTarget` |
| Linked rows | 50 | Server-side content links in the reviewed matrix |
| Unlinked gap | 15 | Content rows with no reviewed server-side link |
| Link sources | skill, buff trigger, buff tick | The server loaders that can resolve an effect id |
| Plot links | reported separately | Plot ownership is not treated as a World target path |

The checked-in unit test uses this matrix as a test oracle. It fails when the content or link counts
move, including when a new orphan increases the unlinked gap. A change to the matrix requires an
explicit audit update; it is not a fallback for runtime behavior.

## Local re-audit note

A read-only re-audit of the available compact content produced 65 type-146 rows. Under the documented
`effects`-mapping join for server links it produced 48 server-linked rows; plot-effect rows were kept as
a separate source and the union covered all 65 rows. The available content therefore does not yet
reproduce the reviewed 50-linked figure. This discrepancy is recorded rather than silently normalized.
It is a reason to keep the runtime slice audit-only and to reconcile the link definition before any
target-enumeration work is attempted.

## Diagnostics

`LoseTargetingContentAudit.Inspect` is called after skill content loading. It emits one bounded summary
with content, server-link, all-link, unlinked, and per-source counts. Structural problems—duplicate
rows, null required fields, or a link naming an unknown type-146 row—fail loudly. A count discrepancy
is reported and left visible; it is not converted into invented target semantics.

The audit matrix and the failure cases live in
`AAEmu.UnitTests/Game/Models/Game/Skills/Effects/LoseTargetingContentAuditTests.cs`.
