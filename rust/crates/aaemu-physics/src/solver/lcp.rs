//! Stages 2 ("LCPCG") and 3 ("LCPCG-unproj") of `InvokeContactSolver` 0x3509E850.

use super::invoke::{Ctx, SolvePath};

impl Ctx<'_> {
    /// Stages 2 and 3 (decompile lines 3439–7130).
    pub(crate) fn stage23(&mut self, nb: i32, dt: f32) -> SolvePath {
        let _ = (nb, dt);
        unimplemented!("stage 2")
    }
}
