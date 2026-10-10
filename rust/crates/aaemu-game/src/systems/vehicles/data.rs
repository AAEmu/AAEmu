//! Game data of the vehicles subsystem: `MateGameData` (mount skills), `SlaveGameData`
//! (slave templates, bindings, initial items / buffs, drop doodads, mount skills, repair
//! effects, `slave_attach_points*.json`), `TransferGameData` (templates, bindings, paths and the
//! client `transfer_path.xml` roads), the template part of `ShipyardManager.Load`, the
//! `TaxationsManager` table the shipyards read and the `ModelManager` vehicle / ship models.

use std::collections::HashMap;

use serde_json::Value;

use crate::data::client_files::ClientFileManager;
use crate::data::sqlite::for_each_row;
use crate::data::world_templates::{WorldSpawnPosition, WorldTemplates};
use crate::data::GameDataError;
use crate::models::unit::{BonusTemplate, UnitAttribute, UnitModifierType};
use crate::systems::DataLoadCtx;

/// `AttachPointKind` (only the values the server code names).
pub mod attach_point {
    pub const NONE: u8 = 0;
    pub const DRIVER: u8 = 1;
    pub const PASSENGER0: u8 = 2;
    pub const PASSENGER1: u8 = 3;
    pub const TRAILED0: u8 = 30;
    pub const HEAL_POINT0: u8 = 36;
    pub const HEAL_POINT9: u8 = 45;
    pub const SYSTEM: u8 = 255;
}

/// `AttachPointKind` names (Newtonsoft accepts the names as dictionary keys).
const ATTACH_POINT_NAMES: &[(&str, u8)] = &[
    ("None", 0),
    ("Driver", 1),
    ("Passenger0", 2),
    ("Passenger1", 3),
    ("Passenger2", 4),
    ("Passenger3", 5),
    ("Passenger4", 6),
    ("Passenger5", 7),
    ("Passenger6", 8),
    ("Cannon0", 9),
    ("Cannon1", 10),
    ("Cannon2", 11),
    ("Cannon3", 12),
    ("Cannon4", 13),
    ("Cannon5", 14),
    ("Cannon6", 15),
    ("Cannon7", 16),
    ("Cannon8", 17),
    ("Hook", 18),
    ("Mast0", 19),
    ("Mast1", 20),
    ("Sail0", 21),
    ("Sail1", 22),
    ("Anchor", 23),
    ("LadderLeft", 24),
    ("LadderRight", 25),
    ("PlankLeft", 26),
    ("PlankRight", 27),
    ("LampFront", 28),
    ("LampRear", 29),
    ("Trailed0", 30),
    ("Trailed1", 31),
    ("Trailed2", 32),
    ("Trailed3", 33),
    ("Trailed4", 34),
    ("Helms", 35),
    ("HealPoint0", 36),
    ("HealPoint1", 37),
    ("HealPoint2", 38),
    ("HealPoint3", 39),
    ("HealPoint4", 40),
    ("HealPoint5", 41),
    ("HealPoint6", 42),
    ("HealPoint7", 43),
    ("HealPoint8", 44),
    ("HealPoint9", 45),
    ("Cannon9", 46),
    ("Cannon10", 47),
    ("Cannon11", 48),
    ("Cannon12", 49),
    ("Cannon13", 50),
    ("Cannon14", 51),
    ("Cannon15", 52),
    ("Cannon16", 53),
    ("Cannon17", 54),
    ("Cannon18", 55),
    ("Cannon19", 56),
    ("NamePlate01", 57),
    ("LadderRearLeft", 58),
    ("LadderRearRight", 59),
    ("Box0", 60),
    ("Box1", 61),
    ("Box2", 62),
    ("Box3", 63),
    ("Box4", 64),
    ("Box5", 65),
    ("Box6", 66),
    ("Box7", 67),
    ("Box8", 68),
    ("Box9", 69),
    ("Box10", 70),
    ("Box11", 71),
    ("Box12", 72),
    ("Box13", 73),
    ("Box14", 74),
    ("Box15", 75),
    ("Box16", 76),
    ("Box17", 77),
    ("Box18", 78),
    ("Box19", 79),
    ("Telescope", 80),
    ("Bell", 81),
    ("Rudder", 82),
    ("Sail2", 83),
    ("Mast2", 84),
    ("System", 255),
];

/// Newtonsoft's conversion of a dictionary key to `AttachPointKind`: an enum name
/// (case-insensitive) or an integer.
pub fn parse_attach_point_key(key: &str) -> Option<u8> {
    let k = key.trim();
    if let Some((_, v)) = ATTACH_POINT_NAMES
        .iter()
        .find(|(n, _)| n.eq_ignore_ascii_case(k))
    {
        return Some(*v);
    }
    k.parse::<i64>().ok().map(|v| v as u8)
}

// ---------------------------------------------------------------------------------------------
// Mates
// ---------------------------------------------------------------------------------------------

/// `NpcMountSkills`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NpcMountSkills {
    pub id: u32,
    pub npc_id: u32,
    pub mount_skill_id: u32,
}

/// `MountSkills`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MountSkills {
    pub id: u32,
    pub name: String,
    pub skill_id: u32,
}

/// `MountAttachedSkills`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct MountAttachedSkills {
    pub id: u32,
    pub mount_skill_id: u32,
    pub attach_point_id: u8,
    pub skill_id: u32,
}

// ---------------------------------------------------------------------------------------------
// Slaves
// ---------------------------------------------------------------------------------------------

/// `SlaveKind`.
pub mod slave_kind {
    pub const BIG_SAILING_SHIP: u32 = 1;
    pub const SMALL_SAILING_SHIP: u32 = 2;
    pub const SPEEDBOAT: u32 = 3;
    pub const BOAT: u32 = 4;
    pub const TANK: u32 = 5;
    pub const MACHINE: u32 = 6;
    pub const SIEGE_WEAPON: u32 = 7;
    pub const SLAVE_EQUIPMENT: u32 = 8;
    pub const FISHBOAT: u32 = 9;
    pub const MERCHANT_SHIP: u32 = 10;
    pub const LEVIATHAN: u32 = 11;
}

/// `SlaveDoodadBindings` (also used for `slave_healing_point_doodads`).
#[derive(Debug, Clone, PartialEq)]
pub struct SlaveDoodadBinding {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub attach_point_id: u8,
    pub doodad_id: u32,
    pub persist: bool,
    pub scale: f32,
}

/// `SlaveBindings`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SlaveBinding {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub slave_id: u32,
    pub attach_point_id: u8,
}

/// `SlaveDropDoodad`.
#[derive(Debug, Clone, PartialEq)]
pub struct SlaveDropDoodad {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub doodad_id: u32,
    pub count: u32,
    pub radius: f32,
    pub on_water: bool,
}

/// `SlaveInitialItems`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SlaveInitialItem {
    pub pack_id: u32,
    pub equip_slot_id: u8,
    pub item_id: u32,
}

/// `SlaveTemplate`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct SlaveTemplate {
    pub id: u32,
    pub name: String,
    pub model_id: u32,
    pub mountable: bool,
    pub spawn_x_offset: f32,
    pub spawn_y_offset: f32,
    /// `FactionId` (`FactionsEnum`).
    pub faction_id: u32,
    pub level: u32,
    pub cost: i32,
    /// `SlaveKind` (raw).
    pub slave_kind: u32,
    pub spawn_valid_area_range: u32,
    pub slave_initial_item_pack_id: u32,
    pub slave_customizing_id: u32,
    pub customizable: bool,
    pub portal_time: f32,
    pub hp25_doodad_count: i32,
    pub hp50_doodad_count: i32,
    pub hp75_doodad_count: i32,
    /// `InitialBuffs` (buff ids).
    pub initial_buffs: Vec<u32>,
    /// `PassiveBuffs` (passive buff ids).
    pub passive_buffs: Vec<u32>,
    pub doodad_bindings: Vec<SlaveDoodadBinding>,
    pub healing_point_doodads: Vec<SlaveDoodadBinding>,
    pub slave_bindings: Vec<SlaveBinding>,
    pub slave_drop_doodads: Vec<SlaveDropDoodad>,
    pub bonuses: Vec<BonusTemplate>,
}

impl SlaveTemplate {
    /// `IsABoat()`.
    pub fn is_a_boat(&self) -> bool {
        use slave_kind::*;
        matches!(
            self.slave_kind,
            BOAT | FISHBOAT | SPEEDBOAT | MERCHANT_SHIP | BIG_SAILING_SHIP | SMALL_SAILING_SHIP
        )
    }
}

/// One attach point of `slave_attach_points*.json` (`WorldSpawnPosition`, rotations converted to
/// radians).
pub type AttachPointLocation = WorldSpawnPosition;

/// The attach points of one model (`SlaveModelAttachPoint`): (model id, points).
pub type ModelAttachPoints = (u32, Vec<(u8, AttachPointLocation)>);

// ---------------------------------------------------------------------------------------------
// Transfers
// ---------------------------------------------------------------------------------------------

/// `TransferBindings`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TransferBinding {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub attach_point_id: u8,
    pub transfer_id: u32,
}

/// `TransferBindingDoodads`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TransferBindingDoodad {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub attach_point_id: u8,
    pub doodad_id: u32,
}

/// `TransferPaths`.
#[derive(Debug, Clone, PartialEq)]
pub struct TransferPath {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub path_name: String,
    pub wait_time_start: f64,
    pub wait_time_end: f64,
}

/// `TransferRoads` (one road of a `transfer_path.xml`).
#[derive(Debug, Clone, PartialEq, Default)]
pub struct TransferRoad {
    pub name: String,
    pub zone_id: u32,
    pub kind: i32,
    pub cell_x: i32,
    pub cell_y: i32,
    pub pos: Vec<WorldSpawnPosition>,
}

/// `TransferTemplate`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct TransferTemplate {
    pub id: u32,
    pub name: String,
    pub model_id: u32,
    pub wait_time: f64,
    pub cyclic: bool,
    pub path_smoothing: f32,
    pub transfer_bindings: Vec<TransferBinding>,
    /// `TransferAllPaths`.
    pub transfer_all_paths: Vec<TransferPath>,
    /// `TransferRoads` (the roads of this template's paths, from the client files).
    pub transfer_roads: Vec<TransferRoad>,
    pub transfer_binding_doodads: Vec<TransferBindingDoodad>,
}

// ---------------------------------------------------------------------------------------------
// Shipyards
// ---------------------------------------------------------------------------------------------

/// `ShipyardSteps`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ShipyardStep {
    pub id: u32,
    pub shipyard_id: u32,
    pub step: i32,
    pub model_id: u32,
    pub skill_id: u32,
    pub num_actions: i32,
    pub max_hp: i32,
}

/// `ShipyardsTemplate`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct ShipyardTemplate {
    pub id: u32,
    pub name: String,
    pub main_model_id: u32,
    pub item_id: u32,
    pub ceremony_anim_time: i32,
    pub spawn_offset_front: f32,
    pub spawn_offset_z: f32,
    pub build_radius: i32,
    pub tax_duration: i32,
    pub origin_item_id: u32,
    pub taxation_id: i32,
    /// `ShipyardSteps` (step -> step), in insertion order.
    pub steps: Vec<(i32, ShipyardStep)>,
}

impl ShipyardTemplate {
    /// `ShipyardSteps[step]`.
    pub fn step(&self, step: i32) -> Option<&ShipyardStep> {
        self.steps.iter().find(|(k, _)| *k == step).map(|(_, s)| s)
    }

    /// `ShipyardSteps.Values.Sum(step => step.NumActions)`.
    pub fn all_actions(&self) -> i32 {
        self.steps.iter().map(|(_, s)| s.num_actions).sum()
    }
}

// ---------------------------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------------------------

/// `VehicleModel` (the members the transfer movement reads).
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct VehicleModel {
    pub id: u32,
    pub velocity: f32,
    pub ang_vel: f32,
    pub wheeled_vehicle_mass: f32,
    pub wheeled_vehicle_max_gear: u32,
    pub wheeled_vehicle_gear_speed_ratio_reverse: f32,
    pub wheeled_vehicle_gear_speed_ratio_1: f32,
    pub wheeled_vehicle_gear_speed_ratio_2: f32,
    pub wheeled_vehicle_gear_speed_ratio_3: f32,
}

/// `ShipModelV1` (the members the slave spawning reads; the rest is the physics' data).
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct ShipModel {
    pub id: u32,
    pub velocity: f32,
    pub mass: f32,
    pub mass_center_x: f32,
    pub mass_center_y: f32,
    pub mass_center_z: f32,
    pub mass_box_size_x: f32,
    pub mass_box_size_y: f32,
    pub mass_box_size_z: f32,
    pub keel_length: f32,
    pub keel_height: f32,
    pub keel_offset_z: f32,
}

/// The vehicles subsystem's game data (`server.game_data.systems.vehicles`).
#[derive(Debug, Default)]
pub struct VehiclesData {
    pub npc_mount_skills: Vec<NpcMountSkills>,
    pub mount_skills: Vec<MountSkills>,
    pub mount_attached_skills: Vec<MountAttachedSkills>,
    pub slave_templates: HashMap<u32, SlaveTemplate>,
    /// `_attachPoints` (model id -> attach point -> location), points in file order.
    pub attach_points: HashMap<u32, Vec<(u8, AttachPointLocation)>>,
    pub slave_initial_items: HashMap<u32, Vec<SlaveInitialItem>>,
    /// `_slaveMountSkills` (id, slave id, mount skill id) in insertion order.
    pub slave_mount_skills: Vec<(u32, u32, u32)>,
    /// `_repairableSlaves` (slave id -> repair effect id).
    pub repairable_slaves: HashMap<u32, u32>,
    pub transfer_templates: HashMap<u32, TransferTemplate>,
    pub shipyard_templates: HashMap<u32, ShipyardTemplate>,
    /// `TaxationsManager.Taxations` (id -> tax).
    pub taxations: HashMap<u32, u32>,
    /// `ModelManager._modelTypes` (model id -> (sub id, sub type)).
    pub model_types: HashMap<u32, (u32, String)>,
    pub vehicle_models: HashMap<u32, VehicleModel>,
    pub ship_models: HashMap<u32, ShipModel>,
}

fn dup(table: &str, id: impl std::fmt::Display) -> GameDataError {
    GameDataError::File(format!(
        "{table}: an item with the same key has already been added (key {id})"
    ))
}

/// The `localized_texts` lookups of the vehicle tables (`LocalizationManager.Get`).
fn load_localized(
    ctx: &DataLoadCtx<'_>,
    tbl: &str,
    column: &str,
) -> rusqlite::Result<HashMap<i64, String>> {
    let mut out = HashMap::new();
    let has_table: bool = ctx
        .conn
        .query_row(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='localized_texts'",
            [],
            |r| r.get::<_, i64>(0),
        )
        .map(|n| n > 0)
        .unwrap_or(false);
    if !has_table {
        return Ok(out);
    }
    let sql = format!(
        "SELECT idx, \"{}\" AS txt FROM localized_texts WHERE tbl_name = ?1 AND tbl_column_name = ?2",
        ctx.language.replace('"', "")
    );
    let res = for_each_row(ctx.conn, &sql, rusqlite::params![tbl, column], |r| {
        out.insert(r.i64("idx")?, r.string_or("txt", "")?);
        Ok(())
    });
    if let Err(e) = res {
        tracing::warn!("localized_texts ({tbl}.{column}): {e}");
    }
    Ok(out)
}

fn localized(map: &HashMap<i64, String>, id: u32, fallback: String) -> String {
    match map.get(&i64::from(id)) {
        Some(t) if !t.is_empty() => t.clone(),
        _ => fallback,
    }
}

impl VehiclesData {
    /// Loads every table (`MateGameData.Load`, `SlaveGameData.Load`, `TransferGameData.Load`,
    /// `ShipyardManager.Load`, taxations, vehicle / ship models).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let mut d = Self::default();
        d.load_mates(ctx)?;
        d.load_slaves(ctx)?;
        d.load_slave_attachment_point_locations(&ctx.app_path.join("Data"));
        d.load_transfers(ctx)?;
        d.load_shipyards(ctx)?;
        d.load_models(ctx)?;
        Ok(d)
    }

    fn load_mates(&mut self, ctx: &DataLoadCtx<'_>) -> Result<(), GameDataError> {
        let mut ids = std::collections::HashSet::new();
        for_each_row(ctx.conn, "SELECT * FROM npc_mount_skills", [], |r| {
            self.npc_mount_skills.push(NpcMountSkills {
                id: r.u32("id")?,
                npc_id: r.u32("npc_id")?,
                mount_skill_id: r.u32("mount_skill_id")?,
            });
            Ok(())
        })?;
        for m in &self.npc_mount_skills {
            if !ids.insert(m.id) {
                return Err(dup("npc_mount_skills", m.id));
            }
        }
        ids.clear();
        for_each_row(ctx.conn, "SELECT * FROM mount_skills", [], |r| {
            self.mount_skills.push(MountSkills {
                id: r.u32("id")?,
                name: r.string_or("name", "")?,
                skill_id: r.u32("skill_id")?,
            });
            Ok(())
        })?;
        for m in &self.mount_skills {
            if !ids.insert(m.id) {
                return Err(dup("mount_skills", m.id));
            }
        }
        ids.clear();
        for_each_row(ctx.conn, "SELECT * FROM mount_attached_skills", [], |r| {
            self.mount_attached_skills.push(MountAttachedSkills {
                id: r.u32("id")?,
                mount_skill_id: r.u32("mount_skill_id")?,
                attach_point_id: r.u32("attach_point_id")? as u8,
                skill_id: r.u32("skill_id")?,
            });
            Ok(())
        })?;
        for m in &self.mount_attached_skills {
            if !ids.insert(m.id) {
                return Err(dup("mount_attached_skills", m.id));
            }
        }
        Ok(())
    }

    fn load_slaves(&mut self, ctx: &DataLoadCtx<'_>) -> Result<(), GameDataError> {
        let names = load_localized(ctx, "slaves", "name")?;
        let mut order = Vec::new();
        for_each_row(ctx.conn, "SELECT * FROM slaves", [], |r| {
            let id = r.u32("id")?;
            let t = SlaveTemplate {
                id,
                name: localized(&names, id, r.string("name")?),
                model_id: r.u32("model_id")?,
                mountable: r.bool("mountable")?,
                spawn_x_offset: r.f32("spawn_x_offset")?,
                spawn_y_offset: r.f32("spawn_y_offset")?,
                faction_id: r.u32_or("faction_id", 0)?,
                level: r.u32("level")?,
                cost: r.i32("cost")?,
                slave_kind: r.u32("slave_kind_id")?,
                spawn_valid_area_range: r.u32_or("spawn_valid_area_range", 0)?,
                slave_initial_item_pack_id: r.u32_or("slave_initial_item_pack_id", 0)?,
                slave_customizing_id: r.u32_or("slave_customizing_id", 0)?,
                customizable: if r.is_null("customizable")? {
                    false
                } else {
                    r.bool("customizable")?
                },
                portal_time: r.f32("portal_time")?,
                hp25_doodad_count: r.i32("hp25_doodad_count")?,
                hp50_doodad_count: r.i32("hp50_doodad_count")?,
                hp75_doodad_count: r.i32("hp75_doodad_count")?,
                ..SlaveTemplate::default()
            };
            order.push(t);
            Ok(())
        })?;
        for t in order {
            let id = t.id;
            if self.slave_templates.insert(id, t).is_some() {
                return Err(dup("slaves", id));
            }
        }
        for_each_row(
            ctx.conn,
            "SELECT * FROM unit_modifiers WHERE owner_type='Slave'",
            [],
            |r| {
                let slave_id = r.u32("owner_id")?;
                let Some(t) = self.slave_templates.get_mut(&slave_id) else {
                    return Ok(());
                };
                let Some(attribute) = UnitAttribute::from_u8(r.u8("unit_attribute_id")?) else {
                    return Ok(());
                };
                t.bonuses.push(BonusTemplate {
                    attribute,
                    modifier_type: UnitModifierType::from_u8(r.u8("unit_modifier_type_id")?),
                    value: r.i32("value")?,
                    linear_level_bonus: r.i32("linear_level_bonus")?,
                });
                Ok(())
            },
        )?;
        for_each_row(ctx.conn, "SELECT * FROM slave_initial_items", [], |r| {
            let item = SlaveInitialItem {
                pack_id: r.u32("slave_initial_item_pack_id")?,
                equip_slot_id: r.u8("equip_slot_id")?,
                item_id: r.u32("item_id")?,
            };
            self.slave_initial_items
                .entry(item.pack_id)
                .or_default()
                .push(item);
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM slave_initial_buffs", [], |r| {
            let slave_id = r.u32("slave_id")?;
            let buff_id = r.u32("buff_id")?;
            if let Some(t) = self.slave_templates.get_mut(&slave_id) {
                t.initial_buffs.push(buff_id);
            }
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM slave_passive_buffs", [], |r| {
            let owner = r.u32("owner_id")?;
            let passive = r.u32("passive_buff_id")?;
            if let Some(t) = self.slave_templates.get_mut(&owner) {
                t.passive_buffs.push(passive);
            }
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM slave_doodad_bindings", [], |r| {
            let b = SlaveDoodadBinding {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                owner_type: r.string("owner_type")?,
                attach_point_id: r.i32("attach_point_id")? as u8,
                doodad_id: r.u32("doodad_id")?,
                persist: r.bool_str("persist")?,
                scale: r.f32("scale")?,
            };
            if let Some(t) = self.slave_templates.get_mut(&b.owner_id) {
                t.doodad_bindings.push(b);
            }
            Ok(())
        })?;
        for_each_row(
            ctx.conn,
            "SELECT * FROM slave_healing_point_doodads",
            [],
            |r| {
                let b = SlaveDoodadBinding {
                    id: r.u32("id")?,
                    owner_id: r.u32("owner_id")?,
                    owner_type: r.string("owner_type")?,
                    attach_point_id: r.i32("attach_point_id")? as u8,
                    doodad_id: r.u32("doodad_id")?,
                    persist: false,
                    scale: 1.0,
                };
                if let Some(t) = self.slave_templates.get_mut(&b.owner_id) {
                    t.healing_point_doodads.push(b);
                }
                Ok(())
            },
        )?;
        for_each_row(ctx.conn, "SELECT * FROM slave_bindings", [], |r| {
            let b = SlaveBinding {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                owner_type: r.string("owner_type")?,
                attach_point_id: r.u32("attach_point_id")? as u8,
                slave_id: r.u32("slave_id")?,
            };
            if let Some(t) = self.slave_templates.get_mut(&b.owner_id) {
                t.slave_bindings.push(b);
            }
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM slave_drop_doodads", [], |r| {
            let d = SlaveDropDoodad {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                owner_type: r.string("owner_type")?,
                doodad_id: r.u32("doodad_id")?,
                count: r.u32("count")?,
                radius: r.f32("radius")?,
                on_water: r.bool_str("on_water")?,
            };
            if d.owner_type != "Slave" {
                tracing::warn!("Non slave-owned drops defined in slave_drop_doodads table");
                return Ok(());
            }
            if let Some(t) = self.slave_templates.get_mut(&d.owner_id) {
                t.slave_drop_doodads.push(d);
            }
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM slave_mount_skills", [], |r| {
            let id = r.u32("id")?;
            let slave = r.u32("slave_id")?;
            let skill = r.u32("mount_skill_id")?;
            if self.slave_mount_skills.iter().any(|(i, _, _)| *i == id) {
                tracing::warn!("Duplicate entry for slave_mount_skills");
            } else {
                self.slave_mount_skills.push((id, slave, skill));
            }
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM repairable_slaves", [], |r| {
            let slave = r.u32("slave_id")?;
            let effect = r.u32("repair_slave_effect_id")?;
            match self.repairable_slaves.entry(slave) {
                std::collections::hash_map::Entry::Occupied(_) => {
                    tracing::warn!("Duplicate entry for repairable_slaves");
                }
                std::collections::hash_map::Entry::Vacant(v) => {
                    v.insert(effect);
                }
            }
            Ok(())
        })?;
        Ok(())
    }

    /// `LoadSlaveAttachmentPointLocations` (`Data/slave_attach_points*.json`).
    pub fn load_slave_attachment_point_locations(&mut self, data_folder: &std::path::Path) {
        tracing::info!("Loading Slave Model Attach Points...");
        let mut files: Vec<std::path::PathBuf> = match std::fs::read_dir(data_folder) {
            Ok(rd) => rd
                .filter_map(|e| e.ok().map(|e| e.path()))
                .filter(|p| {
                    p.file_name().and_then(|n| n.to_str()).is_some_and(|n| {
                        let l = n.to_ascii_lowercase();
                        l.starts_with("slave_attach_points") && l.ends_with(".json")
                    })
                })
                .collect(),
            Err(e) => {
                tracing::error!("Error retrieving slave attachment point files: {e}");
                return;
            }
        };
        files.sort();
        let mut all: Vec<ModelAttachPoints> = Vec::new();
        for path in files {
            let contents = std::fs::read(&path)
                .map(|b| String::from_utf8_lossy(&b).into_owned())
                .unwrap_or_default();
            if contents.trim().is_empty() {
                tracing::warn!("File {} is empty.", path.display());
                continue;
            }
            match parse_attach_points(&contents) {
                Ok(sets) => all.extend(sets),
                Err(e) => tracing::error!("Error parsing {}: {e}", path.display()),
            }
        }
        let count = all.len();
        tracing::info!(
            "{count} slave model attach point{} loaded...",
            if count == 1 { "" } else { "s" }
        );
        self.attach_points.clear();
        for (model, points) in all {
            self.attach_points.insert(model, points);
        }
    }

    fn load_transfers(&mut self, ctx: &DataLoadCtx<'_>) -> Result<(), GameDataError> {
        let names = load_localized(ctx, "transfers", "comment")?;
        let mut order = Vec::new();
        for_each_row(ctx.conn, "SELECT * FROM transfers", [], |r| {
            let id = r.u32("id")?;
            order.push(TransferTemplate {
                id,
                name: localized(&names, id, r.string("comment")?),
                model_id: r.u32("model_id")?,
                wait_time: f64::from(r.f32("wait_time")?),
                cyclic: r.bool_str("cyclic")?,
                path_smoothing: r.f32("path_smoothing")?,
                ..TransferTemplate::default()
            });
            Ok(())
        })?;
        for t in order {
            let id = t.id;
            if self.transfer_templates.insert(id, t).is_some() {
                return Err(dup("transfers", id));
            }
        }
        for_each_row(ctx.conn, "SELECT * FROM transfer_bindings", [], |r| {
            let b = TransferBinding {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                owner_type: r.string("owner_type")?,
                attach_point_id: r.i16("attach_point_id")? as u8,
                transfer_id: r.u32("transfer_id")?,
            };
            if let Some(t) = self.transfer_templates.get_mut(&b.owner_id) {
                t.transfer_bindings.push(b);
            }
            Ok(())
        })?;
        for_each_row(
            ctx.conn,
            "SELECT * FROM transfer_binding_doodads",
            [],
            |r| {
                let b = TransferBindingDoodad {
                    id: r.u32("id")?,
                    owner_id: r.u32("owner_id")?,
                    owner_type: r.string("owner_type")?,
                    attach_point_id: r.i32("attach_point_id")? as u8,
                    doodad_id: r.u32("doodad_id")?,
                };
                if let Some(t) = self.transfer_templates.get_mut(&b.owner_id) {
                    t.transfer_binding_doodads.push(b);
                }
                Ok(())
            },
        )?;
        for_each_row(ctx.conn, "SELECT * FROM transfer_paths", [], |r| {
            let p = TransferPath {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                owner_type: r.string("owner_type")?,
                path_name: r.string("path_name")?,
                wait_time_start: r.f64("wait_time_start")?,
                wait_time_end: r.f64("wait_time_end")?,
            };
            if let Some(t) = self.transfer_templates.get_mut(&p.owner_id) {
                t.transfer_all_paths.push(p);
            }
            Ok(())
        })?;

        // TransferPathXml (the client's transfer_path.xml of every world)
        tracing::info!("Loading transfer_path...");
        match WorldTemplates::load(ctx.files, ctx.app_path) {
            Ok(worlds) => self.load_transfer_roads(ctx.files, &worlds),
            Err(e) => tracing::warn!("transfer_path: no world templates ({e})"),
        }
        Ok(())
    }

    /// The `TransferPathXml` region of `TransferGameData.Load` + `GetOwnerPaths` per world.
    pub fn load_transfer_roads(&mut self, files: &ClientFileManager, worlds: &WorldTemplates) {
        for world in worlds.templates() {
            let dir = format!("game/worlds/{}/level_design/zone", world.name);
            let path_files = files.files_in_directory(&dir, "transfer_path.xml", true);
            let mut transfer_paths: Vec<(u32, Vec<TransferRoad>)> = Vec::new();
            for path in path_files {
                let Some(zone_id) = zone_id_from_path(&path) else {
                    tracing::warn!("Unable to parse zoneId from {path}");
                    continue;
                };
                let contents = files.read_to_string(&path);
                if contents.trim().is_empty() {
                    tracing::warn!("{path} doesn't exists or is empty.");
                    continue;
                }
                tracing::debug!("Loading {path}");
                let origin = crate::systems::npcs::subzone::zone_origin_cell(worlds, zone_id);
                match parse_transfer_path_xml(&contents, zone_id, world.id, origin) {
                    Ok(roads) => {
                        // `transferPaths.Add(zoneId, ...)` (a duplicate zone throws in C#).
                        if transfer_paths.iter().any(|(z, _)| *z == zone_id) {
                            tracing::error!("{path}: duplicate zone {zone_id}");
                            continue;
                        }
                        transfer_paths.push((zone_id, roads));
                    }
                    Err(e) => tracing::error!("{path}: {e}"),
                }
            }
            self.get_owner_paths(&transfer_paths);
        }
    }

    /// `GetOwnerPaths(worldTemplateId)`: copies the roads named by each template's paths.
    pub fn get_owner_paths(&mut self, transfers: &[(u32, Vec<TransferRoad>)]) {
        let mut ids: Vec<u32> = self.transfer_templates.keys().copied().collect();
        ids.sort_unstable();
        for id in ids {
            let Some(t) = self.transfer_templates.get_mut(&id) else {
                continue;
            };
            let names: Vec<String> = t
                .transfer_all_paths
                .iter()
                .map(|p| p.path_name.clone())
                .collect();
            for path_name in names {
                for (_, roads) in transfers {
                    for road in roads.iter().filter(|r| r.name == path_name) {
                        if t.transfer_roads.iter().any(|tr| tr.name == path_name) {
                            continue;
                        }
                        t.transfer_roads.push(TransferRoad {
                            name: road.name.clone(),
                            zone_id: 0,
                            kind: road.kind,
                            cell_x: road.cell_x,
                            cell_y: road.cell_y,
                            pos: road.pos.clone(),
                        });
                    }
                }
            }
        }
    }

    fn load_shipyards(&mut self, ctx: &DataLoadCtx<'_>) -> Result<(), GameDataError> {
        tracing::info!("Loading Shipyards...");
        let mut order = Vec::new();
        for_each_row(ctx.conn, "SELECT * FROM shipyards", [], |r| {
            order.push(ShipyardTemplate {
                id: r.u32("id")?,
                name: r.string("name")?,
                main_model_id: r.u32("main_model_id")?,
                item_id: r.u32("item_id")?,
                ceremony_anim_time: r.i32("ceremony_anim_time")?,
                spawn_offset_front: r.f32("spawn_offset_front")?,
                spawn_offset_z: r.f32("spawn_offset_z")?,
                build_radius: r.i32("build_radius")?,
                tax_duration: r.i32_or("tax_duration", 0)?,
                origin_item_id: r.u32_or("origin_item_id", 0)?,
                taxation_id: r.i32("taxation_id")?,
                steps: Vec::new(),
            });
            Ok(())
        })?;
        for t in order {
            let id = t.id;
            if self.shipyard_templates.insert(id, t).is_some() {
                return Err(dup("shipyards", id));
            }
        }
        tracing::info!("Loaded {} shipyards", self.shipyard_templates.len());
        let mut err = None;
        for_each_row(ctx.conn, "SELECT * FROM shipyard_steps", [], |r| {
            let s = ShipyardStep {
                id: r.u32("id")?,
                shipyard_id: r.u32("shipyard_id")?,
                step: r.i32("step")?,
                model_id: r.u32("model_id")?,
                skill_id: r.u32("skill_id")?,
                num_actions: r.i32("num_actions")?,
                max_hp: r.i32("max_hp")?,
            };
            if let Some(t) = self.shipyard_templates.get_mut(&s.shipyard_id) {
                if t.steps.iter().any(|(k, _)| *k == s.step) {
                    err.get_or_insert(dup("shipyard_steps", s.step));
                } else {
                    t.steps.push((s.step, s));
                }
            }
            Ok(())
        })?;
        if let Some(e) = err {
            return Err(e);
        }
        if ctx.conn.prepare("SELECT * FROM taxations LIMIT 0").is_ok() {
            for_each_row(ctx.conn, "SELECT * FROM taxations", [], |r| {
                self.taxations.insert(r.u32("id")?, r.u32("tax")?);
                Ok(())
            })?;
        }
        Ok(())
    }

    fn load_models(&mut self, ctx: &DataLoadCtx<'_>) -> Result<(), GameDataError> {
        for_each_row(ctx.conn, "SELECT * FROM vehicle_models", [], |r| {
            let m = VehicleModel {
                id: r.u32("id")?,
                velocity: r.f32("velocity")?,
                ang_vel: r.f32("angVel")?,
                wheeled_vehicle_mass: r.f32("wheeled_vehicle_mass")?,
                wheeled_vehicle_max_gear: r.u32("wheeled_vehicle_max_gear")?,
                wheeled_vehicle_gear_speed_ratio_reverse: r
                    .f32("wheeled_vehicle_gear_speed_ratio_reverse")?,
                wheeled_vehicle_gear_speed_ratio_1: r.f32("wheeled_vehicle_gear_speed_ratio_1")?,
                wheeled_vehicle_gear_speed_ratio_2: r.f32("wheeled_vehicle_gear_speed_ratio_2")?,
                wheeled_vehicle_gear_speed_ratio_3: r.f32("wheeled_vehicle_gear_speed_ratio_3")?,
            };
            self.vehicle_models.entry(m.id).or_insert(m);
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM ship_models", [], |r| {
            let m = ShipModel {
                id: r.u32("id")?,
                velocity: r.f32("velocity")?,
                mass: r.f32("mass")?,
                mass_center_x: r.f32("mass_center_x")?,
                mass_center_y: r.f32("mass_center_y")?,
                mass_center_z: r.f32("mass_center_z")?,
                mass_box_size_x: r.f32("mass_box_size_x")?,
                mass_box_size_y: r.f32("mass_box_size_y")?,
                mass_box_size_z: r.f32("mass_box_size_z")?,
                keel_length: r.f32("keel_length")?,
                keel_height: r.f32("keel_height")?,
                keel_offset_z: r.f32("keel_offset_z")?,
            };
            self.ship_models.entry(m.id).or_insert(m);
            Ok(())
        })?;
        for_each_row(ctx.conn, "SELECT * FROM models", [], |r| {
            let id = r.u32("id")?;
            let sub_id = r.u32("sub_id")?;
            let sub_type = r.string("sub_type")?;
            self.model_types.entry(id).or_insert((sub_id, sub_type));
            Ok(())
        })?;
        Ok(())
    }

    // ----- MateGameData -----

    /// `GetMateSkills(npcId)`: distinct mount skill ids in table order.
    pub fn mate_skills(&self, npc_id: u32) -> Vec<u32> {
        let mut out = Vec::new();
        for v in &self.npc_mount_skills {
            if v.npc_id == npc_id && !out.contains(&v.mount_skill_id) {
                out.push(v.mount_skill_id);
            }
        }
        out
    }

    /// `GetMountAttachedSkills(mateSkill, attachPoint)`.
    pub fn mount_attached_skill(&self, mate_skill: u32, attach_point: u8) -> u32 {
        let id = self
            .mount_skills
            .iter()
            .find(|m| m.skill_id == mate_skill)
            .map_or(0, |m| m.id);
        self.mount_attached_skills
            .iter()
            .find(|m| m.mount_skill_id == id && m.attach_point_id == attach_point)
            .map_or(0, |m| m.skill_id)
    }

    /// `GetMountSkillIdForSkill(slaveSkillId)`.
    pub fn mount_skill_id_for_skill(&self, slave_skill_id: u32) -> u32 {
        self.mount_skills
            .iter()
            .find(|m| m.skill_id == slave_skill_id)
            .map_or(0, |m| m.id)
    }

    // ----- SlaveGameData -----

    /// `GetSlaveTemplate(id)`.
    pub fn slave_template(&self, id: u32) -> Option<&SlaveTemplate> {
        self.slave_templates.get(&id)
    }

    /// `GetSlaveMountSkillFromId(slaveMountSkillId)`.
    pub fn slave_mount_skill_from_id(&self, id: u32) -> u32 {
        self.slave_mount_skills
            .iter()
            .find(|(i, _, _)| *i == id)
            .map_or(0, |(_, _, s)| *s)
    }

    /// `GetSlaveMountSkillList(slaveTemplateId)`.
    pub fn slave_mount_skill_list(&self, slave_template_id: u32) -> Vec<u32> {
        self.slave_mount_skills
            .iter()
            .filter(|(_, s, _)| *s == slave_template_id)
            .map(|(_, _, m)| *m)
            .collect()
    }

    /// `GetSlaveInitialItemPack(packId)`.
    pub fn slave_initial_item_pack(&self, pack_id: u32) -> Option<&[SlaveInitialItem]> {
        self.slave_initial_items.get(&pack_id).map(Vec::as_slice)
    }

    /// `GetAttachPointsForSlave(modelId)`.
    pub fn attach_points_for_slave(&self, model_id: u32) -> Option<&[(u8, AttachPointLocation)]> {
        self.attach_points.get(&model_id).map(Vec::as_slice)
    }

    /// `HasRepairEffectId(templateId, expectedEffectId)`.
    pub fn has_repair_effect_id(&self, template_id: u32, effect_id: u32) -> bool {
        self.repairable_slaves.get(&template_id) == Some(&effect_id)
    }

    // ----- TransferGameData -----

    /// `GetTransferTemplate(templateId)`.
    pub fn transfer_template(&self, id: u32) -> Option<&TransferTemplate> {
        self.transfer_templates.get(&id)
    }

    // ----- ModelManager -----

    fn model_of(&self, model_id: u32, kind: &str) -> Option<u32> {
        let (sub_id, sub_type) = self.model_types.get(&model_id)?;
        (sub_type == kind).then_some(*sub_id)
    }

    /// `ModelManager.GetVehicleModels(modelId)`.
    pub fn vehicle_model(&self, model_id: u32) -> Option<&VehicleModel> {
        self.vehicle_models
            .get(&self.model_of(model_id, "VehicleModel")?)
    }

    /// `ModelManager.GetShipModel(modelId)`.
    pub fn ship_model(&self, model_id: u32) -> Option<&ShipModel> {
        self.ship_models.get(&self.model_of(model_id, "ShipModel")?)
    }
}

/// The zone id of a `.../zone/<zoneId>/client/transfer_path.xml` path (the name of the
/// directory two levels up).
pub fn zone_id_from_path(path: &str) -> Option<u32> {
    let parts: Vec<&str> = path.split(['/', '\\']).filter(|p| !p.is_empty()).collect();
    if parts.len() < 3 {
        return None;
    }
    parts[parts.len() - 3].parse().ok()
}

/// `XmlHelper.StringToVector3`.
fn string_to_vector3(s: &str) -> [f32; 3] {
    let xyz: Vec<&str> = s.split(',').collect();
    if xyz.len() == 3 {
        let p = |v: &str| v.trim().parse::<f32>().ok();
        if let (Some(x), Some(y), Some(z)) = (p(xyz[0]), p(xyz[1]), p(xyz[2])) {
            return [x, y, z];
        }
    }
    [0.0; 3]
}

/// One `transfer_path.xml`: every element of the root is a road (`Name`, `Type`, `cellX`,
/// `cellY`); the `Pos` attributes of its grandchildren are the points, converted to world
/// coordinates with the zone's origin cell.
pub fn parse_transfer_path_xml(
    contents: &str,
    zone_id: u32,
    world_id: u32,
    origin: (i32, i32),
) -> Result<Vec<TransferRoad>, String> {
    let doc = roxmltree::Document::parse(contents.trim_start_matches('\u{feff}'))
        .map_err(|e| e.to_string())?;
    let mut out = Vec::new();
    let attr_i32 = |n: roxmltree::Node, name: &str| -> i32 {
        n.attribute(name)
            .and_then(|v| v.trim().parse::<i32>().ok())
            .unwrap_or(0)
    };
    for x_node in doc.root_element().children().filter(|n| n.is_element()) {
        let mut road = TransferRoad {
            zone_id,
            name: x_node.attribute("Name").unwrap_or("").to_string(),
            kind: attr_i32(x_node, "Type"),
            cell_x: attr_i32(x_node, "cellX"),
            cell_y: attr_i32(x_node, "cellY"),
            pos: Vec::new(),
        };
        for child in x_node.children().filter(|n| n.is_element()) {
            for node in child.children().filter(|n| n.is_element()) {
                if let Some(v) = node.attribute("Pos") {
                    let xyz = string_to_vector3(v);
                    road.pos.push(WorldSpawnPosition {
                        world_id,
                        zone_id,
                        x: origin.0 as f32 * 1024.0 + xyz[0],
                        y: origin.1 as f32 * 1024.0 + xyz[1],
                        z: xyz[2],
                        ..WorldSpawnPosition::default()
                    });
                }
            }
        }
        out.push(road);
    }
    Ok(out)
}

/// `List<SlaveModelAttachPoint>` of one `slave_attach_points*.json` (rotations in radians).
pub fn parse_attach_points(text: &str) -> Result<Vec<ModelAttachPoints>, String> {
    use crate::systems::npcs::json;
    let v = json::parse(text).map_err(|e| e.to_string())?;
    let mut out = Vec::new();
    for entry in json::array(&v)? {
        let Value::Object(o) = entry else {
            return Err(format!("expected an object, got {entry}"));
        };
        let model_id = json::u32_or(o, "ModelId", 0)?;
        let mut points = Vec::new();
        match json::prop(o, "AttachPoints") {
            Some(Value::Object(m)) => {
                for (k, p) in m {
                    let ap = parse_attach_point_key(k)
                        .ok_or_else(|| format!("unknown attach point {k}"))?;
                    let Value::Object(po) = p else {
                        return Err(format!("attach point {k}: expected an object"));
                    };
                    let mut pos = crate::systems::npcs::doodad::spawner::parse_position(po)?;
                    pos.roll = pos.roll.to_radians_cs();
                    pos.pitch = pos.pitch.to_radians_cs();
                    pos.yaw = pos.yaw.to_radians_cs();
                    match points.iter_mut().find(|(a, _)| *a == ap) {
                        Some((_, old)) => *old = pos,
                        None => points.push((ap, pos)),
                    }
                }
            }
            None | Some(Value::Null) => {}
            Some(other) => return Err(format!("AttachPoints: expected an object, got {other}")),
        }
        out.push((model_id, points));
    }
    Ok(out)
}

/// `NumericExtensions.DegToRad(float)` (`val * MathF.PI / 180f`).
trait DegToRad {
    fn to_radians_cs(self) -> f32;
}

impl DegToRad for f32 {
    fn to_radians_cs(self) -> f32 {
        self * std::f32::consts::PI / 180.0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn attach_point_keys_accept_numbers_and_names() {
        assert_eq!(parse_attach_point_key("1"), Some(1));
        assert_eq!(parse_attach_point_key("Driver"), Some(1));
        assert_eq!(parse_attach_point_key("healpoint9"), Some(45));
        assert_eq!(parse_attach_point_key("System"), Some(255));
        assert_eq!(parse_attach_point_key("nope"), None);
    }

    #[test]
    fn attach_points_json_converts_degrees() {
        let text = r#"[ { "Name": "Clipper", "ModelId": 128, // comment
            "AttachPoints": { "1": { "X": 0.0, "Y": -2.9, "Z": 1.04 },
                              "59": { "X": 2.1, "Y": -6.4, "Z": -1.5, "Yaw": 180 } } } ]"#;
        let sets = parse_attach_points(text).unwrap();
        assert_eq!(sets.len(), 1);
        assert_eq!(sets[0].0, 128);
        let pts = &sets[0].1;
        assert_eq!(pts[0].0, 1);
        assert_eq!(pts[0].1.world_id, 1);
        assert!((pts[1].1.yaw - std::f32::consts::PI).abs() < 1e-6);
    }

    #[test]
    fn transfer_path_xml_roads() {
        let xml = r#"<?xml version="1.0"?>
<Objects>
  <Object Name="road_a" Type="6" cellX="9" cellY="14">
    <Points>
      <Point Pos="10.5,20,130"/>
      <Point Pos="11,21,131"/>
      <Point Pos="bad"/>
    </Points>
  </Object>
  <Object Name="road_b"/>
</Objects>"#;
        let roads = parse_transfer_path_xml(xml, 129, 0, (9, 14)).unwrap();
        assert_eq!(roads.len(), 2);
        assert_eq!(roads[0].name, "road_a");
        assert_eq!(roads[0].kind, 6);
        assert_eq!(roads[0].cell_x, 9);
        assert_eq!(roads[0].pos.len(), 3);
        assert_eq!(roads[0].pos[0].x, 9.0 * 1024.0 + 10.5);
        assert_eq!(roads[0].pos[0].y, 14.0 * 1024.0 + 20.0);
        assert_eq!(roads[0].pos[0].z, 130.0);
        // An unparsable vector is Vector3.Zero, converted like any other point.
        assert_eq!(roads[0].pos[2].x, 9.0 * 1024.0);
        assert!(roads[1].pos.is_empty());
        assert_eq!(
            zone_id_from_path(
                "game/worlds/main_world/level_design/zone/129/client/transfer_path.xml"
            ),
            Some(129)
        );
    }

    #[test]
    fn owner_paths_copy_named_roads_once() {
        let mut d = VehiclesData::default();
        d.transfer_templates.insert(
            6,
            TransferTemplate {
                id: 6,
                transfer_all_paths: vec![
                    TransferPath {
                        id: 1,
                        owner_id: 6,
                        owner_type: "Transfer".into(),
                        path_name: "r1".into(),
                        wait_time_start: 0.0,
                        wait_time_end: 0.0,
                    },
                    TransferPath {
                        id: 2,
                        owner_id: 6,
                        owner_type: "Transfer".into(),
                        path_name: "r1".into(),
                        wait_time_start: 0.0,
                        wait_time_end: 0.0,
                    },
                ],
                ..TransferTemplate::default()
            },
        );
        let road = TransferRoad {
            name: "r1".into(),
            zone_id: 5,
            pos: vec![WorldSpawnPosition::default(); 2],
            ..TransferRoad::default()
        };
        d.get_owner_paths(&[(5, vec![road.clone()]), (6, vec![road])]);
        let t = d.transfer_template(6).unwrap();
        assert_eq!(t.transfer_roads.len(), 1);
        assert_eq!(t.transfer_roads[0].zone_id, 0);
        assert_eq!(t.transfer_roads[0].pos.len(), 2);
    }
}
