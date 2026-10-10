//! S->C packets sent by the special effect actions (`Effects/SpecialEffects/*`) and by
//! `BubbleEffect` / `ImpulseEffect`. All level 1; field order and widths follow the 1.2 C#
//! `Write`.

use aaemu_commons::{Marshal, PacketStream};

use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;

use super::types::{convert_long, SkillCaster};

/// `SCToggleBeautyshopResponsePacket(state)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCToggleBeautyshopResponsePacket {
    pub state: u8,
}
packet_def!(
    SCToggleBeautyshopResponsePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCToggleBeautyshopResponsePacket
);
impl Marshal for SCToggleBeautyshopResponsePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.state);
    }
}

/// `SCBmPointPacket(bmPoint)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCBmPointPacket {
    pub bm_point: i64,
}
packet_def!(
    SCBmPointPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCBmPointPacket
);
impl Marshal for SCBmPointPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i64(self.bm_point);
    }
}

/// `SCICSCashPointPacket(point)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCICSCashPointPacket {
    pub point: i32,
}
packet_def!(
    SCICSCashPointPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCICSCashPointPacket
);
impl Marshal for SCICSCashPointPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.point);
    }
}

/// `SCNotifyResurrectionPacket(skillCaster)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCNotifyResurrectionPacket {
    pub caster: SkillCaster,
}
packet_def!(
    SCNotifyResurrectionPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCNotifyResurrectionPacket
);
impl Marshal for SCNotifyResurrectionPacket {
    fn write(&self, s: &mut PacketStream) {
        self.caster.write(s);
    }
}

/// `SCCharacterResurrectedPacket(unitId, x, y, z, zRot)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCCharacterResurrectedPacket {
    pub unit_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub z_rot: f32,
}
packet_def!(
    SCCharacterResurrectedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterResurrectedPacket
);
impl Marshal for SCCharacterResurrectedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.unit_id)
            .write_position(self.x, self.y, self.z)
            .write_f32(self.z_rot);
    }
}

/// `SCBlinkUnitPacket(objId, distance, degree, x, y, z)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCBlinkUnitPacket {
    pub obj_id: u32,
    pub distance: f32,
    pub degree: f32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
}
packet_def!(
    SCBlinkUnitPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCBlinkUnitPacket
);
impl Marshal for SCBlinkUnitPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_f32(self.distance)
            .write_f32(self.degree)
            .write_i64(convert_long(self.x))
            .write_i64(convert_long(self.y))
            .write_f32(self.z);
    }
}

/// `SCPauseUserMusicPacket(playerObjId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCPauseUserMusicPacket {
    pub obj_id: u32,
}
packet_def!(
    SCPauseUserMusicPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPauseUserMusicPacket
);
impl Marshal for SCPauseUserMusicPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id);
    }
}

/// `SCSendUserMusicPacket(playerObjId, author, midiData)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCSendUserMusicPacket {
    pub obj_id: u32,
    pub author: String,
    pub midi: Vec<u8>,
}
packet_def!(
    SCSendUserMusicPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSendUserMusicPacket
);
impl Marshal for SCSendUserMusicPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.midi.len() as u32)
            .write_bc(self.obj_id)
            .write_str(&self.author)
            .write_bytes_sized(&self.midi);
    }
}

/// `SCBotSuspectReportedPacket(reporter, suspect)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCBotSuspectReportedPacket {
    pub reporter: String,
    pub suspect: String,
}
packet_def!(
    SCBotSuspectReportedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCBotSuspectReportedPacket
);
impl Marshal for SCBotSuspectReportedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_str(&self.reporter).write_str(&self.suspect);
    }
}

/// `SCLoadInstancePacket(instanceId, zoneId, x, y, z, angX, angY, angZ)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCLoadInstancePacket {
    pub instance_id: u32,
    pub zone_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub ang_x: f32,
    pub ang_y: f32,
    pub ang_z: f32,
}
packet_def!(
    SCLoadInstancePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLoadInstancePacket
);
impl Marshal for SCLoadInstancePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.instance_id)
            .write_u32(self.zone_id)
            .write_f32(self.x)
            .write_f32(self.y)
            .write_f32(self.z)
            .write_f32(self.ang_x)
            .write_f32(self.ang_y)
            .write_f32(self.ang_z);
    }
}

/// `TeleportReason.MoveToLocation`.
pub const TELEPORT_REASON_MOVE_TO_LOCATION: u8 = 14;

/// `SCTeleportUnitPacket(reason, errorMessage, x, y, z, z2)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCTeleportUnitPacket {
    pub reason: u8,
    pub error_message: i16,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub z2: f32,
}
packet_def!(
    SCTeleportUnitPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTeleportUnitPacket
);
impl Marshal for SCTeleportUnitPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.reason)
            .write_i16(self.error_message)
            .write_position(self.x, self.y, self.z)
            .write_f32(self.z2);
    }
}

/// `SCChatBubblePacket(bc, kind1, kind2, type, text)`: `kind2 == 1` writes the text, any other
/// value the bubble id.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCChatBubblePacket {
    pub bc: u32,
    pub kind1: u8,
    pub kind2: u8,
    pub bubble_type: u32,
    pub text: String,
}
packet_def!(
    SCChatBubblePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCChatBubblePacket
);
impl Marshal for SCChatBubblePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.bc)
            .write_u8(self.kind1)
            .write_u8(self.kind2);
        if self.kind2 != 1 {
            s.write_u32(self.bubble_type);
        } else {
            s.write_str(&self.text);
        }
    }
}

/// `SCUnitImpulsePacket(unitObjId, velImpulse, angvelImpulse, impulse, angImpulse)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCUnitImpulsePacket {
    pub obj_id: u32,
    pub vel_impulse: [f32; 3],
    pub angvel_impulse: [f32; 3],
    pub impulse: [f32; 3],
    pub ang_impulse: [f32; 3],
}
packet_def!(
    SCUnitImpulsePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitImpulsePacket
);
impl SCUnitImpulsePacket {
    /// `EncodeUnitObjIdFloat`: the bits of `objId << 8` reinterpreted as a float.
    pub fn encode_obj_id_float(obj_id: u32) -> f32 {
        f32::from_bits(obj_id << 8)
    }
}
impl Marshal for SCUnitImpulsePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_f32(Self::encode_obj_id_float(self.obj_id));
        for v in self
            .vel_impulse
            .iter()
            .chain(&self.angvel_impulse)
            .chain(&self.impulse)
            .chain(&self.ang_impulse)
        {
            s.write_f32(*v);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::{encode, ServerPacket};

    fn body(p: &dyn ServerPacket) -> Vec<u8> {
        let mut s = PacketStream::new();
        p.write_body(&mut s);
        s.into_bytes()
    }

    fn header(op: u16) -> [u8; 6] {
        let o = op.to_le_bytes();
        [0xDD, 0x01, 0, 0, o[0], o[1]]
    }

    #[test]
    fn beautyshop_bm_cash_layouts() {
        let p = SCToggleBeautyshopResponsePacket { state: 1 };
        assert_eq!(body(&p), vec![1]);
        assert_eq!(
            &encode(&p)[..6],
            &header(sc::SCToggleBeautyshopResponsePacket)
        );

        let p = SCBmPointPacket {
            bm_point: 0x0102_0304_0506_0708,
        };
        let mut e = PacketStream::new();
        e.write_i64(0x0102_0304_0506_0708);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCBmPointPacket));

        let p = SCICSCashPointPacket { point: -5 };
        let mut e = PacketStream::new();
        e.write_i32(-5);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCICSCashPointPacket));
    }

    #[test]
    fn notify_resurrection_writes_skill_caster() {
        let p = SCNotifyResurrectionPacket {
            caster: SkillCaster::Item {
                obj_id: 0x0A0B0C,
                item_id: 77,
                item_template_id: 8000,
                type1: 2,
                type2: 3,
            },
        };
        let mut e = PacketStream::new();
        e.write_u8(2)
            .write_bc(0x0A0B0C)
            .write_u64(77)
            .write_u32(8000)
            .write_u8(2)
            .write_u32(3);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCNotifyResurrectionPacket));
    }

    #[test]
    fn character_resurrected_layout() {
        let p = SCCharacterResurrectedPacket {
            unit_id: 0x123456,
            x: 100.5,
            y: 200.25,
            z: 30.0,
            z_rot: 1.5,
        };
        let mut e = PacketStream::new();
        e.write_bc(0x123456)
            .write_position(100.5, 200.25, 30.0)
            .write_f32(1.5);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCCharacterResurrectedPacket));
    }

    #[test]
    fn blink_unit_layout() {
        let p = SCBlinkUnitPacket {
            obj_id: 0x010203,
            distance: 15.0,
            degree: 90.0,
            x: 1.0,
            y: 2.0,
            z: 3.5,
        };
        let mut e = PacketStream::new();
        e.write_bc(0x010203)
            .write_f32(15.0)
            .write_f32(90.0)
            .write_i64(4096i64 << 32)
            .write_i64(8192i64 << 32)
            .write_f32(3.5);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCBlinkUnitPacket));
    }

    #[test]
    fn user_music_layouts() {
        let p = SCPauseUserMusicPacket { obj_id: 0x000102 };
        assert_eq!(body(&p), vec![0x02, 0x01, 0x00]);
        assert_eq!(&encode(&p)[..6], &header(sc::SCPauseUserMusicPacket));

        let p = SCSendUserMusicPacket {
            obj_id: 0x0A,
            author: "Bard".into(),
            midi: vec![0x90, 0x3C, 0x40],
        };
        let mut e = PacketStream::new();
        e.write_u32(3)
            .write_bc(0x0A)
            .write_u16(4)
            .write_raw(b"Bard")
            .write_u16(3)
            .write_raw(&[0x90, 0x3C, 0x40]);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCSendUserMusicPacket));
    }

    #[test]
    fn bot_suspect_reported_layout() {
        let p = SCBotSuspectReportedPacket {
            reporter: "Ann".into(),
            suspect: "Bob".into(),
        };
        let mut e = PacketStream::new();
        e.write_u16(3)
            .write_raw(b"Ann")
            .write_u16(3)
            .write_raw(b"Bob");
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCBotSuspectReportedPacket));
    }

    #[test]
    fn load_instance_and_teleport_layouts() {
        let p = SCLoadInstancePacket {
            instance_id: 1,
            zone_id: 129,
            x: 10.0,
            y: 20.0,
            z: 30.0,
            ang_x: 0.0,
            ang_y: 0.0,
            ang_z: 3.0,
        };
        let mut e = PacketStream::new();
        e.write_u32(1)
            .write_u32(129)
            .write_f32(10.0)
            .write_f32(20.0)
            .write_f32(30.0)
            .write_f32(0.0)
            .write_f32(0.0)
            .write_f32(3.0);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCLoadInstancePacket));

        let p = SCTeleportUnitPacket {
            reason: TELEPORT_REASON_MOVE_TO_LOCATION,
            error_message: 0,
            x: 10.0,
            y: 20.0,
            z: 30.0,
            z2: 1.25,
        };
        let mut e = PacketStream::new();
        e.write_u8(14)
            .write_i16(0)
            .write_position(10.0, 20.0, 30.0)
            .write_f32(1.25);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCTeleportUnitPacket));
    }

    #[test]
    fn chat_bubble_type_and_text_layouts() {
        let p = SCChatBubblePacket {
            bc: 0x0102,
            kind1: 1,
            kind2: 2,
            bubble_type: 4321,
            text: String::new(),
        };
        let mut e = PacketStream::new();
        e.write_bc(0x0102).write_u8(1).write_u8(2).write_u32(4321);
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCChatBubblePacket));

        let p = SCChatBubblePacket {
            bc: 0x0102,
            kind1: 1,
            kind2: 1,
            bubble_type: 4321,
            text: "hi".into(),
        };
        let mut e = PacketStream::new();
        e.write_bc(0x0102)
            .write_u8(1)
            .write_u8(1)
            .write_u16(2)
            .write_raw(b"hi");
        assert_eq!(body(&p), e.into_bytes());
    }

    #[test]
    fn unit_impulse_layout() {
        let p = SCUnitImpulsePacket {
            obj_id: 0x00ABCDEF,
            vel_impulse: [1.0, 2.0, 3.0],
            angvel_impulse: [4.0, 5.0, 6.0],
            impulse: [7.0, 8.0, 9.0],
            ang_impulse: [10.0, 11.0, 12.0],
        };
        let mut e = PacketStream::new();
        e.write_bc(0x00ABCDEF)
            .write_raw(&0xABCD_EF00u32.to_le_bytes());
        for v in 1..=12 {
            e.write_f32(v as f32);
        }
        assert_eq!(body(&p), e.into_bytes());
        assert_eq!(&encode(&p)[..6], &header(sc::SCUnitImpulsePacket));
    }
}
