# r575 UnitState buff snapshot evidence

## Client consumer

The client applies buffs from the unit-state snapshot without emitting a separate
create message per entry. The decoded snapshot carries three buff lists —
**Good**, **Bad** and **Hidden** — each stored as a count/entry pair. On
`OnUnitState` the client initialises the unit's buff manager from those three
lists, preserving the instance index and the buff template id for every entry.

Buffs the client resolves from a template descriptor are applied during that
initialisation; the rest are carried as inactive entries. The practical
consequence for this server is that a buff delivered inside a unit-state
snapshot is already active on the client by the time the snapshot is processed,
so a matching create message is neither required nor observed.

The available client log reports `OnUnitState - id(...), name(...), type(...),
pos(...)`; the snapshot copy path itself is silent. A World send log is not a
client acknowledgement. The evidence for individual snapshot entries is the
copy path and direct read-only observation of the running client.

## Relay ownership regression

Snapshot serialization still preserves Zone-authored buffs. Recording them in
World's outgoing relay registry would incorrectly grant World permission to
send updates/removes for effects owned by Zone. `MarkSnapshot` therefore skips
Zone-authored entries and does not set their `RelayedToZone` flag.

When an entry expires during snapshot replacement, the cleanup uses the same
`BuffCreatedWire.ShouldRelayRemoved` gate as normal expiration. In particular,
even a Zone-authored entry with `RelayedToZone` already set is not echoed back.

The focused tests cover mixed ownership, expiry after serialization, an active
buff, an unrelayed buff, an invalid owner, list caps/passive filtering,
and registry reset/instance isolation. Restoring the previous two behaviors
while retaining the same test seam produces four failures out of six cases;
the fix passes all six. The branch passes the full unit suite, a Release
solution build, and the Game script compiler check.

The pre-existing old-zone cleanup issue in `LeaveZone`/`HandoffOnZoneChange`
is outside this change.

## Live r575 check, 2026-09-18

The corrected code was built and deployed in the integrated local stack and the
full unit suite passed. The native process loaded only `w_solzreed_1`, zone 142,
instance 0. A real r575 client entered with Dannia; the sampled client object was
1066, buff index 8. Read-only process memory sampling ran every 100 ms, with no
injection or native binary changes.

| Time (UTC) | Observation |
| --- | --- |
| 19:04:19 | Client `OnUnitState` for object 1066; World logs its UnitState send. |
| 19:04:19.792133 | Client manager contains template 2423, index 8; restriction counter is 1. |
| 19:04:32.993643 | Last sample containing 2423/index 8; counter remains 1. |
| 19:04:33 | World logs `WZBuffRemoved`, target 1066, buffIndex 8. |
| 19:04:33.094188 | Same client unit no longer contains 2423/index 8; counter is 0. |

The client log uses local time (UTC-03:00):

```text
<16:04:19> [C4ED2400] OnUnitState - id(1066), name(Dannia), type(0), pos(13757.10, 14622.20, 110.96)
```

The World log uses UTC:

```text
19:04:19 [INFO] PlayerEnterService - WZUnitState enter → zoneId=142 ip=172.18.0.1 bcHint=1066 bodyLen=3131
19:04:33 [INFO] Program - WZBuffRemoved → zone zoneId=142 target=1066 buffIndex=8
```

For completeness, World logged a separate Create for index 8 at 19:04:13,
**before** the client `OnUnitState`; it logged no Create/replay for that index
between `OnUnitState` and removal. Thus the log alone should not be described as
proof of an exclusively snapshot-sourced buff. The consumer trace establishes
snapshot loading; the memory trace verifies the actual buff lifecycle and
cleared restriction in the running client. The rare Zone-authored expiration
race is covered by the automated regression, not claimed as a manually
reproduced timing race.
