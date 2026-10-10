//! Port of `AAEmu.Game.Models.Game.Char.Character` (data members, `Write`, inventory wiring).
//! The `GameObject` / `BaseUnit` / `Unit` members (name, level, hp/mp, transform, faction,
//! buffs, model, ...) live in [`Character::unit`] ([`UnitBase`]), shared with every unit kind.

use std::collections::BTreeMap;

use aaemu_commons::{Marshal, PacketStream};

use super::char_data::*;
use super::container::{slot_type, ContainerKind, ContainerRef, ItemContainer};
use super::item::{Item, UnixTime};
pub use super::transform::{convert_long, Transform};
use super::unit::{unit_type_flag, BaseUnitType, UnitBase};
use super::unit_model::UnitCustomModelParams;
use crate::managers::id::IdManager;
use crate::managers::item::ItemManager;

/// `EquipmentContainer` default size: `(int)Enum.GetValues<EquipmentItemSlot>().Max() + 1`
/// (`Cosplay = 27`).
pub const EQUIPMENT_CONTAINER_SIZE: usize = 28;

/// `WorldManager.DefaultInstanceId`.
pub const DEFAULT_INSTANCE_ID: u32 = 0;

/// `Inventory`: the character's containers (shared with the [`ItemManager`]).
#[derive(Debug, Clone)]
pub struct Inventory {
    /// `_itemContainers` in `Enum.GetValues<SlotType>()` order (minus `EquipmentMate`).
    pub containers: Vec<(u8, ContainerRef)>,
}

impl Inventory {
    /// `new Inventory(character)`: grabs (or creates) the persistent containers and applies the
    /// bag / bank sizes.
    pub fn new(
        owner_id: u32,
        num_inventory_slots: u8,
        num_bank_slots: i16,
        items: &ItemManager,
        container_ids: &IdManager,
    ) -> Self {
        let mut containers = Vec::new();
        for st in slot_type::ALL {
            if st == slot_type::EQUIPMENT_MATE {
                continue;
            }
            let c = items.container_for_character(owner_id, st, 0, container_ids);
            Self::attach(&c, st, num_inventory_slots, num_bank_slots);
            containers.push((st, c));
        }
        Self { containers }
    }

    /// An inventory whose containers are not registered anywhere (tests, lobby-only data).
    pub fn standalone(owner_id: u32, num_inventory_slots: u8, num_bank_slots: i16) -> Self {
        let mut containers = Vec::new();
        for st in slot_type::ALL {
            if st == slot_type::EQUIPMENT_MATE {
                continue;
            }
            let kind = if st == slot_type::EQUIPMENT {
                ContainerKind::Equipment
            } else {
                ContainerKind::Item
            };
            let c = ItemContainer::new(kind, owner_id, st, 0).into_ref();
            Self::attach(&c, st, num_inventory_slots, num_bank_slots);
            containers.push((st, c));
        }
        Self { containers }
    }

    fn attach(c: &ContainerRef, st: u8, inv: u8, bank: i16) {
        let mut g = c.lock().expect("container poisoned");
        g.has_owner = true;
        match st {
            slot_type::INVENTORY => g.set_container_size(i32::from(inv)),
            slot_type::BANK => g.set_container_size(i32::from(bank)),
            _ => {}
        }
    }

    pub fn get(&self, st: u8) -> Option<&ContainerRef> {
        self.containers
            .iter()
            .find(|(t, _)| *t == st)
            .map(|(_, c)| c)
    }

    pub fn equipment(&self) -> &ContainerRef {
        self.get(slot_type::EQUIPMENT).expect("equipment container")
    }

    pub fn bag(&self) -> &ContainerRef {
        self.get(slot_type::INVENTORY).expect("bag container")
    }

    pub fn warehouse(&self) -> &ContainerRef {
        self.get(slot_type::BANK).expect("bank container")
    }

    /// `Equipment.GetSlottedItemsList()`.
    pub fn equipment_slots(&self) -> Vec<Option<Item>> {
        self.equipment()
            .lock()
            .expect("container poisoned")
            .slotted_items()
    }
}

/// `Character`.
#[derive(Debug, Clone)]
pub struct Character {
    /// The `GameObject` / `BaseUnit` / `Unit` members.
    pub unit: UnitBase,
    pub id: u32,
    pub account_id: u32,
    pub access_level: i32,
    pub race: u8,
    pub gender: u8,
    pub experience: i32,
    pub recoverable_exp: i32,
    /// `_savedHp` / `_savedMp` (restored after the gear bonuses are known).
    pub saved_hp: i32,
    pub saved_mp: i32,
    pub consumed_labor_power: i32,
    pub ability1: u8,
    pub ability2: u8,
    pub ability3: u8,
    /// `ServerId` (`world_id` column).
    pub server_id: u32,
    pub faction_name: String,
    pub family: u32,
    pub dead_count: i16,
    pub dead_time: UnixTime,
    pub rez_wait_duration: i32,
    pub rez_time: UnixTime,
    pub rez_penalty_duration: i32,
    pub leave_time: UnixTime,
    pub money: i64,
    pub money2: i64,
    pub honor_point: i32,
    pub vocation_point: i32,
    /// Current crime points (clamped to >= 0 by the C# setter).
    pub crime_point: i16,
    /// `InfamyPoint` (column `crime_record`).
    pub infamy_point: i32,
    pub jury_point: i32,
    pub hostile_faction_kills: u32,
    pub honor_gained_in_combat: u32,
    pub died_in_pvp: bool,
    pub died_in_pvp_war_zone: bool,
    pub delete_request_time: UnixTime,
    pub transfer_request_time: UnixTime,
    pub delete_time: UnixTime,
    /// `BmPoint`: `AccountDetails.Loyalty`.
    pub bm_point: i64,
    pub auto_use_aa_point: bool,
    pub prev_point: i32,
    pub point: i32,
    pub gift: i32,
    pub num_inventory_slots: u8,
    pub num_bank_slots: i16,
    pub expanded_expert: u8,
    pub created: UnixTime,
    pub updated: UnixTime,
    pub return_district_id: u32,
    pub resurrection_district_id: u32,
    /// `OnlineTime` in seconds.
    pub online_time: u32,
    pub arrest_count: i32,
    pub accept_guilty_count: i32,
    pub accept_trial_count: i32,
    pub not_guilty_count: i32,
    pub guilty_count: i32,
    pub evidence_reported_count: i32,
    pub bot_reported_count: i32,
    pub reported_as_bot_count: i32,
    pub offline_guilty_time: i32,
    pub offline_guilty_region: i32,
    /// `InitializeLaborCache(accountDetails.Labor, accountDetails.LastUpdated)`.
    pub labor_power: i16,
    pub labor_power_modified: UnixTime,
    pub slots: Vec<ActionSlot>,
    /// `_options` (UI data).
    pub options: BTreeMap<u16, String>,
    pub inventory: Inventory,
    pub abilities: CharacterAbilities,
    pub actability: CharacterActability,
    pub skills: CharacterSkills,
    pub appellations: CharacterAppellations,
    pub portals: CharacterPortals,
    pub friends: Vec<FriendEntry>,
    pub removed_friends: Vec<u32>,
    /// `BlockedList` (owner, blocked id).
    pub blocked: Vec<(u32, u32)>,
    pub removed_blocked: Vec<u32>,
    pub quests: CharacterQuests,
    pub unread_mail: CountUnreadMail,
    pub visual_options: Option<VisualOptions>,
    /// `GameObject.Guid` (`Guid.NewGuid()` when the object is created).
    pub guid: [u8; 16],
    pub obj_id: u32,
    pub is_online: bool,
    /// `FinishedLoading` (`SetPlayerMoved`).
    pub finished_loading: bool,
    pub disabled_set_position: bool,
    pub breath: u32,
    // ---- phase 3 / skills: add this subsystem's Character fields below ----
    /// Character skill / combat state (`SkillCancelled`, `LastCast`, `IsInAggroListOf`, ...).
    pub skill_char: crate::systems::skills::state::CharacterSkillState,
    // ---- phase 3 / npcs: add this subsystem's Character fields below ----
    /// `Bonding` (`BondDoodad`: the doodad seat / bed the character is attached to).
    pub bonding: Option<crate::models::doodad::BondDoodad>,
    // ---- phase 3 / economy: add this subsystem's Character fields below ----

    // ---- phase 3 / social: add this subsystem's Character fields below ----
    /// `InParty` (set through `systems::social::team::set_in_party`, which notifies friends).
    pub in_party: bool,
    /// `LocalPingPosition` (`WorldSpawnPosition` x / y / z).
    pub local_ping_position: (f32, f32, f32),
    /// `OriginFactionName` (`Unit.SetFaction`).
    pub origin_faction_name: String,
    /// `MainWorldPosition` (where a summoned jury member returns to after the trial):
    /// (zone id, x, y, z).
    pub main_world_position: Option<(u32, f32, f32, f32)>,
    // ---- phase 3 / quests: add this subsystem's Character fields below ----
    /// The live quest objects (`Quests.ActiveQuests`, quest event subscriptions,
    /// `CurrentlyPlayingCinemaId`); `quests` holds their DB / wire rows.
    pub quest_state: crate::systems::quests::state::QuestState,
    // ---- phase 3 / vehicles: add this subsystem's Character fields below ----
    /// `IsRiding` (mounted on a mate).
    pub is_riding: bool,
    /// `AttachedPoint` (`AttachPointKind` of the mate / slave seat, 0 = None).
    pub attached_point: u8,
}

/// `Guid.NewGuid().ToByteArray()` (random, version 4 / variant 1).
pub fn new_guid() -> [u8; 16] {
    let mut b: [u8; 16] = rand::random();
    b[7] = (b[7] & 0x0F) | 0x40;
    b[8] = (b[8] & 0x3F) | 0x80;
    b
}

impl Character {
    /// `new Character(modelParams)` with an empty, unregistered inventory.
    pub fn new(id: u32, account_id: u32, model_params: UnitCustomModelParams) -> Self {
        Self {
            unit: UnitBase {
                model_params,
                ..UnitBase::default()
            },
            id,
            account_id,
            access_level: 0,
            race: 0,
            gender: 0,
            experience: 0,
            recoverable_exp: 0,
            saved_hp: 99_999_999,
            saved_mp: 99_999_999,
            consumed_labor_power: 0,
            ability1: 0,
            ability2: 0,
            ability3: 0,
            server_id: 0,
            faction_name: String::new(),
            family: 0,
            dead_count: 0,
            dead_time: 0,
            rez_wait_duration: 0,
            rez_time: 0,
            rez_penalty_duration: 0,
            leave_time: 0,
            money: 0,
            money2: 0,
            honor_point: 0,
            vocation_point: 0,
            crime_point: 0,
            infamy_point: 0,
            jury_point: 0,
            hostile_faction_kills: 0,
            honor_gained_in_combat: 0,
            died_in_pvp: false,
            died_in_pvp_war_zone: false,
            delete_request_time: 0,
            transfer_request_time: 0,
            delete_time: 0,
            bm_point: 0,
            auto_use_aa_point: false,
            prev_point: 0,
            point: 0,
            gift: 0,
            num_inventory_slots: 0,
            num_bank_slots: 0,
            expanded_expert: 0,
            created: 0,
            updated: 0,
            return_district_id: 0,
            resurrection_district_id: 0,
            online_time: 0,
            arrest_count: 0,
            accept_guilty_count: 0,
            accept_trial_count: 0,
            not_guilty_count: 0,
            guilty_count: 0,
            evidence_reported_count: 0,
            bot_reported_count: 0,
            reported_as_bot_count: 0,
            offline_guilty_time: 0,
            offline_guilty_region: 0,
            labor_power: 0,
            labor_power_modified: 0,
            slots: vec![ActionSlot::default(); MAX_ACTION_SLOTS],
            options: BTreeMap::new(),
            inventory: Inventory::standalone(id, 0, 0),
            abilities: CharacterAbilities::default(),
            actability: CharacterActability::default(),
            skills: CharacterSkills::default(),
            appellations: CharacterAppellations::default(),
            portals: CharacterPortals::default(),
            friends: Vec::new(),
            removed_friends: Vec::new(),
            blocked: Vec::new(),
            removed_blocked: Vec::new(),
            quests: CharacterQuests::default(),
            unread_mail: CountUnreadMail::default(),
            visual_options: None,
            guid: new_guid(),
            obj_id: 0,
            is_online: false,
            finished_loading: false,
            disabled_set_position: false,
            breath: 60_000,
            // ---- phase 3 / skills ----
            skill_char: Default::default(),
            // ---- phase 3 / npcs ----
            bonding: None,
            // ---- phase 3 / economy ----

            // ---- phase 3 / social ----
            in_party: false,
            local_ping_position: (0.0, 0.0, 0.0),
            origin_faction_name: String::new(),
            main_world_position: None,
            // ---- phase 3 / quests ----
            quest_state: Default::default(),
            // ---- phase 3 / vehicles ----
            is_riding: false,
            attached_point: 0,
        }
    }

    /// `RaceGender` (`16 * Gender + Race`).
    pub fn race_gender(&self) -> u8 {
        16u8.wrapping_mul(self.gender).wrapping_add(self.race)
    }

    /// `SetAction(slot, type, id)` (C# indexes the array: out of range throws).
    pub fn set_action(&mut self, slot: usize, kind: ActionSlotType, id: u64) {
        match self.slots.get_mut(slot) {
            Some(s) => {
                s.kind = kind;
                s.action_id = id;
            }
            None => tracing::error!("SetAction: slot {slot} out of range"),
        }
    }

    /// `GetOption(key)`.
    pub fn get_option(&self, key: u16) -> String {
        self.options.get(&key).cloned().unwrap_or_default()
    }

    /// `CharacterAbilities.GetActiveAbilities`.
    pub fn active_abilities(&self) -> Vec<u8> {
        [self.ability1, self.ability2, self.ability3]
            .into_iter()
            .filter(|&a| a != ability::NONE)
            .collect()
    }

    /// `GetActionSlotsAsBlob`.
    pub fn action_slots_blob(&self) -> Vec<u8> {
        let mut s = PacketStream::new();
        write_action_slots(&self.slots, &mut s, false);
        s.into_bytes()
    }
}

crate::impl_unit_data!(
    Character,
    BaseUnitType::Character,
    unit_type_flag::CHARACTER,
    {
        fn race_gender(&self) -> u8 {
            Character::race_gender(self)
        }
    }
);

impl Marshal for Character {
    /// `Character.Write` (character list, `SCCreateCharacterResponsePacket`,
    /// `SCCharacterStatePacket`).
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_str(&self.unit.name)
            .write_u8(self.race)
            .write_u8(self.gender)
            .write_u8(self.unit.level)
            .write_i32(self.unit.hp)
            .write_i32(self.unit.mp)
            .write_u32(self.unit.transform.zone_id)
            .write_u32(self.unit.faction_id)
            .write_str(&self.faction_name)
            .write_u32(self.unit.expedition_id)
            .write_u32(self.family);

        for item in self.inventory.equipment_slots() {
            match item {
                None => {
                    s.write_i32(0);
                }
                Some(item) => item.write(s),
            }
        }

        s.write_u8(self.ability1)
            .write_u8(self.ability2)
            .write_u8(self.ability3);

        s.write_i64(convert_long(self.unit.transform.x))
            .write_i64(convert_long(self.unit.transform.y))
            .write_f32(self.unit.transform.z);

        self.unit.model_params.write(s);
        s.write_i16(self.labor_power)
            .write_i64(self.labor_power_modified)
            .write_i16(self.dead_count)
            .write_i64(self.dead_time)
            .write_i32(self.rez_wait_duration)
            .write_i64(self.rez_time)
            .write_i32(self.rez_penalty_duration)
            .write_i64(self.leave_time) // lastWorldLeaveTime
            .write_i64(self.money)
            .write_i64(0) // moneyAmount ?
            .write_i16(self.crime_point) // current crime points (/50)
            .write_i32(self.infamy_point) // total infamy
            .write_i16(0) // crimeScore? trials served?
            .write_i64(self.delete_request_time)
            .write_i64(self.transfer_request_time)
            .write_i64(self.delete_time) // deleteDelay
            .write_i32(self.consumed_labor_power)
            .write_i64(self.bm_point) // loyalty tokens
            .write_i64(self.money2)
            .write_i64(0) // moneyAmount ?
            .write_bool(self.auto_use_aa_point)
            .write_i32(self.prev_point)
            .write_i32(self.point)
            .write_i32(self.gift)
            .write_i64(self.updated)
            .write_u8(0); // forceNameChange ?
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_equipment_layout() {
        let mut c = Character::new(1, 1, UnitCustomModelParams::default());
        c.unit.name = "Ab".into();
        let mut s = PacketStream::new();
        c.write(&mut s);
        let mut model = PacketStream::new();
        c.unit.model_params.write(&mut model);
        let head = 4 + 2 + 2 + 3 + 8 + 4 + 4 + 2 + 4 + 4;
        let equip = 4 * EQUIPMENT_CONTAINER_SIZE;
        let tail = 3
            + 8
            + 8
            + 4
            + model.len()
            + 2
            + 8
            + 2
            + 8
            + 4
            + 8
            + 4
            + 8
            + 8
            + 8
            + 2
            + 4
            + 2
            + 8 * 3
            + 4
            + 8 * 3
            + 1
            + 4 * 3
            + 8
            + 1;
        assert_eq!(s.len(), head + equip + tail);
    }
}
