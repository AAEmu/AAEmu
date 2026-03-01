# Code Terminology

- Audience: Contributors, players, and testers
- Last verified against: `develop` on February 28, 2026
- Prerequisites: None

Below are common terminology and abbreviations used in the code.

## Game Objects

### Doodad

Any object that is spawnable but does not have a health bar.
Examples: crops, trees, doors.

### Unit

Any object that has a health bar and can interact with combat.

### NPC

Non-Playable Character.
Any enemy or friendly person in the game with a health bar.

### Mate

Commonly called pets.

### Slave

Internal name for vehicles like cars, carts, and ships.

### Game objects hierarchy

```text
GameObject - Basic world entity with a location; not used directly
|
+--> BaseUnit - Implements factions and buffs; no stats; not used directly
     |
     +--> Unit - Stats, events, skill controllers, combat logic
     |    |
     |    +--> Character - Player characters
     |    +--> NPC - Non-player characters and enemies
     |    |    +--> Portal (by player) - Player-created portals
     |    +--> Gimmick - Moving doodads, for example elevators
     |    +--> House - Player housing
     |    +--> Shipyard - House-like structure for ship building
     |    +--> Mate - Also known as pets
     |    +--> Slave - Also known as vehicles
     |    +--> Transfer - Fixed-route transport like carriages/airships
     |
     +--> Doodad - Interactive world objects and furniture
          +--> DoodadCoffer - Furniture specialization for housing coffers
```

## Game Terms

### Ability

Main class combat skill trees.

### ActAbility

Vocational skills.

### Appellations

Also known as `Titles`.

### BmPoints

Also known as `Loyalty Tokens` in most game versions.

### Dominion

Internal name for castle sieges, not guild-vs-guild fights.

### Expedition

Internal game name for what most players call a `Guild`.

## Related

- [Home](Home)
- [Components](Components)
- [Developer Notes](Developer-Notes)
