//! Mate / slave / transfer / shipyard packets (`Core/Packets/C2G`, `Core/Packets/G2C`, 1.2 wire
//! formats). `SCUnitAttachedPacket`, `SCMateStatePacket`, `SCSlaveStatePacket` and
//! `SCShipyardStatePacket` are the object-model ones in `packets::world_objects`.

use aaemu_commons::{helpers, Marshal, PacketStream};

use crate::models::item::Item;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;

use super::{mates, shipyards, slaves};

macro_rules! cs_packet {
    ($(#[$m:meta])* $name:ident { $($(#[$fm:meta])* $f:ident : $t:ty),* $(,)? }
        read |$rs:ident| $read:block
        handle |$self:ident, $server:ident, $conn:ident| $handle:block) => {
        $(#[$m])*
        #[derive(Debug, Clone, PartialEq)]
        pub struct $name { $($(#[$fm])* pub $f: $t),* }
        impl $crate::net::registry::ClientPacket for $name {
            const NAME: &'static str = stringify!($name);
            #[allow(unused_variables)]
            fn read($rs: &mut PacketStream) -> $crate::error::Result<Self> $read
            #[allow(unused_variables)]
            fn handle<'a>(
                $self,
                $server: &'a $crate::server::GameServer,
                $conn: &'a mut $crate::net::connection::GameConnection,
            ) -> futures::future::BoxFuture<'a, $crate::error::Result<()>> {
                Box::pin(async move $handle)
            }
        }
    };
}

/// `Connection.ActiveChar` (the C# throws a `NullReferenceException` without one, which only
/// ends the packet).
macro_rules! active_char {
    ($conn:ident) => {
        match $conn.active_char.clone() {
            Some(p) => p,
            None => return Ok(()),
        }
    };
}

/// S->C packet with a `Marshal` body written by the closure.
macro_rules! sc_packet {
    ($(#[$m:meta])* $name:ident { $($(#[$fm:meta])* $f:ident : $t:ty),* $(,)? } |$self:ident, $s:ident| $body:block) => {
        $(#[$m])*
        #[derive(Debug, Clone, PartialEq)]
        pub struct $name { $($(#[$fm])* pub $f: $t),* }
        packet_def!($name, level = LEVEL_PLAIN, opcode = sc::$name);
        impl Marshal for $name {
            fn write(&$self, $s: &mut PacketStream) $body
        }
    };
}

// ---------------------------------------------------------------------------------------------
// S -> C
// ---------------------------------------------------------------------------------------------

sc_packet!(
    /// `SCMateSpawnedPacket(mate)`.
    SCMateSpawnedPacket {
        tl_id: u16,
        id: u32,
        item_id: u64,
        user_state: u8,
        experience: i32,
        mileage: i32,
        spawn_delay_time: u32,
        skills: Vec<u32>,
    } |self, s| {
        s.write_u16(self.tl_id)
            .write_u32(self.id)
            .write_u64(self.item_id)
            .write_u8(self.user_state)
            .write_i32(self.experience)
            .write_i32(self.mileage)
            .write_u32(self.spawn_delay_time);
        for skill in &self.skills {
            s.write_u32(*skill);
        }
        // `for (i = 0; i < 10 - Skills.Count; i++)`: nothing for 10 skills or more.
        for _ in 0..10i32.saturating_sub(self.skills.len() as i32) {
            s.write_i32(0);
        }
    }
);

/// `ItemAndLocation` of the equipment-changed packets.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct ItemAndLocation {
    pub item: Option<Item>,
    pub slot_type: u8,
    pub slot_number: u8,
}

fn write_item_or_zero(s: &mut PacketStream, item: &Option<Item>) {
    match item {
        Some(i) => {
            i.write(s);
        }
        None => {
            s.write_i32(0);
        }
    }
}

sc_packet!(
    /// `SCMateEquipmentChangedPacket(itemOnPet, itemInBag, mateTlId, characterId, passengerId,
    /// bts, success)` (`_num` is always 1, `success` is always written as `true`).
    SCMateEquipmentChangedPacket {
        item_on_pet: ItemAndLocation,
        item_in_bag: ItemAndLocation,
        mate_tl_id: u16,
        character_id: u32,
        passenger_id: u32,
        bts: bool,
    } |self, s| {
        s.write_u32(self.character_id)
            .write_u16(self.mate_tl_id)
            .write_u32(self.passenger_id)
            .write_bool(self.bts)
            .write_u8(1);
        write_item_or_zero(s, &self.item_on_pet.item);
        write_item_or_zero(s, &self.item_in_bag.item);
        s.write_u8(self.item_on_pet.slot_type)
            .write_u8(self.item_on_pet.slot_number)
            .write_u8(self.item_in_bag.slot_type)
            .write_u8(self.item_in_bag.slot_number)
            .write_bool(true);
    }
);

sc_packet!(
    /// `SCSlaveEquipmentChangedPacket(itemOnSlave, itemInBag, slaveTlId, characterId, dbSlaveId,
    /// bts, success, expireTime)`.
    SCSlaveEquipmentChangedPacket {
        item_on_slave: ItemAndLocation,
        item_in_bag: ItemAndLocation,
        slave_tl_id: u16,
        character_id: u32,
        db_slave_id: u32,
        bts: bool,
        success: bool,
    } |self, s| {
        s.write_u32(self.character_id)
            .write_u16(self.slave_tl_id)
            .write_u32(self.db_slave_id)
            .write_bool(self.bts);
        write_item_or_zero(s, &self.item_on_slave.item);
        write_item_or_zero(s, &self.item_in_bag.item);
        s.write_u8(self.item_on_slave.slot_type)
            .write_u8(self.item_on_slave.slot_number)
            .write_u8(self.item_in_bag.slot_type)
            .write_u8(self.item_in_bag.slot_number)
            .write_bool(self.success);
    }
);

sc_packet!(
    /// `SCUnitDetachedPacket(objId, reason)`.
    SCUnitDetachedPacket { obj_id: u32, reason: u8 } |self, s| {
        s.write_bc(self.obj_id).write_u8(self.reason);
    }
);

sc_packet!(
    /// `SCUnitNameChangedPacket(objId, name)`.
    SCUnitNameChangedPacket { obj_id: u32, name: String } |self, s| {
        s.write_bc(self.obj_id).write_str(&self.name);
    }
);

sc_packet!(
    /// `SCSlaveCreatedPacket(ownerObjId, tlId, slaveObjId, hideSpawnEffect, unkId, creatorName)`.
    SCSlaveCreatedPacket {
        owner_obj_id: u32,
        tl_id: u16,
        slave_obj_id: u32,
        hide_spawn_effect: bool,
        unk_id: i64,
        creator_name: String,
    } |self, s| {
        s.write_bc(self.owner_obj_id)
            .write_u16(self.tl_id)
            .write_bc(self.slave_obj_id)
            .write_bool(self.hide_spawn_effect)
            .write_i64(self.unk_id)
            .write_str(&self.creator_name);
    }
);

sc_packet!(
    /// `SCSlaveRemovedPacket(id, tl)`.
    SCSlaveRemovedPacket { obj_id: u32, tl_id: u16 } |self, s| {
        s.write_bc(self.obj_id).write_u16(self.tl_id);
    }
);

sc_packet!(
    /// `SCSlaveDespawnPacket(id)`.
    SCSlaveDespawnPacket { obj_id: u32 } |self, s| {
        s.write_bc(self.obj_id);
    }
);

sc_packet!(
    /// `SCSlaveBoundPacket(masterId, slaveId)`.
    SCSlaveBoundPacket { master_id: u32, slave_obj_id: u32 } |self, s| {
        s.write_u32(self.master_id).write_bc(self.slave_obj_id);
    }
);

sc_packet!(
    /// `SCMySlavePacket(unitId, tl, slaveName, templateId, hp, maxHp, x, y, z)`.
    SCMySlavePacket {
        obj_id: u32,
        tl_id: u16,
        name: String,
        template_id: u32,
        hp: i32,
        max_hp: i32,
        x: f32,
        y: f32,
        z: f32,
    } |self, s| {
        s.write_bc(self.obj_id)
            .write_u16(self.tl_id)
            .write_str(&self.name)
            .write_u32(self.template_id)
            .write_i32(self.hp)
            .write_i32(self.max_hp)
            .write_i64(helpers::convert_coord_to_long(self.x))
            .write_i64(helpers::convert_coord_to_long(self.y))
            .write_f32(self.z);
    }
);

sc_packet!(
    /// `SCEscapeSlavePacket(unitId, x, y, z, rot)`.
    SCEscapeSlavePacket { obj_id: u32, x: f32, y: f32, z: f32, rot: f32 } |self, s| {
        s.write_bc(self.obj_id)
            .write_i64(helpers::convert_coord_to_long(self.x))
            .write_i64(helpers::convert_coord_to_long(self.y))
            .write_f32(self.z)
            .write_f32(self.rot);
    }
);

sc_packet!(
    /// `SCTransferTelescopeToggledPacket(on, range)`.
    SCTransferTelescopeToggledPacket { on: bool, range: f32 } |self, s| {
        s.write_bool(self.on).write_f32(self.range);
    }
);

/// One `Transfer.WriteTelescopeUnit` entry.
#[derive(Debug, Clone, PartialEq)]
pub struct TelescopeUnit {
    pub obj_id: u32,
    pub template_id: u32,
    /// `Transform.World.Position`.
    pub pos: (f32, f32, f32),
    pub name: String,
}

sc_packet!(
    /// `SCTransferTelescopeUnitsPacket(last, transfers)`.
    SCTransferTelescopeUnitsPacket { last: bool, transfers: Vec<TelescopeUnit> } |self, s| {
        s.write_bool(self.last).write_u8(self.transfers.len() as u8);
        for t in &self.transfers {
            s.write_bc(t.obj_id)
                .write_u32(t.template_id)
                .write_position(t.pos.0, t.pos.1, t.pos.2)
                .write_str(&t.name);
        }
    }
);

sc_packet!(
    /// `SCUnhungPacket(unitObjId, targetObjId, reason)`.
    SCUnhungPacket { unit_obj_id: u32, target_obj_id: u32, reason: u32 } |self, s| {
        s.write_bc(self.unit_obj_id)
            .write_bc(self.target_obj_id)
            .write_u32(self.reason);
    }
);

/// `ItemTaskType` values this subsystem sends.
pub mod item_task_type {
    pub const MATE_DEATH: u8 = 59;
    pub const SHIPYARD: u8 = 60;
    pub const REPAIR_SLAVES: u8 = 74;
    pub const UPDATE_SUMMON_MATE_ITEM: u8 = 82;
}

/// `ItemAction.UpdateDetail`.
const ITEM_ACTION_UPDATE_DETAIL: u8 = 9;

/// One `ItemUpdate` task (`SlotType`, `Slot`, `Id`, the 128-byte detail block).
#[derive(Debug, Clone, PartialEq)]
pub struct ItemUpdateTask {
    pub slot_type: u8,
    pub slot: u8,
    pub item_id: u64,
    /// `DetailType` + `WriteDetails` (at most 128 bytes).
    pub details: Vec<u8>,
}

impl ItemUpdateTask {
    /// `new ItemUpdate(item)`.
    pub fn of(item: &Item) -> Self {
        Self {
            slot_type: item.slot_type,
            slot: item.slot as u8,
            item_id: item.id,
            details: item.detail_type_and_details(),
        }
    }
}

sc_packet!(
    /// `SCItemTaskSuccessPacket(action, [ItemUpdate...], [])` (the item-update form this
    /// subsystem sends; the generic item task packet belongs to the economy subsystem).
    SCItemTaskSuccessPacket { action: u8, tasks: Vec<ItemUpdateTask> } |self, s| {
        s.write_u8(self.action).write_u8(self.tasks.len() as u8);
        for t in &self.tasks {
            s.write_u8(ITEM_ACTION_UPDATE_DETAIL)
                .write_u8(t.slot_type)
                .write_u8(t.slot)
                .write_u64(t.item_id)
                .write_i16(128)
                .write_raw(&t.details);
            s.write_raw(&vec![0u8; 128usize.saturating_sub(t.details.len())]);
        }
        s.write_u8(0) // forceRemove
            .write_u32(0); // type(id)
    }
);

// ---------------------------------------------------------------------------------------------
// C -> S
// ---------------------------------------------------------------------------------------------

/// `Item.Read` of an `EquipItem` (the client copy is discarded by the C# handler): the template
/// id (0 = nothing else follows).
fn read_equip_item(s: &mut PacketStream) -> crate::error::Result<u32> {
    let template_id = s.read_u32()?;
    if template_id == 0 {
        return Ok(0);
    }
    s.read_u64()?; // Id
    s.read_u8()?; // Grade
    s.read_u8()?; // ItemFlags
    s.read_i32()?; // Count
    s.read_u8()?; // DetailType
    if s.remaining() >= 55 {
        s.read_bytes(55)?; // EquipItem.ReadDetails
    }
    s.read_i64()?; // CreateTime
    s.read_i32()?; // LifespanMins
    s.read_u32()?; // MadeUnitId
    s.read_u8()?; // WorldId
    s.read_i64()?; // UnsecureTime
    s.read_i64()?; // UnpackTime
    Ok(template_id)
}

cs_packet!(
    /// `CSMountMatePacket`.
    CSMountMatePacket { tl_id: u16, ap: u8, reason: u8 }
    read |s| { Ok(Self { tl_id: s.read_u16()?, ap: s.read_u8()?, reason: s.read_u8()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        mates::mount_mate(server, &p, u32::from(self.tl_id), self.ap, self.reason);
        Ok(())
    }
);

cs_packet!(
    /// `CSUnMountMatePacket`.
    CSUnMountMatePacket { tl_id: u16, ap: u8, reason: u8 }
    read |s| { Ok(Self { tl_id: s.read_u16()?, ap: s.read_u8()?, reason: s.read_u8()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        mates::unmount_mate(server, &p, u32::from(self.tl_id), self.ap, self.reason);
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeMateNamePacket`.
    CSChangeMateNamePacket { tl_id: u16, name: String }
    read |s| { Ok(Self { tl_id: s.read_u16()?, name: s.read_string()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        mates::rename_mount(server, &p, u32::from(self.tl_id), &self.name);
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeMateTargetPacket`.
    CSChangeMateTargetPacket { tl_id: u16, obj_id: u32 }
    read |s| { Ok(Self { tl_id: s.read_u16()?, obj_id: s.read_bc()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        mates::change_target_mate(server, &p, u32::from(self.tl_id), self.obj_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeMateUserStatePacket`.
    CSChangeMateUserStatePacket { tl_id: u16, user_state: u8 }
    read |s| { Ok(Self { tl_id: s.read_u16()?, user_state: s.read_u8()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        mates::change_state_mate(server, &p, u32::from(self.tl_id), self.user_state);
        Ok(())
    }
);

cs_packet!(
    /// `CSRemoveMatePacket`.
    CSRemoveMatePacket { tl_id: u16 }
    read |s| { Ok(Self { tl_id: s.read_u16()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        mates::despawn_mate(server, &p, u32::from(self.tl_id));
        Ok(())
    }
);

/// One item move of `CSChangeMateEquipmentPacket` (client item copies are discarded).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct MateEquipmentMove {
    pub player_item_template_id: u32,
    pub mate_item_template_id: u32,
    pub player_slot_type: u8,
    pub player_slot: u8,
    pub mate_slot_type: u8,
    pub mate_slot: u8,
}

cs_packet!(
    /// `CSChangeMateEquipmentPacket`.
    CSChangeMateEquipmentPacket {
        owning_player_id: u32,
        mate_tl: u16,
        passenger_player_id: u32,
        bts: bool,
        moves: Vec<MateEquipmentMove>,
    }
    read |s| {
        let owning_player_id = s.read_u32()?;
        let mate_tl = s.read_u16()?;
        let passenger_player_id = s.read_u32()?;
        let bts = s.read_bool()?;
        let item_count = s.read_u8()?;
        // The handler reads the items lazily (after the mate lookup); a missing mate ends the
        // packet before the item data is touched.
        let mut moves = Vec::new();
        let rest = s.read_bytes(s.remaining())?;
        let mut rs = PacketStream::from_bytes(rest);
        for _ in 0..item_count {
            let r = (|| -> crate::error::Result<MateEquipmentMove> {
                let player_item_template_id = read_equip_item(&mut rs)?;
                let mate_item_template_id = read_equip_item(&mut rs)?;
                Ok(MateEquipmentMove {
                    player_item_template_id,
                    mate_item_template_id,
                    player_slot_type: rs.read_u8()?,
                    player_slot: rs.read_u8()?,
                    mate_slot_type: rs.read_u8()?,
                    mate_slot: rs.read_u8()?,
                })
            })();
            match r {
                Ok(m) => moves.push(m),
                Err(_) => break,
            }
        }
        Ok(Self { owning_player_id, mate_tl, passenger_player_id, bts, moves })
    }
    handle |self, server, conn| {
        let p = active_char!(conn);
        mates::change_mate_equipment(server, &p, &self);
        Ok(())
    }
);

cs_packet!(
    /// `CSBindSlavePacket`.
    CSBindSlavePacket { tl_id: u16 }
    read |s| { Ok(Self { tl_id: s.read_u16()? }) }
    handle |self, server, conn| {
        tracing::debug!("BindSlave, Tl: {}", self.tl_id);
        let p = active_char!(conn);
        slaves::bind_slave_by_tl(server, &p, u32::from(self.tl_id));
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeSlaveNamePacket` (log only in C#).
    CSChangeSlaveNamePacket { tl_id: u16, name: String }
    read |s| { Ok(Self { tl_id: s.read_u16()?, name: s.read_string()? }) }
    handle |self, server, conn| {
        tracing::debug!("ChangeSlaveName, Tl: {}, Name: {}", self.tl_id, self.name);
        Ok(())
    }
);

cs_packet!(
    /// `CSDespawnSlavePacket`.
    CSDespawnSlavePacket { slave_obj_id: u32 }
    read |s| { Ok(Self { slave_obj_id: s.read_bc()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        slaves::delete(server, Some(&p), p.data().unit.transform.instance_id, self.slave_obj_id, false);
        Ok(())
    }
);

cs_packet!(
    /// `CSDestroySlavePacket`.
    CSDestroySlavePacket { tl_id: u16 }
    read |s| { Ok(Self { tl_id: s.read_u16()? }) }
    handle |self, server, conn| {
        tracing::debug!("DestroySlave, Tl: {}", self.tl_id);
        let p = active_char!(conn);
        slaves::remove_active_slave(server, &p, self.tl_id, false);
        Ok(())
    }
);

cs_packet!(
    /// `CSDiscardSlavePacket`.
    CSDiscardSlavePacket { tl_id: u16 }
    read |s| { Ok(Self { tl_id: s.read_u16()? }) }
    handle |self, server, conn| {
        tracing::debug!("DiscardSlave, Tl: {}", self.tl_id);
        let p = active_char!(conn);
        slaves::unbind_slave(server, &p, u32::from(self.tl_id), slaves::attach_reason::SLAVE_BINDING);
        Ok(())
    }
);

cs_packet!(
    /// `CSSpawnSlavePacket` (log only in C#).
    CSSpawnSlavePacket {
        slave_id: u32,
        x: f32,
        y: f32,
        z: f32,
        z_rot: f32,
        item_id: u64,
        slot_type: u8,
        slot: u8,
        hide_spawn_effect: bool,
    }
    read |s| {
        let slave_id = s.read_u32()?;
        let x = helpers::convert_long_to_coord(s.read_i64()?);
        let y = helpers::convert_long_to_coord(s.read_i64()?);
        let z = s.read_f32()?;
        let z_rot = s.read_f32()?;
        let item_id = s.read_u64()?;
        s.read_u8()?;
        let slot_type = s.read_u8()?;
        s.read_u8()?;
        let slot = s.read_u8()?;
        let hide_spawn_effect = s.read_bool()?;
        Ok(Self { slave_id, x, y, z, z_rot, item_id, slot_type, slot, hide_spawn_effect })
    }
    handle |self, server, conn| {
        tracing::debug!("SpawnSlave, SlaveId: {}", self.slave_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSRepairSlaveItemsPacket` (log only in C#).
    CSRepairSlaveItemsPacket { npc_id: u32 }
    read |s| { Ok(Self { npc_id: s.read_bc()? }) }
    handle |self, server, conn| {
        tracing::debug!("RepairSlaveItems, NpcId: {}", self.npc_id);
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeSlaveEquipmentPacket` (log only in C#).
    CSChangeSlaveEquipmentPacket { id: u32, tl: u16, db_slave_id: u32, bts: bool }
    read |s| {
        Ok(Self { id: s.read_u32()?, tl: s.read_u16()?, db_slave_id: s.read_u32()?, bts: s.read_bool()? })
    }
    handle |self, server, conn| {
        tracing::debug!(
            "ChangeSlaveEquipment, Id: {}, Tl: {}, DbSlaveId: {}, Bts: {}",
            self.id, self.tl, self.db_slave_id, self.bts
        );
        Ok(())
    }
);

cs_packet!(
    /// `CSBoardingTransferPacket` (log only in C#).
    CSBoardingTransferPacket { tl: u16, ap: u8 }
    read |s| { Ok(Self { tl: s.read_u16()?, ap: s.read_u8()? }) }
    handle |self, server, conn| {
        tracing::warn!("BoardingTransfer, Tl: {}, Ap: {}", self.tl, self.ap);
        Ok(())
    }
);

cs_packet!(
    /// `CSCreateShipyardPacket`.
    CSCreateShipyardPacket {
        id: u32,
        x: f32,
        y: f32,
        z: f32,
        z_rot: f32,
        design_item: u64,
        aabb: [f32; 6],
        auto_use_aa_point: bool,
    }
    read |s| {
        let id = s.read_u32()?;
        let x = helpers::convert_long_to_coord(s.read_i64()?);
        let y = helpers::convert_long_to_coord(s.read_i64()?);
        let z = s.read_f32()?;
        let z_rot = s.read_f32()?;
        let design_item = s.read_u64()?;
        let mut aabb = [0f32; 6];
        for v in aabb.iter_mut() {
            *v = s.read_f32()?;
        }
        let auto_use_aa_point = s.read_bool()?;
        Ok(Self { id, x, y, z, z_rot, design_item, aabb, auto_use_aa_point })
    }
    handle |self, server, conn| {
        tracing::warn!(
            "CreateShipyard, Id: {}, X: {}, Y: {}, Z: {}, DesignItem: {}",
            self.id, self.x, self.y, self.z, self.design_item
        );
        // `Connection.ActiveChar` null throws in `Create` (only this packet ends).
        let p = active_char!(conn);
        shipyards::create(server, &p, self.id, self.x, self.y, self.z, self.z_rot, self.design_item, 0);
        Ok(())
    }
);

cs_packet!(
    /// `CSUnhangPacket` (1.2: no target obj id on the wire).
    CSUnhangPacket { unit_obj_id: u32, reason: u32 }
    read |s| { Ok(Self { unit_obj_id: s.read_bc()?, reason: s.read_u32()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        super::harpoon::unhang(server, &p, self.unit_obj_id, self.reason);
        Ok(())
    }
);

/// Registers this subsystem's C->S packets. `CSChangeSlaveTargetPacket` has no 1.2 opcode
/// (`0xfff`) and is not registered.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSMountMatePacket>(LEVEL_PLAIN, cs::CSMountMatePacket);
    reg.register::<CSUnMountMatePacket>(LEVEL_PLAIN, cs::CSUnMountMatePacket);
    reg.register::<CSChangeMateNamePacket>(LEVEL_PLAIN, cs::CSChangeMateNamePacket);
    reg.register::<CSChangeMateTargetPacket>(LEVEL_PLAIN, cs::CSChangeMateTargetPacket);
    reg.register::<CSChangeMateUserStatePacket>(LEVEL_PLAIN, cs::CSChangeMateUserStatePacket);
    reg.register::<CSRemoveMatePacket>(LEVEL_PLAIN, cs::CSRemoveMatePacket);
    reg.register::<CSChangeMateEquipmentPacket>(LEVEL_PLAIN, cs::CSChangeMateEquipmentPacket);
    reg.register::<CSBindSlavePacket>(LEVEL_PLAIN, cs::CSBindSlavePacket);
    reg.register::<CSChangeSlaveNamePacket>(LEVEL_PLAIN, cs::CSChangeSlaveNamePacket);
    reg.register::<CSDespawnSlavePacket>(LEVEL_PLAIN, cs::CSDespawnSlavePacket);
    reg.register::<CSDestroySlavePacket>(LEVEL_PLAIN, cs::CSDestroySlavePacket);
    reg.register::<CSDiscardSlavePacket>(LEVEL_PLAIN, cs::CSDiscardSlavePacket);
    reg.register::<CSSpawnSlavePacket>(LEVEL_PLAIN, cs::CSSpawnSlavePacket);
    reg.register::<CSRepairSlaveItemsPacket>(LEVEL_PLAIN, cs::CSRepairSlaveItemsPacket);
    reg.register::<CSChangeSlaveEquipmentPacket>(LEVEL_PLAIN, cs::CSChangeSlaveEquipmentPacket);
    reg.register::<CSBoardingTransferPacket>(LEVEL_PLAIN, cs::CSBoardingTransferPacket);
    reg.register::<CSCreateShipyardPacket>(LEVEL_PLAIN, cs::CSCreateShipyardPacket);
    reg.register::<CSUnhangPacket>(LEVEL_PLAIN, cs::CSUnhangPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;
    use crate::net::registry::ClientPacket;

    fn frame(op: u16, body: &[u8]) -> Vec<u8> {
        let mut v = vec![0xDD, 0x01, 0, 0];
        v.extend_from_slice(&op.to_le_bytes());
        v.extend_from_slice(body);
        v
    }

    fn bc(v: u32) -> Vec<u8> {
        v.to_le_bytes()[..3].to_vec()
    }

    #[test]
    fn mate_spawned_pads_skills_to_ten() {
        let p = SCMateSpawnedPacket {
            tl_id: 0x0102,
            id: 7,
            item_id: 0x1122334455667788,
            user_state: 1,
            experience: 300,
            mileage: 5,
            spawn_delay_time: 0,
            skills: vec![11, 12],
        };
        let mut b = vec![0x02, 0x01];
        b.extend(7u32.to_le_bytes());
        b.extend(0x1122334455667788u64.to_le_bytes());
        b.push(1);
        b.extend(300i32.to_le_bytes());
        b.extend(5i32.to_le_bytes());
        b.extend(0u32.to_le_bytes());
        b.extend(11u32.to_le_bytes());
        b.extend(12u32.to_le_bytes());
        b.extend([0u8; 32]);
        assert_eq!(encode(&p), frame(sc::SCMateSpawnedPacket, &b));
        // 10+ skills: no padding (C# loop runs 0 times).
        let p2 = SCMateSpawnedPacket {
            skills: (0..11).collect(),
            ..p
        };
        assert_eq!(encode(&p2).len(), 6 + 2 + 4 + 8 + 1 + 4 + 4 + 4 + 44);
    }

    #[test]
    fn slave_packets_bytes() {
        let p = SCSlaveCreatedPacket {
            owner_obj_id: 0x0A0B0C,
            tl_id: 3,
            slave_obj_id: 0x010203,
            hide_spawn_effect: true,
            unk_id: 0,
            creator_name: "Ab".into(),
        };
        let mut b = bc(0x0A0B0C);
        b.extend([3, 0]);
        b.extend(bc(0x010203));
        b.push(1);
        b.extend(0i64.to_le_bytes());
        b.extend([2, 0, b'A', b'b']);
        assert_eq!(encode(&p), frame(sc::SCSlaveCreatedPacket, &b));

        let p = SCSlaveRemovedPacket {
            obj_id: 0x55,
            tl_id: 9,
        };
        let mut b = bc(0x55);
        b.extend([9, 0]);
        assert_eq!(encode(&p), frame(sc::SCSlaveRemovedPacket, &b));
        assert_eq!(
            encode(&SCSlaveDespawnPacket { obj_id: 0x66 }),
            frame(sc::SCSlaveDespawnPacket, &bc(0x66))
        );
        let mut b = 0x1234u32.to_le_bytes().to_vec();
        b.extend(bc(0x77));
        assert_eq!(
            encode(&SCSlaveBoundPacket {
                master_id: 0x1234,
                slave_obj_id: 0x77
            }),
            frame(sc::SCSlaveBoundPacket, &b)
        );

        let p = SCMySlavePacket {
            obj_id: 0x10,
            tl_id: 2,
            name: "S".into(),
            template_id: 68,
            hp: 100,
            max_hp: 200,
            x: 1.0,
            y: 2.0,
            z: 3.5,
        };
        let mut b = bc(0x10);
        b.extend([2, 0, 1, 0, b'S']);
        b.extend(68u32.to_le_bytes());
        b.extend(100i32.to_le_bytes());
        b.extend(200i32.to_le_bytes());
        b.extend(helpers::convert_coord_to_long(1.0).to_le_bytes());
        b.extend(helpers::convert_coord_to_long(2.0).to_le_bytes());
        b.extend(3.5f32.to_le_bytes());
        assert_eq!(encode(&p), frame(sc::SCMySlavePacket, &b));

        let p = SCEscapeSlavePacket {
            obj_id: 1,
            x: 4.0,
            y: 5.0,
            z: 6.0,
            rot: 0.5,
        };
        let mut b = bc(1);
        b.extend(helpers::convert_coord_to_long(4.0).to_le_bytes());
        b.extend(helpers::convert_coord_to_long(5.0).to_le_bytes());
        b.extend(6.0f32.to_le_bytes());
        b.extend(0.5f32.to_le_bytes());
        assert_eq!(encode(&p), frame(sc::SCEscapeSlavePacket, &b));
    }

    #[test]
    fn attach_detach_name_unhung_bytes() {
        let mut b = bc(0x99);
        b.push(5);
        assert_eq!(
            encode(&SCUnitDetachedPacket {
                obj_id: 0x99,
                reason: 5
            }),
            frame(sc::SCUnitDetachedPacket, &b)
        );
        let mut b = bc(0x42);
        b.extend([3, 0, b'P', b'e', b't']);
        assert_eq!(
            encode(&SCUnitNameChangedPacket {
                obj_id: 0x42,
                name: "Pet".into()
            }),
            frame(sc::SCUnitNameChangedPacket, &b)
        );
        let mut b = bc(1);
        b.extend(bc(2));
        b.extend(7u32.to_le_bytes());
        assert_eq!(
            encode(&SCUnhungPacket {
                unit_obj_id: 1,
                target_obj_id: 2,
                reason: 7
            }),
            frame(sc::SCUnhungPacket, &b)
        );
    }

    #[test]
    fn telescope_packets_bytes() {
        assert_eq!(
            encode(&SCTransferTelescopeToggledPacket {
                on: true,
                range: 100.0
            }),
            frame(sc::SCTransferTelescopeToggledPacket, &{
                let mut b = vec![1];
                b.extend(100f32.to_le_bytes());
                b
            })
        );
        let p = SCTransferTelescopeUnitsPacket {
            last: true,
            transfers: vec![TelescopeUnit {
                obj_id: 5,
                template_id: 6,
                pos: (10.0, 20.0, 30.0),
                name: "C".into(),
            }],
        };
        let mut b = vec![1, 1];
        b.extend(bc(5));
        b.extend(6u32.to_le_bytes());
        b.extend(helpers::encode_position(10.0, 20.0, 30.0));
        b.extend([1, 0, b'C']);
        assert_eq!(encode(&p), frame(sc::SCTransferTelescopeUnitsPacket, &b));
    }

    #[test]
    fn equipment_changed_bytes() {
        let p = SCMateEquipmentChangedPacket {
            item_on_pet: ItemAndLocation {
                item: None,
                slot_type: 252,
                slot_number: 3,
            },
            item_in_bag: ItemAndLocation {
                item: None,
                slot_type: 2,
                slot_number: 7,
            },
            mate_tl_id: 0x0203,
            character_id: 0x11,
            passenger_id: 0,
            bts: false,
        };
        let mut b = 0x11u32.to_le_bytes().to_vec();
        b.extend([3, 2]);
        b.extend(0u32.to_le_bytes());
        b.push(0); // bts
        b.push(1); // num
        b.extend(0i32.to_le_bytes());
        b.extend(0i32.to_le_bytes());
        b.extend([252, 3, 2, 7, 1]);
        assert_eq!(encode(&p), frame(sc::SCMateEquipmentChangedPacket, &b));

        let p = SCSlaveEquipmentChangedPacket {
            item_on_slave: ItemAndLocation::default(),
            item_in_bag: ItemAndLocation::default(),
            slave_tl_id: 1,
            character_id: 2,
            db_slave_id: 3,
            bts: true,
            success: false,
        };
        let mut b = 2u32.to_le_bytes().to_vec();
        b.extend([1, 0]);
        b.extend(3u32.to_le_bytes());
        b.push(1);
        b.extend(0i32.to_le_bytes());
        b.extend(0i32.to_le_bytes());
        b.extend([0, 0, 0, 0, 0]);
        assert_eq!(encode(&p), frame(sc::SCSlaveEquipmentChangedPacket, &b));
    }

    #[test]
    fn item_task_success_update_detail_bytes() {
        let p = SCItemTaskSuccessPacket {
            action: item_task_type::UPDATE_SUMMON_MATE_ITEM,
            tasks: vec![ItemUpdateTask {
                slot_type: 2,
                slot: 4,
                item_id: 0x0102,
                details: vec![3, 0x2C, 1, 0, 0, 0, 5],
            }],
        };
        let mut b = vec![82, 1, 9, 2, 4];
        b.extend(0x0102u64.to_le_bytes());
        b.extend(128i16.to_le_bytes());
        b.extend([3, 0x2C, 1, 0, 0, 0, 5]);
        b.extend(vec![0u8; 121]);
        b.push(0);
        b.extend(0u32.to_le_bytes());
        assert_eq!(encode(&p), frame(sc::SCItemTaskSuccessPacket, &b));
    }

    #[test]
    fn decode_client_packets() {
        let mut s = PacketStream::from_bytes(vec![0x05, 0x00, 0x01, 0x04]);
        let p = CSMountMatePacket::read(&mut s).unwrap();
        assert_eq!((p.tl_id, p.ap, p.reason), (5, 1, 4));

        let mut s = PacketStream::from_bytes(vec![0x07, 0x00, 0x30, 0x20, 0x10]);
        let p = CSChangeMateTargetPacket::read(&mut s).unwrap();
        assert_eq!((p.tl_id, p.obj_id), (7, 0x102030));

        let mut body = 3u32.to_le_bytes().to_vec();
        body.extend(helpers::convert_coord_to_long(100.0).to_le_bytes());
        body.extend(helpers::convert_coord_to_long(200.0).to_le_bytes());
        body.extend(50f32.to_le_bytes());
        body.extend(1.5f32.to_le_bytes());
        body.extend(99u64.to_le_bytes());
        for i in 0..6 {
            body.extend((i as f32).to_le_bytes());
        }
        body.push(1);
        let mut s = PacketStream::from_bytes(body);
        let p = CSCreateShipyardPacket::read(&mut s).unwrap();
        assert_eq!(p.id, 3);
        assert_eq!((p.x, p.y, p.z, p.z_rot), (100.0, 200.0, 50.0, 1.5));
        assert_eq!(p.design_item, 99);
        assert!(p.auto_use_aa_point);
        assert_eq!(s.remaining(), 0);

        // CSChangeMateEquipment: two empty items + slots.
        let mut body = 10u32.to_le_bytes().to_vec();
        body.extend([4, 0]);
        body.extend(0u32.to_le_bytes());
        body.push(0);
        body.push(1);
        body.extend(0u32.to_le_bytes());
        body.extend(0u32.to_le_bytes());
        body.extend([2, 5, 252, 1]);
        let mut s = PacketStream::from_bytes(body);
        let p = CSChangeMateEquipmentPacket::read(&mut s).unwrap();
        assert_eq!(p.mate_tl, 4);
        assert_eq!(
            p.moves,
            vec![MateEquipmentMove {
                player_item_template_id: 0,
                mate_item_template_id: 0,
                player_slot_type: 2,
                player_slot: 5,
                mate_slot_type: 252,
                mate_slot: 1
            }]
        );

        let mut body = bc(0x0A);
        body.extend(2u32.to_le_bytes());
        let mut s = PacketStream::from_bytes(body);
        let p = CSUnhangPacket::read(&mut s).unwrap();
        assert_eq!((p.unit_obj_id, p.reason), (0x0A, 2));
    }
}
