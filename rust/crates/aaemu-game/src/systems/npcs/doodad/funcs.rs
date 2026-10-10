//! `DoodadFuncTemplate.Use` of every (non-phase) func (stub: TODO(npcs) — being ported).

use super::runtime::DoodadCtx;
use super::templates::FuncTemplate;
use crate::world::WorldObject;

/// `DoodadFunc.Use(caster, owner, skillId, nextPhase)` → the template's `Use`. Funcs signal a
/// phase change by setting `doodad.phase.to_next_phase` (and `override_phase`).
pub fn use_func(
    _t: &FuncTemplate,
    _ctx: &DoodadCtx<'_>,
    _caster: Option<&WorldObject>,
    _skill_id: u32,
    _next_phase: i32,
) {
}
