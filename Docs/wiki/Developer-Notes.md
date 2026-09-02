# Developer Notes

- Audience: Contributors
- Last verified against: `develop` on February 28, 2026
- Prerequisites: None

## Recent architecture note: manager DI and parallel loading

PRs `#1363` and `#1366` migrated manager construction toward dependency
injection and completed follow-up fixes for parallel loading.

What this means for contributors:

- Manager dependencies are increasingly explicit in constructors.
- Startup manager loading is now less manual and more dependency-driven.
- Parallel initialization surfaced and fixed some concurrency issues.

Operational impact for wiki setup docs is small:

- No user-facing launch command change beyond existing setup workflows.
- Main effects are internal maintainability, testability, and startup behavior.


## Floor ≠ Path (world height)

`GeoDataMode` loads `.bai` navmesh and enables A* pathfinding only. Floor Z for
units goes through `WorldTemplate.Floor` (`FloorQuery`), controlled by config
`World.FloorPolicy` (`FloorPolicyMode`: `ByZHint` | `Legacy`; obsolete alias `TerrainFirst` = `ByZHint`).
Legacy config key `FloorSource` still binds to the same value.

Each query also records which **provider** won (`FloorProvider` in code, `src=`
in logs) — not the same as policy: `Terrain`, `NavSurface`, `LegacyNavNode`, or `Unchanged`.

- `ByZHint` (default): heightmap + nav-surface candidates, pick nearest to `zHint` in a vertical window (`FloorResolver`; L2/TrinityCore/Detour-style). **Not terrain-only.** Outdoor (#1425) and caves/multi-floor (#1033), including main_world cell `.bai`
- `Legacy`: nearest `.bai` node (pre-split behavior; GM rollback for A/B)

GM: `/world set floorpolicy …` (aliases `floorsource` / `fs` / `fp`), `/world set floordebug true`, `/height` (mode / src / deltas).

Opt-in `World.FloorDebug` logs one line per sample. Parse with:

```bash
bash Scripts/find-floor-mismatch.sh --summary
```

Related: `Docs/WorldConfig_en.md` / `Docs/WorldConfig_ru.md`.

## Related

- [Home](Home)
- [Aspire Development Guide](Aspire-Development-Guide)
- [Components](Components)
