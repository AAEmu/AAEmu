# `World.json` settings (AAEmu.Game)

This document describes the settings in `AAEmu.Game/Configurations/World.json`.

## Where to configure

- **File**: `AAEmu.Game/Configurations/World.json`
- **Section**: `World`

Example:

```json
{
  "World": {
    "MOTD": "Welcome to AAEmu!",
    "TargetPhysicsTps": 25.0
  }
}
```

## Parameters

### `MOTD`
- **Type**: `string`
- **Description**: Message of the Day (shown in chat on login).

### `LogoutMessage`
- **Type**: `string`
- **Description**: Message shown when leaving the game.

### `AutoSaveInterval`
- **Type**: `number`
- **Description**: Auto-save interval (minutes).

### `ExpRate`
- **Type**: `number`
- **Description**: Server-side EXP multiplier.

### `HonorRate`
- **Type**: `number`
- **Description**: Server-side honor points multiplier.

### `VocationRate`
- **Type**: `number`
- **Description**: Server-side vocation badge / vocation points multiplier.

### `LootRate`
- **Type**: `number`
- **Description**: Loot dice multiplier (not all loot types are affected).

### `GoldLootMultiplier`
- **Type**: `number`
- **Description**: Gold multiplier for gold obtained from loot.

### `GrowthRate`
- **Type**: `number`
- **Description**: Growth rate multiplier for doodads (growth steps, not simple timers).

### `DaysForTaxPayment`
- **Type**: `number`
- **Description**: Number of days one tax payment covers (default: 7).

### `IgnoreFallDamageAccessLevel`
- **Type**: `number`
- **Description**: Minimum access level that ignores fall damage (dev/testing).

### `GodMode`
- **Type**: `boolean`
- **Description**: When `true`, players take no damage.

### `GeoDataMode`
- **Type**: `boolean`
- **Description**: Enables loading GeoData/NavMesh (dungeons/navigation).

### `PreLoadTerrain`
- **Type**: `boolean`
- **Description**: When `true`, preloads terrain data (slower startup, lower runtime spikes, higher memory usage).

### `MaxInstances`
- **Type**: `number`
- **Description**: Maximum number of instances (including system instances).

### `TargetPhysicsTps`
- **Type**: `number`
- **Description**: Target physics TPS (tick rate for physics threads).

### `ActabilityRate`
- **Type**: `number`
- **Description**: Server-side actability points multiplier.

## Ship wind

### `WindModel`
- **Type**: `string`
- **Path**: `World.WindModel`
- **Allowed values**:
  - **`Official`**: retail-like wind model.
    - wind does **not** change with time of day;
    - a **+15%** max speed bonus applies only when sailing within **±15°** of the **North↔South** axis (both directions);
    - outside the cone, the bonus is **0%**.
  - **`Realistic`**: more realistic model.
    - wind direction rotates smoothly over the day (and sail rig profile logic applies).

Examples:

```json
{
  "World": {
    "WindModel": "Official"
  }
}
```

```json
{
  "World": {
    "WindModel": "Realistic"
  }
}
```

