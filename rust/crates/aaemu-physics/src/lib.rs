//! `aaemu-physics`: a port of CryPhysics, the physics engine of the ArcheAge zone server
//! (`cryphysics_dedicate.dll`, x64, 10.0.2.13; the same engine generation as the 1.2 client's
//! `cryphysics.dll`).
//!
//! **Fidelity policy.** The port matches the original bit for bit wherever feasible: the same
//! precision per operation (single vs double, as the binary computes it), the same operation
//! order, the same constants, and the original's apparent bugs (documented where they occur),
//! because the server must agree with what the client simulates. The test oracle is the real DLL
//! driven standalone (`oracle/physoracle.c`); see the crate README.
//!
//! **Licensing.** Derived only from the shipped binaries (decompiles and disassembly). The
//! published CRYENGINE source is not used.
//!
//! Module map (owners in the crate README):
//! * [`math`] — P0: vectors, matrices, quaternions, idioms, CRT math.
//! * [`sentinel`], [`ids`], [`params`], [`physvars`] — P0: the parameter interface.
//! * [`geometry`] — P1a primitives, P1b BV trees, P1c trimesh/heightfield.
//! * [`collision`] — P2a contact pairs, P2b swept/unprojection.
//! * [`world`] — P3 world core, grid, areas, queries, events.
//! * [`entities`] — P4a base/rigid/buoyancy, P5 living/wheeled/particle.
//! * [`solver`] — P4b contact solver.
//! * [`trace`], [`oracle`] — PT: oracle traces and the oracle runner (test support).

pub mod collision;
pub mod entities;
pub mod geometry;
pub mod ids;
pub mod math;
pub mod oracle;
pub mod params;
pub mod physvars;
pub mod scenario;
pub mod sentinel;
pub mod solver;
pub mod trace;
pub mod world;
