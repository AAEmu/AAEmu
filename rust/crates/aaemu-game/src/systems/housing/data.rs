//! `HousingGameData` (`GameData/HousingGameData.cs`) plus the `TaxationsManager` table it reads
//! (`taxations`) and the `Data/housing_bindings*.json` attach-point positions.
//!
//! * `item_housings`, `housings` (+ `housing_binding_doodads` per template, positions from the
//!   bindings json), `housing_build_steps`, `housing_decorations`, `item_housing_decorations`.
//! * `PostLoad`: `Template.Name = LocalizationManager.Get("housings", "name", id, Name)` and
//!   `Template.Taxation = TaxationsManager.taxations[TaxationId]` (null when missing).
//! * The localized texts `HousingManager` reads at runtime (`housings.name` without fallback for
//!   a new house, `items.name` of design items for the untranslated-name fallback in `Build`).

use std::collections::{BTreeMap, HashMap};
use std::path::Path;

use rusqlite::Connection;
use serde_json::Value;

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;
use crate::systems::DataLoadCtx;

/// `Taxation` (`taxations`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Taxation {
    pub id: u32,
    pub tax: u32,
    pub show: bool,
}

/// `WorldSpawnPosition` as used by the housing bindings (`Yaw`/`Pitch`/`Roll` in degrees there,
/// `ApplyWorldSpawnPositionWithDeg`).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct WorldSpawnPosition {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub yaw: f32,
    pub pitch: f32,
    pub roll: f32,
}

/// `HousingBindingDoodad`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct HousingBindingDoodad {
    /// `AttachPointKind`.
    pub attach_point_id: u8,
    pub doodad_id: u32,
    /// From the bindings json (`new WorldSpawnPosition()` when missing).
    pub position: WorldSpawnPosition,
}

/// `HousingBuildStep`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct HousingBuildStep {
    pub id: u32,
    pub housing_id: u32,
    pub step: i16,
    pub model_id: u32,
    pub skill_id: u32,
    pub num_actions: i32,
}

/// `HousingTemplate`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct HousingTemplate {
    pub id: u32,
    /// Localized in `PostLoad`.
    pub name: String,
    pub category_id: u32,
    pub main_model_id: u32,
    pub door_model_id: u32,
    pub stair_model_id: u32,
    pub auto_z: bool,
    pub gate_exists: bool,
    pub hp: i32,
    pub repair_cost: u32,
    pub garden_radius: f32,
    pub family: String,
    /// `Taxation` (`null` when `taxation_id` has no row).
    pub taxation: Option<Taxation>,
    pub taxation_id: u32,
    pub guard_tower_setting_id: u32,
    pub cinema_radius: f32,
    pub auto_z_offset_x: f32,
    pub auto_z_offset_y: f32,
    pub auto_z_offset_z: f32,
    pub alley: f32,
    pub extra_height_above: f32,
    pub extra_height_below: f32,
    pub deco_limit: u32,
    pub absolute_deco_limit: u32,
    pub housing_deco_limit_id: u32,
    pub is_sellable: bool,
    pub heavy_tax: bool,
    pub always_public: bool,
    /// `BuildSteps` (keyed by `Step`).
    pub build_steps: BTreeMap<i32, HousingBuildStep>,
    /// `HousingBindingDoodad` (row order).
    pub binding_doodads: Vec<HousingBindingDoodad>,
}

impl HousingTemplate {
    /// `BuildSteps.Values.Sum(step => step.NumActions)` (the `Template` setter's `AllAction`).
    pub fn all_actions(&self) -> i32 {
        self.build_steps.values().map(|s| s.num_actions).sum()
    }

    /// `Taxation?.Tax ?? 0`.
    pub fn tax_or_zero(&self) -> u32 {
        self.taxation.map_or(0, |t| t.tax)
    }
}

/// `HousingDecoration`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct HousingDecoration {
    pub id: u32,
    pub name: String,
    pub allow_on_floor: bool,
    pub allow_on_wall: bool,
    pub allow_on_ceiling: bool,
    pub doodad_id: u32,
    pub allow_pivot_on_garden: bool,
    pub actability_group_id: u32,
    pub actability_up: u32,
    pub deco_act_ability_group_id: u32,
    pub allow_mesh_on_garden: bool,
}

/// `ItemHousingDecoration`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ItemHousingDecoration {
    pub id: u32,
    pub item_id: u32,
    pub design_id: u32,
    pub restore: bool,
}

/// `HousingItemHousings`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct HousingItemHousings {
    pub id: u32,
    pub item_id: u32,
    pub design_id: u32,
}

/// `HousingBindingTemplate` (one entry of a `housing_bindings*.json` file).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct HousingBindingTemplate {
    pub template_ids: Vec<u32>,
    pub attach_points: HashMap<u8, WorldSpawnPosition>,
}

/// The housing tables (`server.game_data.systems.housing`).
#[derive(Debug, Clone, Default)]
pub struct HousingData {
    templates: HashMap<u32, HousingTemplate>,
    /// `_housingDecorations` (row order kept for `FirstOrDefault` by doodad id).
    decorations: Vec<HousingDecoration>,
    decoration_index: HashMap<u32, usize>,
    item_housing_decorations: Vec<ItemHousingDecoration>,
    item_housings: Vec<HousingItemHousings>,
    /// `TaxationsManager.taxations`.
    pub taxations: HashMap<u32, Taxation>,
    /// `localized_texts` of `housings.name` (raw translations, may be empty).
    house_names: HashMap<u32, String>,
    /// `localized_texts` of `items.name` for the design items of `item_housings`.
    item_names: HashMap<u32, String>,
    /// Number of binding templates loaded from the json files.
    pub binding_count: usize,
}

impl HousingData {
    /// `HousingGameData.Load` + `PostLoad` (+ `TaxationsManager.Load`).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let bindings = load_housing_bindings(&ctx.app_path.join("Data"));
        Self::load_from(ctx.conn, ctx.language, bindings)
    }

    /// The loaders with explicit bindings (tests).
    pub fn load_from(
        conn: &Connection,
        language: &str,
        bindings: Vec<HousingBindingTemplate>,
    ) -> Result<Self, GameDataError> {
        let mut d = HousingData::default();
        tracing::info!("Loading taxations ...");
        for_each_row(conn, "SELECT * FROM taxations", [], |r| {
            let t = Taxation {
                id: r.u32("id")?,
                tax: r.u32("tax")?,
                show: r.bool_str("show")?,
            };
            if d.taxations.insert(t.id, t).is_some() {
                return Err(dup("taxations", t.id));
            }
            Ok(())
        })?;

        tracing::info!("Loading Housing Information ...");
        for_each_row(conn, "SELECT * FROM item_housings", [], |r| {
            d.item_housings.push(HousingItemHousings {
                id: r.u32("id")?,
                item_id: r.u32("item_id")?,
                design_id: r.u32("design_id")?,
            });
            Ok(())
        })?;

        tracing::info!("Loading Housing Templates...");
        d.binding_count = bindings.len();
        if bindings.is_empty() {
            tracing::warn!("Housing bindings not loaded...");
        } else {
            tracing::info!(
                "{} housing binding{} loaded...",
                bindings.len(),
                if bindings.len() == 1 { "" } else { "s" }
            );
        }

        // housing_binding_doodads grouped per owner (the C# runs one query per template).
        let mut binding_rows: HashMap<u32, Vec<(u8, u32)>> = HashMap::new();
        for_each_row(
            conn,
            "SELECT * FROM housing_binding_doodads WHERE owner_type='Housing'",
            [],
            |r| {
                binding_rows
                    .entry(r.u32("owner_id")?)
                    .or_default()
                    .push((r.i16("attach_point_id")? as u8, r.u32("doodad_id")?));
                Ok(())
            },
        )?;

        for_each_row(conn, "SELECT * FROM housings", [], |r| {
            let mut t = HousingTemplate {
                id: r.u32("id")?,
                name: r.string("name")?,
                category_id: r.u32("category_id")?,
                main_model_id: r.u32("main_model_id")?,
                door_model_id: r.u32_or("door_model_id", 0)?,
                stair_model_id: r.u32_or("stair_model_id", 0)?,
                auto_z: r.bool_str("auto_z")?,
                gate_exists: r.bool_str("gate_exists")?,
                hp: r.i32("hp")?,
                repair_cost: r.u32("repair_cost")?,
                garden_radius: r.f32("garden_radius")?,
                family: r.string("family")?,
                taxation: None,
                taxation_id: r.u32("taxation_id")?,
                guard_tower_setting_id: r.u32_or("guard_tower_setting_id", 0)?,
                cinema_radius: r.f32("cinema_radius")?,
                auto_z_offset_x: r.f32("auto_z_offset_x")?,
                auto_z_offset_y: r.f32("auto_z_offset_y")?,
                auto_z_offset_z: r.f32("auto_z_offset_z")?,
                alley: r.f32("alley")?,
                extra_height_above: r.f32("extra_height_above")?,
                extra_height_below: r.f32("extra_height_below")?,
                deco_limit: r.u32("deco_limit")?,
                absolute_deco_limit: r.u32("absolute_deco_limit")?,
                housing_deco_limit_id: r.u32_or("housing_deco_limit_id", 0)?,
                is_sellable: r.bool_str("is_sellable")?,
                heavy_tax: r.bool_str("heavy_tax")?,
                always_public: r.bool_str("always_public")?,
                build_steps: BTreeMap::new(),
                binding_doodads: Vec::new(),
            };
            let template_bindings = bindings.iter().find(|b| b.template_ids.contains(&t.id));
            for &(attach_point_id, doodad_id) in binding_rows.get(&t.id).into_iter().flatten() {
                let position = template_bindings
                    .and_then(|b| b.attach_points.get(&attach_point_id))
                    .copied()
                    .unwrap_or_default();
                t.binding_doodads.push(HousingBindingDoodad {
                    attach_point_id,
                    doodad_id,
                    position,
                });
            }
            let id = t.id;
            if d.templates.insert(id, t).is_some() {
                return Err(dup("housings", id));
            }
            Ok(())
        })?;
        tracing::info!("Loaded Housing Templates {}", d.templates.len());

        for_each_row(conn, "SELECT * FROM housing_build_steps", [], |r| {
            let housing_id = r.u32("housing_id")?;
            let Some(t) = d.templates.get_mut(&housing_id) else {
                return Ok(());
            };
            let step = HousingBuildStep {
                id: r.u32("id")?,
                housing_id,
                step: r.i16("step")?,
                model_id: r.u32("model_id")?,
                skill_id: r.u32("skill_id")?,
                num_actions: r.i32("num_actions")?,
            };
            // `BuildSteps.Add(step.Step, step)` throws on a duplicate step.
            if t.build_steps.insert(i32::from(step.step), step).is_some() {
                return Err(dup("housing_build_steps", step.id));
            }
            Ok(())
        })?;

        tracing::info!("Loaded Decoration Templates...");
        for_each_row(conn, "SELECT * FROM housing_decorations", [], |r| {
            let deco = HousingDecoration {
                id: r.u32("id")?,
                name: r.string("name")?,
                allow_on_floor: r.bool_str("allow_on_floor")?,
                allow_on_wall: r.bool_str("allow_on_wall")?,
                allow_on_ceiling: r.bool_str("allow_on_ceiling")?,
                doodad_id: r.u32("doodad_id")?,
                allow_pivot_on_garden: r.bool_str("allow_pivot_on_garden")?,
                actability_group_id: r.u32_or("actability_group_id", 0)?,
                actability_up: r.u32_or("actability_up", 0)?,
                deco_act_ability_group_id: r.u32_or("deco_actability_group_id", 0)?,
                allow_mesh_on_garden: r.bool_str("allow_mesh_on_garden")?,
            };
            d.add_decoration(deco)
                .map_err(|id| dup("housing_decorations", id))
        })?;

        for_each_row(conn, "SELECT * FROM item_housing_decorations", [], |r| {
            d.item_housing_decorations.push(ItemHousingDecoration {
                id: r.u32("id")?,
                item_id: r.u32("item_id")?,
                design_id: r.u32("design_id")?,
                restore: r.bool_str("restore")?,
            });
            Ok(())
        })?;

        // PostLoad: localized names and taxations.
        d.load_localized(conn, language);
        let taxations = d.taxations.clone();
        let names = d.house_names.clone();
        for t in d.templates.values_mut() {
            if let Some(n) = names.get(&t.id).filter(|n| !n.is_empty()) {
                t.name = n.clone();
            }
            t.taxation = taxations.get(&t.taxation_id).copied();
        }
        Ok(d)
    }

    /// `localized_texts` rows for `housings.name` and the design items' `items.name` (a
    /// missing language column only loses the translations, as an empty `LocalizationManager`).
    fn load_localized(&mut self, conn: &Connection, language: &str) {
        let col = language.replace('"', "");
        let sql = format!(
            "SELECT tbl_name, idx, \"{col}\" AS txt FROM localized_texts \
             WHERE tbl_column_name = 'name' AND tbl_name IN ('housings', 'items')"
        );
        let design_items: std::collections::HashSet<u32> =
            self.item_housings.iter().map(|h| h.item_id).collect();
        let res = for_each_row(conn, &sql, [], |r| {
            let idx = r.u32("idx")?;
            let text = r.string_or("txt", "")?;
            match r.string("tbl_name")?.as_str() {
                "housings" => {
                    self.house_names.insert(idx, text);
                }
                _ if design_items.contains(&idx) => {
                    self.item_names.insert(idx, text);
                }
                _ => {}
            }
            Ok(())
        });
        if let Err(e) = res {
            tracing::warn!("housing localized names not loaded: {e}");
        }
    }

    /// Adds a template (tests / synthetic data).
    pub fn add_template(&mut self, t: HousingTemplate) {
        self.templates.insert(t.id, t);
    }

    /// Adds a decoration (row order kept). `Err(id)` on a duplicate id.
    pub fn add_decoration(&mut self, deco: HousingDecoration) -> Result<(), u32> {
        if self.decoration_index.contains_key(&deco.id) {
            return Err(deco.id);
        }
        self.decoration_index
            .insert(deco.id, self.decorations.len());
        self.decorations.push(deco);
        Ok(())
    }

    /// Adds an `item_housing_decorations` row (tests).
    pub fn add_item_housing_decoration(&mut self, row: ItemHousingDecoration) {
        self.item_housing_decorations.push(row);
    }

    /// Adds an `item_housings` row (tests).
    pub fn add_item_housing(&mut self, row: HousingItemHousings) {
        self.item_housings.push(row);
    }

    /// Sets a `housings.name` translation (tests).
    pub fn set_house_name(&mut self, id: u32, name: &str) {
        self.house_names.insert(id, name.to_string());
    }

    /// `GetTemplate(designId)`.
    pub fn template(&self, design_id: u32) -> Option<&HousingTemplate> {
        self.templates.get(&design_id)
    }

    pub fn template_count(&self) -> usize {
        self.templates.len()
    }

    pub fn templates(&self) -> impl Iterator<Item = &HousingTemplate> {
        self.templates.values()
    }

    pub fn decoration_count(&self) -> usize {
        self.decorations.len()
    }

    pub fn item_housing_decoration_count(&self) -> usize {
        self.item_housing_decorations.len()
    }

    pub fn item_housing_count(&self) -> usize {
        self.item_housings.len()
    }

    /// `GetItemHousingDecorations(decoDesignId)` (first row with that design).
    pub fn item_housing_decoration(&self, deco_design_id: u32) -> Option<&ItemHousingDecoration> {
        self.item_housing_decorations
            .iter()
            .find(|x| x.design_id == deco_design_id)
    }

    /// Whether this item template supplies the requested decoration (several items may
    /// share a design).
    pub fn decoration_accepts_item(&self, design_id: u32, item_id: u32) -> bool {
        self.item_housing_decorations
            .iter()
            .any(|row| row.design_id == design_id && row.item_id == item_id)
    }

    /// `GetItemIdByDesign(designId)`: the first `item_housings.item_id` of the design whose item
    /// template exists (0 = none).
    pub fn item_id_by_design(&self, design_id: u32, item_exists: impl Fn(u32) -> bool) -> u32 {
        self.item_housings
            .iter()
            .filter(|h| h.design_id == design_id)
            .find(|h| item_exists(h.item_id))
            .map_or(0, |h| h.item_id)
    }

    /// `GetDecorationDesignFromId(designId)`.
    pub fn decoration(&self, design_id: u32) -> Option<&HousingDecoration> {
        self.decoration_index
            .get(&design_id)
            .map(|&i| &self.decorations[i])
    }

    /// `GetDecorationDesignFromDoodadId(doodadId)` (first in row order).
    pub fn decoration_by_doodad(&self, doodad_id: u32) -> Option<&HousingDecoration> {
        self.decorations.iter().find(|d| d.doodad_id == doodad_id)
    }

    /// `LocalizationManager.Get("housings", "name", id)` (fallback `""`).
    pub fn localized_house_name(&self, id: u32) -> String {
        self.house_names.get(&id).cloned().unwrap_or_default()
    }

    /// `LocalizationManager.Get("items", "name", id, fallback)` for design items.
    pub fn localized_item_name(&self, id: u32, fallback: &str) -> String {
        match self.item_names.get(&id) {
            Some(t) if !t.is_empty() => t.clone(),
            _ => fallback.to_string(),
        }
    }
}

fn dup(table: &str, id: u32) -> rusqlite::Error {
    rusqlite::Error::InvalidParameterName(format!(
        "{table}: duplicate key {id} (ArgumentException in C#)"
    ))
}

/// `LoadHousingBindings(dataFolder)`: every `housing_bindings*.json` (sorted by name; the C#
/// `Directory.GetFiles` order is unspecified). Unreadable / invalid files are logged and skipped.
pub fn load_housing_bindings(data_folder: &Path) -> Vec<HousingBindingTemplate> {
    let mut out = Vec::new();
    let entries = match std::fs::read_dir(data_folder) {
        Ok(e) => e,
        Err(e) => {
            tracing::error!("Error retrieving housing binding files: {e}");
            return out;
        }
    };
    let mut files: Vec<_> = entries
        .filter_map(Result::ok)
        .map(|e| e.path())
        .filter(|p| {
            p.file_name().and_then(|n| n.to_str()).is_some_and(|n| {
                let l = n.to_ascii_lowercase();
                l.starts_with("housing_bindings") && l.ends_with(".json")
            })
        })
        .collect();
    files.sort();
    for path in files {
        let Ok(contents) = std::fs::read_to_string(&path) else {
            tracing::info!("Missing file: {}", path.display());
            continue;
        };
        if contents.trim().is_empty() {
            tracing::warn!("File {} is empty.", path.display());
            continue;
        }
        match parse_housing_bindings(&contents) {
            Ok(t) => out.extend(t),
            Err(e) => tracing::error!("Error parsing {}: {e}", path.display()),
        }
    }
    out
}

/// Parses one bindings file (`List<HousingBindingTemplate>`; attach point keys are the numeric
/// `AttachPointKind` values or their names).
pub fn parse_housing_bindings(text: &str) -> Result<Vec<HousingBindingTemplate>, String> {
    use crate::systems::npcs::json;
    let v = json::parse(text).map_err(|e| e.to_string())?;
    let Value::Array(items) = v else {
        return Err("expected an array".into());
    };
    let mut out = Vec::new();
    for item in &items {
        let Value::Object(obj) = item else {
            return Err("expected an object".into());
        };
        let template_ids = json::u32_list(obj, "TemplateId")?;
        let mut attach_points = HashMap::new();
        if let Some(Value::Object(points)) = json::prop(obj, "AttachPointId") {
            for (k, p) in points {
                let key = attach_point_from_key(k)?;
                let Value::Object(p) = p else {
                    return Err(format!("attach point {k}: expected an object"));
                };
                let pos = WorldSpawnPosition {
                    x: json::f32_or(p, "X", 0.0)?,
                    y: json::f32_or(p, "Y", 0.0)?,
                    z: json::f32_or(p, "Z", 0.0)?,
                    yaw: json::f32_or(p, "Yaw", 0.0)?,
                    pitch: json::f32_or(p, "Pitch", 0.0)?,
                    roll: json::f32_or(p, "Roll", 0.0)?,
                };
                // Newtonsoft: a repeated key overwrites.
                attach_points.insert(key, pos);
            }
        }
        out.push(HousingBindingTemplate {
            template_ids,
            attach_points,
        });
    }
    Ok(out)
}

/// `AttachPointKind` dictionary key: a number, or one of the names the housing data uses.
fn attach_point_from_key(k: &str) -> Result<u8, String> {
    if let Ok(v) = k.trim().parse::<i64>() {
        return Ok(v as u8);
    }
    ATTACH_POINT_NAMES
        .iter()
        .find(|(n, _)| n.eq_ignore_ascii_case(k.trim()))
        .map(|(_, v)| *v)
        .ok_or_else(|| format!("unknown AttachPointKind '{k}'"))
}

/// `AttachPointKind` (every named value, `Enum.Parse` for dictionary keys).
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

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    /// The columns the loaders read.
    pub(crate) const TEST_SCHEMA: &str = "
        CREATE TABLE taxations (id INT, tax INT, desc TEXT, SHOW NUM);
        CREATE TABLE item_housings (id INT, item_id INT, design_id INT);
        CREATE TABLE housings (id INT, name TEXT, category_id INT, main_model_id INT,
            door_model_id INT, stair_model_id INT, auto_z NUM, gate_exists NUM, hp INT,
            repair_cost INT, garden_radius REAL, family TEXT, taxation_id INT,
            guard_tower_setting_id INT, cinema_id INT, cinema_radius REAL, auto_z_offset_x REAL,
            auto_z_offset_y REAL, auto_z_offset_z REAL, alley REAL, extra_height_above REAL,
            extra_height_below REAL, deco_limit INT, comments TEXT, absolute_deco_limit INT,
            housing_deco_limit_id INT, is_sellable NUM, heavy_tax NUM, always_public NUM,
            demolish_refund_item_id INT);
        CREATE TABLE housing_binding_doodads (id INT, owner_id INT, owner_type TEXT,
            attach_point_id INT, doodad_id INT);
        CREATE TABLE housing_build_steps (id INT, housing_id INT, step INT, model_id INT,
            skill_id INT, num_actions INT);
        CREATE TABLE housing_decorations (id INT, name TEXT, allow_on_floor NUM, allow_on_wall NUM,
            allow_on_ceiling NUM, doodad_id INT, allow_pivot_on_garden NUM,
            actability_group_id INT, actability_up INT, deco_actability_group_id INT,
            allow_mesh_on_garden NUM);
        CREATE TABLE item_housing_decorations (id INT, item_id INT, design_id INT, RESTORE NUM);
        CREATE TABLE localized_texts (id INT, tbl_name TEXT, tbl_column_name TEXT, idx INT,
            ko TEXT, en_us TEXT);
    ";

    fn sample() -> Connection {
        let c = Connection::open_in_memory().unwrap();
        c.execute_batch(TEST_SCHEMA).unwrap();
        c.execute_batch(
            "INSERT INTO taxations VALUES (1, 0, 'none', 'f'), (2, 100000, 'small', 't');
             INSERT INTO item_housings VALUES (1, 9000, 300), (2, 9001, 300);
             INSERT INTO housings VALUES (300, 'Small House', 1, 700, NULL, NULL, 't', 't', 1600,
                10, 8.0, 'fam', 2, NULL, 0, 0.0, 0, 0, 0, 0, 0, 0, 20, '', 40, NULL, 't', 't', 'f', 0);
             INSERT INTO housings VALUES (301, 'Farm', 1, 701, 5, 6, 'f', 'f', 900,
                0, 0.0, '', 9, 3, 0, 0.0, 0, 0, 0, 0, 0, 0, 0, '', 0, 2, 'f', 'f', 't', 0);
             INSERT INTO housing_binding_doodads VALUES (1, 300, 'Housing', 9, 4001),
                (2, 300, 'Housing', 57, 4002), (3, 301, 'Ship', 9, 4003);
             INSERT INTO housing_build_steps VALUES (10, 300, 0, 710, 11000, 3),
                (11, 300, 1, 711, 11001, 2), (12, 999, 0, 1, 1, 1), (13, 999, 0, 1, 1, 1);
             INSERT INTO housing_decorations VALUES (50, 'Chair', 't', 'f', 'f', 5000, 't', 3, 2, NULL, 'f'),
                (51, 'Chair2', 't', 'f', 'f', 5000, 't', NULL, NULL, NULL, 'f');
             INSERT INTO item_housing_decorations VALUES (1, 8000, 50, 't'), (2, 8001, 50, 'f');
             INSERT INTO localized_texts VALUES (1, 'housings', 'name', 300, '', 'Cottage'),
                (2, 'items', 'name', 9000, '', 'Cottage Design'), (3, 'items', 'name', 1, '', 'x');",
        )
        .unwrap();
        c
    }

    #[test]
    fn loads_tables_bindings_and_post_load() {
        let bindings = parse_housing_bindings(
            r#"[{"TemplateId":[300,302],"AttachPointId":{"9":{"X":1.5,"Y":-2,"Z":0.5,"Yaw":90},
                "NamePlate01":{"Z":3}}}]"#,
        )
        .unwrap();
        let d = HousingData::load_from(&sample(), "en_us", bindings).unwrap();
        assert_eq!(d.template_count(), 2);
        let t = d.template(300).unwrap();
        assert_eq!(t.name, "Cottage"); // PostLoad localization
        assert_eq!(
            t.taxation,
            Some(Taxation {
                id: 2,
                tax: 100000,
                show: true
            })
        );
        assert!(t.auto_z && t.gate_exists && t.is_sellable && t.heavy_tax && !t.always_public);
        assert_eq!((t.door_model_id, t.housing_deco_limit_id), (0, 0));
        assert_eq!(t.build_steps.len(), 2);
        assert_eq!(t.all_actions(), 5);
        assert_eq!(t.binding_doodads.len(), 2);
        assert_eq!(t.binding_doodads[0].attach_point_id, 9);
        assert_eq!(t.binding_doodads[0].position.x, 1.5);
        assert_eq!(t.binding_doodads[0].position.yaw, 90.0);
        assert_eq!(t.binding_doodads[1].position.z, 3.0);
        let t2 = d.template(301).unwrap();
        assert_eq!(t2.name, "Farm"); // no translation
        assert_eq!(t2.taxation, None); // taxation 9 does not exist
        assert!(t2.binding_doodads.is_empty()); // owner_type 'Ship' ignored
        assert!(t2.always_public);
        assert_eq!(d.localized_house_name(300), "Cottage");
        assert_eq!(d.localized_house_name(301), "");
        assert_eq!(d.localized_item_name(9000, "f"), "Cottage Design");
        assert_eq!(d.localized_item_name(9001, "f"), "f");
        assert_eq!(d.decoration_by_doodad(5000).unwrap().id, 50);
        assert_eq!(d.decoration(50).unwrap().actability_up, 2);
        assert_eq!(d.decoration(51).unwrap().actability_group_id, 0);
        assert!(d.item_housing_decoration(50).unwrap().restore);
        assert_eq!(d.item_id_by_design(300, |id| id == 9001), 9001);
        assert_eq!(d.item_id_by_design(300, |_| false), 0);
    }

    #[test]
    fn duplicate_build_step_fails_like_csharp() {
        let c = sample();
        c.execute_batch("INSERT INTO housing_build_steps VALUES (14, 300, 1, 1, 1, 1);")
            .unwrap();
        assert!(HousingData::load_from(&c, "en_us", Vec::new()).is_err());
    }

    #[test]
    fn real_bindings_file_parses() {
        let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("app/Data/housing_bindings.json");
        let Ok(text) = std::fs::read_to_string(&path) else {
            return;
        };
        let b = parse_housing_bindings(&text).unwrap();
        assert_eq!(b.len(), 21);
        assert!(b[0].template_ids.contains(&41));
        assert_eq!(b[0].attach_points[&57].z, 4.0921);
    }

    /// `AAEMU_COMPACT_DB=... cargo test -p aaemu-game housing_real_compact_db -- --ignored`.
    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn housing_real_compact_db() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn =
            Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let data_dir = Path::new(&path).parent().unwrap();
        let bindings = load_housing_bindings(data_dir);
        let d = HousingData::load_from(&conn, "en_us", bindings.clone()).unwrap();
        let rows = |t: &str| -> usize {
            conn.query_row(&format!("SELECT COUNT(*) FROM {t}"), [], |r| {
                r.get::<_, i64>(0)
            })
            .unwrap() as usize
        };
        assert_eq!(d.template_count(), rows("housings"));
        assert_eq!(d.decoration_count(), rows("housing_decorations"));
        assert_eq!(
            d.item_housing_decoration_count(),
            rows("item_housing_decorations")
        );
        assert_eq!(d.item_housing_count(), rows("item_housings"));
        assert_eq!(d.taxations.len(), rows("taxations"));
        let steps: usize = d.templates().map(|t| t.build_steps.len()).sum();
        let bound: usize = d.templates().map(|t| t.binding_doodads.len()).sum();
        let positioned = d
            .templates()
            .flat_map(|t| t.binding_doodads.iter())
            .filter(|b| b.position != WorldSpawnPosition::default())
            .count();
        assert!(steps > 0 && bound > 0);
        assert!(!bindings.is_empty() && positioned > 0);
        // Every template that has a taxation row in the data resolves it (PostLoad).
        assert!(d
            .templates()
            .filter(|t| d.taxations.contains_key(&t.taxation_id))
            .all(|t| t.taxation.is_some()));
        eprintln!(
            "housing templates {} steps {steps} bound doodads {bound} (positioned {positioned}) decorations {} item decorations {} item housings {} taxations {} binding templates {}",
            d.template_count(),
            d.decoration_count(),
            d.item_housing_decoration_count(),
            d.item_housing_count(),
            d.taxations.len(),
            bindings.len()
        );
    }
}
