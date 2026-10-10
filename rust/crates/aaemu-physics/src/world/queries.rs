//! **P3 — queries** (stub): `RayWorldIntersection` (slot 42, 0x351AA730; hits 80 bytes),
//! `PrimitiveWorldIntersection` (slots 64/65), `GetEntitiesInBox` (0x35084680), entity-level
//! queries (slots 59–63). `world.md` §6.
//!
//! Oracle findings to keep: RWI returns 0 until the world's temp entity pools exist (allocated
//! at the first step with an ordinary entity); part flags must contain the RWI geomFlagsAll
//! (high word of the flags, default 0x8000) and intersect geomFlagsAny (default 0xFDFF); unless
//! `gEnv` byte 289 is set, rays with |dir.x| or |dir.y| above an integer config value
//! (gEnv+112 → slot 20 → +248) are rejected ("DrawRayOnGrid" log, an ArcheAge addition).
