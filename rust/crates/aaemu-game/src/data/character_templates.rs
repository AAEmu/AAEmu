//! `CharacterManager.Load`: character templates (`characters`, `item_body_parts`,
//! `character_buffs`), starting equipment / supplies (`character_supplies`,
//! `character_equip_packs`, `equip_pack_cloths`, `equip_pack_weapons`), `bag_expands`,
//! `appellations`, `actability_groups`, `actability_categories`, `expert_limits`,
//! `expand_expert_limits` and `Data/CharTemplates.json` (spawn positions, bag sizes).

use std::collections::HashMap;
use std::path::Path;

use rusqlite::Connection;
use serde::Deserialize;

use super::sqlite::for_each_row;
use super::world_data::parse_lenient_json;
use super::world_templates::WorldSpawnPosition;
use super::GameDataError;

/// `CharacterTemplate`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct CharacterTemplate {
    pub race: u8,
    pub gender: u8,
    pub model_id: u32,
    pub faction_id: u32,
    pub zone_id: u32,
    pub return_district_id: u32,
    pub resurrection_district_id: u32,
    pub spawn_position: WorldSpawnPosition,
    /// Default body parts (`slot_type_id - 23` -> item id), equipment slots 19..=25.
    pub items: [u32; 7],
    pub buffs: Vec<u32>,
    pub num_inventory_slot: u8,
    pub num_bank_slot: i16,
}

/// `EquipItemsTemplate` (item id, grade) per equipment slot.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct EquipItemsTemplate {
    pub headgear: (u32, u8),
    pub necklace: (u32, u8),
    pub shirt: (u32, u8),
    pub belt: (u32, u8),
    pub pants: (u32, u8),
    pub gloves: (u32, u8),
    pub shoes: (u32, u8),
    pub bracelet: (u32, u8),
    pub back: (u32, u8),
    pub cosplay: (u32, u8),
    pub undershirts: (u32, u8),
    pub underpants: (u32, u8),
    pub mainhand: (u32, u8),
    pub offhand: (u32, u8),
    pub ranged: (u32, u8),
    pub musical: (u32, u8),
}

/// `AbilitySupplyItem`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct AbilitySupplyItem {
    pub id: u32,
    pub amount: i32,
    pub grade: u8,
}

/// `AbilityItems`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct AbilityItems {
    pub ability: u8,
    pub items: EquipItemsTemplate,
    pub supplies: Vec<AbilitySupplyItem>,
}

/// `ActabilityTemplate`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ActabilityTemplate {
    pub id: u32,
    pub name: String,
    pub unit_attribute_id: i32,
}

/// `ExpertLimit`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct ExpertLimit {
    pub id: u32,
    pub up_limit: i32,
    pub expert_limit_count: u8,
}

#[derive(Debug, Clone, Default)]
pub struct CharacterData {
    /// keyed by `16 * gender + race`.
    pub templates: HashMap<u8, CharacterTemplate>,
    pub ability_items: HashMap<u8, AbilityItems>,
    /// `_appellations` (id -> buff id).
    pub appellations: HashMap<u32, u32>,
    /// `_actabilities` in insertion order.
    pub actabilities: Vec<ActabilityTemplate>,
    /// `_actabilitiesCategories` (id -> group id).
    pub actability_categories: HashMap<u32, u32>,
    /// `_expertLimits` by step.
    pub expert_limits: Vec<ExpertLimit>,
}

#[derive(Debug, Deserialize, Default)]
#[serde(default, rename_all = "PascalCase")]
struct JsonPos {
    zone_id: u32,
    x: f32,
    y: f32,
    z: f32,
    yaw: f32,
    pitch: f32,
    roll: f32,
}

#[derive(Debug, Deserialize, Default)]
#[serde(default, rename_all = "PascalCase")]
struct CharacterTemplateConfig {
    id: u32,
    name: String,
    pos: Option<JsonPos>,
    num_inventory_slot: u8,
    num_bank_slot: i16,
}

/// `float.DegToRad()`.
/// `NumericExtensions.DegToRad(float)`: `MathF.PI / 180f * val` (that evaluation order: `v * PI
/// / 180` rounds differently and the client sees a different spawn yaw).
fn deg_to_rad(v: f32) -> f32 {
    std::f32::consts::PI / 180.0 * v
}

impl CharacterData {
    /// `GetTemplate(race, gender)`.
    pub fn template(&self, race: u8, gender: u8) -> Option<&CharacterTemplate> {
        self.templates
            .get(&(16u8.wrapping_mul(gender).wrapping_add(race)))
    }

    pub fn actability(&self, id: u32) -> Option<&ActabilityTemplate> {
        self.actabilities.iter().find(|a| a.id == id)
    }

    /// `GetActabilityIdByCategoryId(id)`: the actability of the category's group (0 when the
    /// category or the actability is unknown).
    pub fn actability_id_by_category_id(&self, id: u32) -> u32 {
        self.actability_categories
            .get(&id)
            .and_then(|&group_id| self.actability(group_id))
            .map_or(0, |a| a.id)
    }

    /// `GetExpertLimit(step)`.
    pub fn expert_limit(&self, step: usize) -> Option<&ExpertLimit> {
        self.expert_limits.get(step)
    }

    /// `CharacterManager.Load`. `zone_of` is
    /// `WorldManager.GetZoneId(GetWorldTemplateByName("main_world"), x, y)` and
    /// `default_world_id` `WorldManager.DefaultWorldTemplateId`.
    pub fn load(
        conn: &Connection,
        app_path: &Path,
        zone_of: impl Fn(f32, f32) -> u32,
        default_world_id: u32,
    ) -> Result<Self, GameDataError> {
        let mut d = CharacterData::default();
        let mut temp: HashMap<u32, u8> = HashMap::new();
        let mut rows = Vec::new();
        for_each_row(conn, "SELECT * FROM characters", [], |r| {
            rows.push((
                r.u32("id")?,
                CharacterTemplate {
                    race: r.u8("char_race_id")?,
                    gender: r.u8("char_gender_id")?,
                    model_id: r.u32("model_id")?,
                    faction_id: r.u32("faction_id")?,
                    zone_id: r.u32("starting_zone_id")?,
                    return_district_id: r.u32("default_return_district_id")?,
                    resurrection_district_id: r.u32("default_resurrection_district_id")?,
                    ..CharacterTemplate::default()
                },
            ));
            Ok(())
        })?;
        for (id, mut t) in rows {
            for_each_row(
                conn,
                "SELECT * FROM item_body_parts WHERE model_id=?1 ORDER BY id",
                [t.model_id],
                |r| {
                    let item_id = r.u32_or("item_id", 0)?;
                    let slot = r.i32("slot_type_id")? - 23;
                    // C# `template.Items[slot]` throws IndexOutOfRange outside 0..7.
                    let slot = usize::try_from(slot)
                        .ok()
                        .filter(|&s| s < 7)
                        .ok_or_else(|| {
                            rusqlite::Error::InvalidParameterName(format!(
                                "item_body_parts slot {} out of range",
                                slot + 23
                            ))
                        })?;
                    t.items[slot] = item_id;
                    Ok(())
                },
            )?;
            let key = 16u8.wrapping_mul(t.gender).wrapping_add(t.race);
            if d.templates.insert(key, t).is_some() || temp.insert(id, key).is_some() {
                return Err(GameDataError::File(format!(
                    "duplicate character template {key}"
                )));
            }
        }
        for_each_row(conn, "SELECT * FROM character_buffs", [], |r| {
            let character_id = r.u32("character_id")?;
            let buff_id = r.u32("buff_id")?;
            let key = temp.get(&character_id).ok_or_else(|| {
                rusqlite::Error::InvalidParameterName(format!(
                    "character_buffs: unknown character {character_id}"
                ))
            })?;
            d.templates
                .get_mut(key)
                .expect("template exists")
                .buffs
                .push(buff_id);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM character_supplies", [], |r| {
            let ability = r.u8("ability_id")?;
            d.ability_items
                .entry(ability)
                .or_default()
                .supplies
                .push(AbilitySupplyItem {
                    id: r.u32("item_id")?,
                    amount: r.i32("amount")?,
                    grade: r.u8("grade_id")?,
                });
            Ok(())
        })?;
        let mut packs = Vec::new();
        for_each_row(conn, "SELECT * FROM character_equip_packs", [], |r| {
            packs.push((
                r.u8("ability_id")?,
                r.u32_or("newbie_cloth_pack_id", 0)?,
                r.u32_or("newbie_weapon_pack_id", 0)?,
            ));
            Ok(())
        })?;
        for (ability, cloth, weapon) in packs {
            let mut items = EquipItemsTemplate::default();
            if cloth > 0 {
                for_each_row(
                    conn,
                    "SELECT * FROM equip_pack_cloths WHERE id=?1",
                    [cloth],
                    |r| {
                        let p = |a: &str, b: &str| -> rusqlite::Result<(u32, u8)> {
                            Ok((r.u32(a)?, r.u8(b)?))
                        };
                        items.headgear = p("headgear_id", "headgear_grade_id")?;
                        items.necklace = p("necklace_id", "necklace_grade_id")?;
                        items.shirt = p("shirt_id", "shirt_grade_id")?;
                        items.belt = p("belt_id", "belt_grade_id")?;
                        items.pants = p("pants_id", "pants_grade_id")?;
                        items.gloves = p("glove_id", "glove_grade_id")?;
                        items.shoes = p("shoes_id", "shoes_grade_id")?;
                        items.bracelet = p("bracelet_id", "bracelet_grade_id")?;
                        items.back = p("back_id", "back_grade_id")?;
                        items.cosplay = p("cosplay_id", "cosplay_grade_id")?;
                        items.undershirts = p("undershirt_id", "undershirt_grade_id")?;
                        items.underpants = p("underpants_id", "underpants_grade_id")?;
                        Ok(())
                    },
                )?;
            }
            if weapon > 0 {
                for_each_row(
                    conn,
                    "SELECT * FROM equip_pack_weapons WHERE id=?1",
                    [weapon],
                    |r| {
                        let p = |a: &str, b: &str| -> rusqlite::Result<(u32, u8)> {
                            Ok((r.u32(a)?, r.u8(b)?))
                        };
                        items.mainhand = p("mainhand_id", "mainhand_grade_id")?;
                        items.offhand = p("offhand_id", "offhand_grade_id")?;
                        items.ranged = p("ranged_id", "ranged_grade_id")?;
                        items.musical = p("musical_id", "musical_grade_id")?;
                        Ok(())
                    },
                )?;
            }
            // C#: `_abilityItems.Add(template.Ability, template)` throws when character_supplies
            // already created an entry for the ability.
            if d.ability_items.contains_key(&ability) {
                return Err(GameDataError::File(format!(
                    "CharacterManager: duplicate ability items for ability {ability}"
                )));
            }
            d.ability_items.insert(
                ability,
                AbilityItems {
                    ability,
                    items,
                    supplies: Vec::new(),
                },
            );
        }
        for_each_row(conn, "SELECT id, buff_id FROM appellations", [], |r| {
            d.appellations.insert(r.u32("id")?, r.u32_or("buff_id", 0)?);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM actability_groups", [], |r| {
            d.actabilities.push(ActabilityTemplate {
                id: r.u32("id")?,
                name: r.string("name")?,
                unit_attribute_id: r.i32("unit_attr_id")?,
            });
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM actability_categories", [], |r| {
            d.actability_categories
                .insert(r.u32("id")?, r.u32("group_id")?);
            Ok(())
        })?;
        for_each_row(
            conn,
            "SELECT * FROM expert_limits ORDER BY up_limit ASC",
            [],
            |r| {
                d.expert_limits.push(ExpertLimit {
                    id: r.u32("id")?,
                    up_limit: r.i32("up_limit")?,
                    expert_limit_count: r.u8("expert_limit")?,
                });
                Ok(())
            },
        )?;

        let path = app_path.join("Data").join("CharTemplates.json");
        let content = std::fs::read_to_string(&path).unwrap_or_default();
        if content.trim().is_empty() {
            return Err(GameDataError::File(format!(
                "File {} doesn't exists or is empty.",
                path.display()
            )));
        }
        let configs: Vec<CharacterTemplateConfig> = parse_lenient_json(&content).map_err(|e| {
            GameDataError::File(format!(
                "CharacterManager: Error parsing {} file ({e})",
                path.display()
            ))
        })?;
        for cfg in configs {
            let pos = cfg.pos.unwrap_or_default();
            let point = WorldSpawnPosition {
                // Recalculate ZoneId as this isn't included in the config
                zone_id: zone_of(pos.x, pos.y),
                x: pos.x,
                y: pos.y,
                z: pos.z,
                roll: deg_to_rad(pos.roll),
                pitch: deg_to_rad(pos.pitch),
                yaw: deg_to_rad(pos.yaw),
                world_id: default_world_id,
            };
            let _ = pos.zone_id;
            let _ = &cfg.name;
            for gender in [1u8, 2] {
                let key = (16 * gender).wrapping_add(cfg.id as u8);
                let t = d.templates.get_mut(&key).ok_or_else(|| {
                    GameDataError::File(format!("CharTemplates.json: no template {key}"))
                })?;
                t.spawn_position = point;
                t.num_inventory_slot = cfg.num_inventory_slot;
                t.num_bank_slot = cfg.num_bank_slot;
            }
        }
        tracing::info!("Loaded {} character templates", d.templates.len());
        Ok(d)
    }
}
