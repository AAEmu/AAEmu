//! Shared context of the doodad behaviour code (phase machine, phase funcs, funcs,
//! interactions). Owned by the phase-machine helper; funcs/interactions build on it.

use std::sync::Arc;

use crate::server::GameServer;
use crate::world::{DoodadRef, WorldInstance, WorldObject};

/// Everything a func / phase func `Use` needs: the server, the doodad's world and the doodad.
/// Hold the doodad's lock only briefly (`ctx.doodad.data()`), never across world calls.
pub struct DoodadCtx<'a> {
    pub server: &'a GameServer,
    pub world: Arc<WorldInstance>,
    pub doodad: DoodadRef,
}

impl<'a> DoodadCtx<'a> {
    pub fn new(server: &'a GameServer, world: Arc<WorldInstance>, doodad: DoodadRef) -> Self {
        Self {
            server,
            world,
            doodad,
        }
    }

    /// The doodad as a `WorldObject`.
    pub fn obj(&self) -> WorldObject {
        WorldObject::from(&self.doodad)
    }
}
