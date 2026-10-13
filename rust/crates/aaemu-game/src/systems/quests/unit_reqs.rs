//! `UnitRequirementsGameData` (`unit_reqs`) and `UnitReqs.Validate`, as used by quest
//! components (`CanComponentRun`) and quest spheres (`CanTriggerSphere`).
//!
//! `Validate` reads the owner / target unit through [`ReqSubject`], so callers can implement
//! it for whatever they hold (the quest code: the locked `Character` + its quest state; the
//! current target: a snapshot taken before the character was locked).

use std::collections::HashMap;

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;

/// `UnitReqsKindType`.
pub mod req_kind {
    pub const LEVEL: u32 = 0x01;
    pub const ABILITY: u32 = 0x02;
    pub const RACE: u32 = 0x03;
    pub const GENDER: u32 = 0x04;
    pub const EQUIP_SLOT: u32 = 0x08;
    pub const EQUIP_ITEM: u32 = 0x09;
    pub const OWN_ITEM: u32 = 0x0A;
    pub const TRAINED_SKILL: u32 = 0x0B;
    pub const COMBAT: u32 = 0x0C;
    pub const BUFF: u32 = 0x0F;
    pub const TARGET_BUFF: u32 = 0x10;
    pub const TARGET_COMBAT: u32 = 0x11;
    pub const DOODAD_RANGE: u32 = 0x14;
    pub const EQUIP_SHIELD: u32 = 0x15;
    pub const NO_BUFF: u32 = 0x16;
    pub const TARGET_BUFF_TAG: u32 = 0x17;
    pub const TARGET_HEALTH_LESS_THAN: u32 = 0x1A;
    pub const TARGET_NPC: u32 = 0x1B;
    pub const TARGET_DOODAD: u32 = 0x1C;
    pub const EQUIP_RANGED: u32 = 0x1D;
    pub const NO_BUFF_TAG: u32 = 0x1E;
    pub const COMPLETE_QUEST_CONTEXT: u32 = 0x1F;
    pub const PROGRESS_QUEST_CONTEXT: u32 = 0x20;
    pub const READY_QUEST_CONTEXT: u32 = 0x21;
    pub const TARGET_NPC_GROUP: u32 = 0x22;
    pub const AREA_SPHERE: u32 = 0x23;
    pub const EXCEPT_COMPLETE_QUEST_CONTEXT: u32 = 0x24;
    pub const PRE_COMPLETE_QUEST_CONTEXT: u32 = 0x25;
    pub const TARGET_OWNER_TYPE: u32 = 0x26;
    pub const NOT_UNDER_WATER: u32 = 0x27;
    pub const FACTION_MATCH: u32 = 0x28;
    pub const TOD: u32 = 0x29;
    pub const MOTHER_FACTION: u32 = 0x2A;
    pub const ACT_ABILITY_POINT: u32 = 0x2B;
    pub const CRIME_POINT: u32 = 0x2C;
    pub const HONOR_POINT: u32 = 0x2D;
    pub const CRIME_RECORD: u32 = 0x2E;
    pub const JURY_POINT: u32 = 0x2F;
    pub const SOURCE_OWNER_TYPE: u32 = 0x30;
    pub const APPELLATION: u32 = 0x31;
    pub const LIVING_POINT: u32 = 0x32;
    pub const IN_ZONE: u32 = 0x33;
    pub const OUT_ZONE: u32 = 0x34;
    pub const VERDICT_ONLY: u32 = 0x36;
    pub const FACTION_MATCH_ONLY: u32 = 0x37;
    pub const MOTHER_FACTION_ONLY: u32 = 0x38;
    pub const FACTION_MATCH_ONLY_NOT: u32 = 0x3A;
    pub const MOTHER_FACTION_ONLY_NOT: u32 = 0x3B;
    pub const LABOR_POWER_MARGIN: u32 = 0x43;
    pub const NOT_ON_MOVING_PHYSICAL_VEHICLE: u32 = 0x44;
    pub const MAX_LEVEL: u32 = 0x45;
    pub const EXPEDITION_OWNER: u32 = 0x46;
    pub const EXPEDITION_MEMBER: u32 = 0x47;
    pub const EXCEPT_PROGRESS_QUEST_CONTEXT: u32 = 0x48;
    pub const EXCEPT_READY_QUEST_CONTEXT: u32 = 0x49;
}

/// `UnitReqs` (one `unit_reqs` row).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct UnitReq {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub kind: u32,
    pub value1: u32,
    pub value2: u32,
}

/// What `UnitReqs.Validate` reads from a unit (`BaseUnit` / `Unit` / `Character` members).
/// Methods answer for the unit kind they are implemented for; e.g. a non-player returns
/// `None` from the player-only accessors, like the C# `owner as Character` being null.
pub trait ReqSubject {
    /// `owner as Unit != null` (doodads are not units).
    fn is_unit(&self) -> bool {
        true
    }
    fn is_player(&self) -> bool;
    fn level(&self) -> u8;
    /// `Abilities.GetAbilityLevel(type)` (players only).
    fn ability_level(&self, ability: u8) -> Option<u8>;
    fn race(&self) -> Option<u8>;
    fn gender(&self) -> Option<u8>;
    /// `Equipment.GetItemBySlot(slot) != null`.
    fn equip_slot_used(&self, slot: i32) -> bool;
    /// `Equipment.GetAllItemsByTemplate(templateId, -1, ..)`.
    fn has_equipped(&self, template_id: u32) -> bool;
    /// `Inventory.GetAllItemsByTemplate(null, templateId, -1, ..)` (players only).
    fn owns_item(&self, template_id: u32) -> bool;
    fn has_skill(&self, skill_id: u32) -> bool;
    fn is_in_battle(&self) -> bool;
    fn has_buff(&self, buff_id: u32) -> bool;
    /// `Buffs.CheckBuffTag(tag)`.
    fn has_buff_tag(&self, tag: u32) -> bool;
    fn hpp(&self) -> i32;
    /// `BaseUnitType` (0 = Character, 1 = Npc, ...).
    fn base_unit_type(&self) -> u8;
    /// `TemplateId` when the unit is an NPC.
    fn npc_template_id(&self) -> Option<u32>;
    /// `TemplateId` when the object is a doodad.
    fn doodad_template_id(&self) -> Option<u32> {
        None
    }
    fn is_under_water(&self) -> bool;
    fn faction_id(&self) -> u32;
    fn mother_faction_id(&self) -> u32;
    /// `Quests.HasQuestCompleted(id)` (players only).
    fn quest_completed(&self, quest_id: u32) -> Option<bool>;
    /// `Quests.ActiveQuests.GetValueOrDefault(id)?.Step` (players only; inner `None` = not active).
    fn quest_step(&self, quest_id: u32) -> Option<Option<u8>>;
    fn actability_point(&self, actability_id: u32) -> Option<i32>;
    fn honor_point(&self) -> Option<i32>;
    fn jury_point(&self) -> Option<i32>;
    fn vocation_point(&self) -> Option<i32>;
    fn has_appellation(&self, id: u32) -> Option<bool>;
    /// `Transform.ZoneId` (zone key).
    fn zone_key(&self) -> u32;
    fn position(&self) -> (f32, f32, f32);
    /// `TimedRewardsManager.GetMaxLabor(premium) - LaborPower` (players only).
    fn labor_margin(&self) -> Option<i32>;
    /// `Expedition?.Id`, `Expedition?.OwnerId` (players only).
    fn expedition(&self) -> Option<(u32, u32)>;
    fn character_id(&self) -> Option<u32>;
}

/// World / game data the validation needs besides the units.
pub trait ReqEnv {
    /// `ZoneManager.GetZoneByKey(key)?.Id`.
    fn zone_id_of_key(&self, zone_key: u32) -> Option<u32>;
    /// `TimeManager.GetTime` (in-game hours).
    fn time_of_day(&self) -> f32;
    /// `SphereGameData.IsInsideAreaSphere(sphereId, value2, pos) != null`.
    fn inside_area_sphere(&self, sphere_id: u32, value2: u32, pos: (f32, f32, f32)) -> bool;
    /// Doodads of `template_id` within `range` of the owner (`DoodadRange`, `TargetDoodad`).
    fn doodad_in_range(&self, _template_id: u32, _range: f32) -> bool {
        false
    }
}

/// `UnitReqs.Validate(owner, target).ResultKey == ok`.
pub fn validate(
    req: &UnitReq,
    owner: &dyn ReqSubject,
    target: &dyn ReqSubject,
    env: &dyn ReqEnv,
) -> bool {
    use req_kind::*;
    let unit = owner.is_unit();
    let player = owner.is_player();
    let v1 = req.value1;
    let v2 = req.value2;
    match req.kind {
        LEVEL => {
            unit && u32::from(owner.level()) >= v1 && (v2 == 0 || u32::from(owner.level()) <= v2)
        }
        ABILITY => owner
            .ability_level(v1 as u8)
            .is_some_and(|l| u32::from(l) >= v2),
        RACE => owner.race().is_some_and(|r| u32::from(r) == v1),
        GENDER => owner.gender().is_some_and(|g| u32::from(g) == v1),
        EQUIP_SLOT => unit && owner.equip_slot_used(v1 as i32),
        EQUIP_ITEM => unit && owner.has_equipped(v1),
        OWN_ITEM => player && owner.owns_item(v1),
        TRAINED_SKILL => player && owner.has_skill(v1),
        COMBAT => unit && !owner.is_in_battle(),
        BUFF => unit && owner.has_buff(v1),
        TARGET_BUFF => target.is_unit() && target.has_buff(v1),
        TARGET_COMBAT => target.is_unit() && !target.is_in_battle(),
        DOODAD_RANGE => env.doodad_in_range(v1, v2 as f32 / 1000.0),
        EQUIP_SHIELD => false, // needs weapon holdable templates (not loaded): never a shield
        NO_BUFF => unit && !owner.has_buff(v1),
        TARGET_BUFF_TAG => {
            let t = if target.is_unit() { target } else { owner };
            t.is_unit() && t.has_buff_tag(v1)
        }
        TARGET_HEALTH_LESS_THAN => {
            target.is_unit() && target.hpp() >= v1 as i32 && target.hpp() <= v2 as i32
        }
        TARGET_NPC => target.npc_template_id() == Some(v1),
        TARGET_DOODAD => target.doodad_template_id() == Some(v1) || env.doodad_in_range(v1, 5.0),
        EQUIP_RANGED => unit && owner.equip_slot_used(if v1 == 1 { 18 } else { 17 }),
        NO_BUFF_TAG => unit && !owner.has_buff_tag(v1),
        COMPLETE_QUEST_CONTEXT => owner.quest_completed(v1).unwrap_or(false),
        PROGRESS_QUEST_CONTEXT => {
            owner.quest_step(v1).flatten() == Some(super::model::kind::PROGRESS)
        }
        READY_QUEST_CONTEXT => owner.quest_step(v1).flatten() == Some(super::model::kind::READY),
        TARGET_NPC_GROUP => target.npc_template_id().is_some(),
        AREA_SPHERE => env.inside_area_sphere(v1, v2, owner.position()),
        EXCEPT_COMPLETE_QUEST_CONTEXT => owner.quest_completed(v1).map(|c| !c).unwrap_or(false),
        PRE_COMPLETE_QUEST_CONTEXT => {
            player
                && !owner.quest_completed(v1).unwrap_or(false)
                && owner.quest_step(v1).flatten().is_some()
        }
        TARGET_OWNER_TYPE => target.is_unit() && u32::from(target.base_unit_type()) == v1,
        NOT_UNDER_WATER => unit && !owner.is_under_water(),
        FACTION_MATCH | FACTION_MATCH_ONLY => (if unit { owner.faction_id() } else { 0 }) == v1,
        TOD => {
            let current = (env.time_of_day() * 100.0).floor() as u32;
            current >= v1 && current <= v2
        }
        MOTHER_FACTION | MOTHER_FACTION_ONLY => {
            (if unit { owner.mother_faction_id() } else { 0 }) == v1
        }
        ACT_ABILITY_POINT => owner
            .actability_point(v1)
            .is_some_and(|p| i64::from(p) >= i64::from(v2)),
        CRIME_POINT | CRIME_RECORD => true,
        HONOR_POINT => owner
            .honor_point()
            .is_some_and(|h| i64::from(h) >= i64::from(v1) && i64::from(h) <= i64::from(v2)),
        JURY_POINT => owner
            .jury_point()
            .is_some_and(|j| i64::from(j) >= i64::from(v1)),
        SOURCE_OWNER_TYPE => unit && u32::from(owner.base_unit_type()) == v1,
        APPELLATION => owner.has_appellation(v1).unwrap_or(false),
        LIVING_POINT => owner
            .vocation_point()
            .is_some_and(|p| i64::from(p) >= i64::from(v1) && i64::from(p) <= i64::from(v2)),
        IN_ZONE => env.zone_id_of_key(owner.zone_key()) == Some(v1),
        OUT_ZONE => env.zone_id_of_key(owner.zone_key()) != Some(v1),
        VERDICT_ONLY => true,
        FACTION_MATCH_ONLY_NOT => (if unit { owner.faction_id() } else { 0 }) != v1,
        MOTHER_FACTION_ONLY_NOT => (if unit { owner.mother_faction_id() } else { 0 }) != v1,
        LABOR_POWER_MARGIN => i64::from(v1) <= i64::from(owner.labor_margin().unwrap_or(0)),
        NOT_ON_MOVING_PHYSICAL_VEHICLE => true,
        MAX_LEVEL => player && u32::from(owner.level()) <= v1,
        EXPEDITION_OWNER => match (owner.expedition(), owner.character_id()) {
            (Some((_, owner_id)), Some(id)) => owner_id == id,
            // `player?.Expedition?.OwnerId == player?.Id`: null == null is true
            (None, None) => true,
            (Some(_), None) | (None, Some(_)) => false,
        },
        EXPEDITION_MEMBER => owner.expedition().is_some_and(|(id, _)| id > 0),
        EXCEPT_PROGRESS_QUEST_CONTEXT => {
            !owner.quest_completed(v1).unwrap_or(true)
                && owner.quest_step(v1).flatten() != Some(super::model::kind::PROGRESS)
        }
        EXCEPT_READY_QUEST_CONTEXT => {
            !owner.quest_completed(v1).unwrap_or(true)
                && owner.quest_step(v1).flatten() != Some(super::model::kind::READY)
        }
        _ => true,
    }
}

/// `UnitRequirementsGameData`: `unit_reqs` by owner type (rows in table order).
#[derive(Debug, Clone, Default)]
pub struct UnitReqsData {
    /// `_unitReqs` (by id, first row wins: `TryAdd`).
    pub by_id: HashMap<u32, UnitReq>,
    /// `_unitReqsByOwnerType` -> owner id -> rows (table order).
    pub by_owner: HashMap<String, HashMap<u32, Vec<UnitReq>>>,
    /// Number of rows read.
    pub rows: usize,
}

impl UnitReqsData {
    pub fn load(conn: &Connection) -> Result<Self, GameDataError> {
        let mut d = UnitReqsData::default();
        for_each_row(conn, "SELECT * FROM unit_reqs", [], |r| {
            let t = UnitReq {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                owner_type: r.string("owner_type")?,
                kind: r.u32("kind_id")?,
                value1: r.u32("value1")?,
                value2: r.u32("value2")?,
            };
            d.rows += 1;
            d.by_id.entry(t.id).or_insert_with(|| t.clone());
            d.by_owner
                .entry(t.owner_type.clone())
                .or_default()
                .entry(t.owner_id)
                .or_default()
                .push(t);
            Ok(())
        })?;
        Ok(d)
    }

    /// `GetRequirement(ownerType, ownerId)` (empty when the owner type has no rows; the C#
    /// returns null there and the callers' `ToList()` would throw).
    pub fn requirements(&self, owner_type: &str, owner_id: u32) -> &[UnitReq] {
        self.by_owner
            .get(owner_type)
            .and_then(|m| m.get(&owner_id))
            .map_or(&[], Vec::as_slice)
    }

    /// `CanComponentRun` / `CanTriggerSphere`: OR requires one matching requirement;
    /// AND requires all requirements to match.
    pub fn check_all(
        &self,
        owner_type: &str,
        owner_id: u32,
        or_unit_reqs: bool,
        owner: &dyn ReqSubject,
        target: &dyn ReqSubject,
        env: &dyn ReqEnv,
    ) -> bool {
        let reqs = self.requirements(owner_type, owner_id);
        if reqs.is_empty() {
            return true; // No requirements, we're good
        }
        let mut res = !or_unit_reqs;
        for req in reqs {
            let ok = validate(req, owner, target, env);
            if or_unit_reqs {
                if ok {
                    return true;
                }
            } else {
                res &= ok;
            }
        }
        res
    }

    /// `CanComponentRun(questComponent, ownerUnit)`.
    pub fn can_component_run(
        &self,
        component_id: u32,
        or_unit_reqs: bool,
        owner: &dyn ReqSubject,
        target: &dyn ReqSubject,
        env: &dyn ReqEnv,
    ) -> bool {
        self.check_all(
            "QuestComponent",
            component_id,
            or_unit_reqs,
            owner,
            target,
            env,
        )
    }

    /// `CanTriggerSphere(sphere, ownerUnit)`.
    pub fn can_trigger_sphere(
        &self,
        sphere_id: u32,
        or_unit_reqs: bool,
        owner: &dyn ReqSubject,
        target: &dyn ReqSubject,
        env: &dyn ReqEnv,
    ) -> bool {
        self.check_all("Sphere", sphere_id, or_unit_reqs, owner, target, env)
    }
}
