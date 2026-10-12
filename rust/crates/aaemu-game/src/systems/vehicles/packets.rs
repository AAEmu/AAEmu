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

sc_packet!(
    /// `SCUnitDetachedPacket(objId, reason)`.
    SCUnitDetachedPacket { obj_id: u32, reason: u8 } |self, s| {
        s.write_bc(self.obj_id).write_u8(self.reason);
    }
);

/// `SCUnitNameChangedPacket`: the social subsystem's definition (one per opcode).
pub use crate::systems::social::packets::misc::SCUnitNameChangedPacket;

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

/// `SCTransferTelescopeToggledPacket` / `SCTransferTelescopeUnitsPacket`: the services
/// subsystem's definitions (radar; one per opcode).
pub use crate::systems::services::radar::{
    SCTransferTelescopeToggledPacket, SCTransferTelescopeUnitsPacket,
    TransferTelescopeUnit as TelescopeUnit,
};

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

/// `SCItemTaskSuccessPacket`: the economy subsystem's definition (one per opcode); this
/// subsystem sends its `ItemUpdate` form.
pub use crate::systems::economy::item_packets::SCItemTaskSuccessPacket;

/// `new ItemUpdate(item)`.
pub fn item_update(item: &Item) -> crate::models::item_task::ItemTask {
    crate::models::item_task::ItemTask::ItemUpdate(Box::new(item.clone()))
}

// ---------------------------------------------------------------------------------------------
// C -> S
// ---------------------------------------------------------------------------------------------

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
    /// `CSChangeSlaveNamePacket`.
    CSChangeSlaveNamePacket { tl_id: u16, name: String }
    read |s| { Ok(Self { tl_id: s.read_u16()?, name: s.read_string()? }) }
    handle |self, server, conn| {
        tracing::debug!("ChangeSlaveName, Tl: {}, Name: {}", self.tl_id, self.name);
        let p = active_char!(conn);
        slaves::rename_slave(server, &p, self.tl_id, &self.name);
        Ok(())
    }
);

cs_packet!(
    /// `CSDespawnSlavePacket`.
    CSDespawnSlavePacket { slave_obj_id: u32 }
    read |s| { Ok(Self { slave_obj_id: s.read_bc()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        let instance_id = p.data().unit.transform.instance_id;
        slaves::delete(server, Some(&p), instance_id, self.slave_obj_id, false);
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

cs_packet!(
    /// `CSHangPacket`.
    CSHangPacket { unit_obj_id: u32, target_obj_id: u32 }
    read |s| { Ok(Self { unit_obj_id: s.read_bc()?, target_obj_id: s.read_bc()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        super::harpoon::hang(server, &p, self.unit_obj_id, self.target_obj_id);
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
    reg.register::<CSBindSlavePacket>(LEVEL_PLAIN, cs::CSBindSlavePacket);
    reg.register::<CSChangeSlaveNamePacket>(LEVEL_PLAIN, cs::CSChangeSlaveNamePacket);
    reg.register::<CSDespawnSlavePacket>(LEVEL_PLAIN, cs::CSDespawnSlavePacket);
    reg.register::<CSDestroySlavePacket>(LEVEL_PLAIN, cs::CSDestroySlavePacket);
    reg.register::<CSDiscardSlavePacket>(LEVEL_PLAIN, cs::CSDiscardSlavePacket);
    reg.register::<CSSpawnSlavePacket>(LEVEL_PLAIN, cs::CSSpawnSlavePacket);
    reg.register::<CSBoardingTransferPacket>(LEVEL_PLAIN, cs::CSBoardingTransferPacket);
    reg.register::<CSCreateShipyardPacket>(LEVEL_PLAIN, cs::CSCreateShipyardPacket);
    reg.register::<CSUnhangPacket>(LEVEL_PLAIN, cs::CSUnhangPacket);
    reg.register::<CSHangPacket>(LEVEL_PLAIN, cs::CSHangPacket);
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

        let mut body = bc(0x0A);
        body.extend(2u32.to_le_bytes());
        let mut s = PacketStream::from_bytes(body);
        let p = CSUnhangPacket::read(&mut s).unwrap();
        assert_eq!((p.unit_obj_id, p.reason), (0x0A, 2));
    }
}
