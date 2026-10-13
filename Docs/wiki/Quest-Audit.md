# Quest Technical Audit

- Audience: Contributors and testers
- Last verified against: `rust-rewrite` working tree on October 13, 2026
- Target: ArcheAge 1.2 (`r208022`)

## Result and scope

Every quest record in the available `compact.sqlite3` is accounted for. The quest
subsystem cannot yet be certified as fully working. Port coverage, successful data
loading, and synthetic lifecycle tests do not prove that every authored quest can
be accepted, progressed, rewarded, and resumed in the real client.

The audit covers the loader, act handlers, state machine, packets, event bridges,
requirements, timers, reward/cleanup paths, persistence, and quest sphere code.
The reproducible JSON ledger lists every quest ID, component, base act, detail,
requirement, starter item, and finding. It distinguishes deliberately excluded
tutorials, auxiliary contexts, content records with structural failures, records requiring review, and
records with only static checks. No record is marked as passed through live play.

Database SHA-256:
`ed2921ff434af5279ada601bc95ddc132121eb40edf623ee77b9b1c72fe32025`.

## Data accounting

| Records | Total | Loaded | Other disposition |
| --- | ---: | ---: | --- |
| Quest contexts | 4,876 | 4,775 | 101 category-45 tutorials intentionally excluded |
| Components | 17,851 | 17,720 | 15 tutorial components; 116 referencing absent contexts |
| Base acts | 26,886 | 19,047 | 33 tutorial acts; 7,607 referencing absent components; 199 under orphan components |
| Detail acts | 26,886 | 19,047 | Remaining rows belong to excluded/orphan base acts |

All 19,047 loaded base acts have matching detail rows. There are no duplicated
live detail links or objective indices outside the five wire slots. The loader
reads **64 detail tables**, and loaded quests use **47 act types**. The additional
`quest_act_obj_aliases` table is presentation metadata, not another act handler.
The ten unimplemented act variants in `acts.rs` have **zero uses in loaded quests**;
their presence alone does not explain the active failures.

Only 4,656 loaded records have a Start step. The 119 others are explicitly
accounted for: 82 authored reserved tasks (2148–2229), 21 category-28 tutorial
reward/support definitions, and six named skill/doodad/unit-requirement dummies or
unused contexts (315, 1728, 2046, 1576, 1830, 1831). These **109 auxiliary
contexts** are not standalone player quests. The remaining ten contexts have no
components and still need content evidence: **1391, 3748, 3750–3757**. No Start
components or quest areas were invented for them.

The audit also accounts for all 367 item starter bindings. Sixteen bindings refer
to missing quest contexts. Separately, 43 loaded quests contain 113 references to
missing NPC/item records, and 13 contain a requirement referring to an absent quest
(including one negative requirement). These are review findings: an absent optional
branch/reward and an unavoidable missing objective have different consequences.

The actual read-only `D:/AA1.2/game_pak` contains 2,453 quest sign spheres: 2,216
match loaded components and quest IDs, 229 reference components that are excluded
or absent, and eight reference a loaded component under a different quest ID.
There is no matching sign sphere for 441 loaded sphere acts across 439 quests
(420 accept-sphere acts, 20 objective-sphere acts, and CheckSphere). Absence of an
accept sign does not prove that a client-driven starter cannot work. These are
explicit geometry review findings; the affected IDs and coordinates are in the
merged ledger. The NPC group membership data also contains 38 missing NPC references.

## Confirmed defects fixed during this audit

| Change | Quests using the affected feature | Regression evidence |
| --- | ---: | --- |
| Timer expiration and step-change handlers now check the event's quest ID | 44 | Completing another quest preserves the timer; another timer event does not fail this quest |
| NPC reporting requires an explicit report instead of merely targeting the NPC | 2,749 | Full lifecycle retains the giver as the target and remains Ready until reporting |
| NPC/doodad reports apply only to the requested quest | 2,749 / 15 | Two ready quests at the same object; reporting one leaves the other ready |
| Doodad reporting retains the selected reward and unsubscribes on finalization | 15 | Reward selection is retained; finalized report subscriptions are removed |
| OR requirements require a matching alternative | 37 with component OR requirements | Both failed alternatives reject; either successful alternative accepts; AND and empty lists checked |

Counts describe feature exposure, not a claim that every affected quest previously
failed on every route. Component and sphere requirements share the corrected OR
implementation. Packet layouts and opcodes are unchanged.

## Follow-up fixes and remaining content

The follow-up fixes preserve Unix deadlines through MySQL serialization, restore
timers from earlier components, and fail quests whose deadline passed while
offline. Kill credit now uses the NPC victim and the authored aggro threshold.
CheckSphere tracks local presence without writing wire objective slot 255.
Completion cleanup runs once; ability and prerequisite-quest objectives subscribe
to their events; zone kills enforce their authored zone. Invalid acceptors are
rejected before activation, failed starts release their ID, and forced re-add
clears the completion flag.

Quest components now execute authored NPC follow/path/command/spawn behavior
after releasing the character lock. Guarded NPC death fails the active escort.
Sphere trigger additions are deduplicated, pending additions are removed on
cleanup, all starters see the same prior position, and authored sphere flags use
the text boolean loader.

The merged ledger marks **29 distinct records** as content blocked: the ten
contexts without components above, plus **19 quests containing 21 sphere acts**
without matching geometry in the available 1.2 client pack: **1012, 1024, 1033,
1039, 1372, 1392, 1398, 1400, 1650, 1897, 5272, 5439, 5716, 5898, 5979, 6039,
6050, 6213, 6216**. Missing accept-sphere geometry remains review evidence because
some acceptance is client driven. Quest 1033 also references q1033_ande, which
is absent from the available runtime's 13 path files.

Other reviews remain explicit in the ledger: missing item/NPC/prerequisite
references, action/phase-specific doodad interactions, component acceptance
semantics, requirements that currently pass without checks (including Dominion
and Nation requirements), and authored NPC paths/actions requiring content and
live verification. The NPC action fields are wired; this does not certify all
32 affected quests. Neither static accounting nor these fixes prove every quest
playable.

## Follow-up validation

The targeted regressions cover timed reload and offline expiry, victim aggro
credit, CheckSphere entry/exit, wrong acceptors, escort failure, single cleanup,
full-bag reward mail, and quest/mail item ownership. Mail, auction and player
wallet persistence are also covered by the [Mail Technical Audit](Mail-Audit).
See that report for the real-client and private MySQL evidence and limits.

## Reproduce and inspect

Run from the repository root:

```powershell
python rust/tools/quest_audit/audit.py --db rust/crates/aaemu-game/app/Data/compact.sqlite3 --output D:/aaemu-audits/quests.json
python -m unittest discover -s rust/tools/quest_audit -v
```

Run from `rust/` to verify actual loader accounting and optional client geometry:

```powershell
$env:AAEMU_COMPACT_DB = 'C:/Users/messe/Documents/GitHub/AAEmu-rust-1.2/rust/crates/aaemu-game/app/Data/compact.sqlite3'
$env:AAEMU_GAME_PAK = 'D:/AA1.2/game_pak'
$env:AAEMU_QUEST_SPHERE_AUDIT = 'D:/aaemu-audits/quests-spheres.json'
cargo test -p aaemu-game --lib real_compact_db_quest -- --ignored --nocapture
cargo xtask ci
```

The Python command can merge the exported geometry using
`--spheres D:/aaemu-audits/quests-spheres.json`. The database and client pack are
opened read-only. Generated ledgers, binaries, client assets, and local configs
are not repository content.
Behavior finding rules reflect this source review and must be updated alongside
future engine fixes; they do not execute quest lifecycles.

Audit artifacts on this machine: `D:/aaemu-audits/quests-after-fixes-2026-10-13.json` (complete
ledger), `quests-spheres-2026-10-13.json` (client geometry),
`quest-mail-real-data-verified.log` (real-data checks), and
`quest-mail-ci-verified.log` (workspace checks), all in `D:/aaemu-audits/`.

## Validation limits and next steps

Final cargo xtask ci passes formatting, Clippy with warnings denied, and workspace
tests: **2,857 passed, zero failed, 118 ignored**. The follow-up has **81 quest**
and **187 economy** passing regressions. The private
MySQL rollback/retry/fresh-reload regression passes, including objective progress,
absolute deadlines, offline expiry, mail claims and auction escrow. Five Python
audit-tool tests pass. The full final workspace checks are recorded in
D:/aaemu-audits/quest-mail-ci-verified.log.

The real client read and claimed a mail fixture, then reconnected with an empty
inbox, the original claimed item and exactly one wallet credit persisted. Timed
quest 350 retained its original deadline through reconnect. With a past saved
deadline, the final client/server build immediately displayed and saved Failed. The
[Mail Technical Audit](Mail-Audit) describes fixture setup, evidence and limits.

The quest suite and accounting checks establish representative packet/engine
behavior and exhaustive data disposition. The old real-data test merely
constructs each quest and calls start_quest; it does not execute each objective.
Several legacy tests require AAEMU_QUEST_LEGACY_TESTS, so an ordinary ignored-test
run is insufficient evidence for their behavior.

The 29 content blockers require missing authored components/geometry. Further
certification needs per-quest lifecycles against actual spawn/world data, daily
reset, dialogue, selective rewards, cinematics and spatial objectives. The
remaining ledger reviews are not certified by the representative tests above.
This audit does not include a live playthrough of all quests.

## Related

- [Developer Notes](Developer-Notes)
- [Components](Components)
- [Code Terminology](Code-Terminology)
