//! S->C packets of `Core/Packets/G2C` that no subsystem sends yet (the C# defines them, most
//! have no caller in the 1.2 C# either). Field order and widths follow the C# `Write` methods
//! byte for byte; each one is a level-1 packet.

use aaemu_commons::{Marshal, PacketStream};

use super::packet_def;
use crate::models::char_data::FriendInfo;
use crate::models::character::{convert_long, Character};
use crate::models::item::UnixTime;
use crate::net::packet::{ServerPacket, LEVEL_PLAIN};
use crate::opcodes::sc;
use crate::systems::npcs::schedule_items::MAX_ITEMS_IN_PACKET;

/// Declares a level-1 packet whose fields are written in declaration order.
macro_rules! simple_packet {
    ($(#[$m:meta])* $ty:ident { $($field:ident : $fty:ty => $w:ident),* $(,)? }) => {
        $(#[$m])*
        #[derive(Debug, Clone, PartialEq)]
        pub struct $ty {
            $(pub $field: $fty,)*
        }
        packet_def!($ty, level = LEVEL_PLAIN, opcode = sc::$ty);
        impl Marshal for $ty {
            #[allow(unused_variables)]
            fn write(&self, s: &mut PacketStream) {
                $(s.$w(self.$field);)*
            }
        }
    };
}

simple_packet!(
    /// `SCAddActionPointPacket(int action, int value)`.
    SCAddActionPointPacket { action: i32 => write_i32, value: i32 => write_i32 }
);

simple_packet!(
    /// `SCConflictZoneHonorPointSumPacket(ushort zoneId, int sum)`.
    SCConflictZoneHonorPointSumPacket { zone_id: u16 => write_u16, sum: i32 => write_i32 }
);

simple_packet!(
    /// `SCHackGuardRetAddrsRequestPacket(bool sendAddrs, bool spMd5)`.
    SCHackGuardRetAddrsRequestPacket { send_addrs: bool => write_bool, sp_md5: bool => write_bool }
);

simple_packet!(
    /// `SCIdleKickPacket()` (empty body).
    SCIdleKickPacket {}
);

simple_packet!(
    /// `SCKnockBackUnitPacket(uint objId, float x, float y, float z)`.
    SCKnockBackUnitPacket {
        obj_id: u32 => write_bc,
        x: f32 => write_f32,
        y: f32 => write_f32,
        z: f32 => write_f32,
    }
);

simple_packet!(
    /// `SCMileageChangedPacket(uint objId, int mileage)`.
    SCMileageChangedPacket { obj_id: u32 => write_bc, mileage: i32 => write_i32 }
);

simple_packet!(
    /// `SCMineAmountPacket(int amount)`.
    SCMineAmountPacket { amount: i32 => write_i32 }
);

simple_packet!(
    /// `SCPlaytimePacket(int playTime)`.
    SCPlaytimePacket { play_time: i32 => write_i32 }
);

simple_packet!(
    /// `SCRankAlarmPacket(uint type, byte rankAlarmKind)`.
    SCRankAlarmPacket { kind: u32 => write_u32, rank_alarm_kind: u8 => write_u8 }
);

simple_packet!(
    /// `SCUnderWaterPacket(bool start)`.
    SCUnderWaterPacket { start: bool => write_bool }
);

simple_packet!(
    /// `SCUnitGmModeChangedPacket(uint unitId, int mode, byte value)`.
    SCUnitGmModeChangedPacket {
        unit_id: u32 => write_bc,
        mode: i32 => write_i32,
        value: u8 => write_u8,
    }
);

simple_packet!(
    /// `SCUnitOfflinePacket(uint objId, bool isOffline)`.
    SCUnitOfflinePacket { obj_id: u32 => write_bc, is_offline: bool => write_bool }
);

simple_packet!(
    /// `SCUnitPvPPointsChangedPacket(uint unitId, byte kind, int point)`.
    SCUnitPvPPointsChangedPacket {
        unit_id: u32 => write_bc,
        kind: u8 => write_u8,
        point: i32 => write_i32,
    }
);

simple_packet!(
    /// `SCUnknownPacket(SlotType slotType, byte slot, SlotType slotType2, byte slot2,
    /// short errorMessage)` (debug packet, opcode `SCUnknownPacket`).
    SCUnknownPacket {
        slot_type: u8 => write_u8,
        slot: u8 => write_u8,
        slot_type2: u8 => write_u8,
        slot2: u8 => write_u8,
        error_message: i16 => write_i16,
    }
);

simple_packet!(
    /// `SCUnlockCurrencySlotPacket(SlotType slotType, byte slot)`.
    SCUnlockCurrencySlotPacket { slot_type: u8 => write_u8, slot: u8 => write_u8 }
);

simple_packet!(
    /// `SCWorldQueuePacket(byte worldId, bool isPremium, ushort myTurn, ushort normalLenght,
    /// ushort premiumLenght)`.
    SCWorldQueuePacket {
        world_id: u8 => write_u8,
        is_premium: bool => write_bool,
        my_turn: u16 => write_u16,
        normal_length: u16 => write_u16,
        premium_length: u16 => write_u16,
    }
);

/// `KickedReason` (byte).
pub mod kicked_reason {
    pub const KICK_DUPLICATE_ACCOUNT: u8 = 0x0;
    pub const KICK_BY_GM: u8 = 0x1;
    pub const KICK_BY_MAINTENANCE: u8 = 0x2;
    pub const KICK_BY_INVALID_DOODAD_INTERACTION: u8 = 0x3;
}

/// `SCKickedPacket(KickedReason reason, string msg)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCKickedPacket {
    /// [`kicked_reason`].
    pub reason: u8,
    pub msg: String,
}
packet_def!(
    SCKickedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCKickedPacket
);
impl Marshal for SCKickedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.reason).write_str(&self.msg);
    }
}

/// `SCWorldMessagePacket(byte source, byte messageType, string msg)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCWorldMessagePacket {
    pub source: u8,
    pub message_type: u8,
    pub msg: String,
}
packet_def!(
    SCWorldMessagePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCWorldMessagePacket
);
impl Marshal for SCWorldMessagePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.source)
            .write_u8(self.message_type)
            .write_str(&self.msg);
    }
}

/// `SCCharBriefPacket(uint charId, string name, Race race)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCCharBriefPacket {
    pub char_id: u32,
    pub name: String,
    pub race: u8,
}
packet_def!(
    SCCharBriefPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharBriefPacket
);
impl Marshal for SCCharBriefPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.char_id)
            .write_str(&self.name)
            .write_u8(self.race);
    }
}

/// `SCCharacterBoundPacket(unitObjId, returnDistrict, resurrectionDistrict,
/// returnDistrictChanged, id, name, zoneKey, pos, zRot, isFavorite)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCCharacterBoundPacket {
    pub unit_obj_id: u32,
    pub return_district: u32,
    pub resurrection_district: u32,
    pub return_district_changed: bool,
    pub id: u32,
    pub name: String,
    pub zone_key: u32,
    pub pos: [f32; 3],
    pub z_rot: f32,
    pub is_favorite: bool,
}
packet_def!(
    SCCharacterBoundPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterBoundPacket
);
impl Marshal for SCCharacterBoundPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.unit_obj_id)
            .write_u32(self.return_district)
            .write_u32(self.resurrection_district)
            .write_bool(self.return_district_changed)
            .write_u32(self.id)
            .write_str(&self.name)
            .write_u32(self.zone_key)
            .write_f32(self.pos[0])
            .write_f32(self.pos[1])
            .write_f32(self.pos[2])
            .write_f32(self.z_rot)
            .write_bool(self.is_favorite);
    }
}

/// `(long x, byte unitRelationshipCode, byte unitRelationshipReason)` of the
/// `SC{C,F}vFCombatRelationshipPacket`s.
pub type CombatRelationship = (i64, u8, u8);

fn write_relationships(s: &mut PacketStream, relationships: &[CombatRelationship]) {
    s.write_u8(relationships.len() as u8);
    for &(x, code, reason) in relationships {
        s.write_i64(x).write_u8(code).write_u8(reason);
    }
}

/// `SCCvFCombatRelationshipPacket(relationships)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCCvFCombatRelationshipPacket {
    pub relationships: Vec<CombatRelationship>,
}
packet_def!(
    SCCvFCombatRelationshipPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCvFCombatRelationshipPacket
);
impl Marshal for SCCvFCombatRelationshipPacket {
    fn write(&self, s: &mut PacketStream) {
        write_relationships(s, &self.relationships);
    }
}

/// `SCFvFCombatRelationshipPacket(relationships)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCFvFCombatRelationshipPacket {
    pub relationships: Vec<CombatRelationship>,
}
packet_def!(
    SCFvFCombatRelationshipPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCFvFCombatRelationshipPacket
);
impl Marshal for SCFvFCombatRelationshipPacket {
    fn write(&self, s: &mut PacketStream) {
        write_relationships(s, &self.relationships);
    }
}

/// `Models/Game/ScheduleItem` (a `PacketMarshaler`; the 1.2 C# never builds one). The client
/// treats `item_template_id` as the schedule row id (`scheduleType`), which is why the 10.x
/// upstream renamed it `Type`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ScheduleItem {
    pub item_template_id: u32,
    pub gave: u8,
    pub acumulated: i32,
    pub updated: UnixTime,
}
impl Marshal for ScheduleItem {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.item_template_id)
            .write_u8(self.gave)
            .write_i32(self.acumulated)
            .write_i64(self.updated);
    }
}

/// `SCScheduleItemUpdatePacket(List<ScheduleItem>)`. The 1.2 C# wrote the count as an `int` and
/// never built the packet; the client reads one byte, so the serializer writes a `u8` and caps the
/// list at [`MAX_ITEMS_IN_PACKET`] rows like the 10.x `ScheduleItemRules` helper (verified against
/// the real client through a local proxy).
#[derive(Debug, Clone, PartialEq)]
pub struct SCScheduleItemUpdatePacket {
    pub schedule_items: Vec<ScheduleItem>,
}
packet_def!(
    SCScheduleItemUpdatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCScheduleItemUpdatePacket
);
impl Marshal for SCScheduleItemUpdatePacket {
    fn write(&self, s: &mut PacketStream) {
        let count = self.schedule_items.len().min(MAX_ITEMS_IN_PACKET);
        s.write_u8(count as u8);
        for item in &self.schedule_items[..count] {
            item.write(s);
        }
    }
}

/// `SCScheduleItemSentPacket(uint scheduleType, bool byMail)` (0x151): the answer to a take. The
/// 1.2 client handler (`OnScheduleItemSent`) reads a `u32` (the schedule row id, the same value the
/// update rows carry as `item_template_id`) and one `u8` flag (1 = the reward went to the mailbox).
/// The layout is the 5.0 C# `SCScheduleItemSentPacket(itemTemplateId, byMail)`; the 1.2 C# never
/// built it.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCScheduleItemSentPacket {
    pub item_template_id: u32,
    pub by_mail: bool,
}
packet_def!(
    SCScheduleItemSentPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCScheduleItemSentPacket
);
impl Marshal for SCScheduleItemSentPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.item_template_id).write_bool(self.by_mail);
    }
}

/// `SCSearchListPacket(int total, Friend[] friends, bool success)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCSearchListPacket {
    pub total: i32,
    pub friends: Vec<FriendInfo>,
    pub success: bool,
}
packet_def!(
    SCSearchListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSearchListPacket
);
impl Marshal for SCSearchListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.total).write_i32(self.friends.len() as i32);
        for f in &self.friends {
            f.write(s);
        }
        s.write_bool(self.success);
    }
}

/// `SCCharDetailPacket(Character character, bool success)`: built from the character while
/// its lock is held (the equipment items are written with their full `Item.Write`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCCharDetailPacket {
    pub body: Vec<u8>,
}
packet_def!(
    SCCharDetailPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharDetailPacket
);
impl Marshal for SCCharDetailPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_raw(&self.body);
    }
}

impl SCCharDetailPacket {
    pub fn new(character: &Character, success: bool) -> Self {
        let mut s = PacketStream::new();
        s.write_u32(character.id)
            .write_str(&character.unit.name)
            .write_u8(character.race)
            .write_i32(character.unit.hp.wrapping_mul(100)) // TODO: precise health ?
            .write_u8(character.unit.level)
            .write_u8(character.ability1)
            .write_u8(character.ability2)
            .write_u8(character.ability3)
            .write_i64(convert_long(character.unit.transform.x))
            .write_i64(convert_long(character.unit.transform.y))
            .write_f32(character.unit.transform.z)
            .write_u32(character.unit.transform.zone_id)
            .write_i64(character.leave_time); // TODO: lastWorldLeaveTime
        for item in character.inventory.equipment_slots() {
            match item {
                None => {
                    s.write_i32(0);
                }
                Some(item) => item.write(&mut s),
            }
        }
        s.write_bool(success);
        Self {
            body: s.into_bytes(),
        }
    }
}

/// `SCRawPacket(ushort opcode, byte[] payload)`: a level-1 packet with an arbitrary opcode
/// and body (used by the C# debug tooling).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCRawPacket {
    pub opcode: u16,
    pub payload: Vec<u8>,
}
impl ServerPacket for SCRawPacket {
    fn opcode(&self) -> u16 {
        self.opcode
    }
    fn level(&self) -> u8 {
        LEVEL_PLAIN
    }
    fn name(&self) -> &'static str {
        "SCRawPacket"
    }
    fn write_body(&self, stream: &mut PacketStream) {
        stream.write_raw(&self.payload);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    fn body<P: Marshal>(p: &P) -> Vec<u8> {
        let mut s = PacketStream::new();
        p.write(&mut s);
        s.into_bytes()
    }

    #[test]
    fn simple_packets_match_csharp_layout() {
        assert_eq!(
            body(&SCAddActionPointPacket {
                action: 1,
                value: -2
            }),
            [1, 0, 0, 0, 0xfe, 0xff, 0xff, 0xff]
        );
        assert_eq!(
            body(&SCConflictZoneHonorPointSumPacket {
                zone_id: 0x102,
                sum: 5
            }),
            [2, 1, 5, 0, 0, 0]
        );
        assert_eq!(
            body(&SCHackGuardRetAddrsRequestPacket {
                send_addrs: true,
                sp_md5: false
            }),
            [1, 0]
        );
        assert!(body(&SCIdleKickPacket {}).is_empty());
        assert_eq!(
            body(&SCKnockBackUnitPacket {
                obj_id: 0x030201,
                x: 1.0,
                y: 0.0,
                z: -1.0
            }),
            [1, 2, 3, 0, 0, 0x80, 0x3f, 0, 0, 0, 0, 0, 0, 0x80, 0xbf]
        );
        assert_eq!(
            body(&SCMileageChangedPacket {
                obj_id: 7,
                mileage: 9
            }),
            [7, 0, 0, 9, 0, 0, 0]
        );
        assert_eq!(body(&SCMineAmountPacket { amount: 3 }), [3, 0, 0, 0]);
        assert_eq!(body(&SCPlaytimePacket { play_time: 60 }), [60, 0, 0, 0]);
        assert_eq!(
            body(&SCRankAlarmPacket {
                kind: 2,
                rank_alarm_kind: 4
            }),
            [2, 0, 0, 0, 4]
        );
        assert_eq!(body(&SCUnderWaterPacket { start: true }), [1]);
        assert_eq!(
            body(&SCUnitGmModeChangedPacket {
                unit_id: 5,
                mode: 1,
                value: 1
            }),
            [5, 0, 0, 1, 0, 0, 0, 1]
        );
        assert_eq!(
            body(&SCUnitOfflinePacket {
                obj_id: 5,
                is_offline: true
            }),
            [5, 0, 0, 1]
        );
        assert_eq!(
            body(&SCUnitPvPPointsChangedPacket {
                unit_id: 5,
                kind: 2,
                point: 10
            }),
            [5, 0, 0, 2, 10, 0, 0, 0]
        );
        assert_eq!(
            body(&SCUnknownPacket {
                slot_type: 2,
                slot: 3,
                slot_type2: 1,
                slot2: 4,
                error_message: -1
            }),
            [2, 3, 1, 4, 0xff, 0xff]
        );
        assert_eq!(
            body(&SCUnlockCurrencySlotPacket {
                slot_type: 2,
                slot: 9
            }),
            [2, 9]
        );
        assert_eq!(
            body(&SCWorldQueuePacket {
                world_id: 1,
                is_premium: false,
                my_turn: 3,
                normal_length: 4,
                premium_length: 5
            }),
            [1, 0, 3, 0, 4, 0, 5, 0]
        );
    }

    #[test]
    fn string_packets_match_csharp_layout() {
        assert_eq!(
            body(&SCKickedPacket {
                reason: kicked_reason::KICK_BY_GM,
                msg: "ab".into()
            }),
            [1, 2, 0, b'a', b'b']
        );
        assert_eq!(
            body(&SCWorldMessagePacket {
                source: 1,
                message_type: 2,
                msg: "x".into()
            }),
            [1, 2, 1, 0, b'x']
        );
        assert_eq!(
            body(&SCCharBriefPacket {
                char_id: 3,
                name: "n".into(),
                race: 4
            }),
            [3, 0, 0, 0, 1, 0, b'n', 4]
        );
        let p = SCCharacterBoundPacket {
            unit_obj_id: 1,
            return_district: 2,
            resurrection_district: 3,
            return_district_changed: true,
            id: 4,
            name: "a".into(),
            zone_key: 5,
            pos: [0.0, 0.0, 1.0],
            z_rot: 0.0,
            is_favorite: true,
        };
        let mut want = vec![1, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 1, 4, 0, 0, 0, 1, 0, b'a'];
        want.extend([5, 0, 0, 0]);
        want.extend([0; 8]);
        want.extend(1.0f32.to_le_bytes());
        want.extend([0; 4]);
        want.push(1);
        assert_eq!(body(&p), want);
    }

    #[test]
    fn list_packets_match_csharp_layout() {
        let rel = vec![(-1i64, 2u8, 3u8)];
        let mut want = vec![1];
        want.extend((-1i64).to_le_bytes());
        want.extend([2, 3]);
        assert_eq!(
            body(&SCCvFCombatRelationshipPacket {
                relationships: rel.clone()
            }),
            want
        );
        assert_eq!(
            body(&SCFvFCombatRelationshipPacket { relationships: rel }),
            want
        );

        let f = FriendInfo {
            character_id: 1,
            name: "f".into(),
            ..Default::default()
        };
        let p = SCSearchListPacket {
            total: 9,
            friends: vec![f.clone()],
            success: true,
        };
        let mut want = vec![9, 0, 0, 0, 1, 0, 0, 0];
        want.extend(body(&f));
        want.push(1);
        assert_eq!(body(&p), want);
    }

    #[test]
    fn schedule_item_sent_packet_is_byte_exact() {
        let packet = SCScheduleItemSentPacket {
            item_template_id: 28,
            by_mail: true,
        };
        let want = [28u8, 0, 0, 0, 1];
        assert_eq!(body(&packet), want);
        assert_eq!(
            body(&SCScheduleItemSentPacket {
                item_template_id: 0x0102_0304,
                by_mail: false
            }),
            [4, 3, 2, 1, 0]
        );
        // Level 1 frame on opcode 0x151.
        let mut frame = vec![0xdd, 1, 0, 0, 0x51, 0x01];
        frame.extend(want);
        assert_eq!(encode(&packet), frame);
    }

    #[test]
    fn schedule_item_update_packet_is_byte_exact() {
        // Empty list: the u8 count alone (the 1.2 C# wrote an i32 zero here; the client reads one
        // byte).
        assert_eq!(
            body(&SCScheduleItemUpdatePacket {
                schedule_items: vec![]
            }),
            [0]
        );

        // One row: count + <u32 type, u8 gave, i32 cumulated, i64 updated unix seconds> = 17
        // bytes, on opcode 0x1eb (verified against the real client as `<BIbiq`).
        let packet = SCScheduleItemUpdatePacket {
            schedule_items: vec![ScheduleItem {
                item_template_id: 28,
                gave: 0,
                acumulated: 0,
                updated: 1_385_528_340,
            }],
        };
        let mut want = vec![1u8];
        want.extend(28u32.to_le_bytes());
        want.extend([0, 0, 0, 0, 0]);
        want.extend(1_385_528_340i64.to_le_bytes());
        assert_eq!(body(&packet), want);
        assert_eq!(want.len(), 18);
        let mut frame = vec![0xdd, 1, 0, 0, 0xeb, 0x01];
        frame.extend(&want);
        assert_eq!(encode(&packet), frame);

        // The historical cap: 40 rows serialize as the first 32 (1 + 32 * 17 bytes).
        let rows: Vec<ScheduleItem> = (1..=40)
            .map(|id| ScheduleItem {
                item_template_id: id,
                gave: 0,
                acumulated: 0,
                updated: 0,
            })
            .collect();
        let capped = body(&SCScheduleItemUpdatePacket {
            schedule_items: rows,
        });
        assert_eq!(capped.len(), 1 + MAX_ITEMS_IN_PACKET * 17);
        assert_eq!(capped[0], 32);
        let first = 1;
        assert_eq!(&capped[first..first + 4], 1u32.to_le_bytes());
        let last = 1 + 31 * 17;
        let mut last_row = Vec::new();
        last_row.extend(32u32.to_le_bytes());
        last_row.extend([0u8; 5]);
        last_row.extend(0i64.to_le_bytes());
        assert_eq!(&capped[last..last + last_row.len()], last_row.as_slice());
    }

    #[test]
    fn raw_packet_uses_its_own_opcode() {
        let p = SCRawPacket {
            opcode: 0x123,
            payload: vec![9, 8],
        };
        assert_eq!(encode(&p), [0xdd, 1, 0, 0, 0x23, 0x01, 9, 8]);
    }
}
