//! `DoodadPhaseFuncTemplate.Use` of every phase func (stub: TODO(npcs) — being ported).

use super::runtime::DoodadCtx;
use super::templates::FuncTemplate;
use crate::world::WorldObject;

/// `DoodadPhaseFunc.Use(caster, owner)` → the template's `Use`. Returns `true` to stop the phase
/// funcs of the current phase (C# return value).
pub fn use_phase_func(_t: &FuncTemplate, _ctx: &DoodadCtx<'_>, _caster: Option<&WorldObject>) -> bool {
    false
}
