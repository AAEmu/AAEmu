//! **P4b — contact solver**: `InvokeContactSolver` 0x3509E850 (x86 0x350A62B0; "PreCG", "LCPCG",
//! "LCPCG-unproj", "LCPMC"): CG fast path, projected Gauss–Seidel with a cumulative friction
//! cone (stage 1), LCP-CG (stage 2), positional unprojection (stage 3). `dynamics.md` §3.
//!
//! * [`body`] — the 212-byte rigid body record and the world's body arena.
//! * [`contact`] — the 176-byte entity contact record and the world's contact pool.
//! * [`thread`] — the per-thread solver context ([`SolverThread`]): `InitSolver`,
//!   `RegisterContact`, `DisableCG`, `AllocSolverTmpBuf`.
//! * [`invoke`] — `InvokeContactSolver` ([`SolverThread::invoke`]).
//! * `pgs` — stage 1 (`sub_3509AC90`); `lcp` — stages 2 and 3; [`helpers`] — small math
//!   routines.
//! * [`probe`] — test support: island cases for the oracle's `--p4b` probe.

pub mod body;
pub mod contact;
pub mod helpers;
pub mod invoke;
mod lcp;
mod pgs;
pub mod probe;
pub mod thread;

pub use body::{BodyArena, BodyId, RigidBody};
pub use contact::{Contact, ContactId, ContactOwner, ContactPool};
pub use invoke::{SolvePath, SolveResult};
pub use thread::{MultiBodyEntry, SolverThread};
