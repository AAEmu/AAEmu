//! S->C packets of the character lifecycle and the world (`AAEmu.Game.Core.Packets.G2C`).
//! Field order and widths follow the 1.2 C# `Write` methods; all are level 1.
//!
//! Packets whose C# `Write` reads live game objects (`SCCharacterStatePacket`,
//! `SCUnitStatePacket`, `SCCreateCharacterResponsePacket`) carry their body pre-encoded by the
//! builders in [`crate::world::unit_state`].

use aaemu_commons::{Marshal, PacketStream};

use super::{packet_def, raw_body_packet};
use crate::data::world_data::{FactionRelation, SystemFaction};
use crate::managers::expedition::{ExpeditionMember, ExpeditionRolePolicy};
use crate::models::char_data::{
    write_action_slots, Actability, ActionSlot, ActiveQuest, BlockedInfo, CompletedQuest,
    CountUnreadMail, FriendInfo,
};
use crate::models::item::{Item, UnixTime};
use crate::models::movement::{write_sc_one_unit_movement, MoveType};
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;

raw_body_packet!(SCCharacterStatePacket, sc::SCCharacterStatePacket);
raw_body_packet!(SCUnitStatePacket, sc::SCUnitStatePacket);
raw_body_packet!(
    SCCreateCharacterResponsePacket,
    sc::SCCreateCharacterResponsePacket
);

/// `SCCharacterGamePointsPacket(character)`.
#[derive(Debug, Clone)]
pub struct SCCharacterGamePointsPacket {
    pub honor_point: i32,
    pub vocation_point: i32,
}
packet_def!(
    SCCharacterGamePointsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterGamePointsPacket
);
impl Marshal for SCCharacterGamePointsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.honor_point).write_i32(self.vocation_point);
        for _ in 0..8 {
            s.write_i32(0); // point
        }
    }
}

/// `SCCharacterInvenInitPacket(numInvenSlots, numBankSlots)`.
#[derive(Debug, Clone)]
pub struct SCCharacterInvenInitPacket {
    pub num_inven_slots: u32,
    pub num_bank_slots: u32,
}
packet_def!(
    SCCharacterInvenInitPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterInvenInitPacket
);
impl Marshal for SCCharacterInvenInitPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.num_inven_slots)
            .write_u32(self.num_bank_slots);
    }
}

/// `SCCharacterInvenContentsPacket(type, numChunks, startChunkIdx, items)`.
#[derive(Debug, Clone)]
pub struct SCCharacterInvenContentsPacket {
    pub slot_type: u8,
    pub num_chunks: u8,
    pub start_chunk_idx: u8,
    pub items: Vec<Option<Item>>,
}
packet_def!(
    SCCharacterInvenContentsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterInvenContentsPacket
);
impl Marshal for SCCharacterInvenContentsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.slot_type)
            .write_u8(self.num_chunks)
            .write_u8(self.start_chunk_idx);
        for item in &self.items {
            match item {
                None => {
                    s.write_i32(0);
                }
                Some(i) => i.write(s),
            }
        }
    }
}

/// `SCActionSlotsPacket(slots)`.
#[derive(Debug, Clone)]
pub struct SCActionSlotsPacket {
    pub slots: Vec<ActionSlot>,
}
packet_def!(
    SCActionSlotsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCActionSlotsPacket
);
impl Marshal for SCActionSlotsPacket {
    fn write(&self, s: &mut PacketStream) {
        write_action_slots(&self.slots, s, true);
    }
}

/// `SCQuestsPacket(quests)`; `now_ms` is the encode time used by `Quest.Write`.
#[derive(Debug, Clone)]
pub struct SCQuestsPacket {
    pub quests: Vec<ActiveQuest>,
    pub now_ms: i64,
}
packet_def!(
    SCQuestsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCQuestsPacket
);
impl Marshal for SCQuestsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.quests.len() as i32);
        for q in &self.quests {
            q.write(s, self.now_ms);
        }
    }
}

/// `SCCompletedQuestsPacket(quests)`.
#[derive(Debug, Clone)]
pub struct SCCompletedQuestsPacket {
    pub quests: Vec<CompletedQuest>,
}
packet_def!(
    SCCompletedQuestsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCompletedQuestsPacket
);
impl Marshal for SCCompletedQuestsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.quests.len() as i32);
        for q in &self.quests {
            s.write_u16(q.id).write_raw(&q.body8());
        }
    }
}

/// `SCActabilityPacket(last, actabilities)`.
#[derive(Debug, Clone)]
pub struct SCActabilityPacket {
    pub last: bool,
    pub actabilities: Vec<Actability>,
}
packet_def!(
    SCActabilityPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCActabilityPacket
);
impl Marshal for SCActabilityPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.last)
            .write_u8(self.actabilities.len() as u8);
        for a in &self.actabilities {
            s.write_u32(a.id).write_i32(a.point).write_u8(a.step);
        }
    }
}

/// `SCCountUnreadMailPacket(count)`.
#[derive(Debug, Clone)]
pub struct SCCountUnreadMailPacket {
    pub count: CountUnreadMail,
}
packet_def!(
    SCCountUnreadMailPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCountUnreadMailPacket
);
impl Marshal for SCCountUnreadMailPacket {
    fn write(&self, s: &mut PacketStream) {
        self.count.write(s);
    }
}

/// `SCAppellationsPacket((id, active)[])`.
#[derive(Debug, Clone)]
pub struct SCAppellationsPacket {
    pub appellations: Vec<(u32, bool)>,
}
packet_def!(
    SCAppellationsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAppellationsPacket
);
impl Marshal for SCAppellationsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.appellations.len() as i32);
        for (id, active) in &self.appellations {
            s.write_u32(*id).write_bool(*active);
        }
    }
}

/// `Portal.Write` with the zone-origin offsets already applied.
#[derive(Debug, Clone, PartialEq)]
pub struct PortalWire {
    pub id: u32,
    pub name: String,
    pub zone_id: u32,
    pub off_x: f32,
    pub off_y: f32,
    pub z: f32,
    pub z_rot: f32,
}

impl Marshal for PortalWire {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_str(&self.name)
            .write_u32(self.zone_id)
            .write_f32(self.off_x)
            .write_f32(self.off_y)
            .write_f32(self.z)
            .write_f32(self.z_rot);
    }
}

/// `SCCharacterPortalsPacket(portals)`.
#[derive(Debug, Clone)]
pub struct SCCharacterPortalsPacket {
    pub portals: Vec<PortalWire>,
}
packet_def!(
    SCCharacterPortalsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterPortalsPacket
);
impl Marshal for SCCharacterPortalsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.portals.len() as i32);
        for p in &self.portals {
            p.write(s);
        }
    }
}

/// `SCCharacterReturnDistrictsPacket(portals, returnDistrictId)`.
#[derive(Debug, Clone)]
pub struct SCCharacterReturnDistrictsPacket {
    pub portals: Vec<PortalWire>,
    pub return_district_id: u32,
}
packet_def!(
    SCCharacterReturnDistrictsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterReturnDistrictsPacket
);
impl Marshal for SCCharacterReturnDistrictsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.portals.len() as i32);
        for p in &self.portals {
            p.write(s);
        }
        s.write_u32(self.return_district_id);
    }
}

/// `SCFriendsPacket(total, friends)`.
#[derive(Debug, Clone)]
pub struct SCFriendsPacket {
    pub friends: Vec<FriendInfo>,
}
packet_def!(
    SCFriendsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCFriendsPacket
);
impl Marshal for SCFriendsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.friends.len() as i32)
            .write_i32(self.friends.len() as i32);
        for f in &self.friends {
            f.write(s);
        }
    }
}

/// `SCFriendStatusChangedPacket(friend)`.
#[derive(Debug, Clone)]
pub struct SCFriendStatusChangedPacket {
    pub friend: FriendInfo,
}
packet_def!(
    SCFriendStatusChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCFriendStatusChangedPacket
);
impl Marshal for SCFriendStatusChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        self.friend.write(s);
    }
}

/// `SCBlockedUsersPacket(total, blocked)`.
#[derive(Debug, Clone)]
pub struct SCBlockedUsersPacket {
    pub blocked: Vec<BlockedInfo>,
}
packet_def!(
    SCBlockedUsersPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCBlockedUsersPacket
);
impl Marshal for SCBlockedUsersPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.blocked.len() as i32)
            .write_i32(self.blocked.len() as i32);
        for b in &self.blocked {
            b.write(s);
        }
    }
}

/// `SCConflictZoneStatePacket(zoneId, state, endTime)`.
#[derive(Debug, Clone)]
pub struct SCConflictZoneStatePacket {
    pub zone_group_id: u16,
    pub state: u8,
    pub end_time: UnixTime,
}
packet_def!(
    SCConflictZoneStatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCConflictZoneStatePacket
);
impl Marshal for SCConflictZoneStatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.zone_group_id)
            .write_u8(self.state)
            .write_i64(self.end_time);
    }
}

/// `SystemFaction.Write`.
pub fn write_system_faction(f: &SystemFaction, s: &mut PacketStream) {
    s.write_u32(f.id)
        .write_bool(f.aggro_link)
        .write_u32(f.mother_id)
        .write_str(&f.name)
        .write_u32(f.owner_id)
        .write_str(&f.owner_name)
        .write_i8(f.unit_owner_type)
        .write_u8(f.political_system)
        .write_i64(f.created)
        .write_bool(f.diplomacy_target)
        .write_u8(f.allow_change_name);
}

/// `SCFactionListPacket(SystemFaction[])`.
#[derive(Debug, Clone)]
pub struct SCFactionListPacket {
    pub factions: Vec<SystemFaction>,
}
packet_def!(
    SCFactionListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCFactionListPacket
);
impl Marshal for SCFactionListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.factions.len() as u8);
        for f in &self.factions {
            write_system_faction(f, s);
        }
    }
}

/// `new SCFactionListPacket()` (no factions). Note (C#): this constructor uses the hard-coded
/// type id `0x007`, which in the 1.2 table is `SCTrionConfigPacket`.
#[derive(Debug, Clone, Default)]
pub struct SCFactionListEmptyPacket;
impl crate::net::packet::PacketDef for SCFactionListEmptyPacket {
    const OPCODE: u16 = 0x007;
    const LEVEL: u8 = LEVEL_PLAIN;
    const NAME: &'static str = "SCFactionListPacket";
}
impl Marshal for SCFactionListEmptyPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(0);
    }
}

/// `SCFactionRelationListPacket(FactionRelation[])`.
#[derive(Debug, Clone)]
pub struct SCFactionRelationListPacket {
    pub relations: Vec<FactionRelation>,
}
packet_def!(
    SCFactionRelationListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCFactionRelationListPacket
);

fn write_relations(relations: &[FactionRelation], s: &mut PacketStream) {
    s.write_bool(false) // uiRequest
        .write_u8(relations.len() as u8);
    for r in relations {
        s.write_u32(r.id)
            .write_u32(r.id2)
            .write_u8(r.state)
            .write_i64(r.exp_time)
            .write_i64(0) // type(id)
            .write_u8(0); // nState
    }
}

impl Marshal for SCFactionRelationListPacket {
    fn write(&self, s: &mut PacketStream) {
        write_relations(&self.relations, s);
    }
}

/// `new SCFactionRelationListPacket()`. Note (C#): hard-coded type id `0x008`, which in the
/// 1.2 table is `SCFactionListPacket`.
#[derive(Debug, Clone, Default)]
pub struct SCFactionRelationListEmptyPacket;
impl crate::net::packet::PacketDef for SCFactionRelationListEmptyPacket {
    const OPCODE: u16 = 0x008;
    const LEVEL: u8 = LEVEL_PLAIN;
    const NAME: &'static str = "SCFactionRelationListPacket";
}
impl Marshal for SCFactionRelationListEmptyPacket {
    fn write(&self, s: &mut PacketStream) {
        write_relations(&[], s);
    }
}

/// `SCExpeditionRolePolicyListPacket(policies)`.
#[derive(Debug, Clone)]
pub struct SCExpeditionRolePolicyListPacket {
    pub policies: Vec<ExpeditionRolePolicy>,
}
packet_def!(
    SCExpeditionRolePolicyListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCExpeditionRolePolicyListPacket
);
impl Marshal for SCExpeditionRolePolicyListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.policies.len() as u8);
        for p in &self.policies {
            p.write(s);
        }
    }
}

/// `SCExpeditionMemberListPacket(total, id, members)`.
#[derive(Debug, Clone)]
pub struct SCExpeditionMemberListPacket {
    pub total: u32,
    pub id: u32,
    pub members: Vec<ExpeditionMember>,
}
packet_def!(
    SCExpeditionMemberListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCExpeditionMemberListPacket
);
impl Marshal for SCExpeditionMemberListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.total)
            .write_u8(self.members.len() as u8)
            .write_u32(self.id);
        for m in &self.members {
            m.write(s);
        }
    }
}

/// `SCExpeditionMemberStatusChangedPacket(member, flag)`.
#[derive(Debug, Clone)]
pub struct SCExpeditionMemberStatusChangedPacket {
    pub member: ExpeditionMember,
    pub flag: u8,
}
packet_def!(
    SCExpeditionMemberStatusChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCExpeditionMemberStatusChangedPacket
);
impl Marshal for SCExpeditionMemberStatusChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        self.member.write(s);
        s.write_u8(self.flag);
    }
}

/// `SCResponseUIDataPacket(characterId, uiDataType, uiData)`.
#[derive(Debug, Clone)]
pub struct SCResponseUIDataPacket {
    pub character_id: u32,
    pub ui_data_type: u16,
    pub ui_data: String,
}
packet_def!(
    SCResponseUIDataPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCResponseUIDataPacket
);
impl Marshal for SCResponseUIDataPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id)
            .write_u16(self.ui_data_type)
            .write_str(&self.ui_data)
            // `uiData.Length + 1` (UTF-16 code units).
            .write_i32(self.ui_data.encode_utf16().count() as i32 + 1);
    }
}

/// `SCUnitPointsPacket(id, health, mana)`.
#[derive(Debug, Clone)]
pub struct SCUnitPointsPacket {
    pub obj_id: u32,
    pub hp: i32,
    pub mp: i32,
}
packet_def!(
    SCUnitPointsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitPointsPacket
);
impl Marshal for SCUnitPointsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_i32(self.hp.wrapping_mul(100))
            .write_i32(self.mp.wrapping_mul(100));
    }
}

/// `SCUnitsRemovedPacket(ids)`.
#[derive(Debug, Clone)]
pub struct SCUnitsRemovedPacket {
    pub ids: Vec<u32>,
}
impl SCUnitsRemovedPacket {
    /// `MaxCountPerPacket`.
    pub const MAX_COUNT_PER_PACKET: usize = 500;
}
packet_def!(
    SCUnitsRemovedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitsRemovedPacket
);
impl Marshal for SCUnitsRemovedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.ids.len() as u16);
        for &id in &self.ids {
            s.write_bc(id);
        }
    }
}

/// `SCOneUnitMovementPacket(id, moveType)`.
#[derive(Debug, Clone)]
pub struct SCOneUnitMovementPacket {
    pub obj_id: u32,
    pub move_type: MoveType,
}
packet_def!(
    SCOneUnitMovementPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCOneUnitMovementPacket
);
impl Marshal for SCOneUnitMovementPacket {
    fn write(&self, s: &mut PacketStream) {
        write_sc_one_unit_movement(s, self.obj_id, &self.move_type);
    }
}

/// `SCDetailedTimeOfDayPacket(time)`.
#[derive(Debug, Clone)]
pub struct SCDetailedTimeOfDayPacket {
    pub time: f32,
}
packet_def!(
    SCDetailedTimeOfDayPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDetailedTimeOfDayPacket
);
impl Marshal for SCDetailedTimeOfDayPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_f32(self.time)
            .write_f32(0.001_666_6) // speed
            .write_f32(0.0) // start
            .write_f32(24.0); // end
    }
}

/// `SCTimeOfDayPacket(time)`.
#[derive(Debug, Clone)]
pub struct SCTimeOfDayPacket {
    pub time: f32,
}
packet_def!(
    SCTimeOfDayPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTimeOfDayPacket
);
impl Marshal for SCTimeOfDayPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_f32(self.time);
    }
}

/// `SCOnOffSnowPacket(on)`.
#[derive(Debug, Clone)]
pub struct SCOnOffSnowPacket {
    pub on: bool,
}
packet_def!(
    SCOnOffSnowPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCOnOffSnowPacket
);
impl Marshal for SCOnOffSnowPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.on);
    }
}

/// `SCJoinedChatChannelPacket(type, subType, factionId)`.
#[derive(Debug, Clone)]
pub struct SCJoinedChatChannelPacket {
    pub chat_type: i16,
    pub sub_type: i16,
    pub faction_id: u32,
}
packet_def!(
    SCJoinedChatChannelPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCJoinedChatChannelPacket
);
impl Marshal for SCJoinedChatChannelPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i16(self.chat_type)
            .write_i16(self.sub_type)
            .write_u32(self.faction_id)
            .write_str(""); // name
    }
}

/// `SCLeavedChatChannelPacket(type, subType, factionId)`.
#[derive(Debug, Clone)]
pub struct SCLeavedChatChannelPacket {
    pub chat_type: i16,
    pub sub_type: i16,
    pub faction_id: u32,
}
packet_def!(
    SCLeavedChatChannelPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLeavedChatChannelPacket
);
impl Marshal for SCLeavedChatChannelPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i16(self.chat_type)
            .write_i16(self.sub_type)
            .write_u32(self.faction_id);
    }
}

/// The sender fields `SCChatMessagePacket` reads from its `Character`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ChatSender {
    pub faction_id: u32,
    pub obj_id: u32,
    pub id: u32,
    pub race: u8,
    pub name: String,
    /// `Connection?.GetAttribute("gmFlag") != null` (never set in 1.2).
    pub gm: bool,
}

/// `SCChatMessagePacket(type, [character], message, ability, languageType)`.
#[derive(Debug, Clone)]
pub struct SCChatMessagePacket {
    pub chat_type: i16,
    pub sender: Option<ChatSender>,
    pub message: String,
    pub ability: i32,
    pub language_type: u8,
}
packet_def!(
    SCChatMessagePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCChatMessagePacket
);
impl Marshal for SCChatMessagePacket {
    fn write(&self, s: &mut PacketStream) {
        let c = self.sender.as_ref();
        let faction = c.map_or(0, |c| c.faction_id);
        s.write_i16(self.chat_type)
            .write_i16(faction as i16)
            .write_u32(faction)
            .write_bc(c.map_or(0, |c| c.obj_id))
            .write_u32(c.map_or(0, |c| c.id))
            .write_u8(0) // TODO (C#): languageType
            .write_u8(c.map_or(0, |c| c.race))
            .write_u32(faction);
        match c {
            Some(c) if c.gm => s.write_str(&format!("GM {}", c.name)),
            Some(c) => s.write_str(&c.name),
            None => s.write_str(""),
        };
        s.write_str(&self.message)
            .write_i32(if c.is_some() { self.ability } else { 0 })
            .write_i32(0); // option
    }
}

/// `SCErrorMsgPacket(errorMessage, type, isNotify)`.
#[derive(Debug, Clone)]
pub struct SCErrorMsgPacket {
    pub error1: i16,
    pub error2: i16,
    pub kind: u32,
    pub is_notify: bool,
}
packet_def!(
    SCErrorMsgPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCErrorMsgPacket
);
impl SCErrorMsgPacket {
    pub fn new(error: i16, kind: u32, is_notify: bool) -> Self {
        Self {
            error1: error,
            error2: error,
            kind,
            is_notify,
        }
    }
}
impl Marshal for SCErrorMsgPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i16(self.error1)
            .write_i16(self.error2)
            .write_u32(self.kind)
            .write_bool(self.is_notify);
    }
}

/// `SCPrepareLeaveWorldPacket(time, target, idleKick)`.
#[derive(Debug, Clone)]
pub struct SCPrepareLeaveWorldPacket {
    pub time: i32,
    pub target: u8,
    pub idle_kick: bool,
}
packet_def!(
    SCPrepareLeaveWorldPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCPrepareLeaveWorldPacket
);
impl Marshal for SCPrepareLeaveWorldPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.time)
            .write_u8(self.target)
            .write_bool(self.idle_kick);
    }
}

/// `SCLeaveWorldGrantedPacket(target)`.
#[derive(Debug, Clone)]
pub struct SCLeaveWorldGrantedPacket {
    pub target: u8,
}
packet_def!(
    SCLeaveWorldGrantedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLeaveWorldGrantedPacket
);
impl Marshal for SCLeaveWorldGrantedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.target);
    }
}

/// `SCCharacterCreationFailedPacket(reason)`.
#[derive(Debug, Clone)]
pub struct SCCharacterCreationFailedPacket {
    pub reason: u8,
}
packet_def!(
    SCCharacterCreationFailedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterCreationFailedPacket
);
impl Marshal for SCCharacterCreationFailedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.reason);
    }
}

/// `SCDeleteCharacterResponsePacket(characterId, status, deleteRequestedTime, deleteDelay)`.
#[derive(Debug, Clone)]
pub struct SCDeleteCharacterResponsePacket {
    pub character_id: u32,
    pub status: u8,
    pub delete_request_time: UnixTime,
    pub delete_time: UnixTime,
}
packet_def!(
    SCDeleteCharacterResponsePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDeleteCharacterResponsePacket
);
impl Marshal for SCDeleteCharacterResponsePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id)
            .write_u8(self.status)
            .write_i64(self.delete_request_time)
            .write_i64(self.delete_time);
    }
}

/// `SCCancelCharacterDeleteResponsePacket(characterId, deleteStatus)`.
#[derive(Debug, Clone)]
pub struct SCCancelCharacterDeleteResponsePacket {
    pub character_id: u32,
    pub status: u8,
}
packet_def!(
    SCCancelCharacterDeleteResponsePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCancelCharacterDeleteResponsePacket
);
impl Marshal for SCCancelCharacterDeleteResponsePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id).write_u8(self.status);
    }
}

/// `SCCharacterDeletedPacket(characterId, characterName)`.
#[derive(Debug, Clone)]
pub struct SCCharacterDeletedPacket {
    pub character_id: u32,
    pub name: String,
}
packet_def!(
    SCCharacterDeletedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterDeletedPacket
);
impl Marshal for SCCharacterDeletedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id).write_str(&self.name);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    fn body(p: &dyn crate::net::packet::ServerPacket) -> Vec<u8> {
        encode(p)[6..].to_vec()
    }

    #[test]
    fn small_packets_are_byte_exact() {
        assert_eq!(
            body(&SCUnitPointsPacket {
                obj_id: 0x01_02_03,
                hp: 5,
                mp: 7
            }),
            vec![3, 2, 1, 0xF4, 1, 0, 0, 0xBC, 2, 0, 0]
        );
        assert_eq!(
            body(&SCUnitsRemovedPacket {
                ids: vec![0x100, 0x101]
            }),
            vec![2, 0, 0, 1, 0, 1, 1, 0]
        );
        assert_eq!(
            body(&SCResponseUIDataPacket {
                character_id: 1,
                ui_data_type: 2,
                ui_data: "ab".into()
            }),
            vec![1, 0, 0, 0, 2, 0, 2, 0, b'a', b'b', 3, 0, 0, 0]
        );
        assert_eq!(
            body(&SCJoinedChatChannelPacket {
                chat_type: 1,
                sub_type: 4,
                faction_id: 0
            }),
            vec![1, 0, 4, 0, 0, 0, 0, 0, 0, 0]
        );
        assert_eq!(
            body(&SCErrorMsgPacket::new(300, 0, true)),
            vec![0x2C, 1, 0x2C, 1, 0, 0, 0, 0, 1]
        );
        assert_eq!(
            body(&SCPrepareLeaveWorldPacket {
                time: 10_000,
                target: 1,
                idle_kick: false
            }),
            vec![0x10, 0x27, 0, 0, 1, 0]
        );
        assert_eq!(body(&SCLeaveWorldGrantedPacket { target: 0 }), vec![0]);
        assert_eq!(
            body(&SCDeleteCharacterResponsePacket {
                character_id: 9,
                status: 2,
                delete_request_time: 1,
                delete_time: 2
            }),
            vec![9, 0, 0, 0, 2, 1, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0]
        );
        assert_eq!(
            body(&SCCancelCharacterDeleteResponsePacket {
                character_id: 9,
                status: 3
            }),
            vec![9, 0, 0, 0, 3]
        );
        assert_eq!(
            body(&SCCharacterGamePointsPacket {
                honor_point: 1,
                vocation_point: 2
            })
            .len(),
            40
        );
        assert_eq!(body(&SCOnOffSnowPacket { on: false }), vec![0]);
        assert_eq!(
            body(&SCDetailedTimeOfDayPacket { time: 12.0 }),
            [12.0f32, 0.001_666_6, 0.0, 24.0]
                .iter()
                .flat_map(|f| f.to_le_bytes())
                .collect::<Vec<_>>()
        );
    }

    #[test]
    fn empty_faction_lists_use_the_csharp_hardcoded_ids() {
        let e = encode(&SCFactionListEmptyPacket);
        assert_eq!(e, vec![0xDD, 1, 0, 0, 0x07, 0x00, 0]);
        let e = encode(&SCFactionRelationListEmptyPacket);
        assert_eq!(e, vec![0xDD, 1, 0, 0, 0x08, 0x00, 0, 0]);
    }

    #[test]
    fn chat_message_layout() {
        let p = SCChatMessagePacket {
            chat_type: 0,
            sender: Some(ChatSender {
                faction_id: 101,
                obj_id: 0x100,
                id: 5,
                race: 1,
                name: "Ab".into(),
                gm: false,
            }),
            message: "hi".into(),
            ability: 3,
            language_type: 0,
        };
        let mut want = PacketStream::new();
        want.write_i16(0)
            .write_i16(101)
            .write_u32(101)
            .write_bc(0x100)
            .write_u32(5)
            .write_u8(0)
            .write_u8(1)
            .write_u32(101)
            .write_str("Ab")
            .write_str("hi")
            .write_i32(3)
            .write_i32(0);
        assert_eq!(body(&p), want.into_bytes());
        // System message: no sender, ability forced to 0.
        let p = SCChatMessagePacket {
            chat_type: -2,
            sender: None,
            message: "m".into(),
            ability: 9,
            language_type: 0,
        };
        let b = body(&p);
        assert_eq!(b[..2], (-2i16).to_le_bytes());
        assert_eq!(b[b.len() - 8..], [0; 8]);
    }
}

/// `ErrorMessageType` values used by this phase.
pub mod error_message {
    pub const WHISPER_NO_TARGET: i16 = 265;
    pub const CHAT_NOT_IN_PARTY: i16 = 267;
    pub const CHAT_NOT_IN_RAID: i16 = 268;
    pub const CLOSED_ZONE: i16 = 287;
    pub const CHAT_NOT_IN_EXPEDITION: i16 = 311;
    pub const CHAT_NOT_IN_FAMILY: i16 = 412;
    pub const CHAT_CANNOT_WHISPER_TO_HOSTILE: i16 = 437;
    pub const CHAT_NOT_RAID_OWNER: i16 = 496;
}

/// `SCLeaveWorldCanceledPacket()`.
#[derive(Debug, Clone, Default)]
pub struct SCLeaveWorldCanceledPacket;
packet_def!(
    SCLeaveWorldCanceledPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLeaveWorldCanceledPacket
);
impl Marshal for SCLeaveWorldCanceledPacket {
    fn write(&self, _s: &mut PacketStream) {}
}
