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
//!
//! # Using the solver (island step 0x3507F9B0)
//!
//! ```text
//! thread.init();                                   // InitSolver 0x3509A900
//! for each entity: RegisterContacts (slot 40) →
//!     thread.register_contact(&mut pool, id)       // RegisterContact 0x350994B0
//!     thread.alloc_tmp_contacts(&mut pool, n)      // AllocSolverTmpBuf 0x3509A9C0 (temp records)
//!     thread.disable_cg()                          // 0x35099570 (flag 0x8000000 / in water)
//! let r = thread.invoke(dt, &vars, e0, 0, &mut bodies, &mut pool);
//! post-solve 0x3507F210(a9 = thread.contacts(), a10 = r.n_contacts, a11 = r.ret)
//! ```
//!
//! `e0 = max_idiom(Σ m·2.5e-5, Σ CalcEnergy(dt))` over the island. Every body's solver index
//! for the thread (`B+200+2·tid`) must be 0 on entry (the solver leaves it 0). The outputs:
//! `ret` = `T+4` = number of bodies in the solver list (compared with `W+308` by the
//! post-solve), `*a5` = number of contacts, `*a4` = the contact list (`T+8`, reordered by stage
//! 3), `*a6` = the body list (`T+79880`, not used by the post-solve).
//!
//! # Fidelity notes
//!
//! * **Sort keys.** Only one sort compares addresses: stage 3 sorts each run of contacts with
//!   the same `pbody[0]` by the address of `pbody[1]` (`sub_350995C0`; quicksort, pivot =
//!   middle element swapped to the front, Lomuto partition, "all keys equal to the pivot" exit
//!   on `OR((key − pivot) >> 1) == 0`). The port compares `2·BodyArena::sort_key` (so distinct
//!   keys always count as different, as distinct addresses do); the order equals the
//!   binary's whenever the key order equals the address order (the oracle probe allocates
//!   bodies in index order above the `.data` dummy, matching keys `i + 1` > dummy key 0).
//!   The other two sorts are integer-keyed (body level, `sub_350996D0`; position of a
//!   contact's upper body, `sub_35099840` + `sub_350991B0`) and deterministic.
//! * **NaN.** Islands that produce NaN (a contact between two infinite-mass bodies, `K = 0`, or a
//!   rope constraint on them) are outside the bit-exact domain: NaN sign bits depend on which
//!   operand of a commutative `addss`/`mulss` carries the NaN, LLVM may swap such operands,
//!   and the solver's sign-bit tests then branch differently. All comparisons follow the
//!   binary's unordered-compare semantics (`jbe`/`jb` take NaN, `jae`/`ja` do not, `ucomiss`
//!   equality treats NaN as equal), so finite-but-diverging cases still match.
//! * **Bugs kept** (as in the binary): the difficulty test pushes heavy/light nodes onto
//!   `bg[solver index + 1]` but scans `bg[0..nB)`; stage 2 tests the multi-body limit against
//!   the stale `crec+48` (`K[6]`) where stage 1 uses `K[2]`; the stage-2 `|Δw|` rejection uses
//!   `Ibody` (not its inverse); per-pass `vn` for scalar records reads the stale `Ap.y/Ap.z`;
//!   stage-1 shared-Pn owners of multi-body contacts read 4 bytes before the rope array (port:
//!   0); `AllocSolverTmpBuf`'s pointer relocation range (`176·capacity`) is moot in the port.
//! * The global solver call counter `dword_35218700` is not modelled.

pub mod body;
pub mod contact;
pub mod helpers;
pub mod invoke;
mod lcp;
mod pgs;
pub mod probe;
pub mod thread;
mod unproj;

pub use body::{BodyArena, BodyId, RigidBody};
pub use contact::{Contact, ContactId, ContactOwner, ContactPool};
pub use invoke::{SolvePath, SolveResult};
pub use thread::{MultiBodyEntry, SolverThread};
