//! Doodad packets (`Core/Packets/G2C/SCDoodad*`, `SCBondDoodadPacket`, `SCUnbondDoodadPacket`,
//! and the doodad C->S packets of `Core/Packets/C2G`).
//!
//! `SCDoodadCreated/sCreated/Removed/sRemoved` live in `packets::world_objects` (visibility).
//! `CSSaveDoodadUccStringPacket` is decoded but not registered: the 1.2 C# does not register it
//! (`0x0e3` is commented out, the opcode table has no value for it).
//! `CSLootOpenBagPacket` belongs to the loot code (economy); its doodad part is
//! [`super::manager::loot_open_bag_doodad`].

use aaemu_commons::{helpers, Marshal, PacketStream};
use futures::future::BoxFuture;

use crate::error::Result;
use crate::models::doodad::{BondDoodad, Doodad};
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::world::WorldObject;

/// `ErrorMessageType` values used by the doodad code.
pub mod error_message {
    pub const NO_PERM: i16 = 2;
    pub const NO_INTERACTION_AVAILABLE: i16 = 3;
    pub const LABOR_POWER_NEEDED: i16 = 9;
    pub const INTERACTION_PERMISSION_DENY: i16 = 232;
    pub const FAMILY_NOT_EXIST: i16 = 376;
    pub const ONLY_EXPEDITION_MEMBER: i16 = 808;
}

/// `Character.SendErrorMessage(type)` (`type2 = 0`, `isNotify = true`).
pub fn error_msg(error: i16) -> SCErrorMsgPacket {
    SCErrorMsgPacket::new(error, 0, true)
}

// ---------------------------------------------------------------------------------------------
// S->C
// ---------------------------------------------------------------------------------------------

/// `SCDoodadPhaseChangedPacket(doodad)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCDoodadPhaseChangedPacket {
    pub obj_id: u32,
    pub func_group_id: u32,
    pub time_left: u32,
    pub item_template_id: u32,
}
impl SCDoodadPhaseChangedPacket {
    /// Built from the doodad's state (take it while holding the doodad's lock).
    pub fn new(obj_id: u32, d: &Doodad, now_ms: i64) -> Self {
        Self {
            obj_id,
            func_group_id: d.func_group_id,
            time_left: d.time_left(now_ms),
            item_template_id: d.item_template_id,
        }
    }
}
packet_def!(
    SCDoodadPhaseChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDoodadPhaseChangedPacket
);
impl Marshal for SCDoodadPhaseChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u32(self.func_group_id)
            .write_u32(self.time_left) // growing
            .write_i32(-1) // puzzleGroup
            .write_u32(self.item_template_id);
    }
}

/// `SCDoodadSoundPacket(doodad, soundId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCDoodadSoundPacket {
    pub obj_id: u32,
    pub sound_id: u32,
}
packet_def!(
    SCDoodadSoundPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDoodadSoundPacket
);
impl Marshal for SCDoodadSoundPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_u32(self.sound_id);
    }
}

/// `SCDoodadChangedPacket(id, data)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCDoodadChangedPacket {
    pub obj_id: u32,
    pub data: i32,
}
packet_def!(
    SCDoodadChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDoodadChangedPacket
);
impl Marshal for SCDoodadChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_i32(self.data);
    }
}

/// `SCDoodadOriginatorPacket(objId, newOwnerId, newFaction)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCDoodadOriginatorPacket {
    pub obj_id: u32,
    pub new_owner_id: u32,
    /// `FactionsEnum`.
    pub new_faction: u32,
}
packet_def!(
    SCDoodadOriginatorPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDoodadOriginatorPacket
);
impl Marshal for SCDoodadOriginatorPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u32(self.new_owner_id)
            .write_u32(self.new_faction);
    }
}

/// `SCDoodadQuestAcceptPacket`: the quest subsystem's definition (one per opcode).
pub use crate::systems::quests::packets::SCDoodadQuestAcceptPacket;

/// `SCBondDoodadPacket(unitObjId, bond)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCBondDoodadPacket {
    pub unit_obj_id: u32,
    pub bond: BondDoodad,
}
packet_def!(
    SCBondDoodadPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCBondDoodadPacket
);
impl Marshal for SCBondDoodadPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.unit_obj_id);
        self.bond.write(s);
    }
}

/// `SCUnbondDoodadPacket(characterObjId, characterId, doodadObjId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCUnbondDoodadPacket {
    pub character_obj_id: u32,
    pub character_id: u32,
    pub doodad_obj_id: u32,
}
packet_def!(
    SCUnbondDoodadPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnbondDoodadPacket
);
impl Marshal for SCUnbondDoodadPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.character_obj_id)
            .write_u32(self.character_id)
            .write_bc(self.doodad_obj_id);
    }
}

// ---------------------------------------------------------------------------------------------
// C->S
// ---------------------------------------------------------------------------------------------

/// Registers the doodad C->S packets (`GameNetwork` order).
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSUnbondDoodadPacket>(LEVEL_PLAIN, cs::CSUnbondDoodadPacket);
    reg.register::<CSCreateDoodadPacket>(LEVEL_PLAIN, cs::CSCreateDoodadPacket);
    reg.register::<CSChangeDoodadPhasePacket>(LEVEL_PLAIN, cs::CSChangeDoodadPhasePacket);
    reg.register::<CSChangeDoodadDataPacket>(LEVEL_PLAIN, cs::CSChangeDoodadDataPacket);
    reg.register::<CSSetLogicDoodadPacket>(LEVEL_PLAIN, cs::CSSetLogicDoodadPacket);
}

/// `CSChangeDoodadDataPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSChangeDoodadDataPacket {
    pub obj_id: u32,
    pub data: i32,
}
impl ClientPacket for CSChangeDoodadDataPacket {
    const NAME: &'static str = "CSChangeDoodadDataPacket";
    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            data: s.read_i32()?,
        })
    }
    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "ChangeDoodadData, ObjId: {}, Data: {}",
                self.obj_id,
                self.data
            );
            let Some(me) = conn.active_char.clone() else {
                return Ok(());
            };
            let Some(world) = server.world.world_of(&WorldObject::from(&me)) else {
                return Ok(());
            };
            if let Some(doodad) = world.doodad(self.obj_id) {
                let template_id = doodad.data().template_id;
                tracing::warn!("Doodad: ({template_id})");
                if !super::manager::change_doodad_data(server, &world, &me, &doodad, self.data) {
                    me.send(error_msg(error_message::INTERACTION_PERMISSION_DENY));
                }
            }
            Ok(())
        })
    }
}

/// `CSChangeDoodadPhasePacket` (logged only, as in C#).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSChangeDoodadPhasePacket {
    pub obj_id: u32,
    pub unk_id: u32,
    pub unk2_id: u32,
    pub unk3_id: u32,
}
impl ClientPacket for CSChangeDoodadPhasePacket {
    const NAME: &'static str = "CSChangeDoodadPhasePacket";
    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            unk_id: s.read_u32()?,
            unk2_id: s.read_u32()?,
            unk3_id: s.read_u32()?,
        })
    }
    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "ChangeDoodadPhase, ObjId: {}, Id: {}, {}, {}",
                self.obj_id,
                self.unk_id,
                self.unk2_id,
                self.unk3_id
            );
            Ok(())
        })
    }
}

/// `CSSetLogicDoodadPacket` (logged only, as in C#).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSSetLogicDoodadPacket {
    pub obj_id: u32,
    pub obj2_id: u32,
}
impl ClientPacket for CSSetLogicDoodadPacket {
    const NAME: &'static str = "CSSetLogicDoodadPacket";
    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            obj2_id: s.read_bc()?,
        })
    }
    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("SetLogicDoodad, ObjId: {}, {}", self.obj_id, self.obj2_id);
            Ok(())
        })
    }
}

/// `CSSaveDoodadUccStringPacket` (logged only; not registered, see the module docs).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSaveDoodadUccStringPacket {
    pub obj_id: u32,
    pub data: String,
}
impl ClientPacket for CSSaveDoodadUccStringPacket {
    const NAME: &'static str = "CSSaveDoodadUccStringPacket";
    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            data: s.read_string()?,
        })
    }
    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("SaveDoodadUccString, ObjId: {}", self.obj_id);
            Ok(())
        })
    }
}

/// `CSUnbondDoodadPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSUnbondDoodadPacket {
    pub character_obj_id: u32,
    pub doodad_obj_id: u32,
}
impl ClientPacket for CSUnbondDoodadPacket {
    const NAME: &'static str = "CSUnbondDoodadPacket";
    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            character_obj_id: s.read_bc()?,
            doodad_obj_id: s.read_bc()?,
        })
    }
    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(me) = conn.active_char.clone() else {
                return Ok(());
            };
            super::manager::unbond_doodad(server, &me, self.character_obj_id, self.doodad_obj_id);
            Ok(())
        })
    }
}

/// `CSCreateDoodadPacket`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CSCreateDoodadPacket {
    pub id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub z_rot: f32,
    pub scale: f32,
    pub item_id: u64,
}
impl ClientPacket for CSCreateDoodadPacket {
    const NAME: &'static str = "CSCreateDoodadPacket";
    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            id: s.read_u32()?,
            x: helpers::convert_long_to_coord(s.read_i64()?),
            y: helpers::convert_long_to_coord(s.read_i64()?),
            z: s.read_f32()?,
            z_rot: s.read_f32()?,
            scale: s.read_f32()?,
            item_id: s.read_u64()?,
        })
    }
    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(me) = conn.active_char.clone() else {
                return Ok(());
            };
            super::manager::handle_create_doodad(server, &me, &self);
            Ok(())
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::{encode, ServerPacket};

    fn body(p: &dyn ServerPacket) -> Vec<u8> {
        encode(p)[6..].to_vec()
    }

    fn bc(v: u32) -> [u8; 3] {
        let b = v.to_le_bytes();
        [b[0], b[1], b[2]]
    }

    #[test]
    fn sc_doodad_phase_changed() {
        let mut d = Doodad::new(2336);
        d.func_group_id = 4652;
        d.item_template_id = 27606;
        d.growth_time_ms = 31_000;
        let p = SCDoodadPhaseChangedPacket::new(0x01_0203, &d, 1_000);
        assert_eq!(p.opcode(), sc::SCDoodadPhaseChangedPacket);
        let mut e = bc(0x01_0203).to_vec();
        e.extend(4652u32.to_le_bytes());
        e.extend(30_000u32.to_le_bytes());
        e.extend((-1i32).to_le_bytes());
        e.extend(27606u32.to_le_bytes());
        assert_eq!(body(&p), e);
    }

    #[test]
    fn sc_doodad_sound_changed_originator_quest_accept() {
        let mut e = bc(7).to_vec();
        e.extend(55u32.to_le_bytes());
        assert_eq!(
            body(&SCDoodadSoundPacket {
                obj_id: 7,
                sound_id: 55
            }),
            e
        );

        let mut e = bc(8).to_vec();
        e.extend((-3i32).to_le_bytes());
        assert_eq!(
            body(&SCDoodadChangedPacket {
                obj_id: 8,
                data: -3
            }),
            e
        );

        let mut e = bc(9).to_vec();
        e.extend(10u32.to_le_bytes());
        e.extend(148u32.to_le_bytes());
        let p = SCDoodadOriginatorPacket {
            obj_id: 9,
            new_owner_id: 10,
            new_faction: 148,
        };
        assert_eq!(body(&p), e);

        let mut e = bc(11).to_vec();
        e.extend(4321u32.to_le_bytes());
        let p = SCDoodadQuestAcceptPacket {
            doodad_obj_id: 11,
            quest_id: 4321,
        };
        assert_eq!(body(&p), e);
        assert_eq!(p.opcode(), sc::SCDoodadQuestAcceptPacket);
    }

    #[test]
    fn sc_bond_unbond_doodad() {
        let bond = BondDoodad {
            owner_obj_id: 0x55,
            attach_point: 2,
            kind: 6,
            space: 1,
            spot: 0,
        };
        let p = SCBondDoodadPacket {
            unit_obj_id: 0x44,
            bond,
        };
        let mut e = bc(0x44).to_vec();
        e.push(2);
        e.extend(bc(0x55));
        e.push(6);
        e.extend(1i32.to_le_bytes());
        e.extend(0i32.to_le_bytes());
        assert_eq!(body(&p), e);

        let p = SCUnbondDoodadPacket {
            character_obj_id: 0x44,
            character_id: 3,
            doodad_obj_id: 0x55,
        };
        let mut e = bc(0x44).to_vec();
        e.extend(3u32.to_le_bytes());
        e.extend(bc(0x55));
        assert_eq!(body(&p), e);
    }

    fn stream(f: impl FnOnce(&mut PacketStream)) -> PacketStream {
        let mut s = PacketStream::new();
        f(&mut s);
        PacketStream::from_bytes(s.into_bytes())
    }

    #[test]
    fn cs_packets_decode() {
        let mut s = stream(|s| {
            s.write_bc(0x123).write_i32(3);
        });
        assert_eq!(
            CSChangeDoodadDataPacket::read(&mut s).unwrap(),
            CSChangeDoodadDataPacket {
                obj_id: 0x123,
                data: 3
            }
        );
        let mut s = stream(|s| {
            s.write_bc(5).write_u32(1).write_u32(2).write_u32(3);
        });
        assert_eq!(
            CSChangeDoodadPhasePacket::read(&mut s).unwrap(),
            CSChangeDoodadPhasePacket {
                obj_id: 5,
                unk_id: 1,
                unk2_id: 2,
                unk3_id: 3
            }
        );
        let mut s = stream(|s| {
            s.write_bc(5).write_bc(6);
        });
        assert_eq!(
            CSSetLogicDoodadPacket::read(&mut s).unwrap(),
            CSSetLogicDoodadPacket {
                obj_id: 5,
                obj2_id: 6
            }
        );
        let mut s = stream(|s| {
            s.write_bc(5).write_bc(6);
        });
        assert_eq!(
            CSUnbondDoodadPacket::read(&mut s).unwrap(),
            CSUnbondDoodadPacket {
                character_obj_id: 5,
                doodad_obj_id: 6
            }
        );
        let mut s = stream(|s| {
            s.write_bc(5).write_str("hello");
        });
        assert_eq!(
            CSSaveDoodadUccStringPacket::read(&mut s).unwrap(),
            CSSaveDoodadUccStringPacket {
                obj_id: 5,
                data: "hello".into()
            }
        );
        let mut s = stream(|s| {
            s.write_u32(2336)
                .write_i64(helpers::convert_coord_to_long(1000.5))
                .write_i64(helpers::convert_coord_to_long(2000.25))
                .write_f32(100.0)
                .write_f32(1.5)
                .write_f32(1.0)
                .write_u64(77);
        });
        assert_eq!(
            CSCreateDoodadPacket::read(&mut s).unwrap(),
            CSCreateDoodadPacket {
                id: 2336,
                x: 1000.5,
                y: 2000.25,
                z: 100.0,
                z_rot: 1.5,
                scale: 1.0,
                item_id: 77
            }
        );
    }
}
