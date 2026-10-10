//! **P3 — entity grid** (stub). `world.md` §3. Note from the oracle: entities whose bbox lies
//! below the grid origin's z are not found by the broad phase, and the grid floor uses the
//! magic-number round of `x − 0.5` ([`crate::math::floor_magic`]).
