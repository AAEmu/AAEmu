//! Shared context of the doodad behaviour code (phase machine, phase funcs, funcs,
//! interactions). Owned by the phase-machine helper; funcs/interactions build on it.

use std::sync::Arc;

use super::data::DoodadData;
use crate::net::packet::ServerPacket;
use crate::server::GameServer;
use crate::world::{DoodadRef, PlayerRef, WorldInstance, WorldObject};

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

    /// A context for `doodad` in its own world (`ParentWorld`: region, else
    /// `Transform.InstanceId`). `None` when that instance does not exist.
    pub fn of(server: &'a GameServer, doodad: &DoodadRef) -> Option<Self> {
        let world = server.world.world_of(&WorldObject::from(doodad))?;
        Some(Self::new(server, world, doodad.clone()))
    }

    /// The doodad as a `WorldObject`.
    pub fn obj(&self) -> WorldObject {
        WorldObject::from(&self.doodad)
    }

    /// `DoodadManager` game data.
    pub fn data(&self) -> &'a DoodadData {
        &self.server.game_data.systems.npcs.doodad
    }

    /// `Doodad.BroadcastPacket(packet, self)` (doodads never include themselves).
    pub fn broadcast(&self, packet: &dyn ServerPacket) {
        crate::world::broadcast(&self.world, self.obj(), packet, false);
    }

    /// `caster as Character`.
    pub fn as_player(caster: Option<&WorldObject>) -> Option<&PlayerRef> {
        caster.and_then(WorldObject::as_player)
    }
}
