# Compact content scripts

`*.sqlite3` is gitignored. These SQL files are how a clone picks up client compact
row changes without downloading a new 200 MB database.

This folder is **not** `SQL/updates`. Those scripts are MySQL `aaemu_game` /
`aaemu_login` only.

## What r584 → r589 adds

`2026-09-03_compact_r584_to_r589.sql` is the client compact row delta (same
schema, 47 tables). Matching primary keys are updated; new keys are inserted;
only the 584 `hero_schedules` ids that 589 dropped are deleted. Extra local
tables and extra local rows stay.

Notable rows: Kraken `tower_defs` 65, Leviathan / Black Dragon `game_schedules`
1010–1014, Dew Plains War `instances` 14, enchant grade cap 7 → 12, Auroria
`zones.closed` / `zone_groups.hide_world_pos`, trade specialties.

Intended source compact is client **r584** (or a later compact that is missing
these rows). Re-run is a no-op (`_aaemu_compact_updates`).

## Apply

**Game / World** (`Data/compact.sqlite3`): applied once at boot by
`CompactSqliteUpdater`. Kill-switch: `AAEMU_SKIP_COMPACT_UPDATES=1`.

**Dedicate** (`compact.sqlite3` and/or `game.sqlite3` from your zone data root):

```text
python SQL/compact/apply_compact_sql.py --db path/to/compact.sqlite3
python SQL/compact/apply_compact_sql.py --db path/to/game.sqlite3
```

A section whose table is missing is skipped (client compact vs a server-augmented
file).

## Regenerating a later patch

```text
python SQL/compact/generate_delta.py --old compact_old.sqlite3 --new compact_new.sqlite3 --out YYYY-MM-DD_compact_rFROM_to_rTO.sql
```
