# Native NPC AI diagnostics

Native Zone hosts own NPC perception, behavior and attack selection. World applies
damage and executes the skills requested through `ZWStartSkill`. A World log saying
that target, aggro and combat packets were sent does not establish that the native
host resolved both units or transitioned the NPC to its attack behavior.

## Native host launch settings

World-managed hosts now use the same AI update settings as ZoneManager:

| `ZoneHost` configuration property | Native option | Default |
|---|---|---:|
| `NpcMoveSkipStanding` | `npc_move_skip_standing` | 0 |
| `NpcMoveSkipDisabledAi` | `npc_move_skip_disabledAI` | 0 |
| `NpcMovementSkip` | `npc_movement_skip` | 0 |
| `AiSystemUpdate` | `ai_systemupdate` | 1 |

These settings make host launches consistent instead of relying on the native
movement-skip defaults. `ExtraArguments` follows these options and can override
them. Changes apply when a host is launched; they do not reconfigure an existing
host. Hosts launched independently of World continue to use their own launcher
configuration.

This corrects a launch configuration difference. It is not proof that movement
skipping caused a particular NPC combat failure.

## Reading a reproduction

Use World player-handoff records to identify the player's zone. A recently
launched host may be unrelated to the player or the NPC being tested.

The relays include the following context:

- `ZWStartSkill`, `ZWMakeAggroTargetHostile` and posture messages identify their
  source zone, instance and connection session.
- `WZ damage handoff` identifies the destination host, source player tracking,
  target NPC tracking, NPC template, spawner, AI file and AI parameter ID.
- Transform zone and instance are reported separately from the connection so
  ownership discrepancies are visible.
- A tracking warning means the World-side destination registry lacks the NPC or
  attacking player. Registry membership is not a native acknowledgement.

Posture and skill-request context uses Debug logging. Damage handoff and hostile
context uses Info logging. AI names and parameter IDs come from the configured
content database; the diagnostic path does not change its rows or select a
replacement AI script.

## Stationary NPC comparison

In the examined 10.0.2.13 r575 content, template 3463 uses `hold_position` and
3489 uses `archer_hold_position`; template 3462 uses `roaming`. The stationary
scripts already define combat transitions. Replacing them with `roaming` would
change their intended idle behavior without establishing why combat failed.

An isolated native-host check also received `ZWStartSkill` from each of these
three templates using continent coordinates, explicit faction synchronization,
and the launch settings above. Each phase used a separate NPC object ID and
removed the previous NPC; casts were identified by their decoded caster ID.
This establishes that the templates can request attacks in that setup. It does
not reproduce the original player's complete entry, movement, equipment, or
combat lifecycle, and does not establish a repair for that session's failure.

To validate a repair, compare stationary and roaming NPCs in the same host and
player-entry lifecycle. Observe natural aggression before dealing damage, then
check retaliation. Confirm native player presence, NPC behavior and attention
target alongside the relay logs. Repeat against the same stationary NPC after
leaving and re-entering the host to distinguish a lifecycle problem from a
template-specific problem.

## Resurrection state

World-side tracking and visible HP do not prove that a native player is alive.
An administrative revival must send `WZUnitResurrection`, clear native combat,
and synchronize HP/MP, as the normal client resurrection path does. Without
that sequence, an NPC can receive the damage handoff but refuse the stale dead
target. Leaving and re-entering the zone can mask the omission by recreating
the player. The `revive` command now sends this sequence; `heal self` explicitly
selects the caller and healing synchronizes native HP/MP.
