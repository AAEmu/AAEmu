//! The item side of the item skill effects (`Models/Game/Skills/Effects/SpecialEffects/*` and the
//! item effects of `Models/Game/Skills/Effects`), as plain functions the skills subsystem calls
//! from its effect dispatch with the resolved inputs:
//!
//! * `caster is Character` -> the caller passes the [`PlayerRef`] (or does not call when the
//!   caster is not a character; [`item_conversion`] / [`cleanup_look_convert`] then set
//!   `skill.Cancelled` as the C# does);
//! * `casterObj is SkillItem` -> `Some(SkillItem { item_id, item_template_id })`, else `None`;
//! * `targetObj is SkillCastItemTarget` -> `Some(id)` (or `Some((id, objId))`), else `None`;
//! * `skillObject is SkillObjectItemGradeEnchantingSupport` -> `Some(SupportItemId)`;
//! * `Random.Shared` -> a [`LootRng`] (`SharedRandom` in production, `ScriptedRng` in tests).
//!
//! Functions that return [`Outcome`] tell the caller whether the C# set `skill.Cancelled`.
//! The C# stubs (`// TODO ...` + a debug log) are mirrored by [`stub_effect`].

use std::collections::HashMap;
use std::sync::Arc;

use rusqlite::Connection;

use crate::data::formulas::FormulaKind;
use crate::data::items::{FullGradeTemplate, ItemGradeEnchantingSupport, TemplateKind};
use crate::data::sqlite::for_each_row;
use crate::managers::chat::chat_type;
use crate::models::container::{equip_slot, slot_type, ContainerRef};
use crate::models::item::{Item, ItemClass};
use crate::models::item_task::{task_type, ItemFx, ItemTask};
use crate::net::packet::ServerPacket;
use crate::packets::world::SCChatMessagePacket;
use crate::server::GameServer;
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::systems::HookSlot;
use crate::world::{MateRef, ObjectKind, PlayerRef, WorldObject};

use super::error_message;
use super::inventory::{self, lock_pair, send_error_message_to, Inv, ItemTxn};
use super::item_effect_packets::{
    grade_enchant_result, SCGradeEnchantBroadcastPacket, SCGradeEnchantResultPacket,
    SCItemSocketingLunagemResultPacket, SCItemSocketingLunastoneResultPacket,
    SCItemUccDataChangedPacket, SCSkillEndedPacket,
};
use super::item_packets::{SCItemTaskSuccessPacket, SCUnitEquipmentsChangedPacket};
use super::loot::{self, LootRng};

/// `ActabilityType.None` / `ActabilityType.Fishing`.
pub mod actability_type {
    pub const NONE: u8 = 0;
    pub const FISHING: u8 = 7;
}

/// `SkillItem` (the `casterObj` of an item-used skill).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SkillItem {
    pub item_id: u64,
    pub item_template_id: u32,
}

/// Whether the effect set `skill.Cancelled = true`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Outcome {
    Done,
    Cancelled,
}

// ---------------------------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------------------------

/// Tables the effect bodies read that no other subsystem loads yet.
#[derive(Debug, Default)]
pub struct ItemEffectsData {
    /// `ZoneGroup.FishingSeaLootPackId` / `FishingLandLootPackId` by zone group id (every
    /// `zone_groups` row: a missing key is `GetZoneGroupById == null`).
    pub fishing_loot_packs: HashMap<u32, FishingLootPacks>,
    /// `SlaveGameData._repairableSlaves` (slave id -> repair effect id; first row wins).
    pub repairable_slaves: HashMap<u32, u32>,
    /// `ItemGameData._itemGradeBuffs` (item template id -> grade -> buff id).
    pub item_grade_buffs: HashMap<u32, HashMap<u8, u32>>,
}

/// The fishing loot packs of a zone group.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct FishingLootPacks {
    pub sea: u32,
    pub land: u32,
}

impl ItemEffectsData {
    pub fn load(conn: &Connection) -> rusqlite::Result<Self> {
        let mut d = Self::default();
        for_each_row(conn, "SELECT * FROM zone_groups", [], |r| {
            d.fishing_loot_packs.insert(
                r.u32("id")?,
                FishingLootPacks {
                    sea: r.u32_or("fishing_sea_loot_pack_id", 0)?,
                    land: r.u32_or("fishing_land_loot_pack_id", 0)?,
                },
            );
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM item_grade_buffs", [], |r| {
            // `_itemGradeBuffs[itemId].Add(itemGrade, buffId)` (a duplicate throws in C#; the first
            // row is kept).
            d.item_grade_buffs
                .entry(r.u32("item_id")?)
                .or_default()
                .entry(r.u8("item_grade_id")?)
                .or_insert(r.u32("buff_id")?);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM repairable_slaves", [], |r| {
            let slave_id = r.u32("slave_id")?;
            let effect_id = r.u32("repair_slave_effect_id")?;
            match d.repairable_slaves.entry(slave_id) {
                std::collections::hash_map::Entry::Occupied(_) => {
                    tracing::warn!("Duplicate entry for repairable_slaves");
                }
                std::collections::hash_map::Entry::Vacant(v) => {
                    v.insert(effect_id);
                }
            }
            Ok(())
        })?;
        Ok(d)
    }

    /// `ItemGameData.GetItemBuff(itemId, gradeId)` (the buff id).
    pub fn item_grade_buff(&self, item_id: u32, grade: u8) -> Option<u32> {
        self.item_grade_buffs.get(&item_id)?.get(&grade).copied()
    }

    /// `SlaveGameData.HasRepairEffectId(templateId, expectedEffectId)`.
    pub fn has_repair_effect_id(&self, slave_id: u32, effect_id: u32) -> bool {
        self.repairable_slaves.get(&slave_id) == Some(&effect_id)
    }
}

// ---------------------------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------------------------

/// `HousingManager.GetHouseAtLocation(x, y)` + `house.AllowedToInteract(character)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct HouseAtLocation {
    pub house_id: u32,
    pub house_obj_id: u32,
    pub allowed_to_interact: bool,
}

/// What `PutDownBackpackEffect` hands to `DoodadManager.Create(...)` and the doodad setup.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct BackpackDoodadSpawn {
    /// `BackpackDoodadId` (doodad template).
    pub doodad_template_id: u32,
    pub item_id: u64,
    pub item_template_id: u32,
    pub ucc_id: u64,
    /// `pos` (world position one metre in front of the character, rotation 0).
    pub x: f32,
    pub y: f32,
    pub z: f32,
    /// Rotation (roll, pitch, yaw): 0 for a pack put down, the character's rotation for a pack
    /// dropped on death.
    pub rot: [f32; 3],
    /// The house the pack is put on (`OwnerType = Housing`, `ParentObj = house`).
    pub house: Option<HouseAtLocation>,
}

/// What the item effects need from other subsystems (mates: `systems::npcs` / mate owner,
/// housing, doodads). Defaults are the "not ported yet" behaviour; the mate lookups scan the
/// world's mates.
pub trait ItemEffectHooks: Send + Sync {
    /// `ParentWorld.MateManager.GetActiveMateByTlId(tlId)`.
    fn active_mate_by_tl_id(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        tl_id: u16,
    ) -> Option<MateRef> {
        world_mates(server, &WorldObject::from(player))
            .into_iter()
            .find(|m| m.data().unit.tl_id == tl_id)
    }

    /// The mate whose `Id` (mates table id) is `mate_id` (`MateEquipmentContainer.ParentUnit`).
    fn mate_by_id(&self, server: &GameServer, owner: &PlayerRef, mate_id: u32) -> Option<MateRef> {
        world_mates(server, &WorldObject::from(owner))
            .into_iter()
            .find(|m| m.data().id == mate_id)
    }

    /// `mate.Equipment` (`ItemManager.GetItemContainerForCharacter(owner.Id, EquipmentMate,
    /// mate, mate.Id)`, as `CharacterMates.SpawnMount` sets it).
    fn mate_equipment(&self, server: &GameServer, mate: &MateRef) -> Option<ContainerRef> {
        let (owner_obj_id, mate_id) = {
            let m = mate.data();
            (m.owner_obj_id, m.id)
        };
        let owner = server.world.character_by_obj_id(owner_obj_id)?;
        Some(server.items.container_for_character(
            owner.character_id,
            slot_type::EQUIPMENT_MATE,
            mate_id,
            &server.ids.container,
        ))
    }

    /// `PublicFarmManager.InPublicFarm(character.ParentWorld.Template, position)`.
    fn in_public_farm(&self, _server: &GameServer, _player: &PlayerRef) -> bool {
        false
    }

    /// `HousingManager.GetHouseAtLocation(x, y)` (+ `AllowedToInteract(character)`).
    fn house_at(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _x: f32,
        _y: f32,
    ) -> Option<HouseAtLocation> {
        None
    }

    /// `DoodadManager.Create(world, 0, BackpackDoodadId, character, true)` + the setup in
    /// `PutDownBackpackEffect` (`IsPersistent`, transform, `ItemId`, `ItemTemplateId`, `UccId`,
    /// scale 1, `PlantTime`, house parent, `InitDoodad`, `Spawn`, `Save`). `false` = `Create`
    /// returned null.
    fn spawn_backpack_doodad(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        spawn: &BackpackDoodadSpawn,
    ) -> bool {
        tracing::warn!(
            "Doodad {}, from BackpackDoodadId could not be created",
            spawn.doodad_template_id
        );
        false
    }
}

/// The default [`ItemEffectHooks`].
#[derive(Debug, Default)]
pub struct DefaultItemEffectHooks;

impl ItemEffectHooks for DefaultItemEffectHooks {}

/// State of the item effects (`server.systems.economy.item_effects`).
#[derive(Debug, Default)]
pub struct ItemEffectsManager {
    pub hooks: HookSlot<dyn ItemEffectHooks>,
}

impl ItemEffectsManager {
    /// The installed hooks, or the defaults.
    pub fn hooks(&self) -> Arc<dyn ItemEffectHooks> {
        self.hooks
            .get()
            .unwrap_or_else(|| Arc::new(DefaultItemEffectHooks))
    }
}

/// Every mate in the world of `near`.
pub fn world_mates(server: &GameServer, near: &WorldObject) -> Vec<MateRef> {
    let Some(world) = server.world.world_of(near) else {
        return Vec::new();
    };
    world
        .all_of_kind(ObjectKind::Mate)
        .into_iter()
        .filter_map(|o| o.as_mate().cloned())
        .collect()
}

// ---------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------

fn lock(c: &ContainerRef) -> std::sync::MutexGuard<'_, crate::models::container::ItemContainer> {
    c.lock().expect("container poisoned")
}

/// `BroadcastPacket(packet, true)` of a character.
fn broadcast_with_self(server: &GameServer, player: &PlayerRef, packet: &dyn ServerPacket) {
    let obj = WorldObject::from(player);
    match server.world.world_of(&obj) {
        Some(world) => crate::world::broadcast(&world, obj, packet, true),
        None => player.send_dyn(packet),
    }
}

/// `ItemManager.GetItemByItemId(id)` with mutation: the item in its persistent container, or
/// the loose item.
fn with_any_item_mut<R>(server: &GameServer, id: u64, f: impl FnOnce(&mut Item) -> R) -> Option<R> {
    if let Some(c) = server.items.holding_container(id) {
        let mut g = lock(&c);
        return g.get_item_by_id_mut(id).map(f);
    }
    let mut item = server.items.take_loose(id)?;
    let r = f(&mut item);
    server.items.add_loose(item);
    Some(r)
}

/// `Character.SendMessage(ChatType.System, message)` through the transaction queue.
fn system_message(txn: &mut ItemTxn<'_>, player: &PlayerRef, message: String) {
    txn.out().send(
        player,
        SCChatMessagePacket {
            chat_type: chat_type::SYSTEM,
            sender: None,
            message,
            ability: 0,
            language_type: 0,
        },
    );
}

fn task_packet(action: u8, tasks: Vec<ItemTask>) -> SCItemTaskSuccessPacket {
    SCItemTaskSuccessPacket {
        action,
        tasks,
        force_remove: Vec::new(),
    }
}

// ---------------------------------------------------------------------------------------------
// GradeEnchant
// ---------------------------------------------------------------------------------------------

/// `GradeEnchant.Execute` (special effect `GradeEnchant`; regrade scrolls). `value1 != 0` =
/// lucky scroll (great success possible), `value3` = item kind (1 weapon, 2 armor, 24
/// accessory). `support_item_id` = `SkillObjectItemGradeEnchantingSupport.SupportItemId`.
/// Sends `SCSkillEndedPacket(skill_tl_id)` itself, as the C# body does.
#[allow(clippy::too_many_arguments)]
pub fn grade_enchant(
    server: &GameServer,
    player: &PlayerRef,
    scroll: Option<SkillItem>,
    target_item_id: Option<u64>,
    support_item_id: Option<u64>,
    values: [i32; 4],
    skill_tl_id: u16,
    rng: &mut dyn LootRng,
) {
    let [value1, value2, value3, value4] = values;
    tracing::debug!(
        "Special effects: GradeEnchant value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
    );
    let Some(scroll) = scroll else { return };
    let Some(target_id) = target_item_id else {
        return;
    };
    let use_charm = support_item_id.is_some_and(|id| id != 0);
    let is_lucky = value1 != 0;
    let data = &server.game_data.items;

    let mut txn = ItemTxn::begin(server);
    let inv = Inv::of(player);
    let Some(holder) = inv.holder_of(target_id) else {
        return; // Invalid item
    };
    let Some(item) = lock(&holder).get_item_by_id(target_id).cloned() else {
        return;
    };
    let initial_grade = item.grade;
    let Some(grade_template) = data.full_grade(i32::from(item.grade)).cloned() else {
        // C#: NullReferenceException in GoldCost.
        tracing::error!("GradeEnchant: no grade template for grade {}", item.grade);
        return;
    };
    let Some(cost) = gold_cost(server, &grade_template, &item, value3) else {
        return;
    };
    if cost == -1 {
        return; // No gold on template, invalid ?
    }
    if player.data().money < i64::from(cost) {
        send_error_message_to(&mut txn.out(), player, error_message::NOT_ENOUGH_MONEY);
        return;
    }
    if !inv.check_items(slot_type::INVENTORY, scroll.item_template_id, 1) {
        // No scroll
        send_error_message_to(
            &mut txn.out(),
            player,
            error_message::NOT_ENOUGH_REQUIRED_ITEM,
        );
        return;
    }
    let mut charm: Option<(Item, ItemGradeEnchantingSupport)> = None;
    if use_charm {
        let Some(charm_item) = inv.item_by_id(support_item_id.unwrap_or(0)) else {
            return;
        };
        let Some(info) = data.enchanting_support(charm_item.template_id).copied() else {
            // C#: NullReferenceException on charmInfo.RequireGradeMin.
            tracing::error!(
                "GradeEnchant: item {} is not a grade enchanting support",
                charm_item.template_id
            );
            return;
        };
        if info.require_grade_min != -1 && i32::from(item.grade) < info.require_grade_min {
            send_error_message_to(
                &mut txn.out(),
                player,
                error_message::NOT_ENOUGH_REQUIRED_ITEM,
            );
            return;
        }
        if info.require_grade_max != -1 && i32::from(item.grade) > info.require_grade_max {
            send_error_message_to(&mut txn.out(), player, error_message::GRADE_ENCHANT_MAX);
            return;
        }
        charm = Some((charm_item, info));
    }

    // All seems to be in order, roll item, consume items and send the results
    let Some((result, new_grade)) = roll_regrade(
        server,
        &grade_template,
        is_lucky,
        charm.as_ref().map(|c| &c.1),
        rng,
    ) else {
        return;
    };
    let after = if result == grade_enchant_result::BREAK {
        // Poof
        lock(&holder)
            .remove_item_task(task_type::GRADE_ENCHANT, target_id, true, &mut txn.env)
            .unwrap_or_else(|| item.clone())
    } else {
        // No Poof
        let mut g = lock(&holder);
        let Some(it) = g.get_item_by_id_mut(target_id) else {
            return;
        };
        if let Some(grade) = new_grade {
            it.grade = grade;
            it.dirty = true;
        }
        let after = it.clone();
        drop(g);
        txn.out().send(
            player,
            task_packet(
                task_type::GRADE_ENCHANT,
                vec![ItemTask::grade_change(&after, after.grade)],
            ),
        );
        after
    };

    // Consume
    inventory::subtract_money(
        &mut txn.out(),
        player,
        slot_type::INVENTORY,
        cost,
        task_type::DEPOSIT_MONEY,
    );
    // The scroll is consumed by the skill as a reagent.
    if let Some((charm_item, _)) = &charm {
        lock(&inv.bag()).consume_item(
            task_type::GRADE_ENCHANT,
            charm_item.template_id,
            1,
            Some(charm_item.id),
            &mut txn.env,
        );
    }
    txn.out().send(
        player,
        SCGradeEnchantResultPacket {
            result,
            item: after.clone(),
            type1: initial_grade,
            type2: after.grade,
        },
    );
    drop(txn);
    broadcast_with_self(server, player, &SCSkillEndedPacket { tl: skill_tl_id });
    report_enchant(server, player, result, after.grade);

    // Let the world know if we got lucky enough
    if after.grade >= 8
        && (result == grade_enchant_result::SUCCESS
            || result == grade_enchant_result::GREAT_SUCCESS)
    {
        let name = player.data().unit.name.clone();
        server
            .world
            .broadcast_to_server(&SCGradeEnchantBroadcastPacket {
                char_name: name,
                result,
                item: after.clone(),
                type1: initial_grade,
                type2: after.grade,
            });
    }
}

/// ENCHANT_ITEM (the resulting grade, successes only) / ENCHANT_FAILURE (a plain failure)
/// achievement credit for one regrade.
fn report_enchant(server: &GameServer, player: &PlayerRef, result: u8, grade: u8) {
    let input = if result == grade_enchant_result::SUCCESS
        || result == grade_enchant_result::GREAT_SUCCESS
    {
        AchievementInput::Increment {
            kind_id: char_record_kind::ENCHANT_ITEM,
            amount: 1,
            matcher: RecordMatch::Value1(u32::from(grade)),
        }
    } else if result == grade_enchant_result::FAIL {
        AchievementInput::Increment {
            kind_id: char_record_kind::ENCHANT_FAILURE,
            amount: 1,
            matcher: RecordMatch::Any,
        }
    } else {
        return;
    };
    crate::systems::report_achievement(server, player, input);
}

/// `GradeEnchant.RollRegrade`: `(result, new grade)` (`None` new grade = unchanged). `None`
/// where the C# throws (`GetGradeTemplateByOrder` returned null).
pub fn roll_regrade(
    server: &GameServer,
    grade_template: &FullGradeTemplate,
    is_lucky: bool,
    charm: Option<&ItemGradeEnchantingSupport>,
    rng: &mut dyn LootRng,
) -> Option<(u8, Option<u8>)> {
    let success_roll = rng.next_i32(0, 10000);
    let break_roll = rng.next_i32(0, 10000);
    let downgrade_roll = rng.next_i32(0, 10000);
    let great_success_roll = rng.next_i32(0, 10000);
    let g = grade_template;
    let chance = |base: i32, f: fn(&ItemGradeEnchantingSupport) -> (i32, i32)| match charm {
        Some(c) => {
            let (ratio, mul) = f(c);
            charm_chance(base, ratio, mul)
        }
        None => base,
    };
    let success_chance = chance(g.enchant_success_ratio, |c| {
        (c.add_success_ratio, c.add_success_mul)
    });
    let great_success_chance = chance(g.enchant_great_success_ratio, |c| {
        (c.add_great_success_ratio, c.add_great_success_mul)
    });
    let break_chance = chance(g.enchant_break_ratio, |c| {
        (c.add_break_ratio, c.add_break_mul)
    });
    let downgrade_chance = chance(g.enchant_downgrade_ratio, |c| {
        (c.add_downgrade_ratio, c.add_downgrade_mul)
    });
    let next_grade = |change: i32| -> Option<u8> {
        match server
            .game_data
            .items
            .grade_by_order(g.grade_order + change)
        {
            Some(t) => Some(t.grade as u8),
            None => {
                // C#: NullReferenceException on GetNextGrade(...).Grade
                tracing::error!(
                    "GradeEnchant: no grade with order {}",
                    g.grade_order + change
                );
                None
            }
        }
    };

    if success_roll < success_chance {
        if is_lucky && great_success_roll < great_success_chance {
            let increase = charm.map_or(2, |c| 2 + c.add_great_success_grade);
            let grade = next_grade(increase)?;
            return Some((grade_enchant_result::GREAT_SUCCESS, Some(grade)));
        }
        let grade = next_grade(1)?;
        return Some((grade_enchant_result::SUCCESS, Some(grade)));
    }
    if break_roll < break_chance {
        return Some((grade_enchant_result::BREAK, None));
    }
    if downgrade_roll < downgrade_chance {
        // `newGrade < 0` is never true for a byte.
        let new_grade = rng.next_i32(g.enchant_downgrade_min, g.enchant_downgrade_max) as u8;
        return Some((grade_enchant_result::DOWNGRADE, Some(new_grade)));
    }
    Some((grade_enchant_result::FAIL, None))
}

/// `GradeEnchant.GetCharmChance`.
pub fn charm_chance(base_chance: i32, charm_ratio: i32, charm_mul: i32) -> i32 {
    base_chance
        .wrapping_add(charm_ratio)
        .wrapping_add((f64::from(base_chance) * (f64::from(charm_mul) / 100.0)) as i32)
}

/// `GradeEnchant.GoldCost(gradeTemplate, item, itemType)`: `Some(-1)` = no slot type,
/// `None` where the C# throws (template cast / missing enchanting cost / formula).
pub fn gold_cost(
    server: &GameServer,
    grade_template: &FullGradeTemplate,
    item: &Item,
    item_type: i32,
) -> Option<i32> {
    let data = &server.game_data.items;
    let Some(t) = data.template(item.template_id) else {
        tracing::error!("GradeEnchant: unknown item template {}", item.template_id);
        return None;
    };
    let slot_type_id = match (item_type, &t.kind) {
        (1, TemplateKind::Weapon(e))
        | (2, TemplateKind::Armor(e))
        | (24, TemplateKind::Accessory(e)) => e.slot_type_id,
        (1 | 2 | 24, _) => {
            // C#: InvalidCastException on the template cast.
            tracing::error!(
                "GradeEnchant: item {} does not match item type {item_type}",
                item.template_id
            );
            return None;
        }
        _ => 0,
    };
    if slot_type_id == 0 {
        return Some(-1);
    }
    let Some(enchanting_cost) = data.equip_slot_enchanting_cost(slot_type_id) else {
        tracing::error!("GradeEnchant: no enchanting cost for slot type {slot_type_id}");
        return None;
    };
    let params: HashMap<&str, f64> = [
        ("item_grade", f64::from(grade_template.enchant_cost)),
        ("item_level", f64::from(t.level)),
        ("equip_slot_enchant_cost", f64::from(enchanting_cost.cost)),
    ]
    .into_iter()
    .collect();
    let Some(formula) = server
        .game_data
        .formulas
        .get_formula_by_kind(FormulaKind::GradeEnchantCost)
    else {
        tracing::error!("GradeEnchant: no GradeEnchantCost formula");
        return None;
    };
    Some(formula.evaluate(&params) as i32)
}

// ---------------------------------------------------------------------------------------------
// Socketing / tempering / looks
// ---------------------------------------------------------------------------------------------

/// `ItemSocketing.Execute` (lunagems; `Item.DawnStone` removes every gem). Sends
/// `SCSkillEndedPacket(skill_tl_id)` itself.
pub fn item_socketing(
    server: &GameServer,
    player: &PlayerRef,
    gem: Option<SkillItem>,
    target_item_id: Option<u64>,
    values: [i32; 4],
    skill_tl_id: u16,
    rng: &mut dyn LootRng,
) {
    let [value1, value2, value3, value4] = values;
    tracing::debug!(
        "Special effects: ItemSocketing value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
    );
    let Some(gem) = gem else {
        tracing::error!("Special effects: ItemSocketing casterObj is not a SkillItem");
        return;
    };
    let Some(target_id) = target_item_id else {
        tracing::error!("Special effects: ItemSocketing targetObj is not a SkillCastItemTarget");
        return;
    };
    let mut txn = ItemTxn::begin(server);
    let inv = Inv::of(player);
    let target_holder = inv.holder_of(target_id);
    let gem_item = inv.item_by_id(gem.item_id);
    let (Some(holder), Some(gem_item)) = (target_holder, gem_item) else {
        tracing::warn!(
            "Special effects: ItemSocketing targetItem {target_id} or gemItem {} not found",
            gem.item_id
        );
        return;
    };
    let mut g = lock(&holder);
    let Some(equip) = g.get_item_by_id_mut(target_id) else {
        return;
    };
    if !equip.is_equip() {
        tracing::warn!("Special effects: ItemSocketing targetItem {target_id} was not a EquipItem");
        return;
    }
    let mut result = 0u8;
    let mut installed = false;
    let gems = &mut equip.details.gem_ids;
    if gem_item.template_id != crate::data::items::ITEM_DAWN_STONE {
        // Add LunaGem
        let gem_count = gems.iter().filter(|&&id| id != 0).count();
        // Reject if the item has no free gem sockets
        if gem_count >= gems.len() {
            tracing::warn!(
                "Special effects: ItemSocketing target {target_id} has no free gem sockets"
            );
            return;
        }
        // Roll for Success
        let gem_roll = rng.next_i32(0, 10000);
        let gem_chance = server.game_data.items.socket_chance(gem_count as u32);
        if i64::from(gem_roll) < i64::from(gem_chance) {
            gems[gem_count] = gem_item.template_id;
            result = 1;
        } else {
            // Failed: a failed socket attempt removes ALL currently socketed gems.
            gems.fill(0);
        }
        installed = true;
    } else {
        // DawnStone
        gems.fill(0);
        result = 1;
    }
    // GemIds is a plain array: the C# does not mark the item dirty.
    let after = equip.clone();
    drop(g);
    txn.out().send(
        player,
        task_packet(task_type::SOCKETING, vec![ItemTask::update(&after)]),
    );
    txn.out().send(
        player,
        SCItemSocketingLunagemResultPacket {
            result,
            item_id: after.id,
            template_id: gem_item.template_id,
            install: installed,
        },
    );
    drop(txn);
    broadcast_with_self(server, player, &SCSkillEndedPacket { tl: skill_tl_id });
}

/// `MagicalEnchant.Execute` (world interaction: lunastones). The C# checks `targetType` before
/// `casterType`.
pub fn magical_enchant(
    server: &GameServer,
    player: &PlayerRef,
    skill_item: Option<SkillItem>,
    target_item_id: Option<u64>,
) {
    let Some(target_id) = target_item_id else {
        return;
    };
    let Some(skill_item) = skill_item else { return };
    let mut txn = ItemTxn::begin(server);
    let bag = Inv::of(player).bag();
    let mut g = lock(&bag);
    let Some(equip) = g.get_item_by_id_mut(target_id) else {
        return;
    };
    if !equip.is_equip() {
        return;
    }
    equip.details.rune_id = skill_item.item_template_id; // not a dirty-marking property
    let after = equip.clone();
    drop(g);
    txn.out().send(
        player,
        SCItemSocketingLunastoneResultPacket {
            result: true,
            item_id: after.id,
            template_id: skill_item.item_template_id,
        },
    );
    txn.out().send(
        player,
        task_packet(task_type::ENCHANT_MAGICAL, vec![ItemTask::update(&after)]),
    );
}

/// `ItemCapScale.Execute` (tempering). `skill_id` = `skill.Id` (`GetItemCapScale(skillId)`).
pub fn item_cap_scale(
    server: &GameServer,
    player: &PlayerRef,
    temper: Option<SkillItem>,
    target_item_id: Option<u64>,
    skill_id: u32,
    values: [i32; 4],
    rng: &mut dyn LootRng,
) {
    let [value1, value2, value3, value4] = values;
    tracing::debug!(
        "Special effects: ItemCapScale value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
    );
    // C#: the (SkillItem) / (SkillCastItemTarget) casts throw on other types.
    let (Some(_), Some(target_id)) = (temper, target_item_id) else {
        tracing::error!("ItemCapScale: caster or target object of the wrong type");
        return;
    };
    let mut txn = ItemTxn::begin(server);
    let inv = Inv::of(player);
    let Some(holder) = inv.holder_of(target_id) else {
        return;
    };
    let mut g = lock(&holder);
    let Some(equip) = g.get_item_by_id_mut(target_id) else {
        return;
    };
    if !equip.is_equip() {
        tracing::error!("ItemCapScale: item {target_id} is not an EquipItem"); // InvalidCast
        return;
    }
    let Some(scale) = server.game_data.items.item_cap_scale(skill_id).copied() else {
        tracing::error!("ItemCapScale: no item cap scale for skill {skill_id}"); // NRE
        return;
    };
    let physical = rng.next_i32(scale.scale_min, scale.scale_max) as u16;
    let magical = rng.next_i32(scale.scale_min, scale.scale_max) as u16;
    equip.details.temper_physical = physical;
    equip.details.temper_magical = magical;
    let after = equip.clone();
    drop(g);
    // The temper item is consumed as a skill reagent.
    txn.out().send(
        player,
        task_packet(task_type::ENCHANT_PHYSICAL, vec![ItemTask::update(&after)]),
    );
    system_message(
        &mut txn,
        player,
        format!("Temper:\n |cFFFFFFFF{physical}%|r Physical\n|cFFFFFFFF{magical}%|r Magical"),
    );
}

/// `ItemCapScaleReset.Execute`.
pub fn item_cap_scale_reset(
    server: &GameServer,
    player: &PlayerRef,
    temper: Option<SkillItem>,
    target_item_id: Option<u64>,
    values: [i32; 4],
) {
    let [value1, value2, value3, value4] = values;
    tracing::debug!(
        "Special effects: ItemCapScaleReset value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
    );
    let (Some(_), Some(target_id)) = (temper, target_item_id) else {
        tracing::error!("ItemCapScaleReset: caster or target object of the wrong type");
        return;
    };
    let mut txn = ItemTxn::begin(server);
    let inv = Inv::of(player);
    let Some(holder) = inv.holder_of(target_id) else {
        return;
    };
    let mut g = lock(&holder);
    let Some(equip) = g.get_item_by_id_mut(target_id) else {
        return;
    };
    if !equip.is_equip() {
        tracing::error!("ItemCapScaleReset: item {target_id} is not an EquipItem");
        return;
    }
    equip.details.temper_physical = 0;
    equip.details.temper_magical = 0;
    let after = equip.clone();
    drop(g);
    txn.out().send(
        player,
        task_packet(task_type::ENCHANT_PHYSICAL, vec![ItemTask::update(&after)]),
    );
    system_message(&mut txn, player, "Tempering reset".to_string());
}

/// `CleanupLookConvert.Execute` (removes an item's look image, consumes one powder).
pub fn cleanup_look_convert(
    server: &GameServer,
    player: &PlayerRef,
    powder: Option<SkillItem>,
    target_item_id: Option<u64>,
    values: [i32; 4],
) -> Outcome {
    let [value1, value2, value3, value4] = values;
    tracing::debug!(
        "Special effects: CleanupLookConvert value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
    );
    let Some(target_id) = target_item_id else {
        return Outcome::Cancelled;
    };
    let mut txn = ItemTxn::begin(server);
    let inv = Inv::of(player);
    let Some(holder) = inv.holder_of(target_id) else {
        return Outcome::Cancelled;
    };
    let image = lock(&holder)
        .get_item_by_id(target_id)
        .map_or(0, |i| i.details.image_item_template_id);
    if image == 0 {
        // Is not an item with an image on it
        send_error_message_to(
            &mut txn.out(),
            player,
            error_message::ITEM_LOOK_CONVERT_AS_INVALID_COMBINATION,
        );
        return Outcome::Cancelled;
    }
    let Some(powder) = powder else {
        return Outcome::Cancelled;
    };
    let Some(powder_item) = inv.item_by_id(powder.item_id) else {
        return Outcome::Cancelled;
    };
    if powder_item.count < 1 {
        return Outcome::Cancelled;
    }
    let after = {
        let mut g = lock(&holder);
        let Some(it) = g.get_item_by_id_mut(target_id) else {
            return Outcome::Cancelled;
        };
        it.details.image_item_template_id = 0;
        it.dirty = true;
        it.clone()
    };
    txn.out().send(
        player,
        task_packet(task_type::CONVERT_ITEM_LOOK, vec![ItemTask::update(&after)]),
    );
    if let Some(c) = inv.holder_of_any(powder_item.id) {
        lock(&c).consume_item(
            task_type::CONVERT_ITEM_LOOK,
            powder_item.template_id,
            1,
            Some(powder_item.id),
            &mut txn.env,
        );
    }
    Outcome::Done
}

// ---------------------------------------------------------------------------------------------
// Conversion / gain item / loot packs
// ---------------------------------------------------------------------------------------------

/// `ItemConversion.Execute` (disenchanting a bag item into its conversion product).
pub fn item_conversion(
    server: &GameServer,
    player: &PlayerRef,
    target_item_id: Option<u64>,
    values: [i32; 4],
    rng: &mut dyn LootRng,
) -> Outcome {
    let [value1, value2, value3, value4] = values;
    tracing::debug!(
        "Special effects: ItemConversion value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
    );
    let Some(target_id) = target_item_id else {
        return Outcome::Cancelled;
    };
    let data = &server.game_data.items;
    let conv = &server.game_data.systems.economy.items.conversion;
    let mut txn = ItemTxn::begin(server);
    let inv = Inv::of(player);
    let bag = inv.bag();
    let Some(target) = lock(&bag).get_item_by_id(target_id).cloned() else {
        return Outcome::Cancelled;
    };
    let Some(template) = data.template(target.template_id) else {
        return Outcome::Cancelled;
    };
    if !template.disenchantable {
        return Outcome::Cancelled;
    }
    let id = target.template_id;
    let Some(reagent) = conv.reagent_for_item(target.grade, template.impl_id, id, template.level)
    else {
        tracing::error!("Couldn't find Reagent for item {id}");
        return Outcome::Cancelled;
    };
    let Some(product) = conv.product_from_reagent(reagent) else {
        tracing::error!("Couldn't find Product from Reagent for item {id}");
        return Outcome::Cancelled;
    };
    let product_roll = rng.next_i32(0, 10000);
    if product_roll < product.chance_rate {
        // give product (TODO in C#: weights)
        let value = rng.next_i32(product.min_output, product.max_output.wrapping_add(1));
        let ok = lock(&bag).acquire_default_item(
            task_type::CONVERSION,
            product.output_item_id,
            value,
            -1,
            0,
            &mut txn.env,
        );
        if !ok {
            send_error_message_to(&mut txn.out(), player, error_message::BAG_FULL);
            return Outcome::Cancelled;
        }
    }
    // consumes target item from stack or if there is only 1, destroy item
    lock(&bag).consume_item(
        task_type::CONVERSION,
        target.template_id,
        1,
        Some(target.id),
        &mut txn.env,
    );
    Outcome::Done
}

/// `GainItem.Execute` (`value1` = item template, `value2` = count).
pub fn gain_item(server: &GameServer, player: &PlayerRef, values: [i32; 4]) {
    let [value1, value2, value3, value4] = values;
    tracing::debug!(
        "Special effects: GainItem value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
    );
    if value1 <= 0 {
        tracing::warn!("Special effects: GainItem ignored invalid item template id {value1}");
        return;
    }
    let item_id = value1 as u32;
    let count = if value2 > 0 { value2 } else { 1 };
    let mut txn = ItemTxn::begin(server);
    let inv = Inv::of(player);
    let acquired = inventory::try_add_new_item(
        &mut txn,
        &inv,
        task_type::SKILL_EFFECT_GAIN_ITEM,
        item_id,
        count,
        0,
        0,
    );
    if !acquired {
        tracing::warn!(
            "Special effects: GainItem failed to give item {item_id} x{count} to character {}",
            player.character_id
        );
    }
}

/// `GainItemWithEmblemImprint.Execute`: `UccManager.CreateStamp(target, sourceItem)` where
/// the source is the casting item (`ItemManager.GetItemByItemId`) and `target` must be a
/// character.
pub fn gain_item_with_emblem_imprint(
    server: &GameServer,
    target: Option<&PlayerRef>,
    source: Option<SkillItem>,
    values: [i32; 4],
) {
    let [value1, value2, value3, value4] = values;
    tracing::debug!(
        "Special effects: GainItemWithEmblemImprint value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
    );
    let Some(source) = source else {
        tracing::error!("GainItemWithEmblemImprint: casterObj is not a SkillItem"); // InvalidCast
        return;
    };
    let Some(source_item) = server.items.item_by_id(source.item_id) else {
        return;
    };
    if let Some(player) = target {
        create_stamp(server, player, &source_item);
    }
}

/// `UccManager.CreateStamp(player, sourceInk)`: a new crest stamp with the ink's UCC.
pub fn create_stamp(server: &GameServer, player: &PlayerRef, source_ink: &Item) {
    let mut txn = ItemTxn::begin(server);
    let Some(mut item) = txn.env.create(crate::data::items::ITEM_CREST_STAMP, 1, 0) else {
        tracing::error!("CreateStamp: could not create a crest stamp"); // NRE in C#
        return;
    };
    item.set_ucc(source_ink.ucc_id);
    let bag = Inv::of(player).bag();
    let data = txn.data();
    let res = lock(&bag).add_item(
        task_type::GAIN_ITEM_WITH_UCC,
        item,
        -1,
        data,
        &mut txn.env.fx,
    );
    if let Err(item) = res {
        // The new item stays in the C# `_allItems`.
        txn.env.fx.push(ItemFx::Orphaned(Box::new(item)));
    }
}

/// `UccManager.ApplyStamp(stamp, targetItem)`.
pub fn apply_stamp(stamp: &Item, target: &mut Item) {
    target.set_ucc(stamp.ucc_id);
}

/// Resolve both UCC operands in the caster's inventory while the caller holds an ItemTxn.
/// Loose items and other characters' containers cannot be addressed by an item skill.
fn owned_ucc_items(
    player: &PlayerRef,
    source: SkillItem,
    target_id: u64,
) -> Option<(Item, ContainerRef)> {
    if source.item_id == target_id {
        return None;
    }
    let inv = Inv::of(player);
    let source_item = inv.item_by_id(source.item_id)?;
    if source_item.owner_id != u64::from(player.character_id)
        || source_item.template_id != source.item_template_id
        || source_item.count <= 0
    {
        return None;
    }
    let holder = inv.holder_of(target_id)?;
    let valid_target = lock(&holder)
        .get_item_by_id(target_id)
        .is_some_and(|item| item.owner_id == u64::from(player.character_id) && item.count > 0);
    valid_target.then_some((source_item, holder))
}

/// `ImprintUccEffect.Apply` (stamp a UCC onto an item). The caller supplies the caster and
/// cancels rejected effects before the skill's subsequent reagent consumption.
pub fn imprint_ucc(
    server: &GameServer,
    target: Option<&PlayerRef>,
    stamp: Option<SkillItem>,
    target_item_id: Option<u64>,
) -> Outcome {
    tracing::trace!("ImprintUccEffect");
    let (Some(player), Some(stamp), Some(target_id)) = (target, stamp, target_item_id) else {
        return Outcome::Cancelled;
    };
    let mut txn = ItemTxn::begin(server);
    let Some((stamp_item, holder)) = owned_ucc_items(player, stamp, target_id) else {
        return Outcome::Cancelled;
    };
    let target_item = {
        let mut container = lock(&holder);
        let Some(target_item) = container.get_item_by_id_mut(target_id) else {
            return Outcome::Cancelled;
        };
        apply_stamp(&stamp_item, target_item);
        target_item.clone()
    };
    txn.out().send(
        player,
        SCItemUccDataChangedPacket {
            ucc_id: stamp_item.ucc_id,
            player_id: player.character_id,
            target_item_id: target_item.id,
        },
    );
    txn.out().send(
        player,
        task_packet(
            task_type::GAIN_ITEM_WITH_UCC,
            vec![ItemTask::update_bits(&target_item)],
        ),
    );
    // Retail consumes the stamp with QuestRemoveSupplies; the C# leaves it to the skill.
    Outcome::Done
}

/// `CleanupUccEffect.Apply` (bleach: removes the UCC of an item).
pub fn cleanup_ucc(
    server: &GameServer,
    target: Option<&PlayerRef>,
    bleach: Option<SkillItem>,
    target_item_id: Option<u64>,
) -> Outcome {
    tracing::trace!("CleanupUccEffect");
    let (Some(player), Some(bleach), Some(target_id)) = (target, bleach, target_item_id) else {
        return Outcome::Cancelled;
    };
    let mut txn = ItemTxn::begin(server);
    let Some((_, holder)) = owned_ucc_items(player, bleach, target_id) else {
        return Outcome::Cancelled;
    };
    let target_item = {
        let mut container = lock(&holder);
        let Some(target_item) = container.get_item_by_id_mut(target_id) else {
            return Outcome::Cancelled;
        };
        target_item.set_ucc(0);
        target_item.clone()
    };
    txn.out().send(
        player,
        SCItemUccDataChangedPacket {
            ucc_id: 0,
            player_id: player.character_id,
            target_item_id: target_item.id,
        },
    );
    txn.out().send(
        player,
        task_packet(
            task_type::GAIN_ITEM_WITH_UCC,
            vec![ItemTask::update_bits(&target_item)],
        ),
    );
    Outcome::Done
}

/// `FishingLoot.Execute`: the fishing loot pack of the target's zone group (land pack above
/// z 101, sea pack otherwise).
pub fn fishing_loot(
    server: &GameServer,
    player: &PlayerRef,
    target: &WorldObject,
    values: [i32; 4],
    rng: &mut dyn LootRng,
) {
    let [value1, value2, value3, value4] = values;
    tracing::debug!(
        "Special effects: FishingLoot value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
    );
    let zone_key = target.transform().zone_id;
    let Some(zone) = server.game_data.zones.zone_by_key(zone_key) else {
        tracing::error!("FishingLoot: unknown zone {zone_key}"); // NRE in C#
        return;
    };
    let packs = server
        .game_data
        .systems
        .economy
        .item_effects
        .fishing_loot_packs
        .get(&zone.group_id)
        .copied();
    let Some(packs) = packs else {
        tracing::warn!(
            "{} seems to be trying to fish out of bounds.",
            player.data().unit.name
        );
        return;
    };
    let z = match server.world.world_of(target) {
        Some(w) => w.world_pos_rot(target).z,
        None => target.transform().z,
    };
    let loot_table_id = if z > 101.0 { packs.land } else { packs.sea };
    let Some(pack) = server.game_data.systems.economy.loot.pack(loot_table_id) else {
        return;
    };
    if pack.loots.is_empty() {
        return;
    }
    let mut txn = ItemTxn::begin(server);
    if !loot::give_loot_pack(
        &mut txn,
        player,
        pack,
        actability_type::FISHING,
        task_type::SKILL_EFFECT_GAIN_ITEM,
        None,
        None,
        rng,
    ) {
        send_error_message_to(&mut txn.out(), player, error_message::BAG_FULL);
    }
}

/// `GainLootPackItemEffect` template fields.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct GainLootPackItemEffect {
    pub loot_pack_id: u32,
    pub consume_source_item: bool,
    pub consume_item_id: u32,
    pub consume_count: i32,
    pub inherit_grade: bool,
}

/// `GainLootPackItemEffect.Apply`.
pub fn gain_loot_pack_item(
    server: &GameServer,
    player: &PlayerRef,
    effect: &GainLootPackItemEffect,
    source: Option<SkillItem>,
    rng: &mut dyn LootRng,
) {
    let Some(pack) = server
        .game_data
        .systems
        .economy
        .loot
        .pack(effect.loot_pack_id)
    else {
        return;
    };
    if pack.loots.is_empty() {
        return;
    }
    tracing::debug!("GainLootPackItemEffect {}", effect.loot_pack_id);
    // TODO (C#): Find the related ActAbility
    let mut inherited_grade = None;
    let mut txn = ItemTxn::begin(server);
    let bag = Inv::of(player).bag();
    if !effect.consume_source_item && effect.consume_item_id > 0 && effect.consume_count > 0 {
        // the tractor collects water
        lock(&bag).consume_item(
            task_type::CONSUME_SKILL_SOURCE,
            effect.consume_item_id,
            effect.consume_count,
            None,
            &mut txn.env,
        );
    }
    if effect.consume_source_item {
        let Some(source) = source else { return };
        let Some(source_item) = lock(&bag).get_item_by_id(source.item_id).cloned() else {
            tracing::warn!(
                "Invalid loot result items {} in lootpack {}",
                source.item_id,
                effect.loot_pack_id
            );
            return;
        };
        if effect.inherit_grade {
            inherited_grade = Some(source_item.grade);
        }
        lock(&bag).remove_item_task(
            task_type::CONSUME_SKILL_SOURCE,
            source_item.id,
            true,
            &mut txn.env,
        );
    }
    // Give the results
    if !loot::give_loot_pack(
        &mut txn,
        player,
        pack,
        actability_type::NONE,
        task_type::SKILL_EFFECT_GAIN_ITEM,
        None,
        inherited_grade,
        rng,
    ) {
        send_error_message_to(&mut txn.out(), player, error_message::LOOT_ITEM_CREATE);
    }
}

/// `ConversionEffect.Apply` (only traces in C#).
pub fn conversion_effect() {
    tracing::trace!("ConversionEffect");
}

// ---------------------------------------------------------------------------------------------
// Backpacks / slaves
// ---------------------------------------------------------------------------------------------

/// `PutDownBackpackEffect.Apply` (drop the equipped trade pack as a doodad in front of the
/// character, then re-equip the glider the pack replaced).
pub fn put_down_backpack(
    server: &GameServer,
    player: &PlayerRef,
    backpack_doodad_id: u32,
    pack_item: Option<SkillItem>,
) {
    tracing::debug!("PutDownBackpackEffect");
    let Some(pack_item) = pack_item else { return };
    let economy = &server.systems.economy;
    let hooks = economy.item_effects.hooks();
    let inv = Inv::of(player);
    let equipment = inv.equipment();
    let Some(item) = lock(&equipment).get_item_by_id(pack_item.item_id).cloned() else {
        return;
    };
    if hooks.in_public_farm(server, player) {
        let mut txn = ItemTxn::begin(server);
        send_error_message_to(
            &mut txn.out(),
            player,
            error_message::COMMON_FARM_NOT_ALLOWED_TYPE,
        );
        return;
    }
    let char_id = player.character_id;
    let prev_id = economy.previous_backpack(char_id);
    let previous_glider = lock(&inv.bag()).get_item_by_id(prev_id).cloned();
    // If no longer valid, reset the value here
    if previous_glider
        .as_ref()
        .is_none_or(|g| g.slot_type != slot_type::INVENTORY)
    {
        economy.set_previous_backpack(char_id, 0);
    }

    // pos = in front of the character (1 m), always facing north
    let obj = WorldObject::from(player);
    let here = match server.world.world_of(&obj) {
        Some(w) => w.world_pos_rot(&obj),
        None => obj.transform().local(),
    };
    let x = here.x + -here.rot_z.sin(); // -distance * sin(yaw), distance = 1
    let y = here.y + 1.0f32 * here.rot_z.cos();
    let z = here.z;

    let house = hooks.house_at(server, player, x, y);
    if let Some(h) = house {
        // Trying to put on a house location, we need to do some checks
        if !h.allowed_to_interact {
            let mut txn = ItemTxn::begin(server);
            send_error_message_to(&mut txn.out(), player, error_message::BACKPACK);
            return;
        }
    }

    {
        let mut txn = ItemTxn::begin(server);
        let Some(system) = inv.get(slot_type::SYSTEM) else {
            tracing::error!("PutDownBackpack: character {char_id} has no system container");
            return;
        };
        let data = txn.data();
        let moved = {
            let (mut sys, eq) = lock_pair(&system, &equipment);
            match eq {
                Some(mut eq) => sys.move_item_from(
                    &mut eq,
                    task_type::DROP_BACKPACK,
                    item.id,
                    -1,
                    data,
                    &mut txn.env.fx,
                ),
                None => false,
            }
        };
        if !moved {
            return;
        }
    }

    // Spawn doodad
    tracing::debug!("PutDownPackEffect");
    let spawn = BackpackDoodadSpawn {
        doodad_template_id: backpack_doodad_id,
        item_id: item.id,
        item_template_id: item.template_id,
        ucc_id: item.ucc_id,
        x,
        y,
        z,
        rot: [0.0; 3],
        house,
    };
    if !hooks.spawn_backpack_doodad(server, player, &spawn) {
        return;
    }
    if let Some(world) = server.world.world_of(&obj) {
        crate::world::broadcast(
            &world,
            obj.clone(),
            &SCUnitEquipmentsChangedPacket {
                obj_id: player.obj_id,
                items: vec![(equip_slot::BACKPACK as u8, None)],
            },
            false,
        );
    }
    if let Some(glider) = previous_glider {
        if inv.equipped(equip_slot::BACKPACK).is_none() {
            let mut txn = ItemTxn::begin(server);
            // The C# reads the live glider object's location here.
            let glider = inv
                .holder_of_any(glider.id)
                .and_then(|c| lock(&c).get_item_by_id(glider.id).cloned())
                .unwrap_or(glider);
            inventory::split_or_move_item(
                &mut txn,
                &inv,
                task_type::SWAP_ITEMS,
                glider.id,
                glider.slot_type,
                glider.slot as u8,
                0,
                slot_type::EQUIPMENT,
                equip_slot::BACKPACK as u8,
                0,
            );
        }
    }
}

/// `Character.DropTradePackToFloor()` (`Character.DoDie`): an equipped trade pack is moved to
/// the system container and spawned as a doodad (the `PutDownBackpackEffect` doodad of the
/// pack's use skill) where the character died, on the ground.
pub fn drop_trade_pack_to_floor(server: &GameServer, player: &PlayerRef) {
    let inv = Inv::of(player);
    let Some(item) = inv.equipped(equip_slot::BACKPACK) else {
        return;
    };
    let use_skill_id = match server.game_data.items.template(item.template_id) {
        Some(t) => match t.kind {
            TemplateKind::Backpack { backpack_type }
                if backpack_type == crate::data::items::backpack_kind::TRADE_PACK =>
            {
                t.use_skill_id
            }
            _ => return,
        },
        None => return,
    };
    // Find the linked doodad of this item's put down effect
    let skills = &server.game_data.systems.skills;
    let Some(item_skill) = skills.skill(use_skill_id) else {
        // C#: `itemSkill.Effects` of a null skill throws.
        tracing::error!("DropTradePackToFloor: skill {use_skill_id} not found");
        return;
    };
    use crate::systems::skills::data::effects::{EffectKind, EffectTemplate};
    let backpack_doodad_id = item_skill
        .effects
        .iter()
        .filter_map(|e| e.template)
        .find_map(|(kind, id)| match (kind, skills.effect(kind, id)) {
            (
                EffectKind::PutDownBackpackEffect,
                Some(EffectTemplate::PutDownBackpack {
                    backpack_doodad_id, ..
                }),
            ) => Some(*backpack_doodad_id),
            _ => None,
        })
        .unwrap_or(0);
    if backpack_doodad_id == 0 {
        return;
    }
    let equipment = inv.equipment();
    {
        let mut txn = ItemTxn::begin(server);
        let Some(system) = inv.get(slot_type::SYSTEM) else {
            tracing::error!(
                "DropTradePackToFloor: character {} has no system container",
                player.character_id
            );
            return;
        };
        let data = txn.data();
        let moved = {
            let (mut sys, eq) = lock_pair(&system, &equipment);
            match eq {
                Some(mut eq) => sys.move_item_from(
                    &mut eq,
                    task_type::DROP_BACKPACK,
                    item.id,
                    -1,
                    data,
                    &mut txn.env.fx,
                ),
                None => false,
            }
        };
        if !moved {
            return;
        }
    }
    // Spawn doodad
    tracing::trace!("Spawn tradepack on floor on death");
    let obj = WorldObject::from(player);
    let here = match server.world.world_of(&obj) {
        Some(w) => w.world_pos_rot(&obj),
        None => obj.transform().local(),
    };
    let ground = crate::systems::ai::geodata::geo_data_height(
        server,
        &obj,
        crate::systems::skills::controllers::Vec3::new(here.x, here.y, here.z),
    );
    let spawn = BackpackDoodadSpawn {
        doodad_template_id: backpack_doodad_id,
        item_id: item.id,
        item_template_id: item.template_id,
        ucc_id: item.ucc_id,
        x: here.x,
        y: here.y,
        z: ground,
        rot: [here.rot_x, here.rot_y, here.rot_z],
        house: None,
    };
    let hooks = server.systems.economy.item_effects.hooks();
    if !hooks.spawn_backpack_doodad(server, player, &spawn) {
        return;
    }
    if let Some(world) = server.world.world_of(&obj) {
        crate::world::broadcast(
            &world,
            obj.clone(),
            &SCUnitEquipmentsChangedPacket {
                obj_id: player.obj_id,
                items: vec![(equip_slot::BACKPACK as u8, None)],
            },
            false,
        );
    }
}

/// `RepairSlaveEffect.Apply` (repair kit on a destroyed vehicle's summon item).
/// `item_target` = `SkillCastItemTarget` `(Id, ObjId)`; `effect_id` = the effect's `Id`.
pub fn repair_slave_item(server: &GameServer, effect_id: u32, item_target: Option<(u64, u32)>) {
    let Some((item_id, obj_id)) = item_target else {
        tracing::warn!("RepairSlaveEffect target is not a SkillCastItemTarget");
        return;
    };
    let mut txn = ItemTxn::begin(server);
    let item = server.items.item_by_id(item_id);
    let Some(target_player) = server.world.character_by_obj_id(obj_id) else {
        tracing::warn!("RepairSlaveEffect target unit {obj_id} is not a player");
        return;
    };
    let Some(item) = item.filter(|i| i.class == ItemClass::SummonSlave) else {
        tracing::warn!("RepairSlaveEffect target item {item_id} is not a salve summon item");
        return;
    };
    let Some(TemplateKind::SummonSlave { slave_id }) = server
        .game_data
        .items
        .template(item.template_id)
        .map(|t| t.kind.clone())
    else {
        return;
    };
    if !server
        .game_data
        .systems
        .economy
        .item_effects
        .has_repair_effect_id(slave_id, effect_id)
    {
        send_error_message_to(
            &mut txn.out(),
            &target_player,
            error_message::ITEM_FAILED_REPAIR,
        );
        tracing::warn!(
            "{} tried to use the wrong repair item {} (template: {} for slave type {slave_id}",
            target_player.data().unit.name,
            item.id,
            item.template_id
        );
        return;
    }
    let Some(after) = with_any_item_mut(server, item_id, |i| {
        i.details.slave_is_destroyed = 0;
        i.details.slave_repair_start_time = chrono::Utc::now().timestamp();
        i.dirty = true;
        i.clone()
    }) else {
        return;
    };
    txn.out().send(
        &target_player,
        task_packet(task_type::REPAIR_SLAVES, vec![ItemTask::update(&after)]),
    );
    tracing::debug!(
        "{} repaired slave on item {}",
        target_player.data().unit.name,
        after.id
    );
}

// ---------------------------------------------------------------------------------------------
// C# stubs
// ---------------------------------------------------------------------------------------------

/// The item special effects that are `// TODO ...` stubs in the C# (they only log).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StubItemEffect {
    ExpToItem,
    GainGachaLootPackItem,
    GainItemWithPosImprint,
    ItemEvolving,
    ItemEvolvingReRoll,
    ItemGradeEnchanting,
    ItemRefurbishment,
    ItemSmelting,
    PhysicalEnchantArmor,
    PhysicalEnchantWeapon,
    RechargeItemBuff,
    RechargeItemRndAttrUnitModifier,
    RechargeItemSkill,
    RepairAuthorityInBag,
    RevertItemLook,
}

/// The `Execute` of a [`StubItemEffect`]: `if (caster is Character) Logger.Debug(...)`.
pub fn stub_effect(effect: StubItemEffect, caster_is_character: bool, values: [i32; 4]) {
    if caster_is_character {
        let [value1, value2, value3, value4] = values;
        tracing::debug!(
            "Special effects: {effect:?} value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}"
        );
    }
}
