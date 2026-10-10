//! `DoodadManager` runtime (stub: TODO(npcs) — being ported). The function signatures below are
//! the API the spawners (`spawn_manager`, `doodad::spawner`) build on; keep them stable.

use std::sync::Arc;

use crate::server::GameServer;
use crate::world::{DoodadRef, WorldInstance, WorldObject};

/// `DoodadManager` runtime state.
#[derive(Debug, Default)]
pub struct DoodadManager {}

/// `DoodadManager.Create(parentWorld, bcId, templateId, ownerObject, skipPhaseInitialization)`:
/// a new doodad (not yet shown). `obj_id == 0` allocates a new object id. `None` when the
/// template does not exist.
pub fn create(
    _server: &GameServer,
    _world: &Arc<WorldInstance>,
    _obj_id: u32,
    _template_id: u32,
    _owner: Option<&WorldObject>,
    _skip_phase_init: bool,
) -> Option<DoodadRef> {
    None
}

/// `Doodad.Spawn()` (`GameObject.Spawn`: AddObject + Show, plus the doodad side effects).
pub fn spawn(_server: &GameServer, _world: &Arc<WorldInstance>, _doodad: &DoodadRef) {}

/// `Doodad.Delete()` (hide + remove, area triggers, expired item, DB row of persistent ones).
pub fn delete(_server: &GameServer, _world: &Arc<WorldInstance>, _doodad: &DoodadRef) {}
