//! Port of `Models/Game/Char/CharacterCraft` (`Craft` / `EndCraft` / `CancelCraft`,
//! `FreeRegrade`), `Models/Tasks/Skills/CraftTask`, `CSExecuteCraft`, `CSSetCraftingPayPacket`
//! and `SCCraftFailedPacket`.
//!
//! A craft is a skill cast on the crafting doodad: [`craft`] validates the craft and asks the
//! skills subsystem to cast the craft skill ([`CraftHooks::start_craft_cast`]); when the cast's
//! `CraftEffect` applies, the skills subsystem calls [`end_craft`], which hands out the products,
//! consumes the materials and schedules the next craft of the batch. Labor is not consumed
//! here: `Skill.EndSkill` (skills subsystem) charges the skill's `ConsumeLaborPower` through
//! `inventory::change_labor`, exactly like the C#.
//!
//! `CharacterCraft`'s per-character state lives in [`CraftManager`] (keyed by character id).

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use aaemu_commons::{Marshal, PacketStream};

use crate::error::Result;
use crate::models::container::slot_type;
use crate::models::item_task::task_type;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::systems::HookSlot;
use crate::world::{DoodadRef, PlayerRef, WorldObject};

use super::craft_data::Craft;
use super::error_message;
use super::inventory::{self, Inv, ItemTxn, Out};
use super::item_handlers::{active, sync_client_packet};

/// `DoodadFuncPermission`.
pub mod doodad_func_permission {
    pub const ANY: u8 = 0;
    pub const PERMISSION1: u8 = 1;
    pub const PERMISSION2: u8 = 2;
    pub const OWNER_ONLY: u8 = 3;
    pub const PERMISSION4: u8 = 4;
    pub const OWNER_RAID_MEMBERS: u8 = 5;
    pub const SAME_ACCOUNT: u8 = 6;
    pub const ZONE_RESIDENTS: u8 = 8;
}

/// What crafting needs from other subsystems. Every method has a "not ported yet" default.
pub trait CraftHooks: Send + Sync {
    /// skills: `new Skill(SkillManager.GetSkillTemplate(skillId)) { CastTimeMultiplier }
    /// .Use(owner, SkillCasterUnit(owner.ObjId), SkillCastDoodadTarget(doodadObjId), null,
    /// false)`. When the cast's `CraftEffect` applies, the skills subsystem calls
    /// [`end_craft`]. Default: nothing is cast (the craft never completes).
    fn start_craft_cast(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _skill_id: u32,
        _doodad_obj_id: u32,
        _cast_time_multiplier: f32,
    ) {
        tracing::warn!("CraftHooks::start_craft_cast is not installed (skills subsystem)");
    }

    /// skills: `if (Owner.SkillTask != null) Owner.SkillTask.Skill.Cancelled = true;
    /// Owner.InterruptSkills();`.
    fn cancel_craft_skill(&self, _server: &GameServer, _player: &PlayerRef) {}

    /// npcs: `Doodad.FuncPermission` (`PermId` of the first current func, `Any` without one).
    fn doodad_func_permission(&self, _server: &GameServer, _doodad: &DoodadRef) -> u8 {
        doodad_func_permission::ANY
    }

    /// housing: `HousingManager.GetByAccountId(accountId)` -> the `Transform.ZoneId` of every
    /// house of the account.
    fn account_house_zone_ids(&self, _server: &GameServer, _account_id: u32) -> Vec<u32> {
        Vec::new()
    }

    /// housing: `HousingManager.GetHouseAtLocation(x, y)` +
    /// `GetActAbilityBonusFromHouse(actabilityGroupId, house)` (0 without a house).
    fn house_actability_bonus(
        &self,
        _server: &GameServer,
        _x: f32,
        _y: f32,
        _actability_group_id: i32,
    ) -> u32 {
        0
    }

    /// Housing bonus scoped to the crafting player's world instance.
    fn house_actability_bonus_in_world(
        &self,
        server: &GameServer,
        _instance_id: u32,
        x: f32,
        y: f32,
        actability_group_id: i32,
    ) -> u32 {
        self.house_actability_bonus(server, x, y, actability_group_id)
    }

    /// quests: `QuestManager.DoOnCraftEvents(owner, craftId)`.
    fn on_craft(&self, _server: &GameServer, _player: &PlayerRef, _craft_id: u32) {}
}

/// `CharacterCraft`'s private state.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CraftState {
    pub count: i32,
    /// `CurrentCraft?.Id`.
    pub current_craft: Option<u32>,
    /// Crafter doodad obj id.
    pub doodad_id: u32,
    pub consume_labor_power: i32,
    pub is_crafting: bool,
}

/// `CharacterCraft` of every character (`server.systems.economy.craft`).
#[derive(Debug, Default)]
pub struct CraftManager {
    states: Mutex<HashMap<u32, CraftState>>,
    /// Installed by the skills / npcs / housing / quests owners (see [`CraftHooks`]).
    pub hooks: HookSlot<dyn CraftHooks>,
}

struct NoHooks;
impl CraftHooks for NoHooks {}

impl CraftManager {
    /// A copy of `character_id`'s craft state.
    pub fn state(&self, character_id: u32) -> CraftState {
        self.states
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .get(&character_id)
            .copied()
            .unwrap_or_default()
    }

    /// `CharacterCraft.IsCrafting`.
    pub fn is_crafting(&self, character_id: u32) -> bool {
        self.state(character_id).is_crafting
    }

    fn update<R>(&self, character_id: u32, f: impl FnOnce(&mut CraftState) -> R) -> R {
        let mut s = self.states.lock().unwrap_or_else(|e| e.into_inner());
        f(s.entry(character_id).or_default())
    }

    /// Forgets a character's state (the C# object dies with its `Character`).
    pub fn forget(&self, character_id: u32) {
        self.states
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .remove(&character_id);
    }

    fn hooks(&self) -> Arc<dyn CraftHooks> {
        self.hooks.get().unwrap_or_else(|| Arc::new(NoHooks))
    }
}

/// `SendErrorMessage(errorMsgType1, errorMsgType2, type, isNotify)`.
fn error2(out: &mut Out<'_>, player: &PlayerRef, e1: i16, e2: i16, kind: u32, notify: bool) {
    out.send(
        player,
        SCErrorMsgPacket {
            error1: e1,
            error2: e2,
            kind,
            is_notify: notify,
        },
    );
}

/// `Character.SendDebugMessage(message)`: a system chat line when `DebugInfo` is on and the
/// character's access level reaches `DebugInfoLevel` (the account access level, read from the
/// DB by `GetEffectiveAccessLevel`, is not consulted here).
pub fn send_debug_message(server: &GameServer, out: &mut Out<'_>, player: &PlayerRef, msg: &str) {
    if !server.config.debug_info {
        return;
    }
    let level = player.data().access_level;
    if level < server.config.debug_info_level as i32 {
        return;
    }
    out.send(
        player,
        crate::packets::world::SCChatMessagePacket {
            chat_type: -2, // ChatType.System
            sender: None,
            message: msg.to_string(),
            ability: 0,
            language_type: 0,
        },
    );
}

/// `MathUtil.CalculateDistance(a, b)` (2D, computed in doubles).
pub fn distance_2d(ax: f32, ay: f32, bx: f32, by: f32) -> f32 {
    let dx = f64::from(ax) - f64::from(bx);
    let dy = f64::from(ay) - f64::from(by);
    (dx * dx + dy * dy).sqrt() as f32
}

/// `ZoneManager.GetZoneByKey(zoneKey)?.GroupId ?? 0`.
pub fn zone_group_id(server: &GameServer, zone_key: u32) -> u32 {
    server
        .game_data
        .zones
        .zone_by_key(zone_key)
        .map_or(0, |z| z.group_id)
}

/// A direct crafting packet must obey the same house access rules as interacting with
/// its furniture. Portable crafts and public world stations keep their normal skill checks.
fn housing_bench_access(
    server: &GameServer,
    player: &PlayerRef,
    doodad_id: u32,
    skill_id: u32,
) -> bool {
    let Some(doodad) = server
        .world
        .object(doodad_id)
        .and_then(|o| o.as_doodad().cloned())
    else {
        return true;
    };
    let (owner_type, house_id, instance_id, deleted) = {
        let d = doodad.data();
        (
            d.owner_type,
            d.owner_db_id,
            d.transform.instance_id,
            d.phase.deleted,
        )
    };
    if owner_type != crate::models::doodad::doodad_owner_type::HOUSING {
        return true;
    }
    let Some(house) = crate::systems::housing::house_by_id(server, house_id) else {
        return false;
    };
    if deleted
        || player.data().unit.transform.instance_id != instance_id
        || house.data().unit.transform.instance_id != instance_id
        || !crate::systems::housing::allowed_to_interact(server, &house, player)
    {
        return false;
    }
    let Some(template) = server.game_data.systems.skills.skill(skill_id) else {
        return true;
    };
    if template.max_range <= 0 {
        return true;
    }
    let source = crate::systems::skills::geometry::Target::Obj(WorldObject::from(player));
    let target = crate::systems::skills::geometry::Target::Obj(WorldObject::from(&doodad));
    let source_pos = crate::systems::skills::geometry::world_pos(server, &source);
    let target_pos = crate::systems::skills::geometry::world_pos(server, &target);
    [
        source_pos.x,
        source_pos.y,
        source_pos.z,
        target_pos.x,
        target_pos.y,
        target_pos.z,
    ]
    .into_iter()
    .all(f32::is_finite)
        && crate::systems::skills::geometry::distance_to(server, &source, &target, true)
            <= template.max_range as f32
}

/// `CharacterCraft.Craft(craft, count, doodadId)`.
pub fn craft(server: &GameServer, player: &PlayerRef, craft_id: u32, count: i32, doodad_id: u32) {
    let Some(craft) = server.game_data.systems.economy.craft.craft(craft_id) else {
        // C#: CraftManager.GetCraftById throws KeyNotFoundException before Craft() runs.
        tracing::error!("Craft: unknown craft {craft_id}");
        return;
    };
    let mgr = &server.systems.economy.craft;
    let char_id = player.character_id;
    mgr.update(char_id, |s| {
        s.current_craft = Some(craft.id);
        s.count = count;
        s.doodad_id = doodad_id;
    });
    let hooks = mgr.hooks();
    let mut out = Out::Direct;

    // check if you are equipped with a backpack or glider
    let inv = Inv::of(player);
    if !inventory::can_replace_glider_in_backpack_slot(&server.game_data.items, &inv) {
        // TODO verified
        error2(
            &mut out,
            player,
            error_message::CRAFT_CANT_ACT_ANY_MORE,
            error_message::BACKPACK_OCCUPIED,
            0,
            false,
        );
        cancel_craft(server, player);
        return;
    }

    // Check if we have enough materials
    let has_materials = craft.craft_materials.is_empty()
        || craft
            .craft_materials
            .iter()
            .all(|m| inv.items_count(m.item_id, -1) >= m.amount);
    if !has_materials {
        // TODO not verified
        error2(
            &mut out,
            player,
            error_message::CRAFT_CANT_ACT_ANY_MORE,
            error_message::NOT_ENOUGH_REQUIRED_ITEM,
            0,
            false,
        );
        cancel_craft(server, player);
        return;
    }

    // Check if we have permission to actually use the doodad (mostly sanity check since the
    // client already checks this before you can craft)
    let mut has_permission = true;
    if !housing_bench_access(server, player, doodad_id, craft.skill_id) {
        error2(
            &mut out,
            player,
            error_message::CRAFT_CANT_ACT_ANY_MORE,
            error_message::CRAFT_PERMISSION_DENY,
            0,
            false,
        );
        cancel_craft(server, player);
        return;
    }
    let world = server.world.world_of(&WorldObject::from(player));
    let doodad = world.as_ref().and_then(|w| w.doodad(doodad_id));
    if let Some(doodad) = &doodad {
        let perm = hooks.doodad_func_permission(server, doodad);
        if perm != doodad_func_permission::ANY {
            use doodad_func_permission as p;
            match perm {
                p::ANY
                | p::PERMISSION1
                | p::PERMISSION2
                | p::OWNER_ONLY
                | p::PERMISSION4
                | p::OWNER_RAID_MEMBERS => {}
                p::SAME_ACCOUNT => {
                    let (owner_type, owner_id) = {
                        let d = doodad.data();
                        (d.owner_type, d.owner_id)
                    };
                    if owner_type == crate::models::doodad::doodad_owner_type::CHARACTER {
                        let my_account = player.data().account_id;
                        has_permission = match server.world.character_by_id(owner_id) {
                            Some(o) => o.data().account_id == my_account,
                            None => {
                                // C#: GetCharacterById(...).AccountId -> NullReferenceException
                                tracing::error!(
                                    "Craft: doodad owner {owner_id} is not online (C# throws)"
                                );
                                return;
                            }
                        };
                    }
                }
                p::ZONE_RESIDENTS => {
                    has_permission = false;
                    let zone_group = zone_group_id(server, doodad.data().transform.zone_id);
                    let account = player.data().account_id;
                    for house_zone in hooks.account_house_zone_ids(server, account) {
                        if zone_group_id(server, house_zone) == zone_group {
                            has_permission = true;
                            break;
                        }
                    }
                }
                _ => {
                    // C#: throw new ArgumentOutOfRangeException
                    tracing::error!("Craft: unknown DoodadFuncPermission {perm}");
                    return;
                }
            }
            let (template_id, obj_id) = (doodad.data().template_id, doodad.obj_id);
            send_debug_message(
                server,
                &mut out,
                player,
                &format!(
                    "Crafting using @DOODAD_NAME({template_id}) - {template_id} (objId: {obj_id}) with current permission {} = {}",
                    permission_name(perm),
                    if has_permission { "True" } else { "False" }
                ),
            );
        }
    }

    if !has_permission {
        // TODO not verified
        error2(
            &mut out,
            player,
            error_message::CRAFT_CANT_ACT_ANY_MORE,
            error_message::CRAFT_PERMISSION_DENY,
            0,
            false,
        );
        cancel_craft(server, player);
        return;
    }

    mgr.update(char_id, |s| s.is_crafting = true);

    let Some(skill) = server
        .game_data
        .systems
        .economy
        .craft
        .skills
        .get(&craft.skill_id)
        .copied()
    else {
        // C#: new Skill(null) -> NullReferenceException (IsCrafting stays set)
        tracing::error!(
            "Craft: unknown skill {} of craft {}",
            craft.skill_id,
            craft.id
        );
        return;
    };
    mgr.update(char_id, |s| {
        s.consume_labor_power = skill.consume_labor_power
    });
    let mut speed_multiplier = 1f32;
    if skill.actability_group_id > 0 {
        let mut current_points = 0u32;
        let found = player
            .data()
            .actability
            .actabilities
            .iter()
            .find(|a| a.id == skill.actability_group_id as u32)
            .map(|a| (a.step, a.point));
        if let Some((step, point)) = found {
            speed_multiplier *= inventory::actability_time_multiplier(step);
            current_points = point as u32;
        }
        // Check bonus from housing
        if let Some(w) = &world {
            let pos = w.world_pos_rot(&WorldObject::from(player));
            current_points = current_points.wrapping_add(hooks.house_actability_bonus_in_world(
                server,
                w.id,
                pos.x,
                pos.y,
                skill.actability_group_id,
            ));
        }
        // Validate skill level
        if i64::from(craft.actability_limit) > i64::from(current_points) {
            inventory::send_error_message(
                player,
                error_message::ACTABILITY_NOT_ENOUGH_POINT,
                skill.actability_group_id as u32,
                true,
            );
            cancel_craft(server, player);
            // This breaks the craft panel, but shouldn't happen if the client is in sync with
            // the server
            return;
        }
    }
    hooks.start_craft_cast(server, player, craft.skill_id, doodad_id, speed_multiplier);
}

fn permission_name(perm: u8) -> &'static str {
    use doodad_func_permission as p;
    match perm {
        p::ANY => "Any",
        p::PERMISSION1 => "Permission1",
        p::PERMISSION2 => "Permission2",
        p::OWNER_ONLY => "OwnerOnly",
        p::PERMISSION4 => "Permission4",
        p::OWNER_RAID_MEMBERS => "OwnerRaidMembers",
        p::SAME_ACCOUNT => "SameAccount",
        p::ZONE_RESIDENTS => "ZoneResidents",
        _ => "?",
    }
}

/// `CharacterCraft.EndCraft()` (called by the skills subsystem when the craft skill's
/// `CraftEffect` applies).
pub fn end_craft(server: &GameServer, player: &PlayerRef) {
    let mgr = &server.systems.economy.craft;
    let char_id = player.character_id;
    let state = mgr.update(char_id, |s| {
        s.count -= 1;
        s.is_crafting = false;
        *s
    });
    let Some(craft) = state
        .current_craft
        .and_then(|id| server.game_data.systems.economy.craft.craft(id))
    else {
        cancel_craft(server, player);
        return;
    };
    let labor = player.data().labor_power;
    if i32::from(labor) < state.consume_labor_power {
        let mut out = Out::Direct;
        send_debug_message(
            server,
            &mut out,
            player,
            "|cFFFFFF00[Craft] Not enough Labor Powers for crafting! Performing a fictitious crafting step...|r",
        );
        // TODO not verified
        error2(
            &mut out,
            player,
            error_message::CRAFT_CANT_ACT_ANY_MORE,
            error_message::NOT_ENOUGH_LABOR_POWER,
            0,
            false,
        );
        craft_or_cancel(server, player);
        return;
    }

    let inv = Inv::of(player);
    if inv.free_slot_count(slot_type::INVENTORY) < craft.craft_products.len() as i32 {
        // TODO not verified
        error2(
            &mut Out::Direct,
            player,
            error_message::CRAFT_CANT_ACT_ANY_MORE,
            error_message::NOT_ENOUGH_SPACE,
            0,
            false,
        );
        craft_or_cancel(server, player);
        return;
    }

    // Keep the permission check and product/material commit together with house transfers
    // and permission changes. Economy transactions are acquired after this housing lock.
    let housing_bench = server
        .world
        .object(state.doodad_id)
        .and_then(|o| o.as_doodad().cloned())
        .is_some_and(|d| d.data().owner_type == crate::models::doodad::doodad_owner_type::HOUSING);
    let operation = housing_bench.then(|| {
        server
            .systems
            .housing
            .operation_lock
            .lock()
            .unwrap_or_else(|e| e.into_inner())
    });
    if !housing_bench_access(server, player, state.doodad_id, craft.skill_id) {
        drop(operation);
        error2(
            &mut Out::Direct,
            player,
            error_message::CRAFT_CANT_ACT_ANY_MORE,
            error_message::CRAFT_PERMISSION_DENY,
            0,
            false,
        );
        cancel_craft(server, player);
        return;
    }
    if !hand_out_products(server, player, &inv, craft) {
        drop(operation);
        cancel_craft(server, player);
        return;
    }
    drop(operation);

    if let Some(h) = mgr.hooks.get() {
        h.on_craft(server, player, craft.id);
    }

    if state.count > 0 {
        schedule_craft(server, player);
    } else {
        cancel_craft(server, player);
    }
}

/// The product / material part of `EndCraft` ("Improper" heuristic grade inheritance of the
/// 1.2 C#). `false` = a trade pack could not be equipped (the craft is cancelled).
fn hand_out_products(server: &GameServer, player: &PlayerRef, inv: &Inv, craft: &Craft) -> bool {
    let items = &server.game_data.items;
    let mut txn = ItemTxn::begin(server);
    let bag = inv.bag();

    // "Improper" Heuristic Grade inheritance to be used for 1.2 only, due to unset flags in
    // compact. Search for the first equipment material in the craft
    let mut inherited_grade: Option<u8> = None;
    for material in &craft.craft_materials {
        if items
            .template(material.item_id)
            .is_some_and(|t| t.equip().is_some())
        {
            let g = bag.lock().expect("container poisoned");
            let (found, _) = g.items_by_template(material.item_id, -1);
            if let Some(first) = found.first().and_then(|id| g.get_item_by_id(*id)) {
                inherited_grade = Some(first.grade);
                break;
            }
        }
    }

    // The products handed out, for the MAKE_ITEM_* achievement credit once the craft went through.
    let mut produced: Vec<(u32, i32, i32)> = Vec::new();
    for product in &craft.craft_products {
        let grade_to_use = match inherited_grade {
            // If we found an equipment material, inherit grade and roll for free regrade
            Some(g) => free_regrade(server, i32::from(g)),
            None if product.item_grade_id > 0 => product.item_grade_id as i32,
            None => -1,
        };
        // Check if template allows grade changes
        if let Some(t) = items.template(product.item_id) {
            let msg = format!(
                "Product template {} - FixedGrade: {}, Gradable: {}",
                product.item_id,
                t.fixed_grade,
                if t.gradable { "True" } else { "False" }
            );
            let mut out = txn.out();
            send_debug_message(server, &mut out, player, &msg);
        }
        // Check if we're crafting a trade pack, if so, try to remove currently equipped
        // backpack slot
        if !inventory::is_auto_equip_trade_pack(items, product.item_id) {
            bag.lock()
                .expect("container poisoned")
                .acquire_default_item(
                    task_type::CRAFT_ACT_SAVED,
                    product.item_id,
                    product.amount,
                    grade_to_use,
                    player.character_id,
                    &mut txn.env,
                );
            produced.push((product.item_id, grade_to_use, product.amount));
        } else if !inventory::try_equip_new_backpack(
            &mut txn,
            inv,
            task_type::CRAFT_PICKUP_PRODUCT,
            product.item_id,
            product.amount,
            grade_to_use,
            player.character_id,
        ) {
            let mut out = txn.out();
            error2(
                &mut out,
                player,
                error_message::CRAFT_CANT_ACT_ANY_MORE,
                error_message::BACKPACK_OCCUPIED,
                0,
                false,
            );
            return false;
        } else {
            produced.push((product.item_id, grade_to_use, product.amount));
        }
    }

    for material in &craft.craft_materials {
        bag.lock().expect("container poisoned").consume_item(
            task_type::CRAFT_ACT_SAVED,
            material.item_id,
            material.amount,
            None,
            &mut txn.env,
        );
    }
    report_crafted(server, player, &produced);
    true
}

/// MAKE_ITEM_TYPE / MAKE_ITEM_IMPL achievement credit for the products of one craft. Runs inside
/// the craft's item transaction: the achievement hooks defer the report until it is released.
fn report_crafted(server: &GameServer, player: &PlayerRef, produced: &[(u32, i32, i32)]) {
    let items = &server.game_data.items;
    let mut inputs = Vec::new();
    for &(item_id, requested_grade, amount) in produced {
        let Some(template) = items.template(item_id) else {
            continue;
        };
        // The grade the container gives the new item (`AcquireDefaultItem`).
        let mut grade = requested_grade;
        if template.fixed_grade >= 0 && !template.gradable {
            grade = template.fixed_grade;
        }
        if grade == -1 {
            grade = template.fixed_grade;
        }
        let grade = grade.max(0) as u32;
        let amount = i64::from(amount);
        inputs.push(AchievementInput::Increment {
            kind_id: char_record_kind::MAKE_ITEM_TYPE,
            amount,
            matcher: RecordMatch::GradeWildcard {
                value1: item_id,
                grade: Some(grade),
            },
        });
        if template.impl_id > 0 {
            inputs.push(AchievementInput::Increment {
                kind_id: char_record_kind::MAKE_ITEM_IMPL,
                amount,
                matcher: RecordMatch::GradeWildcard {
                    value1: template.impl_id as u32,
                    grade: Some(grade),
                },
            });
        }
    }
    crate::systems::report_achievements(server, player, &inputs);
}

/// `CharacterCraft.CraftOrCancel()`.
fn craft_or_cancel(server: &GameServer, player: &PlayerRef) {
    if server
        .systems
        .economy
        .craft
        .state(player.character_id)
        .count
        > 0
    {
        schedule_craft(server, player);
    } else {
        cancel_craft(server, player);
    }
}

/// The delay `ScheduleCraft` waits before the next craft of the batch: the remaining global
/// cooldown when it is longer than the skill's `CooldownTime`.
pub fn next_craft_delay_ms(server: &GameServer, player: &PlayerRef, skill_id: u32) -> Option<i64> {
    let skill = server
        .game_data
        .systems
        .economy
        .craft
        .skills
        .get(&skill_id)?;
    let gcd = player.data().unit.global_cooldown_ms;
    let to_gcd = gcd - crate::world::now_ms();
    Some(if to_gcd > i64::from(skill.cooldown_time) {
        to_gcd
    } else {
        i64::from(skill.cooldown_time)
    })
}

/// `CharacterCraft.ScheduleCraft()`: a [`CraftTask`] after [`next_craft_delay_ms`].
fn schedule_craft(server: &GameServer, player: &PlayerRef) {
    let state = server.systems.economy.craft.state(player.character_id);
    let Some(craft) = state
        .current_craft
        .and_then(|id| server.game_data.systems.economy.craft.craft(id))
    else {
        return;
    };
    let Some(delay) = next_craft_delay_ms(server, player, craft.skill_id) else {
        // C#: skillTemplate.CooldownTime on a null template -> NullReferenceException
        tracing::error!("ScheduleCraft: unknown skill {}", craft.skill_id);
        return;
    };
    let task = CraftTask {
        player: player.clone(),
        craft_id: craft.id,
        obj_id: state.doodad_id,
        count: state.count,
    };
    let (Some(server), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        tracing::error!("ScheduleCraft: no server handle / runtime");
        return;
    };
    rt.spawn(async move {
        tokio::time::sleep(std::time::Duration::from_millis(delay.max(0) as u64)).await;
        task.execute(&server);
    });
}

/// `CraftTask(character, craftId, objId, count)`.
#[derive(Debug, Clone)]
pub struct CraftTask {
    pub player: PlayerRef,
    pub craft_id: u32,
    pub obj_id: u32,
    pub count: i32,
}

impl CraftTask {
    /// `CraftTask.Execute`.
    pub fn execute(&self, server: &GameServer) {
        if self.count > 0 {
            craft(server, &self.player, self.craft_id, self.count, self.obj_id);
        }
    }
}

/// `CharacterCraft.CancelCraft()`.
pub fn cancel_craft(server: &GameServer, player: &PlayerRef) {
    let mgr = &server.systems.economy.craft;
    mgr.update(player.character_id, |s| {
        s.is_crafting = false;
        s.current_craft = None;
        s.count = 0;
        s.doodad_id = 0;
    });
    // Also cancel the related skill ? I don't think this really does anything for crafts, but
    // can't hurt I guess
    if let Some(h) = mgr.hooks.get() {
        h.cancel_craft_skill(server, player);
    }
    // Might want to send a packet here, I think there is a packet when crafting fails. Not
    // sure yet.
}

/// `CharacterCraft.FreeRegrade(baseGrade)`: 5% chance of one grade more (unless the grade
/// already equals `ItemManager.MaxGradeValue`, the highest `grade_order`).
pub fn free_regrade(server: &GameServer, base_grade: i32) -> i32 {
    free_regrade_with(server, base_grade, rand::random::<u32>() % 20)
}

/// [`free_regrade`] with the roll (`Random.Shared.Next(0, 20)`) given.
pub fn free_regrade_with(server: &GameServer, base_grade: i32, lucky_roll: u32) -> i32 {
    let mut grade = base_grade;
    let max_grade = server
        .game_data
        .items
        .grades
        .values()
        .map(|g| g.grade_order)
        .max()
        .unwrap_or(0);
    // Check grade is not already max
    if grade != max_grade {
        // 5% chance
        if lucky_roll < 1 {
            grade += 1;
        }
    }
    grade
}

// -------------------------------------------------------------------------------------------
// Packets
// -------------------------------------------------------------------------------------------

pub fn register(reg: &mut PacketRegistry) {
    // Not registered in the 1.2 `GameNetwork` (no opcode): `CSSetCraftingPayPacket`.
    reg.register::<CSExecuteCraft>(LEVEL_PLAIN, cs::CSExecuteCraft);
}

/// `CSExecuteCraft`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSExecuteCraft {
    pub craft_id: u32,
    pub obj_id: u32,
    pub count: i32,
}

impl CSExecuteCraft {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            craft_id: s.read_u32()?,
            obj_id: s.read_bc()?,
            count: s.read_i32()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::debug!(
            "CSExecuteCraft, craftId : {} , objId : {}, count : {}",
            self.craft_id,
            self.obj_id,
            self.count
        );
        let Some(p) = active(conn) else { return };
        craft(server, &p, self.craft_id, self.count, self.obj_id);
    }
}
sync_client_packet!(CSExecuteCraft);

/// `CSSetCraftingPayPacket` (only logged in C#).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSetCraftingPayPacket {
    pub obj_id: u32,
    pub money_amount: i32,
}

impl CSSetCraftingPayPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            money_amount: s.read_i32()?,
        })
    }

    fn run(self, _server: &GameServer, _conn: &mut GameConnection) {
        tracing::warn!(
            "SetCraftingPay, ObjId: {}, MoneyAmount: {}",
            self.obj_id,
            self.money_amount
        );
    }
}
sync_client_packet!(CSSetCraftingPayPacket);

/// `SCCraftFailedPacket(id, craftId, count)` (never sent by the 1.2 C#; "TODO needs fixing").
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCCraftFailedPacket {
    pub id: u32,
    pub craft_id: u32,
    pub count: i32,
}
packet_def!(
    SCCraftFailedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCraftFailedPacket
);
impl Marshal for SCCraftFailedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id).write_i32(self.count);
        for _ in 0..self.count.max(0) {
            s.write_u32(self.craft_id);
        }
    }
}
