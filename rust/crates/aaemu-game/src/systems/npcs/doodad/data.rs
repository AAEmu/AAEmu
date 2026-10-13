//! `DoodadManager.Load` (`Core/Managers/UnitManagers/DoodadManager.cs`): doodad templates
//! (`doodad_almighties`, `doodad_groups`), func groups, funcs, phase funcs, every
//! `doodad_func_*` template table, the consume changer items and the template caches, plus the
//! read-only getters of `DoodadManager` (`GetTemplate`, `GetFunc`, `GetFuncsForGroup`,
//! `GetPhaseFunc`, `GetFuncTemplate`, `GetPhaseFuncTemplate`, `GetDoodadFuncGroups(Id)`,
//! `GetDoodadFuncs`, `GetDoodadPhaseFuncs`, `GetDoodadFuncConsumeChangerItemList`,
//! `GetTreasureChestTemplateIds`).
//!
//! C# behaviour mirrored:
//! * The first `doodad_func_groups` pass (`ORDER BY doodad_almighty_id, ...`) runs before any
//!   template exists, so it adds nothing (its column reads still run). The groups come from the
//!   second, unordered pass, which does not read `model` (`Model` stays null).
//! * `doodad_funcs` / `doodad_phase_funcs` lists keep their `ORDER BY doodad_func_group_id,
//!   actual_func_id` order; `FuncGroups` of a template and `IsCofferTemplate` follow the insertion
//!   order of `_allFuncGroups` (plain `doodad_func_groups` row order).
//! * Template tables go to `_funcTemplates` or `_phaseFuncTemplates` by the base class of their
//!   C# type (dictionaries created by reflection). Item / child tables whose classes derive from
//!   `DoodadPhaseFuncTemplate` therefore live with the phase func templates:
//!   `DoodadFuncBuyFishItem`, `DoodadFuncBuyFishModel`, `DoodadFuncConsumeChangerModel(Item)`,
//!   `DoodadFuncConvertFishItem`, `DoodadFuncCraftStartCraft`, `DoodadFuncCoffer`, ...
//! * Duplicate ids in a template table, in `doodad_funcs` or `doodad_almighties` fail the load
//!   (`Dictionary.Add` throws); duplicate func groups are logged and skipped (`TryAdd` +
//!   `Logger.Fatal`); duplicate `doodad_groups` / consume changer items keep the first row.
//! * A `doodad_func_clout_effects` row for an unknown clout, or a template with an unknown
//!   `group_id`, fails the load.
//! * `CreateTemplateCaches` sums the `Delay` of `DoodadFuncGrowth` templates looked up through
//!   the *funcs* of each group (`GetFuncsForGroup`, not the phase funcs) with
//!   `GetPhaseFuncTemplate`; `TotalDoodadGrowthTime` falls back to `GrowthTime` when <= 0.
//! * `GetBoolean(col, true)` columns use the `"t"`/`"1"` string rule, `GetBoolean(col)` columns
//!   (`doodad_groups.is_export`, `removed_by_house`) are `GetInt64 != 0`.

use std::collections::{HashMap, HashSet};

use rusqlite::Connection;

use super::templates::*;
use crate::data::sqlite::{for_each_row, SqlRow};
use crate::data::GameDataError;
use crate::systems::DataLoadCtx;

pub use super::templates::DoodadTemplate;

/// One template dictionary per C# class name (`Dictionary<string, Dictionary<uint, T>>`).
pub type FuncTemplateMap = HashMap<&'static str, HashMap<u32, FuncTemplate>>;

/// `DoodadManager` game data.
#[derive(Debug, Default)]
pub struct DoodadData {
    /// `_templates`.
    pub templates: HashMap<u32, DoodadTemplate>,
    /// `_templates` insertion order (`doodad_almighties` row order; `Values` enumeration order).
    pub template_order: Vec<u32>,
    /// `_allFuncGroups` in insertion order.
    pub all_func_groups: Vec<DoodadFuncGroups>,
    /// `_allFuncGroups` keys -> index in [`Self::all_func_groups`].
    pub all_func_groups_index: HashMap<u32, usize>,
    /// `_funcsByGroups`.
    pub funcs_by_groups: HashMap<u32, Vec<DoodadFunc>>,
    /// `_funcsById` (by `FuncKey`).
    pub funcs_by_id: HashMap<u32, DoodadFunc>,
    /// `_phaseFuncs`.
    pub phase_funcs: HashMap<u32, Vec<DoodadPhaseFunc>>,
    /// `_funcTemplates` (classes deriving from `DoodadFuncTemplate`).
    pub func_templates: FuncTemplateMap,
    /// `_phaseFuncTemplates` (classes deriving from `DoodadPhaseFuncTemplate`).
    pub phase_func_templates: FuncTemplateMap,
    /// `_doodadFuncConsumeChangerItem` values in insertion order (ids unique, first row wins).
    pub consume_changer_items: Vec<DoodadFuncConsumeChangerItem>,
    /// `_doodadGroups`.
    pub doodad_groups: HashMap<u32, DoodadGroups>,
}

fn data_err(msg: String) -> GameDataError {
    GameDataError::File(msg)
}

/// `reader.GetInt32(col, -1) >= 0 ? reader.GetInt32(col) : -1`.
fn next_phase_non_negative(r: &SqlRow<'_, '_>, col: &str) -> rusqlite::Result<i32> {
    let v = r.i32_or(col, -1)?;
    Ok(if v >= 0 { v } else { -1 })
}

/// `DoodadFuncTod.TodAsHours` as computed by `DoodadManager.Load`.
pub fn tod_as_hours(id: u32, tod_value: u32) -> f32 {
    let mut tod = tod_value;
    // Correction for typos in the compact DB
    while tod >= 2400 {
        tod /= 10;
    }
    let hh = tod / 100;
    let mut mm = tod % 100;
    if mm >= 60 {
        tracing::warn!("DoodadFuncToD has invalid value for minutes, Id {id}, ToD {tod_value}");
    }
    mm %= 60;
    hh as f32 * 1f32 + mm as f32 / 60f32
}

/// One `doodad_func_*` table: `SELECT *`, build, `dict[class].Add(func.Id, func)`.
fn load_templates(
    conn: &Connection,
    dict: &mut FuncTemplateMap,
    sql: &str,
    mut build: impl FnMut(&SqlRow<'_, '_>) -> rusqlite::Result<FuncTemplate>,
) -> Result<(), GameDataError> {
    let mut rows = Vec::new();
    for_each_row(conn, sql, [], |r| {
        rows.push(build(r)?);
        Ok(())
    })?;
    for func in rows {
        let class = func.func_type();
        let map = dict
            .get_mut(class)
            .ok_or_else(|| data_err(format!("doodad: no template dictionary for {class}")))?;
        let id = func.id();
        if map.contains_key(&id) {
            return Err(data_err(format!(
                "doodad: duplicate {class} id {id} ({sql}): An item with the same key has already been added"
            )));
        }
        map.insert(id, func);
    }
    Ok(())
}

impl DoodadData {
    /// `DoodadManager.Load`.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        Self::load_from(ctx.conn)
    }

    /// `DoodadManager.Load` on a connection.
    pub fn load_from(conn: &Connection) -> Result<Self, GameDataError> {
        let mut d = Self::default();
        for (class, phase) in FUNC_TEMPLATE_CLASSES {
            if phase {
                d.phase_func_templates.insert(class, HashMap::new());
            } else {
                d.func_templates.insert(class, HashMap::new());
            }
        }

        tracing::info!("Loading doodad functions ...");
        d.load_func_groups_discarded(conn)?;
        d.load_funcs(conn)?;
        d.load_phase_funcs(conn)?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_animates",
            |r| {
                Ok(FuncTemplate::Animate(DoodadFuncAnimate {
                    id: r.u32("id")?,
                    name: r.string("name")?,
                    play_once: r.bool_str("play_once")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_area_triggers",
            |r| {
                Ok(FuncTemplate::AreaTrigger(DoodadFuncAreaTrigger {
                    id: r.u32("id")?,
                    npc_id: r.u32_or("npc_id", 0)?,
                    is_enter: r.bool_str("is_enter")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_attachments",
            |r| {
                Ok(FuncTemplate::Attachment(DoodadFuncAttachment {
                    id: r.u32("id")?,
                    attach_point_id: r.u8("attach_point_id")?,
                    space: r.i32("space")?,
                    bond_kind_id: u32::from(r.u8("bond_kind_id")?),
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_bindings",
            |r| {
                Ok(FuncTemplate::Binding(DoodadFuncBinding {
                    id: r.u32("id")?,
                    district_id: r.u32("district_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_bubbles",
            |r| {
                Ok(FuncTemplate::Bubble(DoodadFuncBubble {
                    id: r.u32("id")?,
                    bubble_id: r.u32("bubble_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_buffs",
            |r| {
                Ok(FuncTemplate::Buff(DoodadFuncBuff {
                    id: r.u32("id")?,
                    buff_id: r.u32("buff_id")?,
                    radius: r.f32("radius")?,
                    count: r.i32("count")?,
                    perm_id: r.u32("perm_id")?,
                    relationship_id: r.u32("relationship_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_butchers",
            |r| {
                Ok(FuncTemplate::Butcher(DoodadFuncButcher {
                    id: r.u32("id")?,
                    corpse_model: r.string("corpse_model")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_buy_fish_items",
            |r| {
                Ok(FuncTemplate::BuyFishItem(DoodadFuncBuyFishItem {
                    id: r.u32("id")?,
                    doodad_func_buy_fish_id: r.u32("doodad_func_buy_fish_id")?,
                    item_id: r.u32("item_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_buy_fish_models",
            |r| {
                Ok(FuncTemplate::BuyFishModel(DoodadFuncBuyFishModel {
                    id: r.u32("id")?,
                    name: r.string("name")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_buy_fishes",
            |r| {
                Ok(FuncTemplate::BuyFish(DoodadFuncBuyFish {
                    id: r.u32("id")?,
                    item_id: r.u32_or("item_id", 0)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_catches",
            |r| Ok(FuncTemplate::Catch(DoodadFuncCatch { id: r.u32("id")? })),
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_cereal_harvests",
            |r| {
                Ok(FuncTemplate::CerealHarvest(DoodadFuncCerealHarvest {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_cleanup_logic_links",
            |r| {
                Ok(FuncTemplate::CleanupLogicLink(DoodadFuncCleanupLogicLink {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_climate_reacts",
            |r| {
                Ok(FuncTemplate::ClimateReact(DoodadFuncClimateReact {
                    id: r.u32("id")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_climbs",
            |r| {
                Ok(FuncTemplate::Climb(DoodadFuncClimb {
                    id: r.u32("id")?,
                    climb_type_id: r.u32("climb_type_id")?,
                    allow_horizontal_multi_hanger: r.bool_str("allow_horizontal_multi_hanger")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_clouts",
            |r| {
                Ok(FuncTemplate::Clout(DoodadFuncClout {
                    id: r.u32("id")?,
                    duration: r.i32("duration")?,
                    tick: r.i32("tick")?,
                    target_relation: r.u32("target_relation_id")? as u8,
                    buff_id: r.u32_or("buff_id", 0)?,
                    projectile_id: r.u32_or("projectile_id", 0)?,
                    show_to_friendly_only: r.bool_str("show_to_friendly_only")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                    aoe_shape_id: r.u32("aoe_shape_id")?,
                    target_buff_tag_id: r.u32_or("target_buff_tag_id", 0)?,
                    target_no_buff_tag_id: r.u32_or("target_no_buff_tag_id", 0)?,
                    use_origin_source: r.bool_str("use_origin_source")?,
                    effects: Vec::new(),
                }))
            },
        )?;
        d.load_clout_effects(conn)?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_coffer_perms",
            |r| {
                Ok(FuncTemplate::CofferPerm(DoodadFuncCofferPerm {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_coffers",
            |r| {
                Ok(FuncTemplate::Coffer(DoodadFuncCoffer {
                    id: r.u32("id")?,
                    capacity: r.i32("capacity")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_conditional_uses",
            |r| {
                Ok(FuncTemplate::ConditionalUse(DoodadFuncConditionalUse {
                    id: r.u32("id")?,
                    skill_id: r.u32_or("skill_id", 0)?,
                    fake_skill_id: r.u32_or("fake_skill_id", 0)?,
                    quest_id: r.u32_or("quest_id", 0)?,
                    quest_trigger_phase: r.u32_or("quest_trigger_phase", 0)?,
                    item_id: r.u32_or("item_id", 0)?,
                    item_trigger_phase: r.u32_or("item_trigger_phase", 0)?,
                }))
            },
        )?;
        d.load_consume_changer_items(conn)?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_consume_changer_model_items",
            |r| {
                Ok(FuncTemplate::ConsumeChangerModelItem(
                    DoodadFuncConsumeChangerModelItem {
                        id: r.u32("id")?,
                        doodad_func_consume_changer_model_id: r
                            .u32("doodad_func_consume_changer_model_id")?,
                        item_id: r.u32("item_id")?,
                    },
                ))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_consume_changer_models",
            |r| {
                Ok(FuncTemplate::ConsumeChangerModel(
                    DoodadFuncConsumeChangerModel {
                        id: r.u32("id")?,
                        name: r.string("name")?,
                    },
                ))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_consume_changers",
            |r| {
                Ok(FuncTemplate::ConsumeChanger(DoodadFuncConsumeChanger {
                    id: r.u32("id")?,
                    slot_id: r.u32("slot_id")?,
                    count: r.i32("count")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_consume_items",
            |r| {
                Ok(FuncTemplate::ConsumeItem(DoodadFuncConsumeItem {
                    id: r.u32("id")?,
                    item_id: r.u32("item_id")?,
                    count: r.i32("count")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_convert_fish_items",
            |r| {
                Ok(FuncTemplate::ConvertFishItem(DoodadFuncConvertFishItem {
                    id: r.u32("id")?,
                    doodad_func_convert_fish_id: r.u32("doodad_func_convert_fish_id")?,
                    item_id: r.u32("item_id")?,
                    loot_pack_id: r.u32("loot_pack_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_convert_fishes",
            |r| {
                Ok(FuncTemplate::ConvertFish(DoodadFuncConvertFish {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_craft_acts",
            |r| {
                Ok(FuncTemplate::CraftAct(DoodadFuncCraftAct {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_craft_cancels",
            |r| {
                Ok(FuncTemplate::CraftCancel(DoodadFuncCraftCancel {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_craft_directs",
            |r| {
                Ok(FuncTemplate::CraftDirect(DoodadFuncCraftDirect {
                    id: r.u32("id")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_craft_get_items",
            |r| {
                Ok(FuncTemplate::CraftGetItem(DoodadFuncCraftGetItem {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_craft_infos",
            |r| {
                Ok(FuncTemplate::CraftInfo(DoodadFuncCraftInfo {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_craft_packs",
            |r| {
                Ok(FuncTemplate::CraftPack(DoodadFuncCraftPack {
                    id: r.u32("id")?,
                    craft_pack_id: r.u32("craft_pack_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_craft_start_crafts",
            |r| {
                Ok(FuncTemplate::CraftStartCraft(DoodadFuncCraftStartCraft {
                    id: r.u32("id")?,
                    doodad_func_craft_start_id: r.u32("doodad_func_craft_start_id")?,
                    craft_id: r.u32("craft_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_craft_starts",
            |r| {
                Ok(FuncTemplate::CraftStart(DoodadFuncCraftStart {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_crop_harvests",
            |r| {
                Ok(FuncTemplate::CropHarvest(DoodadFuncCropHarvest {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_crystal_collects",
            |r| {
                Ok(FuncTemplate::CrystalCollect(DoodadFuncCrystalCollect {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_cutdownings",
            |r| {
                Ok(FuncTemplate::Cutdowning(DoodadFuncCutdowning {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_cutdowns",
            |r| {
                Ok(FuncTemplate::Cutdown(DoodadFuncCutdown {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_dairy_collects",
            |r| {
                Ok(FuncTemplate::DairyCollect(DoodadFuncDairyCollect {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_declare_sieges",
            |r| {
                Ok(FuncTemplate::DeclareSiege(DoodadFuncDeclareSiege {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_digs",
            |r| Ok(FuncTemplate::Dig(DoodadFuncDig { id: r.u32("id")? })),
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_dig_terrains",
            |r| {
                Ok(FuncTemplate::DigTerrain(DoodadFuncDigTerrain {
                    id: r.u32("id")?,
                    radius: r.i32("radius")?,
                    life: r.i32("life")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_dyeingredient_collects",
            |r| {
                Ok(FuncTemplate::DyeingredientCollect(
                    DoodadFuncDyeingredientCollect { id: r.u32("id")? },
                ))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_enter_instances",
            |r| {
                Ok(FuncTemplate::EnterInstance(DoodadFuncEnterInstance {
                    id: r.u32("id")?,
                    zone_id: r.u32("zone_id")?,
                    item_id: r.u32_or("item_id", 0)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_enter_sys_instances",
            |r| {
                Ok(FuncTemplate::EnterSysInstance(DoodadFuncEnterSysInstance {
                    id: r.u32("id")?,
                    zone_id: r.u32("zone_id")?,
                    faction_id: r.u32_or("faction_id", 0)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_evidence_item_loots",
            |r| {
                Ok(FuncTemplate::EvidenceItemLoot(DoodadFuncEvidenceItemLoot {
                    id: r.u32("id")?,
                    skill_id: r.u32("skill_id")?,
                    crime_value: r.i16("crime_value")?,
                    crime_kind_id: r.u32("crime_kind_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_exchanges",
            |r| {
                Ok(FuncTemplate::Exchange(DoodadFuncExchange {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_exit_induns",
            |r| {
                Ok(FuncTemplate::ExitIndun(DoodadFuncExitIndun {
                    id: r.u32("id")?,
                    return_point_id: r.u32_or("return_point_id", 0)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_fake_uses",
            |r| {
                Ok(FuncTemplate::FakeUse(DoodadFuncFakeUse {
                    id: r.u32("id")?,
                    skill_id: r.u32_or("skill_id", 0)?,
                    fake_skill_id: r.u32_or("fake_skill_id", 0)?,
                    target_parent: r.bool_str("target_parent")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_feeds",
            |r| {
                Ok(FuncTemplate::Feed(DoodadFuncFeed {
                    id: r.u32("id")?,
                    item_id: r.u32("item_id")?,
                    count: r.i32("count")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_fiber_collects",
            |r| {
                Ok(FuncTemplate::FiberCollect(DoodadFuncFiberCollect {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_finals",
            |r| {
                Ok(FuncTemplate::Final(DoodadFuncFinal {
                    id: r.u32("id")?,
                    after: r.i32_or("after", 0)?,
                    respawn: r.bool_str("respawn")?,
                    min_time: r.i32_or("min_time", 0)?,
                    max_time: r.i32_or("max_time", 0)?,
                    show_tip: r.bool_str("show_tip")?,
                    show_end_time: r.bool_str("show_end_time")?,
                    tip: r.string("tip")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_fish_schools",
            |r| {
                Ok(FuncTemplate::FishSchool(DoodadFuncFishSchool {
                    id: r.u32("id")?,
                    npc_spawner_id: r.u32("npc_spawner_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_fruit_picks",
            |r| {
                Ok(FuncTemplate::FruitPick(DoodadFuncFruitPick {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_gass_extracts",
            |r| {
                Ok(FuncTemplate::GassExtract(DoodadFuncGassExtract {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_growths",
            |r| {
                Ok(FuncTemplate::Growth(DoodadFuncGrowth {
                    id: r.u32("id")?,
                    delay: r.i32("delay")?,
                    start_scale: r.i32("start_scale")?,
                    end_scale: r.i32("end_scale")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_harvests",
            |r| {
                Ok(FuncTemplate::Harvest(DoodadFuncHarvest {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_house_farms",
            |r| {
                Ok(FuncTemplate::HouseFarm(DoodadFuncHouseFarm {
                    id: r.u32("id")?,
                    item_category_id: r.u32("item_category_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_housing_areas",
            |r| {
                Ok(FuncTemplate::HousingArea(DoodadFuncHousingArea {
                    id: r.u32("id")?,
                    faction_id: r.u32("faction_id")?,
                    radius: r.i32("radius")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_hungers",
            |r| {
                Ok(FuncTemplate::Hunger(DoodadFuncHunger {
                    id: r.i32("id")? as u32,
                    hungry_term: r.i32("hungry_term")?,
                    full_step: r.i32("full_step")?,
                    phase_change_limit: r.i32("phase_change_limit")?,
                    next_phase: next_phase_non_negative(r, "next_phase")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_insert_counters",
            |r| {
                Ok(FuncTemplate::InsertCounter(DoodadFuncInsertCounter {
                    id: r.u32("id")?,
                    count: r.i32("count")?,
                    item_id: r.u32("item_id")?,
                    item_count: r.i32("item_count")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_logics",
            |r| {
                Ok(FuncTemplate::Logic(DoodadFuncLogic {
                    id: r.u32("id")?,
                    operation_id: r.u32("operation_id")?,
                    delay_id: r.u32("delay_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_logic_family_providers",
            |r| {
                Ok(FuncTemplate::LogicFamilyProvider(
                    DoodadFuncLogicFamilyProvider {
                        id: r.u32("id")?,
                        family_id: r.u32("family_id")?,
                    },
                ))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_logic_family_subscribers",
            |r| {
                Ok(FuncTemplate::LogicFamilySubscriber(
                    DoodadFuncLogicFamilySubscriber {
                        id: r.u32("id")?,
                        family_id: r.u32("family_id")?,
                    },
                ))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_loot_items",
            |r| {
                Ok(FuncTemplate::LootItem(DoodadFuncLootItem {
                    id: r.u32("id")?,
                    world_interaction_id: r.u32("wi_id")? as i32,
                    item_id: r.u32("item_id")?,
                    count_min: r.i32("count_min")?,
                    count_max: r.i32("count_max")?,
                    percent: r.i32("percent")?,
                    remain_time: r.i32("remain_time")?,
                    group_id: r.u32("group_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_loot_packs",
            |r| {
                Ok(FuncTemplate::LootPack(DoodadFuncLootPack {
                    id: r.u32("id")?,
                    loot_pack_id: r.u32("loot_pack_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_machine_parts_collects",
            |r| {
                Ok(FuncTemplate::MachinePartsCollect(
                    DoodadFuncMachinePartsCollect { id: r.u32("id")? },
                ))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_medicalingredient_mines",
            |r| {
                Ok(FuncTemplate::MedicalingredientMine(
                    DoodadFuncMedicalingredientMine { id: r.u32("id")? },
                ))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_mows",
            |r| Ok(FuncTemplate::Mow(DoodadFuncMow { id: r.u32("id")? })),
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_navi_mark_pos_to_maps",
            |r| {
                Ok(FuncTemplate::NaviMarkPosToMap(DoodadFuncNaviMarkPosToMap {
                    id: r.u32("id")?,
                    x: r.i32("x")?,
                    y: r.i32("y")?,
                    z: r.i32("z")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_navi_namings",
            |r| {
                Ok(FuncTemplate::NaviNaming(DoodadFuncNaviNaming {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_navi_open_bounties",
            |r| {
                Ok(FuncTemplate::NaviOpenBounty(DoodadFuncNaviOpenBounty {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_navi_open_mailboxes",
            |r| {
                Ok(FuncTemplate::NaviOpenMailbox(DoodadFuncNaviOpenMailbox {
                    id: r.u32("id")?,
                    duration: r.i32("duration")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_navi_open_portals",
            |r| {
                Ok(FuncTemplate::NaviOpenPortal(DoodadFuncNaviOpenPortal {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_navi_remove_timers",
            |r| {
                Ok(FuncTemplate::NaviRemoveTimer(DoodadFuncNaviRemoveTimer {
                    id: r.u32("id")?,
                    after: r.i32("after")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_navi_removes",
            |r| {
                Ok(FuncTemplate::NaviRemove(DoodadFuncNaviRemove {
                    id: r.u32("id")?,
                    req_labor_power: r.i32("req_lp")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_navi_teleports",
            |r| {
                Ok(FuncTemplate::NaviTeleport(DoodadFuncNaviTeleport {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_open_farm_infos",
            |r| {
                Ok(FuncTemplate::OpenFarmInfo(DoodadFuncOpenFarmInfo {
                    id: r.u32("id")?,
                    farm_id: r.u32("farm_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_open_papers",
            |r| {
                Ok(FuncTemplate::OpenPaper(DoodadFuncOpenPaper {
                    id: r.u32("id")?,
                    book_page_id: r.u32_or("book_page_id", 0)?,
                    book_id: r.u32_or("book_id", 0)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_ore_mines",
            |r| {
                Ok(FuncTemplate::OreMine(DoodadFuncOreMine {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_parent_infos",
            |r| {
                Ok(FuncTemplate::ParentInfo(DoodadFuncParentInfo {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_parrots",
            |r| Ok(FuncTemplate::Parrot(DoodadFuncParrot { id: r.u32("id")? })),
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_plant_collects",
            |r| {
                Ok(FuncTemplate::PlantCollect(DoodadFuncPlantCollect {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_play_flow_graphs",
            |r| {
                Ok(FuncTemplate::PlayFlowGraph(DoodadFuncPlayFlowGraph {
                    id: r.u32("id")?,
                    event_on_phase_change_id: r.u32("event_on_phase_change_id")?,
                    event_on_visible_id: r.u32("event_on_visible_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_pulse_triggers",
            |r| {
                Ok(FuncTemplate::PulseTrigger(DoodadFuncPulseTrigger {
                    id: r.u32("id")?,
                    flag: r.bool_str("flag")?,
                    next_phase: next_phase_non_negative(r, "next_phase")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_pulses",
            |r| {
                Ok(FuncTemplate::Pulse(DoodadFuncPulse {
                    id: r.u32("id")?,
                    flag: r.bool_str("flag")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_purchases",
            |r| {
                Ok(FuncTemplate::Purchase(DoodadFuncPurchase {
                    id: r.u32("id")?,
                    item_id: r.u32_or("item_id", 0)?,
                    count: r.i32("count")?,
                    coin_item_id: r.u32_or("coin_item_id", 0)?,
                    coin_count: r.i32_or("coin_count", 0)?,
                    currency_id: r.u32("currency_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_puzzle_ins",
            |r| {
                Ok(FuncTemplate::PuzzleIn(DoodadFuncPuzzleIn {
                    id: r.u32("id")?,
                    group_id: r.u32("group_id")?,
                    ratio: r.f32("ratio")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_puzzle_outs",
            |r| {
                Ok(FuncTemplate::PuzzleOut(DoodadFuncPuzzleOut {
                    id: r.u32("id")?,
                    group_id: r.u32("group_id")?,
                    ratio: r.f32("ratio")?,
                    anim: r.string("anim")?,
                    projectile_id: r.u32_or("projectile_id", 0)?,
                    loot_pack_id: r.u32_or("loot_pack_id", 0)?,
                    delay: r.i32("delay")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                    projectile_delay: r.i32("projectile_delay")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_puzzle_rolls",
            |r| {
                Ok(FuncTemplate::PuzzleRoll(DoodadFuncPuzzleRoll {
                    id: r.u32("id")?,
                    item_id: r.u32("item_id")?,
                    count: r.i32("count")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_quests",
            |r| {
                Ok(FuncTemplate::Quest(DoodadFuncQuest {
                    id: r.u32("id")?,
                    quest_kind_id: r.u32("quest_kind_id")?,
                    quest_id: r.u32("quest_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_ratio_changes",
            |r| {
                Ok(FuncTemplate::RatioChange(DoodadFuncRatioChange {
                    id: r.u32("id")?,
                    ratio: r.i32("ratio")?,
                    next_phase: next_phase_non_negative(r, "next_phase")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_ratio_respawns",
            |r| {
                Ok(FuncTemplate::RatioRespawn(DoodadFuncRatioRespawn {
                    id: r.u32("id")?,
                    ratio: r.i32("ratio")?,
                    spawn_doodad_id: r.u32("spawn_doodad_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_recover_items",
            |r| {
                Ok(FuncTemplate::RecoverItem(DoodadFuncRecoverItem {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_remove_instances",
            |r| {
                Ok(FuncTemplate::RemoveInstance(DoodadFuncRemoveInstance {
                    id: r.u32("id")?,
                    zone_id: r.u32("zone_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_remove_items",
            |r| {
                Ok(FuncTemplate::RemoveItem(DoodadFuncRemoveItem {
                    id: r.u32("id")?,
                    item_id: r.u32("item_id")?,
                    count: r.i32("count")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_renew_items",
            |r| {
                Ok(FuncTemplate::RenewItem(DoodadFuncRenewItem {
                    id: r.u32("id")?,
                    skill_id: r.u32("skill_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_require_items",
            |r| {
                Ok(FuncTemplate::RequireItem(DoodadFuncRequireItem {
                    id: r.u32("id")?,
                    world_interaction_id: r.u32("wi_id")? as i32,
                    item_id: r.u32("item_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_require_quests",
            |r| {
                Ok(FuncTemplate::RequireQuest(DoodadFuncRequireQuest {
                    id: r.u32("id")?,
                    world_interaction_id: r.u32("wi_id")? as i32,
                    quest_id: r.u32("quest_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_respawns",
            |r| {
                Ok(FuncTemplate::Respawn(DoodadFuncRespawn {
                    id: r.u32("id")?,
                    min_time: r.i32("min_time")?,
                    max_time: r.i32("max_time")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_rock_mines",
            |r| {
                Ok(FuncTemplate::RockMine(DoodadFuncRockMine {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_seed_collects",
            |r| {
                Ok(FuncTemplate::SeedCollect(DoodadFuncSeedCollect {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_shears",
            |r| {
                Ok(FuncTemplate::Shear(DoodadFuncShear {
                    id: r.u32("id")?,
                    shear_type_id: r.u32("shear_type_id")?,
                    shear_term: r.i32("shear_term")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_siege_periods",
            |r| {
                Ok(FuncTemplate::SiegePeriod(DoodadFuncSiegePeriod {
                    id: r.u32("id")?,
                    siege_period_id: r.u32("siege_period_id")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                    defense: r.bool_str("defense")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_signs",
            |r| {
                Ok(FuncTemplate::Sign(DoodadFuncSign {
                    id: r.u32("id")?,
                    name: r.string("name")?,
                    pick_num: r.i32("pick_num")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_skill_hits",
            |r| {
                Ok(FuncTemplate::SkillHit(DoodadFuncSkillHit {
                    id: r.u32("id")?,
                    skill_id: r.u32("skill_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_skin_offs",
            |r| {
                Ok(FuncTemplate::SkinOff(DoodadFuncSkinOff {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_soil_collects",
            |r| {
                Ok(FuncTemplate::SoilCollect(DoodadFuncSoilCollect {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_spawn_gimmicks",
            |r| {
                Ok(FuncTemplate::SpawnGimmick(DoodadFuncSpawnGimmick {
                    id: r.u32("id")?,
                    gimmick_id: r.u32("gimmick_id")?,
                    faction_id: r.u32("faction_id")?,
                    scale: r.f32("scale")?,
                    offset_x: r.f32("offset_x")?,
                    offset_y: r.f32("offset_y")?,
                    offset_z: r.f32("offset_z")?,
                    velocity_x: r.f32("velocity_x")?,
                    velocity_y: r.f32("velocity_y")?,
                    velocity_z: r.f32("velocity_z")?,
                    angle_x: r.f32("angle_x")?,
                    angle_y: r.f32("angle_y")?,
                    angle_z: r.f32("angle_z")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_spawn_mgmts",
            |r| {
                Ok(FuncTemplate::SpawnMgmt(DoodadFuncSpawnMgmt {
                    id: r.u32("id")?,
                    group_id: r.u32("group_id")?,
                    spawn: r.bool_str("spawn")?,
                    zone_id: r.u32("zone_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_spice_collects",
            |r| {
                Ok(FuncTemplate::SpiceCollect(DoodadFuncSpiceCollect {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_stamp_makers",
            |r| {
                Ok(FuncTemplate::StampMaker(DoodadFuncStampMaker {
                    id: r.u32("id")?,
                    consume_money: r.i32("consume_money")?,
                    item_id: r.u32("item_id")?,
                    consume_item_id: r.u32("consume_item_id")?,
                    consume_count: r.i32("consume_count")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_store_uis",
            |r| {
                Ok(FuncTemplate::StoreUi(DoodadFuncStoreUi {
                    id: r.u32("id")?,
                    merchant_pack_id: r.u32("merchant_pack_id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_timers",
            |r| {
                Ok(FuncTemplate::Timer(DoodadFuncTimer {
                    id: r.u32("id")?,
                    delay: r.i32("delay")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                    keep_requester: r.bool_str("keep_requester")?,
                    show_tip: r.bool_str("show_tip")?,
                    show_end_time: r.bool_str("show_end_time")?,
                    tip: r.string("tip")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_tods",
            |r| {
                let mut func = DoodadFuncTod {
                    id: r.u32("id")?,
                    tod: r.u32("tod")?,
                    next_phase: next_phase_non_negative(r, "next_phase")?,
                    tod_as_hours: 0.0,
                };
                func.tod_as_hours = tod_as_hours(func.id, func.tod);
                Ok(FuncTemplate::Tod(func))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_tree_byproducts_collects",
            |r| {
                Ok(FuncTemplate::TreeByProductsCollect(
                    DoodadFuncTreeByProductsCollect { id: r.u32("id")? },
                ))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_ucc_imprints",
            |r| {
                Ok(FuncTemplate::UccImprint(DoodadFuncUccImprint {
                    id: r.u32("id")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_uses",
            |r| {
                Ok(FuncTemplate::Use(DoodadFuncUse {
                    id: r.u32("id")?,
                    skill_id: r.u32_or("skill_id", 0)?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_water_volumes",
            |r| {
                Ok(FuncTemplate::WaterVolume(DoodadFuncWaterVolume {
                    id: r.u32("id")?,
                    level_change: r.f32("levelChange")?,
                    duration: r.f32("duration")?,
                }))
            },
        )?;
        load_templates(
            conn,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_zone_reacts",
            |r| {
                Ok(FuncTemplate::ZoneReact(DoodadFuncZoneReact {
                    id: r.u32("id")?,
                    zone_group_id: r.u32("zone_group_id")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                }))
            },
        )?;
        tracing::info!("Finished loading doodad functions ...");
        d.load_all_func_groups(conn)?;
        d.load_doodad_templates(conn)?;
        tracing::info!("Loaded {} doodad templates", d.templates.len());

        d.create_template_caches();
        Ok(d)
    }

    /// First `doodad_func_groups` pass: `GetTemplate` finds nothing yet (`_templates` is empty),
    /// so only the column reads have an effect (errors).
    fn load_func_groups_discarded(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        for_each_row(
            conn,
            "SELECT * FROM doodad_func_groups ORDER BY doodad_almighty_id, doodad_func_group_kind_id",
            [],
            |r| {
                let group = DoodadFuncGroups {
                    id: r.u32("id")?,
                    almighty: r.u32("doodad_almighty_id")?,
                    group_kind_id: r.u32("doodad_func_group_kind_id")?,
                    sound_id: r.u32_or("sound_id", 0)?,
                    model: Some(r.string_or("model", "")?),
                };
                if let Some(template) = self.templates.get_mut(&group.almighty) {
                    template.func_groups.push(group);
                }
                Ok(())
            },
        )?;
        Ok(())
    }

    /// `doodad_funcs`.
    fn load_funcs(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        let mut funcs = Vec::new();
        for_each_row(
            conn,
            "SELECT * FROM doodad_funcs ORDER BY doodad_func_group_id, actual_func_id",
            [],
            |r| {
                funcs.push(DoodadFunc {
                    func_key: r.u32("id")?,
                    group_id: r.u32("doodad_func_group_id")?,
                    func_id: r.u32("actual_func_id")?,
                    func_type: r.string("actual_func_type")?,
                    next_phase: r.i32_or("next_phase", -1)?,
                    sound_id: r.u32_or("sound_id", 0)?,
                    skill_id: r.u32_or("func_skill_id", 0)?,
                    perm_id: r.u32("perm_id")?,
                    count: r.i32_or("act_count", 0)?,
                });
                Ok(())
            },
        )?;
        for func in funcs {
            self.funcs_by_groups
                .entry(func.group_id)
                .or_default()
                .push(func.clone());
            let key = func.func_key;
            if self.funcs_by_id.insert(key, func).is_some() {
                return Err(data_err(format!(
                    "doodad: duplicate doodad_funcs id {key}: An item with the same key has already been added"
                )));
            }
        }
        Ok(())
    }

    /// `doodad_phase_funcs`.
    fn load_phase_funcs(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        for_each_row(
            conn,
            "SELECT * FROM doodad_phase_funcs ORDER BY doodad_func_group_id, actual_func_id",
            [],
            |r| {
                let func = DoodadPhaseFunc {
                    group_id: r.u32("doodad_func_group_id")?,
                    func_id: r.u32("actual_func_id")?,
                    func_type: r.string("actual_func_type")?,
                };
                self.phase_funcs
                    .entry(func.group_id)
                    .or_default()
                    .push(func);
                Ok(())
            },
        )?;
        Ok(())
    }

    /// `doodad_func_clout_effects` -> `DoodadFuncClout.Effects` (unknown clout: KeyNotFound).
    fn load_clout_effects(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        let mut rows = Vec::new();
        for_each_row(conn, "SELECT * FROM doodad_func_clout_effects", [], |r| {
            let clout_id = r.u32("doodad_func_clout_id")?;
            rows.push((clout_id, r.u32("effect_id")?));
            Ok(())
        })?;
        let clouts = self
            .phase_func_templates
            .get_mut("DoodadFuncClout")
            .ok_or_else(|| data_err("doodad: no DoodadFuncClout dictionary".into()))?;
        for (clout_id, effect_id) in rows {
            match clouts.get_mut(&clout_id) {
                Some(FuncTemplate::Clout(c)) => c.effects.push(effect_id),
                _ => {
                    return Err(data_err(format!(
                        "doodad: doodad_func_clout_effects references unknown clout {clout_id}"
                    )))
                }
            }
        }
        Ok(())
    }

    /// `doodad_func_consume_changer_items` (`TryAdd`: the first row of an id wins).
    fn load_consume_changer_items(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        let mut seen: HashSet<u32> = self.consume_changer_items.iter().map(|e| e.id).collect();
        for_each_row(
            conn,
            "SELECT * FROM doodad_func_consume_changer_items",
            [],
            |r| {
                let entry = DoodadFuncConsumeChangerItem {
                    id: r.u32("id")?,
                    doodad_func_consume_changer_id: r.u32("doodad_func_consume_changer_id")?,
                    item_id: r.u32("item_id")?,
                };
                if seen.insert(entry.id) {
                    self.consume_changer_items.push(entry);
                }
                Ok(())
            },
        )?;
        Ok(())
    }

    /// Second `doodad_func_groups` pass (`_allFuncGroups`, no `model` read).
    fn load_all_func_groups(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        for_each_row(conn, "SELECT * FROM doodad_func_groups", [], |r| {
            let group = DoodadFuncGroups {
                id: r.u32("id")?,
                almighty: r.u32("doodad_almighty_id")?,
                group_kind_id: r.u32("doodad_func_group_kind_id")?,
                sound_id: r.u32_or("sound_id", 0)?,
                model: None,
            };
            if self.all_func_groups_index.contains_key(&group.id) {
                tracing::error!("Failed to add FuncGroups: {}", group.id);
            } else {
                self.all_func_groups_index
                    .insert(group.id, self.all_func_groups.len());
                self.all_func_groups.push(group);
            }
            Ok(())
        })?;
        Ok(())
    }

    /// `LoadDoodadTemplates`: `doodad_groups`, `doodad_almighties`, func groups binding.
    fn load_doodad_templates(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        tracing::info!("Loading doodad templates...");
        self.doodad_groups.clear();
        for_each_row(conn, "SELECT * FROM doodad_groups", [], |r| {
            let group = DoodadGroups {
                id: r.u32("id")?,
                guard_on_field_time: r.u32("guard_on_field_time")?,
                is_export: r.bool("is_export")?,
                removed_by_house: r.bool("removed_by_house")?,
            };
            self.doodad_groups.entry(group.id).or_insert(group);
            Ok(())
        })?;

        // `IsCofferTemplate` for every template at once (same result, one pass).
        let coffer_capacities = self.coffer_capacities();
        let mut rows = Vec::new();
        for_each_row(conn, "SELECT * from doodad_almighties", [], |r| {
            let template_id = r.u32("id")?;
            let coffer_capacity = if template_id == 0 {
                -1
            } else {
                coffer_capacities.get(&template_id).copied().unwrap_or(-1)
            };
            let group_id = r.u32("group_id")?;
            let template = DoodadTemplate {
                id: template_id,
                once_one_man: r.bool_str("once_one_man")?,
                once_one_interaction: r.bool_str("once_one_interaction")?,
                mgmt_spawn: r.bool_str("mgmt_spawn")?,
                percent: r.i32_or("percent", 0)?,
                min_time: r.i32_or("min_time", 0)?,
                max_time: r.i32_or("max_time", 0)?,
                model_kind_id: r.u32("model_kind_id")?,
                use_creator_faction: r.bool_str("use_creator_faction")?,
                force_tod_top_priority: r.bool_str("force_tod_top_priority")?,
                milestone_id: r.u32_or("milestone_id", 0)?,
                group_id,
                // Filled below (the C# throws right after reading group_id).
                group: DoodadGroups::default(),
                use_target_decal: r.bool_str("use_target_decal")?,
                use_target_silhouette: r.bool_str("use_target_silhouette")?,
                use_target_highlight: r.bool_str("use_target_highlight")?,
                target_decal_size: r.f32_or("target_decal_size", 0.0)?,
                sim_radius: r.i32_or("sim_radius", 0)?,
                collide_ship: r.bool_str("collide_ship")?,
                collide_vehicle: r.bool_str("collide_vehicle")?,
                climate_id: r.u32_or("climate_id", 0)? as i32,
                save_indun: r.bool_str("save_indun")?,
                force_up_action: r.bool_str("force_up_action")?,
                parentable: r.bool_str("parentable")?,
                childable: r.bool_str("childable")?,
                faction_id: r.u32("faction_id")?,
                growth_time: r.i32_or("growth_time", 0)?,
                despawn_on_collision: r.bool_str("despawn_on_collision")?,
                no_collision: r.bool_str("no_collision")?,
                restrict_zone_id: r.u32_or("restrict_zone_id", 0)?,
                func_groups: Vec::new(),
                total_doodad_growth_time: 0,
                coffer_capacity: (coffer_capacity > 0).then_some(coffer_capacity),
            };
            rows.push((template, group_id));
            Ok(())
        })?;
        for (mut template, group_id) in rows {
            let Some(group) = self.doodad_groups.get(&group_id) else {
                return Err(data_err(format!(
                    "Invalid doodad group {group_id} for doodad {}",
                    template.id
                )));
            };
            template.group = *group;
            let id = template.id;
            if self.templates.insert(id, template).is_some() {
                return Err(data_err(format!(
                    "doodad: duplicate doodad_almighties id {id}: An item with the same key has already been added"
                )));
            }
            self.template_order.push(id);
        }

        // Bind FuncGroups to Template
        for group in &self.all_func_groups {
            if let Some(template) = self.templates.get_mut(&group.almighty) {
                template.func_groups.push(group.clone());
            }
        }
        Ok(())
    }

    /// `IsCofferTemplate` of every template id that has a coffer: walks `_allFuncGroups` in
    /// order and keeps the first `DoodadFuncCoffer` capacity found per template, exactly what
    /// `IsCofferTemplate(id)` returns for each id (-1 / absent otherwise).
    fn coffer_capacities(&self) -> HashMap<u32, i32> {
        let mut out = HashMap::new();
        for group in &self.all_func_groups {
            if out.contains_key(&group.almighty) {
                continue;
            }
            if let Some(capacity) = self.first_coffer_capacity_of_group(group.id) {
                out.insert(group.almighty, capacity);
            }
        }
        out
    }

    fn first_coffer_capacity_of_group(&self, group_id: u32) -> Option<i32> {
        let funcs = self.funcs_by_groups.get(&group_id)?;
        funcs.iter().find_map(|func| {
            match self.phase_func_template(func.func_id, &func.func_type) {
                Some(FuncTemplate::Coffer(c)) => Some(c.capacity),
                _ => None,
            }
        })
    }

    /// `IsCofferTemplate(templateId)`: the coffer capacity of the first `DoodadFuncCoffer`
    /// reachable from the template's func groups (through `doodad_funcs`), else -1.
    pub fn is_coffer_template(&self, template_id: u32) -> i32 {
        if template_id == 0 {
            return -1;
        }
        for group in &self.all_func_groups {
            if group.almighty != template_id {
                continue;
            }
            if let Some(capacity) = self.first_coffer_capacity_of_group(group.id) {
                return capacity;
            }
        }
        -1
    }

    /// `CreateTemplateCaches`: `TotalDoodadGrowthTime` of every template.
    pub fn create_template_caches(&mut self) {
        let mut totals = Vec::with_capacity(self.templates.len());
        for template in self.templates.values() {
            let mut total: i32 = 0;
            for group in &template.func_groups {
                for func in self.funcs_for_group(group.id) {
                    if let Some(FuncTemplate::Growth(g)) =
                        self.phase_func_template(func.func_id, &func.func_type)
                    {
                        total = total.wrapping_add(g.delay);
                    }
                }
            }
            if total <= 0 {
                total = template.growth_time;
            }
            totals.push((template.id, total));
        }
        for (id, total) in totals {
            if let Some(t) = self.templates.get_mut(&id) {
                t.total_doodad_growth_time = total;
            }
        }
    }

    /// `DoodadManager.Exist`.
    pub fn exist(&self, template_id: u32) -> bool {
        self.templates.contains_key(&template_id)
    }

    /// `DoodadManager.GetTemplate`.
    pub fn template(&self, template_id: u32) -> Option<&DoodadTemplate> {
        self.templates.get(&template_id)
    }

    /// `GetFunc(funcId)` (by `FuncKey`, the `doodad_funcs` row id).
    pub fn func(&self, func_key: u32) -> Option<&DoodadFunc> {
        self.funcs_by_id.get(&func_key)
    }

    /// `GetFunc(funcGroupId, skillId)`: the first func of the group whose `SkillId` is the
    /// skill, or whose `DoodadFuncFakeUse.FakeSkillId` / `DoodadFuncUse.SkillId` (> 0) is the
    /// skill; else the first func without skill and with `NextPhase != -1`; else the first func
    /// without skill.
    pub fn func_for_skill(&self, func_group_id: u32, skill_id: u32) -> Option<&DoodadFunc> {
        let funcs = self.funcs_by_groups.get(&func_group_id)?;
        for func in funcs {
            if func.skill_id == skill_id {
                return Some(func);
            }
            match self.func_template(func.func_id, &func.func_type) {
                // Special handler for fake use skill id
                Some(FuncTemplate::FakeUse(t))
                    if t.fake_skill_id > 0 && t.fake_skill_id == skill_id =>
                {
                    return Some(func)
                }
                // Special handler for use (func) skill id
                Some(FuncTemplate::Use(t)) if t.skill_id > 0 && t.skill_id == skill_id => {
                    return Some(func)
                }
                _ => {}
            }
        }
        // First we skip functions with NextPhase = -1
        if let Some(func) = funcs.iter().find(|f| f.skill_id == 0 && f.next_phase != -1) {
            return Some(func);
        }
        // Then we search with NextPhase = -1
        funcs.iter().find(|f| f.skill_id == 0)
    }

    /// `GetFuncsForGroup` (empty when the group has no funcs).
    pub fn funcs_for_group(&self, func_group_id: u32) -> &[DoodadFunc] {
        self.funcs_by_groups
            .get(&func_group_id)
            .map_or(&[], Vec::as_slice)
    }

    /// `GetDoodadFuncs` (same as [`Self::funcs_for_group`]).
    pub fn doodad_funcs(&self, func_group_id: u32) -> &[DoodadFunc] {
        self.funcs_for_group(func_group_id)
    }

    /// `GetPhaseFunc` / `GetDoodadPhaseFuncs` (empty when the group has no phase funcs).
    pub fn phase_funcs(&self, func_group_id: u32) -> &[DoodadPhaseFunc] {
        self.phase_funcs
            .get(&func_group_id)
            .map_or(&[], Vec::as_slice)
    }

    /// `GetFuncTemplate(funcId, funcType)`: only classes deriving from `DoodadFuncTemplate`.
    pub fn func_template(&self, func_id: u32, func_type: &str) -> Option<&FuncTemplate> {
        let func_type = if func_type == "DoodadFuncTreeByproductsCollect" {
            "DoodadFuncTreeByProductsCollect"
        } else {
            func_type
        };
        self.func_templates.get(func_type)?.get(&func_id)
    }

    /// `GetPhaseFuncTemplate(funcId, funcType)`: only classes deriving from
    /// `DoodadPhaseFuncTemplate`.
    pub fn phase_func_template(&self, func_id: u32, func_type: &str) -> Option<&FuncTemplate> {
        self.phase_func_templates.get(func_type)?.get(&func_id)
    }

    /// `GetDoodadFuncGroups(templateId)` (empty for an unknown template).
    pub fn doodad_func_groups(&self, template_id: u32) -> &[DoodadFuncGroups] {
        self.templates
            .get(&template_id)
            .map_or(&[], |t| t.func_groups.as_slice())
    }

    /// `GetDoodadFuncGroupsId(templateId)`.
    pub fn func_groups_ids(&self, template_id: u32) -> Vec<u32> {
        self.doodad_func_groups(template_id)
            .iter()
            .map(|g| g.id)
            .collect()
    }

    /// `_allFuncGroups[id]`.
    pub fn func_group(&self, func_group_id: u32) -> Option<&DoodadFuncGroups> {
        self.all_func_groups_index
            .get(&func_group_id)
            .map(|&i| &self.all_func_groups[i])
    }

    /// `GetDoodadFuncConsumeChangerItemList(doodadFuncConsumeChangerId)`: item ids in
    /// insertion order.
    pub fn consume_changer_items(&self, doodad_func_consume_changer_id: u32) -> Vec<u32> {
        self.consume_changer_items
            .iter()
            .filter(|e| e.doodad_func_consume_changer_id == doodad_func_consume_changer_id)
            .map(|e| e.item_id)
            .collect()
    }

    /// `GetTreasureChestTemplateIds`: templates of groups 55..=59, in template order.
    pub fn treasure_chest_template_ids(&self) -> Vec<u32> {
        self.template_order
            .iter()
            .filter(|id| {
                self.templates
                    .get(id)
                    .is_some_and(|t| (55..=59).contains(&t.group_id))
            })
            .copied()
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// `(table, class)` of every `doodad_func_*` template table `DoodadManager.Load` reads.
    const TEMPLATE_TABLES: [(&str, &str); 119] = [
        ("doodad_func_animates", "DoodadFuncAnimate"),
        ("doodad_func_area_triggers", "DoodadFuncAreaTrigger"),
        ("doodad_func_attachments", "DoodadFuncAttachment"),
        ("doodad_func_bindings", "DoodadFuncBinding"),
        ("doodad_func_bubbles", "DoodadFuncBubble"),
        ("doodad_func_buffs", "DoodadFuncBuff"),
        ("doodad_func_butchers", "DoodadFuncButcher"),
        ("doodad_func_buy_fish_items", "DoodadFuncBuyFishItem"),
        ("doodad_func_buy_fish_models", "DoodadFuncBuyFishModel"),
        ("doodad_func_buy_fishes", "DoodadFuncBuyFish"),
        ("doodad_func_catches", "DoodadFuncCatch"),
        ("doodad_func_cereal_harvests", "DoodadFuncCerealHarvest"),
        (
            "doodad_func_cleanup_logic_links",
            "DoodadFuncCleanupLogicLink",
        ),
        ("doodad_func_climate_reacts", "DoodadFuncClimateReact"),
        ("doodad_func_climbs", "DoodadFuncClimb"),
        ("doodad_func_clouts", "DoodadFuncClout"),
        ("doodad_func_coffer_perms", "DoodadFuncCofferPerm"),
        ("doodad_func_coffers", "DoodadFuncCoffer"),
        ("doodad_func_conditional_uses", "DoodadFuncConditionalUse"),
        (
            "doodad_func_consume_changer_model_items",
            "DoodadFuncConsumeChangerModelItem",
        ),
        (
            "doodad_func_consume_changer_models",
            "DoodadFuncConsumeChangerModel",
        ),
        ("doodad_func_consume_changers", "DoodadFuncConsumeChanger"),
        ("doodad_func_consume_items", "DoodadFuncConsumeItem"),
        (
            "doodad_func_convert_fish_items",
            "DoodadFuncConvertFishItem",
        ),
        ("doodad_func_convert_fishes", "DoodadFuncConvertFish"),
        ("doodad_func_craft_acts", "DoodadFuncCraftAct"),
        ("doodad_func_craft_cancels", "DoodadFuncCraftCancel"),
        ("doodad_func_craft_directs", "DoodadFuncCraftDirect"),
        ("doodad_func_craft_get_items", "DoodadFuncCraftGetItem"),
        ("doodad_func_craft_infos", "DoodadFuncCraftInfo"),
        ("doodad_func_craft_packs", "DoodadFuncCraftPack"),
        (
            "doodad_func_craft_start_crafts",
            "DoodadFuncCraftStartCraft",
        ),
        ("doodad_func_craft_starts", "DoodadFuncCraftStart"),
        ("doodad_func_crop_harvests", "DoodadFuncCropHarvest"),
        ("doodad_func_crystal_collects", "DoodadFuncCrystalCollect"),
        ("doodad_func_cutdownings", "DoodadFuncCutdowning"),
        ("doodad_func_cutdowns", "DoodadFuncCutdown"),
        ("doodad_func_dairy_collects", "DoodadFuncDairyCollect"),
        ("doodad_func_declare_sieges", "DoodadFuncDeclareSiege"),
        ("doodad_func_digs", "DoodadFuncDig"),
        ("doodad_func_dig_terrains", "DoodadFuncDigTerrain"),
        (
            "doodad_func_dyeingredient_collects",
            "DoodadFuncDyeingredientCollect",
        ),
        ("doodad_func_enter_instances", "DoodadFuncEnterInstance"),
        (
            "doodad_func_enter_sys_instances",
            "DoodadFuncEnterSysInstance",
        ),
        (
            "doodad_func_evidence_item_loots",
            "DoodadFuncEvidenceItemLoot",
        ),
        ("doodad_func_exchanges", "DoodadFuncExchange"),
        ("doodad_func_exit_induns", "DoodadFuncExitIndun"),
        ("doodad_func_fake_uses", "DoodadFuncFakeUse"),
        ("doodad_func_feeds", "DoodadFuncFeed"),
        ("doodad_func_fiber_collects", "DoodadFuncFiberCollect"),
        ("doodad_func_finals", "DoodadFuncFinal"),
        ("doodad_func_fish_schools", "DoodadFuncFishSchool"),
        ("doodad_func_fruit_picks", "DoodadFuncFruitPick"),
        ("doodad_func_gass_extracts", "DoodadFuncGassExtract"),
        ("doodad_func_growths", "DoodadFuncGrowth"),
        ("doodad_func_harvests", "DoodadFuncHarvest"),
        ("doodad_func_house_farms", "DoodadFuncHouseFarm"),
        ("doodad_func_housing_areas", "DoodadFuncHousingArea"),
        ("doodad_func_hungers", "DoodadFuncHunger"),
        ("doodad_func_insert_counters", "DoodadFuncInsertCounter"),
        ("doodad_func_logics", "DoodadFuncLogic"),
        (
            "doodad_func_logic_family_providers",
            "DoodadFuncLogicFamilyProvider",
        ),
        (
            "doodad_func_logic_family_subscribers",
            "DoodadFuncLogicFamilySubscriber",
        ),
        ("doodad_func_loot_items", "DoodadFuncLootItem"),
        ("doodad_func_loot_packs", "DoodadFuncLootPack"),
        (
            "doodad_func_machine_parts_collects",
            "DoodadFuncMachinePartsCollect",
        ),
        (
            "doodad_func_medicalingredient_mines",
            "DoodadFuncMedicalingredientMine",
        ),
        ("doodad_func_mows", "DoodadFuncMow"),
        (
            "doodad_func_navi_mark_pos_to_maps",
            "DoodadFuncNaviMarkPosToMap",
        ),
        ("doodad_func_navi_namings", "DoodadFuncNaviNaming"),
        ("doodad_func_navi_open_bounties", "DoodadFuncNaviOpenBounty"),
        (
            "doodad_func_navi_open_mailboxes",
            "DoodadFuncNaviOpenMailbox",
        ),
        ("doodad_func_navi_open_portals", "DoodadFuncNaviOpenPortal"),
        (
            "doodad_func_navi_remove_timers",
            "DoodadFuncNaviRemoveTimer",
        ),
        ("doodad_func_navi_removes", "DoodadFuncNaviRemove"),
        ("doodad_func_navi_teleports", "DoodadFuncNaviTeleport"),
        ("doodad_func_open_farm_infos", "DoodadFuncOpenFarmInfo"),
        ("doodad_func_open_papers", "DoodadFuncOpenPaper"),
        ("doodad_func_ore_mines", "DoodadFuncOreMine"),
        ("doodad_func_parent_infos", "DoodadFuncParentInfo"),
        ("doodad_func_parrots", "DoodadFuncParrot"),
        ("doodad_func_plant_collects", "DoodadFuncPlantCollect"),
        ("doodad_func_play_flow_graphs", "DoodadFuncPlayFlowGraph"),
        ("doodad_func_pulse_triggers", "DoodadFuncPulseTrigger"),
        ("doodad_func_pulses", "DoodadFuncPulse"),
        ("doodad_func_purchases", "DoodadFuncPurchase"),
        ("doodad_func_puzzle_ins", "DoodadFuncPuzzleIn"),
        ("doodad_func_puzzle_outs", "DoodadFuncPuzzleOut"),
        ("doodad_func_puzzle_rolls", "DoodadFuncPuzzleRoll"),
        ("doodad_func_quests", "DoodadFuncQuest"),
        ("doodad_func_ratio_changes", "DoodadFuncRatioChange"),
        ("doodad_func_ratio_respawns", "DoodadFuncRatioRespawn"),
        ("doodad_func_recover_items", "DoodadFuncRecoverItem"),
        ("doodad_func_remove_instances", "DoodadFuncRemoveInstance"),
        ("doodad_func_remove_items", "DoodadFuncRemoveItem"),
        ("doodad_func_renew_items", "DoodadFuncRenewItem"),
        ("doodad_func_require_items", "DoodadFuncRequireItem"),
        ("doodad_func_require_quests", "DoodadFuncRequireQuest"),
        ("doodad_func_respawns", "DoodadFuncRespawn"),
        ("doodad_func_rock_mines", "DoodadFuncRockMine"),
        ("doodad_func_seed_collects", "DoodadFuncSeedCollect"),
        ("doodad_func_shears", "DoodadFuncShear"),
        ("doodad_func_siege_periods", "DoodadFuncSiegePeriod"),
        ("doodad_func_signs", "DoodadFuncSign"),
        ("doodad_func_skill_hits", "DoodadFuncSkillHit"),
        ("doodad_func_skin_offs", "DoodadFuncSkinOff"),
        ("doodad_func_soil_collects", "DoodadFuncSoilCollect"),
        ("doodad_func_spawn_gimmicks", "DoodadFuncSpawnGimmick"),
        ("doodad_func_spawn_mgmts", "DoodadFuncSpawnMgmt"),
        ("doodad_func_spice_collects", "DoodadFuncSpiceCollect"),
        ("doodad_func_stamp_makers", "DoodadFuncStampMaker"),
        ("doodad_func_store_uis", "DoodadFuncStoreUi"),
        ("doodad_func_timers", "DoodadFuncTimer"),
        ("doodad_func_tods", "DoodadFuncTod"),
        (
            "doodad_func_tree_byproducts_collects",
            "DoodadFuncTreeByProductsCollect",
        ),
        ("doodad_func_ucc_imprints", "DoodadFuncUccImprint"),
        ("doodad_func_uses", "DoodadFuncUse"),
        ("doodad_func_water_volumes", "DoodadFuncWaterVolume"),
        ("doodad_func_zone_reacts", "DoodadFuncZoneReact"),
    ];

    fn func(key: u32, group: u32, func_id: u32, ty: &str, next: i32, skill: u32) -> DoodadFunc {
        DoodadFunc {
            group_id: group,
            func_id,
            func_key: key,
            func_type: ty.to_string(),
            next_phase: next,
            skill_id: skill,
            ..Default::default()
        }
    }

    fn with_classes() -> DoodadData {
        let mut d = DoodadData::default();
        for (class, phase) in FUNC_TEMPLATE_CLASSES {
            if phase {
                d.phase_func_templates.insert(class, HashMap::new());
            } else {
                d.func_templates.insert(class, HashMap::new());
            }
        }
        d
    }

    fn add(d: &mut DoodadData, t: FuncTemplate) {
        let dict = if t.is_phase_func() {
            &mut d.phase_func_templates
        } else {
            &mut d.func_templates
        };
        dict.get_mut(t.func_type()).unwrap().insert(t.id(), t);
    }

    #[test]
    fn func_template_classes_are_complete() {
        assert_eq!(FUNC_TEMPLATE_CLASSES.len(), 121);
        assert_eq!(FUNC_TEMPLATE_CLASSES.iter().filter(|c| c.1).count(), 42);
        let t = FuncTemplate::Tod(DoodadFuncTod {
            id: 7,
            ..Default::default()
        });
        assert_eq!(
            (t.func_type(), t.id(), t.is_phase_func()),
            ("DoodadFuncTod", 7, true)
        );
        let t = FuncTemplate::BuyFishItem(DoodadFuncBuyFishItem::default());
        assert!(t.is_phase_func());
        let t = FuncTemplate::FakeUse(DoodadFuncFakeUse::default());
        assert!(!t.is_phase_func());
        assert_eq!(t.func_type(), "DoodadFuncFakeUse");
    }

    #[test]
    fn get_func_by_group_and_skill() {
        let mut d = with_classes();
        add(
            &mut d,
            FuncTemplate::FakeUse(DoodadFuncFakeUse {
                id: 1,
                skill_id: 0,
                fake_skill_id: 500,
                target_parent: false,
            }),
        );
        add(
            &mut d,
            FuncTemplate::Use(DoodadFuncUse {
                id: 2,
                skill_id: 600,
            }),
        );
        add(
            &mut d,
            FuncTemplate::Coffer(DoodadFuncCoffer {
                id: 3,
                capacity: 10,
            }),
        );
        let funcs = vec![
            func(10, 1, 3, "DoodadFuncCoffer", -1, 0),
            func(11, 1, 1, "DoodadFuncFakeUse", 5, 0),
            func(12, 1, 2, "DoodadFuncUse", -1, 0),
            func(13, 1, 9, "DoodadFuncLootItem", 7, 42),
        ];
        d.funcs_by_groups.insert(1, funcs);
        let key = |d: &DoodadData, skill| d.func_for_skill(1, skill).map(|f| f.func_key);
        // SkillId match first, in list order (func 10 has SkillId 0 == skill 0).
        assert_eq!(key(&d, 0), Some(10));
        assert_eq!(key(&d, 42), Some(13));
        assert_eq!(key(&d, 500), Some(11));
        assert_eq!(key(&d, 600), Some(12));
        // Fallback: first SkillId == 0 with NextPhase != -1, then first SkillId == 0.
        assert_eq!(key(&d, 777), Some(11));
        d.funcs_by_groups.get_mut(&1).unwrap()[1].next_phase = -1;
        assert_eq!(key(&d, 777), Some(10));
        // No skill-less func.
        d.funcs_by_groups.insert(2, vec![func(20, 2, 9, "X", 1, 5)]);
        assert_eq!(d.func_for_skill(2, 6), None);
        assert_eq!(d.func_for_skill(3, 0), None);
        // FakeSkillId 0 never matches through the template.
        add(
            &mut d,
            FuncTemplate::FakeUse(DoodadFuncFakeUse {
                id: 4,
                ..Default::default()
            }),
        );
        d.funcs_by_groups
            .insert(4, vec![func(30, 4, 4, "DoodadFuncFakeUse", 1, 9)]);
        assert_eq!(d.func_for_skill(4, 0), None);
        // A phase template class in doodad_funcs is not found by GetFuncTemplate.
        add(
            &mut d,
            FuncTemplate::DeclareSiege(DoodadFuncDeclareSiege { id: 5 }),
        );
        assert!(d.func_template(5, "DoodadFuncDeclareSiege").is_none());
        assert!(d.phase_func_template(5, "DoodadFuncDeclareSiege").is_some());
    }

    #[test]
    fn template_caches_and_coffers() {
        let mut d = with_classes();
        add(
            &mut d,
            FuncTemplate::Growth(DoodadFuncGrowth {
                id: 1,
                delay: 1000,
                ..Default::default()
            }),
        );
        add(
            &mut d,
            FuncTemplate::Growth(DoodadFuncGrowth {
                id: 2,
                delay: 500,
                ..Default::default()
            }),
        );
        add(
            &mut d,
            FuncTemplate::Coffer(DoodadFuncCoffer {
                id: 1,
                capacity: 50,
            }),
        );
        add(
            &mut d,
            FuncTemplate::Coffer(DoodadFuncCoffer { id: 2, capacity: 0 }),
        );
        d.funcs_by_groups
            .insert(100, vec![func(1, 100, 1, "DoodadFuncGrowth", -1, 0)]);
        d.funcs_by_groups
            .insert(101, vec![func(2, 101, 2, "DoodadFuncGrowth", -1, 0)]);
        d.funcs_by_groups
            .insert(200, vec![func(3, 200, 2, "DoodadFuncCoffer", -1, 0)]);
        d.funcs_by_groups
            .insert(201, vec![func(4, 201, 1, "DoodadFuncCoffer", -1, 0)]);
        let g = |id, almighty| DoodadFuncGroups {
            id,
            almighty,
            ..Default::default()
        };
        d.all_func_groups = vec![g(100, 1), g(101, 1), g(200, 2), g(201, 2), g(300, 3)];
        for t in [1, 2, 3] {
            let func_groups = d
                .all_func_groups
                .iter()
                .filter(|x| x.almighty == t)
                .cloned()
                .collect();
            d.templates.insert(
                t,
                DoodadTemplate {
                    id: t,
                    growth_time: 77,
                    func_groups,
                    ..Default::default()
                },
            );
        }
        d.create_template_caches();
        assert_eq!(d.templates[&1].total_doodad_growth_time, 1500);
        assert_eq!(d.templates[&2].total_doodad_growth_time, 77);
        // First coffer in group order wins, even with capacity 0.
        assert_eq!(d.is_coffer_template(2), 0);
        assert_eq!(d.is_coffer_template(1), -1);
        assert_eq!(d.is_coffer_template(0), -1);
        let caps = d.coffer_capacities();
        for t in [1, 2, 3] {
            assert_eq!(caps.get(&t).copied().unwrap_or(-1), d.is_coffer_template(t));
        }
    }

    #[test]
    fn tod_hours() {
        assert_eq!(tod_as_hours(1, 2000), 20.0);
        assert_eq!(tod_as_hours(1, 930), 9.5);
        // 24000 -> 2400 -> 240: 02:40
        assert_eq!(tod_as_hours(1, 24000), 2.0 + 40.0 / 60.0);
        assert_eq!(tod_as_hours(1, 175), 1.25); // 01:75, minutes 75 % 60 = 15
    }

    #[test]
    fn partial_load_from_sqlite() {
        let c = Connection::open_in_memory().unwrap();
        c.execute_batch(
            "CREATE TABLE doodad_func_groups (id INT, model TEXT, doodad_almighty_id INT, doodad_func_group_kind_id INT, sound_id INT);
             INSERT INTO doodad_func_groups VALUES (11, 'm', 2, 2, NULL), (10, 'm', 2, 1, 5), (12, 'm', 1, 1, NULL), (10, 'dup', 1, 1, NULL);
             CREATE TABLE doodad_funcs (id INT, doodad_func_group_id INT, actual_func_id INT, actual_func_type TEXT, next_phase INT, sound_id INT, func_skill_id INT, perm_id INT, act_count INT);
             INSERT INTO doodad_funcs VALUES (2, 10, 9, 'DoodadFuncFakeUse', NULL, NULL, NULL, 1, NULL), (1, 10, 3, 'DoodadFuncCoffer', 11, 4, 77, 0, 2);
             CREATE TABLE doodad_func_coffers (id INT, capacity INT);
             INSERT INTO doodad_func_coffers VALUES (3, 150);
             CREATE TABLE doodad_func_fake_uses (id INT, skill_id INT, fake_skill_id INT, target_parent TEXT);
             INSERT INTO doodad_func_fake_uses VALUES (9, NULL, 13162, 't');
             CREATE TABLE doodad_groups (id INT, guard_on_field_time INT, is_export TEXT, removed_by_house TEXT);
             INSERT INTO doodad_groups VALUES (24, 0, 't', 1), (24, 5, 'f', 0);
             CREATE TABLE doodad_almighties (id INT, once_one_man TEXT, once_one_interaction TEXT, mgmt_spawn TEXT, percent INT, min_time INT, max_time INT, model_kind_id INT, use_creator_faction TEXT, force_tod_top_priority TEXT, milestone_id INT, group_id INT, use_target_decal TEXT, use_target_silhouette TEXT, use_target_highlight TEXT, target_decal_size REAL, sim_radius INT, collide_ship TEXT, collide_vehicle TEXT, climate_id INT, save_indun TEXT, force_up_action TEXT, parentable TEXT, childable TEXT, faction_id INT, growth_time INT, despawn_on_collision TEXT, no_collision TEXT, restrict_zone_id INT);
             INSERT INTO doodad_almighties VALUES (2, 't', 'f', 'f', NULL, NULL, NULL, 1, 'f', 'f', NULL, 24, 't', 'f', 'f', NULL, NULL, 'f', 'f', NULL, 'f', 'f', 'f', 'f', 0, 3000, 'f', 'f', NULL),
                                                  (1, 'f', 'f', 'f', 5, 1, 2, 1, 'f', 'f', NULL, 24, 't', 'f', 'f', 1.5, 3, 'f', 'f', 2, 'f', 'f', 'f', 'f', 101, NULL, 'f', 'f', 7);",
        )
        .unwrap();
        let mut d = with_classes();
        d.load_func_groups_discarded(&c).unwrap();
        d.load_funcs(&c).unwrap();
        load_templates(
            &c,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_coffers",
            |r| {
                Ok(FuncTemplate::Coffer(DoodadFuncCoffer {
                    id: r.u32("id")?,
                    capacity: r.i32("capacity")?,
                }))
            },
        )
        .unwrap();
        load_templates(
            &c,
            &mut d.func_templates,
            "SELECT * FROM doodad_func_fake_uses",
            |r| {
                Ok(FuncTemplate::FakeUse(DoodadFuncFakeUse {
                    id: r.u32("id")?,
                    skill_id: r.u32_or("skill_id", 0)?,
                    fake_skill_id: r.u32_or("fake_skill_id", 0)?,
                    target_parent: r.bool_str("target_parent")?,
                }))
            },
        )
        .unwrap();
        // Duplicate template id: Dictionary.Add throws.
        assert!(load_templates(
            &c,
            &mut d.phase_func_templates,
            "SELECT * FROM doodad_func_coffers",
            |r| {
                Ok(FuncTemplate::Coffer(DoodadFuncCoffer {
                    id: r.u32("id")?,
                    capacity: 0,
                }))
            }
        )
        .is_err());
        d.load_all_func_groups(&c).unwrap();
        d.load_doodad_templates(&c).unwrap();
        d.create_template_caches();

        // ORDER BY doodad_func_group_id, actual_func_id.
        let keys: Vec<u32> = d.funcs_for_group(10).iter().map(|f| f.func_key).collect();
        assert_eq!(keys, [1, 2]);
        let f = d.func(1).unwrap();
        assert_eq!(
            (f.next_phase, f.sound_id, f.skill_id, f.count),
            (11, 4, 77, 2)
        );
        let f = d.func(2).unwrap();
        assert_eq!(
            (f.next_phase, f.sound_id, f.skill_id, f.count),
            (-1, 0, 0, 0)
        );
        // Func groups: row order, duplicate id skipped, model never kept.
        let ids: Vec<u32> = d.all_func_groups.iter().map(|g| g.id).collect();
        assert_eq!(ids, [11, 10, 12]);
        assert_eq!(d.func_groups_ids(2), [11, 10]);
        assert!(d.doodad_func_groups(2).iter().all(|g| g.model.is_none()));
        assert_eq!(d.func_group(10).unwrap().sound_id, 5);
        // Coffer template through doodad_funcs -> phase template.
        let t2 = d.template(2).unwrap();
        assert_eq!(t2.coffer_capacity, Some(150));
        assert!(t2.once_one_man && t2.use_target_decal && !t2.once_one_interaction);
        // doodad_groups: TryAdd keeps the first row; GetBoolean(col) on 't' is false.
        assert_eq!(
            t2.group,
            DoodadGroups {
                id: 24,
                guard_on_field_time: 0,
                is_export: false,
                removed_by_house: true
            }
        );
        assert_eq!(t2.total_doodad_growth_time, 3000);
        let t1 = d.template(1).unwrap();
        assert_eq!(t1.coffer_capacity, None);
        assert_eq!(
            (
                t1.percent,
                t1.min_time,
                t1.max_time,
                t1.sim_radius,
                t1.climate_id
            ),
            (5, 1, 2, 3, 2)
        );
        assert_eq!(
            (t1.faction_id, t1.growth_time, t1.restrict_zone_id),
            (101, 0, 7)
        );
        assert_eq!(t1.target_decal_size, 1.5);
        assert_eq!(d.template_order, [2, 1]);
        assert_eq!(d.func_for_skill(10, 13162).map(|f| f.func_key), Some(2));
        assert_eq!(d.func_for_skill(10, 77).map(|f| f.func_key), Some(1));
        // Missing doodad group: GameException.
        c.execute_batch("UPDATE doodad_almighties SET group_id = 99 WHERE id = 1")
            .unwrap();
        let mut d2 = with_classes();
        assert!(d2.load_doodad_templates(&c).is_err());
    }

    fn count(c: &Connection, table: &str) -> usize {
        c.query_row(&format!("SELECT COUNT(*) FROM {table}"), [], |r| {
            r.get::<_, i64>(0)
        })
        .unwrap() as usize
    }

    #[test]
    #[ignore]
    fn real_compact_db_doodads() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let c =
            Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let start = std::time::Instant::now();
        let d = DoodadData::load_from(&c).unwrap();
        eprintln!("doodad data loaded in {:?}", start.elapsed());

        for (table, class) in TEMPLATE_TABLES {
            let dict = if d.func_templates.contains_key(class) {
                &d.func_templates
            } else {
                &d.phase_func_templates
            };
            assert_eq!(dict[class].len(), count(&c, table), "{table}");
        }
        assert!(d.func_templates["DoodadFuncAuctionUi"].is_empty());
        assert!(d.func_templates["DoodadFuncBankUi"].is_empty());
        assert_eq!(d.templates.len(), count(&c, "doodad_almighties"));
        assert_eq!(d.all_func_groups.len(), count(&c, "doodad_func_groups"));
        assert_eq!(d.funcs_by_id.len(), count(&c, "doodad_funcs"));
        let n_funcs: usize = d.funcs_by_groups.values().map(Vec::len).sum();
        assert_eq!(n_funcs, count(&c, "doodad_funcs"));
        let n_phase: usize = d.phase_funcs.values().map(Vec::len).sum();
        assert_eq!(n_phase, count(&c, "doodad_phase_funcs"));
        assert_eq!(d.doodad_groups.len(), count(&c, "doodad_groups"));
        assert_eq!(
            d.consume_changer_items.len(),
            count(&c, "doodad_func_consume_changer_items")
        );
        let effects: usize = d.phase_func_templates["DoodadFuncClout"]
            .values()
            .map(|t| match t {
                FuncTemplate::Clout(c) => c.effects.len(),
                _ => unreachable!(),
            })
            .sum();
        assert_eq!(effects, count(&c, "doodad_func_clout_effects"));
        eprintln!(
            "templates {}, func groups {}, funcs {}, phase funcs {}, func templates {}, phase func templates {}, clout effects {}",
            d.templates.len(),
            d.all_func_groups.len(),
            d.funcs_by_id.len(),
            n_phase,
            d.func_templates.values().map(HashMap::len).sum::<usize>(),
            d.phase_func_templates.values().map(HashMap::len).sum::<usize>(),
            effects,
        );

        // Template 2336 (water the flowerbed), see the comment at the top of Doodad.cs.
        let t = d.template(2336).unwrap();
        assert_eq!(t.group_id, 24);
        assert_eq!(d.func_groups_ids(2336), [4651, 4652, 5136, 5137]);
        let start_group = d.func_group(4651).unwrap();
        assert_eq!(start_group.group_kind_id, doodad_func_group_kind::START);
        let pf = d.phase_funcs(4651);
        assert_eq!(pf.len(), 1);
        assert_eq!(
            (pf[0].func_id, pf[0].func_type.as_str()),
            (250, "DoodadFuncTod")
        );
        match d.phase_func_template(250, "DoodadFuncTod") {
            Some(FuncTemplate::Tod(tod)) => {
                assert_eq!(
                    (tod.tod, tod.next_phase, tod.tod_as_hours),
                    (2000, 5136, 20.0)
                );
            }
            other => panic!("{other:?}"),
        }
        assert!(d.func_template(250, "DoodadFuncTod").is_none());
        let funcs = d.funcs_for_group(4651);
        assert_eq!(funcs.len(), 1);
        let f = &funcs[0];
        assert_eq!(
            (
                f.func_key,
                f.func_id,
                f.func_type.as_str(),
                f.next_phase,
                f.skill_id
            ),
            (3914, 543, "DoodadFuncFakeUse", 4652, 0)
        );
        match d.func_template(543, "DoodadFuncFakeUse") {
            Some(FuncTemplate::FakeUse(u)) => {
                assert_eq!(
                    (u.skill_id, u.fake_skill_id, u.target_parent),
                    (0, 13162, false)
                );
            }
            other => panic!("{other:?}"),
        }
        assert_eq!(
            d.func_for_skill(4651, 13162).map(|f| f.func_key),
            Some(3914)
        );
        assert_eq!(d.func_for_skill(4651, 1).map(|f| f.func_key), Some(3914));
        let p = d.phase_funcs(4652).first().expect("phase func of 4652");
        assert_eq!((p.func_id, p.func_type.as_str()), (822, "DoodadFuncTimer"));
        // "DoodadFuncTreeByproductsCollect" (DB spelling) misses the C# class name.
        assert!(d
            .func_templates
            .contains_key("DoodadFuncTreeByProductsCollect"));
        assert!(!d
            .func_templates
            .contains_key("DoodadFuncTreeByproductsCollect"));

        // Coffers: templates with a DoodadFuncCoffer reachable through doodad_funcs.
        let coffers: Vec<_> = d.templates.values().filter(|t| t.is_coffer()).collect();
        assert!(!coffers.is_empty());
        for t in &coffers {
            assert_eq!(Some(d.is_coffer_template(t.id)), t.coffer_capacity);
        }
        let mut list: Vec<_> = coffers.iter().map(|t| (t.id, t.coffer_capacity)).collect();
        list.sort_unstable();
        eprintln!("coffer templates: {list:?}");
        assert_eq!(d.treasure_chest_template_ids().len(), 12);
        // No DoodadFuncGrowth in doodad_funcs: TotalDoodadGrowthTime is always GrowthTime.
        assert!(d
            .templates
            .values()
            .all(|t| t.total_doodad_growth_time == t.growth_time));
    }
}

#[cfg(test)]
#[path = "manager_tests.rs"]
mod manager_tests;

#[cfg(test)]
#[path = "group_loading_tests.rs"]
mod group_loading_tests;
