# r575 UnitState buff snapshot evidence

## Native consumer

The reference is the x64 `x2game-dev_dedicate.dll`, SHA-256
`8936ce897d7610d2d4e0a27be9cc97708930c33e4cb910c03d17f23088a4891a`,
image base `0x39000000`. All addresses below are RVAs.

The native `OnUnitState` handler (`0x36b560`) passes the decoded buff block
through `0x36b3c0` -> `0x32bde0` (character path) -> `0x360590` -> `0x35d260`.
The last function initializes the unit's buff manager with
`0x44f9b0(unit + 0x86a0, unit, snapshotBuffs)`.

That loader reads three counts at offsets `0`, `0xb08`, and `0x11f0` in the
decoded snapshot structure, and copies their entries into the native manager:

| List | Manager count offset | Manager entries offset |
| --- | --- | --- |
| Good | `0xa0` | `0xa8` |
| Bad | `0xca8` | `0xcb0` |
| Hidden | `0x1430` | `0x1438` |

The copy preserves the instance index and buff template ID, advancing `0x58`
bytes per source entry and `0x60` per manager entry. These are **decoded native
structure offsets**, not serialized packet offsets. The initializer then calls
`0x450870`, which iterates the copied entries via `0x450a70`, resolves each
template through `0xabf310`, and calls `0x44feb0` for entries with a descriptor.
This is the native path by which the unit acquires snapshot buffs without a
separate `WZBuffCreated` for each entry.

The available native log reports `OnUnitState - id(...), name(...), type(...),
pos(...)`; the snapshot copy function itself has no `X2Log` call. A World send
log is not a native registration acknowledgement. The evidence for individual
snapshot entries is the native copy path and direct read-only memory samples.

## Relay ownership regression

Snapshot serialization still preserves Zone-authored buffs. Recording them in
World's outgoing relay registry would incorrectly grant World permission to
send updates/removes for effects owned by Zone. `MarkSnapshot` therefore skips
Zone-authored entries and does not set their `RelayedToZone` flag.

When an entry expires during snapshot replacement, the cleanup uses the same
`BuffCreatedWire.ShouldRelayRemoved` gate as normal expiration. In particular,
even a Zone-authored entry with `RelayedToZone` already set is not echoed back.

The focused tests cover mixed ownership, expiry after serialization, an active
buff, an unrelayed buff, an invalid owner, native list caps/passive filtering,
and registry reset/instance isolation. Restoring the previous two behaviors
while retaining the same test seam produces four failures out of six cases;
the fix passes all six. The independent PR branch passes all 4,604 unit tests,
a Release solution build, and the Game script compiler check.

The pre-existing old-zone cleanup issue in `LeaveZone`/`HandoffOnZoneChange`
is outside this change.

## Live r575 check, 2026-09-18

The corrected code was also built and deployed in the integrated local fork
(5,464 unit tests passed). The native process loaded only `w_solzreed_1`, zone
142, instance 0. Its module hash matches the reference above. The real r575
client entered with Dannia; the sampled native object was 1066, buff index 8.
Read-only process memory sampling ran every 100 ms, with no injection or native
binary changes.

| Time (UTC) | Observation |
| --- | --- |
| 19:04:19 | Native `OnUnitState` for object 1066; World logs its UnitState send. |
| 19:04:19.792133 | Native manager contains template 2423, index 8; restriction counter is 1. |
| 19:04:32.993643 | Last sample containing 2423/index 8; counter remains 1. |
| 19:04:33 | World logs `WZBuffRemoved`, target 1066, buffIndex 8. |
| 19:04:33.094188 | Same native unit no longer contains 2423/index 8; counter is 0. |

The native log uses local time (UTC-03:00):

```text
<16:04:19> [C4ED2400] OnUnitState - id(1066), name(Dannia), type(0), pos(13757.10, 14622.20, 110.96)
```

The World log uses UTC:

```text
19:04:19 [INFO] PlayerEnterService - WZUnitState enter → zoneId=142 ip=172.18.0.1 bcHint=1066 bodyLen=3131
19:04:33 [INFO] Program - WZBuffRemoved → zone zoneId=142 target=1066 buffIndex=8
```

For completeness, World logged a separate Create for index 8 at 19:04:13,
**before** native OnUnitState; it logged no Create/replay for that index between
OnUnitState and removal. Thus the log alone should not be described as proof
of an exclusively snapshot-sourced buff. The static consumer trace establishes
snapshot loading; the memory trace verifies the actual buff lifecycle and
cleared restriction in the running native process. The rare Zone-authored
expiration race is covered by the automated regression, not claimed as a
manually reproduced timing race.

The integrated runtime used image
`sha256:f2a13ca1ac7e1e4bc67c1e35dee4b17a90c2108ab6b567e96689cab6dc6317bd`
and `AAEmu.World.dll` SHA-256
`7b482bac7f423bf864e3bfb925e9722356d74c2631161fa78f2cec5dddf6df5a`.
This was live validation of the integrated fork; the independent contribution
branch was validated with its build, unit suite, and compiler check.
