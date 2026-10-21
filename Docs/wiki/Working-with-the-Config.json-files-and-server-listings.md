# Working with Config Files and Server Listings

- Audience: Contributors, players, and testers
- Last verified against: `develop` on February 28, 2026
- Prerequisites: Basic JSON editing and AAEmu project structure familiarity

## Overview

AAEmu now stores login server listings in configuration (`GameServers`) instead
of MySQL `aaemu_login.game_servers`.

This allows consistent behavior across manual, Docker, and Aspire workflows.

## Login server `Config.json` and `Config.Local.json`

The login server reads configuration from JSON files and environment variables.
Use `Config.Local.json` for machine-specific overrides.

### `GameServers` schema

`GameServers` is a list of server entries with this shape:

```json
{
  "GameServers": [
    {
      "Id": 1,
      "Name": "AAEmu.Game",
      "Host": "127.0.0.1",
      "Port": 1239,
      "Hidden": false
    }
  ]
}
```

Field meanings:

- `Id`: unique game server id.
- `Name`: display name in server selection.
- `Host`: address reachable by clients.
- `Port`: client connection port.
- `Hidden`: whether to hide this entry from listing.

### Environment variable mapping (login)

You can define server entries via environment variables:

```text
GameServers__0__ID=1
GameServers__0__Name=AAEmu.Game
GameServers__0__Host=127.0.0.1
GameServers__0__Port=1239
GameServers__0__Hidden=false
```

## Game server configuration and precedence

The game server supports `Config.Local.json` as the final override layer.

Effective load order:

1. `AAEmu.Game/Config.json`
1. `AAEmu.Game/Configurations/*.json` (all matching files)
1. `AAEmu.Game/Config.Local.json` (loaded last)

If the same setting exists in multiple places, `Config.Local.json` wins.

## `game_pak` configuration

Set `game_pak` source in one of these places:

- `AAEmu.Game/Configurations/ClientData.json`, or
- `AAEmu.Game/Config.Local.json` for local override.

For contributor workflows, prefer `Config.Local.json`.

## Migration note from old setup docs

Older instructions referenced `aaemu_login.game_servers` and SQL inserts.
That is no longer the source of truth for server listings.

Use login `GameServers` configuration instead.

## Nymph Queen encounter (quest 2008)

The encounter is shared by the challenger and nearby party members present when
it starts. Those members can accept the quest before Lympheroda retreats; other
players cannot join or deal damage during that encounter.

Defaults are in `AAEmu.Game/Configurations/QuestEncounters.json`. Override them
in Game's `Config.Local.json`:

```json
{
  "NymphQueenEncounter": {
    "CombatInactivitySeconds": 60,
    "DialogueSeconds": 50,
    "ResetDelaySeconds": 5
  }
}
```

Only participant damage refreshes the combat timer. Retreat at half health
starts Nepheroda's dialogue window. Leaving combat, owner death,
or expiry of a timer fails unfinished participants and restores one friendly
Lympheroda after the reset delay. Abandon the failed quest before accepting it
again. After a successful conversation, quest credit is retained for the final
turn-in to Opheila, and the encounter resets for the next challenger.

Each character selection revalidates persisted encounter quests before sending
them to the client. Unfinished combat/dialogue quests are marked failed, including
after a disconnect, a missing NPC, or a server restart. Completed conversation
credit and quests ready for final turn-in are retained. No offline event is needed;
the encounter timers still recover abandoned NPCs.
Timer values are seconds and are clamped to a minimum of one second.

Abandoning or removing quest 2008 also resets its active encounter. Wait for the
five-second reset delay before accepting it again. The GM command `/quest add
2008` uses the same encounter lifecycle and can recover a missing Lympheroda
from her existing world spawner; it does not place a new queen at the player's
position or interrupt another player's active encounter.

## Related

- [Installation & Setup](Installation-&-Setup)
- [Aspire Development Guide](Aspire-Development-Guide)
- [Mini troubleshoot guide](Mini-troubleshoot-guide)
- [FAQ](FAQ)
