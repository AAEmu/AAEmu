//! `NpcManager` runtime: `Create` (stub: TODO(npcs) — being ported).

use crate::server::GameServer;
use crate::world::NpcRef;

/// `NpcManager` runtime state.
#[derive(Debug, Default)]
pub struct NpcManager {}

/// `NpcManager.Create(world, objectId, templateId)`: a new NPC (not yet in the world).
/// `obj_id == 0` allocates a new object id. `None` when the template does not exist.
pub fn create(_server: &GameServer, _obj_id: u32, _template_id: u32) -> Option<NpcRef> {
    None
}
