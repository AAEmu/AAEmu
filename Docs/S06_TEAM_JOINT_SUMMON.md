# S06 — Team joint, break, and summon consent

Ledger item: *"Implement team joint/break and summon/reply. Team joint/summon request families are
parsed but not stateful. Depends Existing Team/Squad core. Done when: Authorized teams join/break/summon
with correct membership, position and cleanup."*

Everything below lives in `AAEmu.Game` (World side). Team is a World↔Client social structure, so no
zone relay and no Server change is involved.

## What the client actually does

Read from the extracted client UI and the packet schema before writing any handler:

| Client behaviour |
|---|
| `X2Team:JointInfoReq(TEAM_JOINT_MENU_CHAT / MENU_TARGET, name)` is an **info query**: the server answers with the **same** mode and the target team id, and the joint menu opens from that reply. Only mode 3 opens the *request* frame |
| The request frame collects a `leader` choice and sends `X2Team:JointOk(leader)` |
| The *response* frame has no role choice; it echoes the server's `leader` flag back through `JointOk(leader)` / `JointCancel(leader, timeout)` |
| Response frame reads `name`, `memberCount`, `leader`; `leader` decides crown_gold/officer and the role text |
| `SCTeamJointInfoPacket.mode` accepts **1, 2, 3, 4**: 1/2 are the client's menu modes, 3 opens the request frame, 4 opens the response frame (`TeamJointModes`) |
| The `19` next to `TEAM_JOINT_RESPONSE` and the `17` next to `TEAM_JOINT_BROKEN` in the client's event table are the **lengths of those names**, not modes |
| Summon button is owner-only, shows the item cost, then calls `X2Team:RequestSummon()` with no arguments |
| Summon target dialog: 60 s timer, accept/reject, "do not receive" checkbox, combat blocks accept client-side, close sends `RequestSummonReply(result, name)` |
| Summon button is hidden entirely when the `block_joint_raid` feature bit is set |

## Implemented

**Joint handshake.** `CSTeamJointInfoPacket` (0x0C3) → `TeamJointManager.RequestJointInfo`:
rejects any mode outside 1–4 and any mode that is not a client request mode, authorizes the
requester as a raid owner/officer, resolves the named target in this World, refuses a party target /
a self-team / an already-jointed or already-pending team, and checks the combined member count
against `Team.RaidMemberLimit`.

The two menu modes are **info queries**, not handshake steps. Mode 1/2 is answered with
`SCTeamJointInfoPacket` (0x120) carrying the **same** mode and the target team id, which is what
opens the client's joint menu; a target that cannot be joined is answered with team id 0 so the
menu still renders. Only **mode 3** opens the request frame and leaves a pending round behind, and
it answers in mode 3.

`CSTeamJointPacket` (0x0C4) carries the two-step answer. The requesting side records its leader
choice and forwards `SCTeamJointInfoPacket` in **mode 4** to the other team's owner; the second
answer must echo the same leader flag (the client has no way to change it, so a mismatch is a
protocol error, not a legitimate decline) and must repeat the same type token. On the second accept
the two Teams are federated — they stay independent Team objects — and each member gets a fresh
`SCJoinedTeamPacket` plus `SCTeamJointPacket` (0x121) carrying the leader team id and its joint order.

**Break.** `CSTeamJointBreakPacket` (0x0C5) is the ask/answer pair. Only a joint leader that is
also the raid owner/officer may ask; the peer team must answer; accept dissolves the roster and
re-publishes both headers with cleared joint fields, decline answers the asker. A disbanded team
dissolves the joint through the same path and is skipped in the fan-out, so a disband does not send
a break notice to the team that is going away.

**Summon consent — exactly once per round.** `CSTeamSummonGetPacket` (0x0C6, no body) is
owner-of-a-raid only. Eligible online team members (not the caller, not already pending) receive
`SCTeamSummonSuggestPacket` (0x125) with the summoner name, team id, zone and position; the summoner
receives `SCTeamSummonGetPacket` (0x123) with the id list. `CSTeamSummonReplyPacket` (0x0C7) must
match the pending summoner name. Both answers **consume the round atomically before acting**: a
duplicated accept finds nothing left to consume, is refused, and cannot emit a second
`SCTeamSummonPacket` (0x124). Accept also re-validates that both parties are still online and that
the recipient is not in combat.

**Cleanup.** Every path that can orphan joint state releases it:
`TeamManager.SetOffline` (the `IsOnline → false` transition every despawn shares),
`TeamManager.DisbandTeam`, `GameConnection.OnDisconnect` (hard disconnect) and
`EnterWorldManager.LeaveWorldTask` (orderly leave). All of them clear the pending joint requests,
summon rounds and break asks owned by the character, and a disband additionally dissolves the
session. See *Session lifetime* below for the one deliberate exception.

**Team header.** `Team` now carries real `JointId` / `IsJointLeader` / `JointOrder` and serializes
them; before this they were hardcoded zeros with a comment saying the client desyncs.

## Capacity and authorization

- Member cap is `Team.RaidMemberLimit` (the existing raid header constant), checked as
  `first <= limit && second <= limit - first`. No new number is introduced.
- Party teams cannot joint; owners and raid officers can. A break additionally requires
  `IsJointLeader`.
- The summon round is owner-only, matching the client button.
- A joint can only ever hold two raids here. The client's raid tab and `GetMyTeamJointOrder()` are
  built for a federation, but no packet in this family carries a "add a third team" operation, so a
  larger federation is out of scope for the evidence available.

## Session lifetime and ownership

A character's logout clears **only the pending state that character owns**: the joint requests it
raised as its raid's owner/officer, the summon rounds it opened or was the target of, and the break
asks it raised. A plain member logging off does **not** cancel a request or break ask that an online
owner or officer is still working on — that work belongs to whoever started it. Pending state is
therefore keyed by the owning *character*, not by the team, so one member's disconnect cannot
cancel another member's handshake.

A live joint **session** is dissolved only when the team goes away, not when any member logs out:
`TeamManager` hands a raid over to another online member when its owner disconnects, and a plain
member's alt logging off must not tear a 50-player federation down. The two cases that *do* dissolve
the session are the last online owner of a member team disappearing, and the team being disbanded
(which is the hook `OnTeamDisbanded` covers, including the crash path where the team is torn down
without an orderly leave). A disbanded team clears every request that *names* it regardless of who
raised it, because that request can no longer complete.

**The hook is covered by an integration test.** `TeamJointSetOfflineIntegrationTests` builds a real
`TeamManager`, a real `Team` and real `Character` objects wired to a recording session, swaps the
private singleton slots the `IsOnline` setter reaches through, and then flips `IsOnline = false` —
proving `Character.IsOnline → TeamManager.SetOffline → TeamJointManager.OnCharacterLogout` actually
runs in production rather than only when the manager is called by hand. Deleting the hook from
`TeamManager.SetOffline` makes both of those tests fail (verified by mutation).

## Not implemented, and why

- **Item cost for summon.** `X2Team:GetSummonItem()` returns the item the client shows in the
  confirmation dialog, and `ItemTaskType` 145 (`team-summon`) is a real client task name. The
  shipped content has no team-summon item row: `content_configs` only has `expedition_summon_item`
  (id 112 → 52128) and `expedition_summon_role` (365 → 3), and no item uses skill 39700
  (`공격대원 소환 시도`) as `use_skill_id`. The one shipped item, 52128, describes summoning
  *expedition* members. No item is therefore debited here; inventing a mapping would violate the
  no-hardcoded-content rule. The `ItemTaskType` 145 name is available for whoever adds the cost once
  a content-backed row exists.
- **Teleport on accepted summon.** Special effect 171 (`TeamSummon`, skill 39700, one row with all
  values zero) has no action class, and `SpecialEffectOwnershipRules` marks it unsupported. The
  accept path stops at `SCTeamSummonPacket`, exactly like the expedition consent phase; the landing
  needs the special-effect class and a teleport reason, neither of which is evidence-backed yet.
- **`SCTeamSummonGetPacket.type` meaning.** The field is a u32 and the client dialog ignores it;
  the team id is sent. The client reads only name, zone id and position.
- **`SCTeamJointPacket.packetMode`.** This packet has its own mode table, not the
  `SCTeamJointInfoPacket` numbering. Only **mode 1** ("set") makes a member's client store the
  joint; any other mode takes the relay branch, echoes a joint-info packet back to the server and
  leaves the request refused, so the joint would exist only server-side. A committed joint therefore
  goes out as `PacketModeSet` (1) and a refused one as the same mode with `targetTeamId` 0.
- **Target-context-menu request path (mode 2).** The client invokes that menu with an *empty* name
  and this packet carries no target unit id, so there is nothing to resolve it against. The server
  therefore falls back to the requester's current selection and, failing that, answers the query with
  a zero team id rather than opening a request frame. The chat-menu path (mode 1) carries the name
  and needs no fallback.
- **Three-or-more-raid federation** — see *Session lifetime and ownership*.

## Tests

`AAEmu.UnitTests` — full suite, 6664 passing:

- `TeamJointFlowTests` — the whole stateful slice against an in-memory `ITeamJointContext` and a
  controllable clock: request→prompt→commit for both leader roles, leader-flag mismatch, wrong type
  token, decline and timeout, expiry, all four known wire modes plus rejection of server-only and
  out-of-range modes, **mode 1/2 answered as an info query with the same mode and no pending round**
  (including a target that cannot be joined answered with team id 0), **every member receiving the
  commit with the storing mode**, **a refusal carrying the storing mode with a zero target team id**,
  **an expired break ask telling the asker the round lapsed**, target-menu/empty-name resolution
  through the requester's selection, foreign world, officer authorization, party
  and self targets, over-capacity; break ask/accept/decline and leader-only authority; disband
  cleanup including the skipped-team fan-out; disconnect cleanup for a member, for an owner whose
  team survives, and for the last online owner; **ownership of pending state** — a plain member's
  logout leaves the owner's request intact, the requesting owner's own logout cancels it, the joint
  leader's own logout retracts their break ask while a peer's logout does not, and a recipient's
  logout closes the summon round addressed to them; summon suggest/accept/reject/duplicate-accept/
  name-mismatch/combat/offline-summoner/expiry/no-double-open.
- `TeamJointSetOfflineIntegrationTests` — the real `Character.IsOnline = false` → `TeamManager.SetOffline`
  → joint-manager path, built from real `TeamManager`/`Team`/`Character` objects and singleton slots.
- `SCTeamJointSummonPacketWireTests` — every new G2C packet's field order and width, all four joint
  modes on the wire, and the storing `packetMode` byte pinned as the literal 1.
- `CSTeamJointSummonPacketReadTests` — every S06 C2G packet's parse, including the empty body.
- `TeamJointRosterTests` — roster ordering, duplicate/over-capacity/non-leader-first refusal, follower
  removal re-indexing, leader removal refusal, authorization/capacity rules, and the `Team` header
  carrying joint state in native order.
- `TeamJointManagerTests` — unknown-character entry points are inert, `GetSession` is null until a
  federation exists, and the joint capacity comes from `Team.RaidMemberLimit`.
