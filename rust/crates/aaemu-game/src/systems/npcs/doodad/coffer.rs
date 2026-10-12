//! `DoodadCoffer` (chests): permissions (`AllowedToInteract`), `AllowRemoval`, open / close
//! tracking (`DoodadManager.OpenCofferDoodad` / `CloseCofferDoodad` / `CloseCoffersOpenedBy`),
//! `CSCofferInteractionPacket` and `SCCofferContentsUpdatePacket`.
//!
//! The coffer members live in `Doodad::coffer` (`Some` = the C# object is a `DoodadCoffer`);
//! the container itself is a phase-1 `ItemContainer` (`server.items`). Moving items in / out
//! of a coffer (`CSSplitCofferItemPacket`, `CSSwapCofferItemsPacket`) is inventory code
//! (economy).

use aaemu_commons::{Marshal, PacketStream};
use futures::future::BoxFuture;

use super::funcs::{economy, error_message as err, send_error};
use crate::error::Result;
use crate::models::doodad::DoodadCofferState;
use crate::models::house::housing_permission;
use crate::models::item::Item;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::world::{DoodadRef, PlayerRef, WorldObject};

/// `ChestType.Otherworldly`.
pub const CHEST_TYPE_OTHERWORLDLY: u8 = 2;

/// `SCCofferContentsUpdatePacket.MaxSlotsToSend`.
pub const MAX_SLOTS_TO_SEND: u8 = 30;

/// The coffer members of a new doodad of `template_id` (`DoodadManager.Create`:
/// `template is DoodadCofferTemplate` -> `new DoodadCoffer { Capacity }`), `None` for other
/// templates. The container is assigned by `InitializeCoffer` / the persistence code.
pub fn new_coffer_state(server: &GameServer, template_id: u32) -> Option<DoodadCofferState> {
    let capacity = server
        .game_data
        .systems
        .npcs
        .doodad
        .template(template_id)?
        .coffer_capacity?;
    Some(DoodadCofferState {
        capacity,
        ..DoodadCofferState::default()
    })
}

/// `DoodadCoffer.AllowedToInteract(character)` (`base.AllowedToInteract` is always true).
/// Non-coffer doodads are always allowed.
pub fn allowed_to_interact(server: &GameServer, doodad: &DoodadRef, player: &PlayerRef) -> bool {
    let (permission, owner_id, container_id) = {
        let d = doodad.data();
        let Some(c) = &d.coffer else {
            return true;
        };
        (d.data, d.owner_id, c.container_id)
    };
    let (char_id, account_id, family, expedition) = {
        let c = player.data();
        (c.id, c.account_id, c.family, c.unit.expedition_id)
    };
    // `(HousingPermission)Data`: only the low byte values are named.
    let permission = u8::try_from(permission).unwrap_or(u8::MAX);
    if permission == housing_permission::PUBLIC {
        return true;
    }
    // Try to use the owner Character if it's already in the world.
    let owner = server.world.character_by_id(owner_id);
    let eco = economy(server);
    match permission {
        housing_permission::PRIVATE => {
            let has_container =
                container_id != 0 && server.items.container_by_id(container_id).is_some();
            if has_container
                && eco.coffer_chest_type(server, container_id) == CHEST_TYPE_OTHERWORLDLY
            {
                char_id == owner_id
            } else {
                account_id == server.names.get_character_account(owner_id)
            }
        }
        housing_permission::FAMILY => {
            let owner_family = owner
                .as_ref()
                .map(|o| o.data().family)
                .unwrap_or_else(|| eco.family_of_character(server, owner_id));
            owner_family != 0 && family != 0 && owner_family == family
        }
        housing_permission::GUILD => {
            // `owner?.Expedition?.Id ?? GetExpeditionOfCharacter(OwnerId)`
            let owner_guild = owner
                .as_ref()
                .map(|o| o.data().unit.expedition_id)
                .filter(|&e| e != 0)
                .unwrap_or_else(|| eco.expedition_of_character(server, owner_id));
            owner_guild != 0 && expedition != 0 && expedition == owner_guild
        }
        _ => true,
    }
}

/// `DoodadCoffer.AllowRemoval()` part: `ItemContainer == null || ItemContainer.Items.Count <= 0`
/// (true for non-coffers).
pub fn coffer_is_empty(server: &GameServer, doodad: &DoodadRef) -> bool {
    let container_id = match &doodad.data().coffer {
        Some(c) => c.container_id,
        None => return true,
    };
    if container_id == 0 {
        return true;
    }
    server
        .items
        .container_by_id(container_id)
        .is_none_or(|c| c.lock().expect("container poisoned").items.is_empty())
}

/// `SCCofferContentsUpdatePacket(cofferDoodad, firstSlot)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCCofferContentsUpdatePacket {
    pub obj_id: u32,
    pub container_id: u64,
    /// (slot, item) for `slotCount` slots from `firstSlot`.
    pub slots: Vec<(u8, Option<Item>)>,
}
packet_def!(
    SCCofferContentsUpdatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCofferContentsUpdatePacket
);
impl Marshal for SCCofferContentsUpdatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u64(self.container_id)
            .write_u8(self.slots.len() as u8);
        for (slot, item) in &self.slots {
            s.write_u8(*slot);
            match item {
                Some(i) => i.write(s),
                None => {
                    s.write_u32(0);
                }
            }
        }
    }
}

impl SCCofferContentsUpdatePacket {
    /// The packet for `coffer` (its state copied from the doodad) starting at `first_slot`.
    pub fn new(
        server: &GameServer,
        obj_id: u32,
        coffer: &DoodadCofferState,
        first_slot: u8,
    ) -> Self {
        let first = i32::from(first_slot);
        let mut last_slot = first + i32::from(MAX_SLOTS_TO_SEND);
        if last_slot >= coffer.capacity {
            last_slot = coffer.capacity;
        }
        let slot_count = (last_slot - first).min(i32::from(MAX_SLOTS_TO_SEND)) as u8;
        let container = (coffer.container_id != 0)
            .then(|| server.items.container_by_id(coffer.container_id))
            .flatten();
        if container.is_none() {
            tracing::warn!("No ItemContainer assigned to Coffer, objId: {obj_id}");
        }
        let slots = (0..slot_count)
            .map(|i| {
                let slot = first_slot.wrapping_add(i);
                let item = container.as_ref().and_then(|c| {
                    c.lock()
                        .expect("container poisoned")
                        .get_item_by_slot(i32::from(slot))
                        .cloned()
                });
                (slot, item)
            })
            .collect();
        Self {
            obj_id,
            // `GetItemContainerId()`: `ItemContainer?.ContainerId ?? 0`
            container_id: container.map_or(0, |_| coffer.container_id),
            slots,
        }
    }
}

/// The coffer `obj_id` in `player`'s world.
fn coffer_doodad(server: &GameServer, player: &PlayerRef, obj_id: u32) -> Option<DoodadRef> {
    let instance = player.data().unit.transform.instance_id;
    let doodad = server.world.world(instance)?.doodad(obj_id)?;
    let is_coffer = doodad.data().coffer.is_some();
    is_coffer.then_some(doodad)
}

/// `DoodadManager.OpenCofferDoodad(character, objId)`.
pub fn open_coffer_doodad(server: &GameServer, player: &PlayerRef, obj_id: u32) -> bool {
    let (char_id, name) = {
        let c = player.data();
        (c.id, c.unit.name.clone())
    };
    let Some(doodad) = coffer_doodad(server, player, obj_id) else {
        crate::systems::social::crime::sus_log(
            server,
            crate::systems::services::sus::CATEGORY_CHEATING,
            char_id,
            &format!("{name} tried to open doodad {obj_id} as a Coffer"),
        );
        return false;
    };
    if !allowed_to_interact(server, &doodad, player) {
        return false;
    }
    let Some(world) = server.world.world_of(&WorldObject::from(player)) else {
        return false;
    };
    if world
        .world_pos_rot(&WorldObject::from(player))
        .distance_to(&world.world_pos_rot(&WorldObject::from(&doodad)), false)
        > 5.0
    {
        return false;
    }
    let coffer = {
        let mut d = doodad.data();
        let c = d.coffer.as_mut().expect("coffer");
        // Somebody already using this ?
        if c.opened_by.is_some() {
            return false;
        }
        c.opened_by = Some(char_id);
        c.clone()
    };
    let mut first_slot: u8 = 0;
    while i32::from(first_slot) < coffer.capacity {
        player.send(SCCofferContentsUpdatePacket::new(
            server, obj_id, &coffer, first_slot,
        ));
        // `firstSlot += MaxSlotsToSend` on a byte (wraps above 255, as C#; the loop ends there
        // instead of spinning forever).
        match first_slot.checked_add(MAX_SLOTS_TO_SEND) {
            Some(v) => first_slot = v,
            None => break,
        }
    }
    true
}

/// `DoodadManager.CloseCofferDoodad(character, objId)`.
pub fn close_coffer_doodad(server: &GameServer, player: &PlayerRef, obj_id: u32) -> bool {
    let (char_id, name) = {
        let c = player.data();
        (c.id, c.unit.name.clone())
    };
    let Some(doodad) = coffer_doodad(server, player, obj_id) else {
        crate::systems::social::crime::sus_log(
            server,
            crate::systems::services::sus::CATEGORY_CHEATING,
            player.character_id,
            &format!("{name} tried to close doodad {obj_id} as a Coffer"),
        );
        return false;
    };
    let mut d = doodad.data();
    let c = d.coffer.as_mut().expect("coffer");
    if c.opened_by.is_some_and(|id| id != char_id) {
        return false;
    }
    c.opened_by = None;
    true
}

/// `DoodadManager.CloseCoffersOpenedBy(character)` (disconnect / leave world).
pub fn close_coffers_opened_by(server: &GameServer, player: &PlayerRef) {
    let (char_id, instance) = {
        let c = player.data();
        (c.id, c.unit.transform.instance_id)
    };
    let Some(world) = server.world.world(instance) else {
        return;
    };
    for doodad in world.all_doodads() {
        let mut d = doodad.data();
        if let Some(c) = d.coffer.as_mut() {
            if c.opened_by == Some(char_id) {
                c.opened_by = None;
            }
        }
    }
}

/// `CSCofferInteractionPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSCofferInteractionPacket {
    pub coffer_obj_id: u32,
    pub opening: bool,
}

impl ClientPacket for CSCofferInteractionPacket {
    const NAME: &'static str = "CSCofferInteractionPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            coffer_obj_id: s.read_bc()?,
            opening: s.read_bool()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "CofferInteraction, cofferObjId: {}, opening: {}",
                self.coffer_obj_id,
                self.opening
            );
            let Some(me) = conn.active_char.clone() else {
                return Ok(());
            };
            let name = me.data().unit.name.clone();
            if self.opening {
                if !open_coffer_doodad(server, &me, self.coffer_obj_id) {
                    tracing::warn!("{name} failed to Open coffer objId {}", self.coffer_obj_id);
                    // If it failed, the coffer is likely in use by somebody else
                    send_error(&me, err::COFFER_IN_USE);
                }
            } else if !close_coffer_doodad(server, &me, self.coffer_obj_id) {
                tracing::warn!("{name} failed to Close coffer objId {}", self.coffer_obj_id);
            }
            Ok(())
        })
    }
}

/// Coffer C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSCofferInteractionPacket>(LEVEL_PLAIN, cs::CSCofferInteractionPacket);
}
