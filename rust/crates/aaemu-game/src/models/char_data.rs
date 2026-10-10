//! The per-character collections loaded by `Character.Load()` and written by the select /
//! spawn packets: `ActionSlot[]`, `CharacterAbilities`, `CharacterActability`,
//! `CharacterSkills`, `CharacterAppellations`, `CharacterPortals`, `CharacterFriends`,
//! `CharacterBlocked`, `CharacterQuests` (rows only), `CharacterVisualOptions`,
//! `CountUnreadMail`, `Friend` / `Blocked` infos.

use aaemu_commons::{Marshal, MarshalError, PacketStream};

use crate::data::world_data::Portal;
use crate::data::{CharacterData, SkillData};

/// `Character.MaxActionSlots`.
pub const MAX_ACTION_SLOTS: usize = 85;

/// `ActionSlotType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum ActionSlotType {
    #[default]
    None,
    ItemType,
    Spell,
    Macro,
    ItemId,
    RidePetSpell,
    /// Unknown byte (C# logs "Unknown ActionSlotType!" and writes no id).
    Unknown(u8),
}

impl ActionSlotType {
    pub fn from_u8(v: u8) -> Self {
        match v {
            0 => Self::None,
            1 => Self::ItemType,
            2 => Self::Spell,
            3 => Self::Macro,
            4 => Self::ItemId,
            5 => Self::RidePetSpell,
            o => Self::Unknown(o),
        }
    }
    pub fn as_u8(self) -> u8 {
        match self {
            Self::None => 0,
            Self::ItemType => 1,
            Self::Spell => 2,
            Self::Macro => 3,
            Self::ItemId => 4,
            Self::RidePetSpell => 5,
            Self::Unknown(o) => o,
        }
    }
}

/// `ActionSlot`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct ActionSlot {
    pub kind: ActionSlotType,
    pub action_id: u64,
}

/// Writes the slot list the way `SCActionSlotsPacket.Write` / `GetActionSlotsAsBlob` do.
pub fn write_action_slots(slots: &[ActionSlot], s: &mut PacketStream, packet: bool) {
    for slot in slots {
        s.write_u8(slot.kind.as_u8());
        match slot.kind {
            ActionSlotType::None => {}
            ActionSlotType::ItemType | ActionSlotType::Spell | ActionSlotType::RidePetSpell => {
                s.write_u32(slot.action_id as u32);
            }
            ActionSlotType::ItemId => {
                s.write_u64(slot.action_id);
            }
            ActionSlotType::Macro | ActionSlotType::Unknown(_) => {
                if packet {
                    tracing::error!("SCActionSlotsPacket, Unknown ActionSlotType!");
                } else {
                    tracing::error!("GetActionSlotsAsBlob, Unknown ActionSlotType!");
                }
            }
        }
    }
}

/// `LoadActionSlots(PacketStream slotsBlob)`: reads into existing slots. C# `ReadByte` past the
/// end logs and returns 0, so a short blob leaves the remaining slots `None`.
pub fn read_action_slots(slots: &mut [ActionSlot], blob: &[u8]) {
    let mut s = PacketStream::from_bytes(blob.to_vec());
    for slot in slots.iter_mut() {
        let kind = ActionSlotType::from_u8(s.read_u8().unwrap_or(0));
        slot.kind = kind;
        match kind {
            ActionSlotType::None => {}
            ActionSlotType::ItemType | ActionSlotType::Spell | ActionSlotType::RidePetSpell => {
                slot.action_id = u64::from(s.read_u32().unwrap_or(0));
            }
            ActionSlotType::ItemId => slot.action_id = s.read_u64().unwrap_or(0),
            _ => tracing::error!("LoadActionSlots, Unknown ActionSlotType!"),
        }
    }
}

/// `AbilityType`.
pub mod ability {
    pub const GENERAL: u8 = 0;
    pub const NONE: u8 = 11;
}

/// `Ability`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Ability {
    pub id: u8,
    pub order: u8,
    pub exp: i32,
}

/// `CharacterAbilities`: abilities 1..=10 in dictionary order.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CharacterAbilities {
    pub abilities: Vec<Ability>,
}

impl Default for CharacterAbilities {
    fn default() -> Self {
        Self {
            abilities: (1..11)
                .map(|id| Ability {
                    id,
                    order: 255,
                    exp: 0,
                })
                .collect(),
        }
    }
}

impl CharacterAbilities {
    pub fn get_mut(&mut self, id: u8) -> Option<&mut Ability> {
        self.abilities.iter_mut().find(|a| a.id == id)
    }

    /// `SetAbility(id, order)`.
    pub fn set_ability(&mut self, id: u8, order: u8) {
        if let Some(a) = self.get_mut(id) {
            a.order = order;
        }
    }

    /// `Load` row: replaces the entry (orders from the active abilities).
    pub fn load_row(&mut self, id: u8, exp: i32, actives: [u8; 3]) {
        let mut a = Ability {
            id,
            order: 255,
            exp,
        };
        if id == actives[0] {
            a.order = 0;
        }
        if id == actives[1] {
            a.order = 1;
        }
        if id == actives[2] {
            a.order = 2;
        }
        match self.abilities.iter_mut().find(|x| x.id == id) {
            Some(x) => *x = a,
            None => self.abilities.push(a),
        }
    }
}

/// `Actability`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Actability {
    pub id: u32,
    pub point: i32,
    pub step: u8,
}

/// `CharacterActability` (insertion order).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct CharacterActability {
    pub actabilities: Vec<Actability>,
}

impl CharacterActability {
    /// New characters: one entry per `actability_groups` row.
    pub fn for_new_character(data: &CharacterData) -> Self {
        Self {
            actabilities: data
                .actabilities
                .iter()
                .map(|t| Actability {
                    id: t.id,
                    point: 0,
                    step: 0,
                })
                .collect(),
        }
    }
}

/// `Skill`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Skill {
    pub id: u32,
    pub level: u8,
}

/// `CharacterSkills` (`Skills` and `PassiveBuffs` in insertion order).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct CharacterSkills {
    pub skills: Vec<Skill>,
    pub passive_buffs: Vec<u32>,
    /// `_removed`.
    pub removed: Vec<u32>,
}

impl CharacterSkills {
    /// `GetUsedSkillPoints(ability)` (`ability == 0` = all).
    pub fn used_skill_points(&self, data: &SkillData, ability: u8) -> i32 {
        let mut points = 0;
        for s in &self.skills {
            if let Some(t) = data.skill(s.id) {
                if ability == 0 || t.ability_id == ability {
                    points += t.skill_points;
                }
            }
        }
        for b in &self.passive_buffs {
            let a = data.passive_buff(*b).map_or(0, |t| t.ability_id);
            if ability == 0 || a == ability {
                points += 1;
            }
        }
        points
    }

    /// `AddSkill(template, level, packet)`: the level is recomputed from the ability level.
    pub fn add_skill_template(&mut self, data: &SkillData, skill_id: u32, ab_level: i32) {
        let Some(t) = data.skill(skill_id) else {
            return;
        };
        let level = if t.level_step > 0 {
            ((ab_level - t.ability_level) / t.level_step + 1) as u8
        } else {
            1
        };
        if self.skills.iter().any(|s| s.id == skill_id) {
            // Dictionary.Add throws on duplicates.
            tracing::error!("AddSkill: skill {skill_id} already learned");
            return;
        }
        self.skills.push(Skill { id: t.id, level });
    }
}

/// `CharacterAppellations`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct CharacterAppellations {
    pub appellations: Vec<u32>,
    pub active: u32,
}

/// `VisitedDistrict`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct VisitedDistrict {
    pub id: u32,
    pub sub_zone: u32,
    pub owner: u32,
}

/// `CharacterPortals`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct CharacterPortals {
    pub private_portals: Vec<Portal>,
    pub visited_districts: Vec<VisitedDistrict>,
    /// `DistrictPortals` (populated from the recall data).
    pub district_portals: Vec<Portal>,
}

impl CharacterPortals {
    /// `PopulateDistrictPortals`.
    pub fn populate_district_portals(&mut self, data: &crate::data::PortalData) {
        self.district_portals.clear();
        for v in &self.visited_districts {
            let Some(portals) = data.recall_by_sub_zone(v.sub_zone) else {
                continue;
            };
            for p in portals {
                if !self.district_portals.iter().any(|x| x.id == p.id) {
                    self.district_portals.push(p.clone());
                }
            }
        }
    }
}

/// `FriendTemplate`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FriendEntry {
    pub id: u32,
    pub friend_id: u32,
    pub owner: u32,
}

/// `Friend` (what `SCFriendsPacket` / `SCFriendStatusChangedPacket` write).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct FriendInfo {
    pub character_id: u32,
    pub name: String,
    pub race: u8,
    pub level: u8,
    pub health: i32,
    pub abilities: [u8; 3],
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub zone_id: u32,
    pub in_party: bool,
    pub is_online: bool,
    pub last_world_leave_time: i64,
}

impl Marshal for FriendInfo {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id)
            .write_str(&self.name)
            .write_u8(self.race)
            .write_u8(self.level)
            .write_i32(self.health)
            .write_u8(self.abilities[0])
            .write_u8(self.abilities[1])
            .write_u8(self.abilities[2])
            .write_i64(crate::models::character::convert_long(self.x))
            .write_i64(crate::models::character::convert_long(self.y))
            .write_f32(self.z)
            .write_u32(self.zone_id)
            .write_u32(0) // type(id)
            .write_bool(self.in_party)
            .write_bool(self.is_online)
            .write_i64(self.last_world_leave_time);
    }
}

/// `Blocked`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct BlockedInfo {
    pub character_id: u32,
    pub name: String,
}

impl Marshal for BlockedInfo {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id).write_str(&self.name);
    }
}

/// `CompletedQuest` (`Body` kept as the stored bytes).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CompletedQuest {
    pub id: u16,
    pub body: Vec<u8>,
}

impl CompletedQuest {
    /// `quest.Body.CopyTo(new byte[8])`.
    pub fn body8(&self) -> [u8; 8] {
        let mut b = [0u8; 8];
        let n = self.body.len().min(8);
        b[..n].copy_from_slice(&self.body[..n]);
        b
    }
}

/// An active `Quest` as stored in the `quests` table (`ReadData` / `WriteData`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ActiveQuest {
    pub id: u32,
    pub template_id: u32,
    pub status: u8,
    pub objectives: [i32; 5],
    pub step: u8,
    pub acceptor_type: u8,
    pub component_id: u32,
    pub acceptor_id: u32,
    /// `Time` (quest time limit end, unix seconds).
    pub time: i64,
}

impl ActiveQuest {
    /// `ReadData`.
    pub fn read_data(&mut self, data: &[u8]) -> Result<(), MarshalError> {
        let mut s = PacketStream::from_bytes(data.to_vec());
        for o in self.objectives.iter_mut() {
            *o = s.read_i32()?;
        }
        self.step = s.read_u8()?;
        self.acceptor_type = s.read_u8()?;
        self.component_id = s.read_u32()?;
        self.acceptor_id = s.read_u32()?;
        self.time = s.read_i64()?;
        Ok(())
    }

    /// `WriteData`.
    pub fn write_data(&self) -> Vec<u8> {
        let mut s = PacketStream::new();
        for o in self.objectives {
            s.write_i32(o);
        }
        s.write_u8(self.step)
            .write_u8(self.acceptor_type)
            .write_u32(self.component_id)
            .write_u32(self.acceptor_id)
            .write_i64(self.time);
        s.into_bytes()
    }

    /// `Quest.Write` (`now` in unix ms for `LeftTime`, unix seconds for `acceptTime`).
    pub fn write(&self, s: &mut PacketStream, now_ms: i64) {
        let time_ms = self.time.saturating_mul(1000);
        let left_time: i32 = if time_ms > now_ms {
            (time_ms - now_ms) as i32
        } else {
            -1
        };
        s.write_u32(self.id)
            .write_u32(self.template_id)
            .write_u8(self.status);
        for o in self.objectives {
            s.write_i32(o);
        }
        s.write_bool(false) // isCheckSet
            .write_bc(0) // ObjId
            .write_u32(0) // type(id)
            .write_bc(0)
            .write_bc(0)
            .write_i32(left_time)
            .write_u32(if left_time == -1 {
                0
            } else {
                self.component_id
            })
            .write_i64(0) // DoodadId
            .write_i64(now_ms / 1000) // acceptTime
            .write_u8(self.acceptor_type)
            .write_u32(self.acceptor_id);
    }
}

/// `CharacterQuests` (data only; quest logic is a later phase).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct CharacterQuests {
    pub active: Vec<ActiveQuest>,
    pub completed: Vec<CompletedQuest>,
    /// `_removed` (template ids).
    pub removed: Vec<u32>,
}

/// `CharacterVisualOptions`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct VisualOptions {
    pub flag: u8,
    pub stp: Option<[u8; 6]>,
    pub helmet: bool,
    pub back_holdable: bool,
    pub cosplay: bool,
    pub cosplay_backpack: bool,
}

impl VisualOptions {
    /// `Read`.
    pub fn read(s: &mut PacketStream) -> Result<Self, MarshalError> {
        let mut v = VisualOptions {
            flag: s.read_u8()?,
            ..Default::default()
        };
        if v.flag & 1 == 1 {
            let b = s.read_bytes(6)?;
            v.stp = Some(b.try_into().expect("6 bytes"));
        }
        if v.flag & 2 == 2 {
            v.helmet = s.read_bool()?;
        }
        if v.flag & 4 == 4 {
            v.back_holdable = s.read_bool()?;
        }
        if v.flag & 8 == 8 {
            v.cosplay = s.read_bool()?;
        }
        if v.flag & 16 == 16 {
            v.cosplay_backpack = s.read_bool()?;
        }
        Ok(v)
    }

    /// `WriteOptions`. `None` when `Stp` is null: C# `Write((byte[])null)` throws, which aborts
    /// the enclosing `PacketMarshaler` write (the packet is sent truncated).
    pub fn write_options(&self, s: &mut PacketStream) -> Option<()> {
        s.write_raw(self.stp.as_ref()?);
        s.write_bool(self.helmet)
            .write_bool(self.back_holdable)
            .write_bool(self.cosplay)
            .write_bool(self.cosplay_backpack);
        Some(())
    }
}

/// `CountUnreadMail`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CountUnreadMail {
    pub sent: i32,
    pub received: i32,
    pub mia_received: i32,
    pub commercial_received: i32,
}

impl Marshal for CountUnreadMail {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.sent)
            .write_i32(self.received)
            .write_i32(self.mia_received)
            .write_i32(self.commercial_received);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn action_slot_blob_roundtrip() {
        let mut slots = [ActionSlot::default(); MAX_ACTION_SLOTS];
        slots[1] = ActionSlot {
            kind: ActionSlotType::Spell,
            action_id: 7,
        };
        slots[2] = ActionSlot {
            kind: ActionSlotType::ItemId,
            action_id: 0x0102_0304_0506,
        };
        let mut s = PacketStream::new();
        write_action_slots(&slots, &mut s, false);
        assert_eq!(s.len(), MAX_ACTION_SLOTS + 4 + 8);
        let mut back = [ActionSlot::default(); MAX_ACTION_SLOTS];
        read_action_slots(&mut back, s.as_bytes());
        assert_eq!(back, slots);
    }

    #[test]
    fn quest_data_roundtrip_and_write() {
        let mut q = ActiveQuest {
            id: 1,
            template_id: 2,
            status: 3,
            objectives: [1, 2, 3, 4, 5],
            step: 6,
            acceptor_type: 1,
            component_id: 9,
            acceptor_id: 10,
            time: 0,
        };
        let data = q.write_data();
        assert_eq!(data.len(), 20 + 1 + 1 + 4 + 4 + 8);
        let mut r = q.clone();
        r.objectives = [0; 5];
        r.read_data(&data).unwrap();
        assert_eq!(r, q);
        q.time = 0;
        let mut s = PacketStream::new();
        q.write(&mut s, 5_000);
        assert_eq!(
            s.len(),
            4 + 4 + 1 + 20 + 1 + 3 + 4 + 3 + 3 + 4 + 4 + 8 + 8 + 1 + 4
        );
        // LeftTime -1 -> component id written as 0.
        let b = s.as_bytes();
        assert_eq!(b[43..47], (-1i32).to_le_bytes());
        assert_eq!(b[47..51], [0, 0, 0, 0]);
    }

    #[test]
    fn visual_options_read_and_null_stp() {
        let mut s = PacketStream::from_bytes(vec![0x1F, 1, 2, 3, 4, 5, 6, 1, 0, 1, 0]);
        let v = VisualOptions::read(&mut s).unwrap();
        let mut out = PacketStream::new();
        v.write_options(&mut out).unwrap();
        assert_eq!(out.as_bytes(), &[1, 2, 3, 4, 5, 6, 1, 0, 1, 0]);
        let mut s = PacketStream::from_bytes(vec![0x02, 1]);
        let v = VisualOptions::read(&mut s).unwrap();
        assert!(v.write_options(&mut PacketStream::new()).is_none());
    }
}
