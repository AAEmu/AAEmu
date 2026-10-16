//! The economy subsystem's implementations of the other subsystems' hooks (installed by
//! [`install`] from `System::start`; tests call it too):
//!
//! | hook | implementation |
//! |---|---|
//! | skills `ItemSkillHooks` | [`EconomyForSkills`]: gear stats, holdables, item counts / consumes, the item `SpecialEffect`s (`item_effects::*`), the item effects (`GainLootPackItem`, `ImprintUcc`, `CleanupUcc`, `PutDownBackpack`, `RecoverExp`, `CraftEffect` collect / craft), the gear buffs of `UpdateGearBonuses` |
//! | npcs `EconomyBridge` | [`EconomyForNpcs`]: NPC loot (`LootManager.GenerateLoot`), doodad func items, coffers |
//! | quests `QuestHooks` (item / money / mail part) | [`EconomyForQuests`] (`server.systems.quests.economy_hooks`) |
//! | housing `HousingEconomyHooks` | [`EconomyForHousing`] |
//! | vehicles `VehicleItemHooks` | [`EconomyForVehicles`] (`EndCraft`) |
//! | economy `MailHousingHooks`, `CraftHooks`, `ItemEffectHooks` | [`EconomyComposite`]: direct calls into housing / npcs / skills / quests |
//! | services `WebApiEconomyHooks`, `CHARACTER_ITEM_OPS`, `SheetMusicInventory`, `BUYBACK_WIPE` | [`EconomyForServices`] |

use std::collections::HashSet;
use std::sync::Arc;

use crate::data::items::TemplateKind;
use crate::models::container::{equip_slot, slot_type, ContainerRef, ItemContainer};
use crate::models::item::{
    EquipStat, Item, ItemClass, BIND_ON_UNPACK, ITEM_FLAG_SKINIZED, ITEM_FLAG_UNPACKED,
};
use crate::models::item_task::{task_type, ItemFxList, ItemTask};
use crate::server::GameServer;
use crate::systems::skills::data::{EffectKind, EffectTemplate};
use crate::systems::skills::effects::EffectArgs;
use crate::systems::skills::ext::{
    GearStats, GradeEnchantRequest, HoldableInfo, ItemActionResult, ItemSkillHooks,
};
use crate::systems::skills::types::{SkillCastTarget, SkillCaster, SkillObject};
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::world::{DoodadRef, NpcRef, PlayerRef, WorldObject};

use super::error_message;
use super::inventory::{self, send_error_message_to, Inv, ItemTxn, Out};
use super::item_effects::{self, Outcome, SkillItem};
use super::item_packets::SCItemTaskSuccessPacket;
use super::loot::SharedRandom;

fn lock(c: &ContainerRef) -> std::sync::MutexGuard<'_, ItemContainer> {
    c.lock().expect("container poisoned")
}

fn task_packet(action: u8, tasks: Vec<ItemTask>) -> SCItemTaskSuccessPacket {
    SCItemTaskSuccessPacket {
        action,
        tasks,
        force_remove: Vec::new(),
    }
}

/// `SkillItem` of the caster object.
fn skill_item(c: &SkillCaster) -> Option<SkillItem> {
    match *c {
        SkillCaster::Item {
            item_id,
            item_template_id,
            ..
        } => Some(SkillItem {
            item_id,
            item_template_id,
        }),
        _ => None,
    }
}

/// `SkillCastItemTarget` `(Id, ObjId)`.
fn item_target(t: &SkillCastTarget) -> Option<(u64, u32)> {
    match *t {
        SkillCastTarget::Item { id, obj_id, .. } => Some((id, obj_id)),
        _ => None,
    }
}

fn player_caster<'a>(args: &'a EffectArgs<'_>) -> Option<&'a PlayerRef> {
    args.caster.as_ref().and_then(WorldObject::as_player)
}

fn player_target<'a>(args: &'a EffectArgs<'_>) -> Option<&'a PlayerRef> {
    args.target.as_obj().and_then(WorldObject::as_player)
}

/// `skill.Cancelled = true`.
fn cancel_skill(args: &EffectArgs<'_>) {
    if let Some(s) = args.source.skill() {
        s.state().cancelled = true;
    }
}

/// The equipment items of a unit (`Equipment.Items`).
fn equipment_items(unit: &WorldObject) -> Vec<Item> {
    match unit {
        WorldObject::Player(p) => lock(&Inv::of(p).equipment()).items.clone(),
        WorldObject::Npc(n) => n.data().equipment.iter().flatten().cloned().collect(),
        WorldObject::Mate(m) => m.data().equipment.iter().flatten().cloned().collect(),
        WorldObject::Slave(s) => s.data().equipment.iter().flatten().cloned().collect(),
        _ => Vec::new(),
    }
}

/// `Equipment.GetItemBySlot(slot)` of a unit.
fn equipped_by_slot(unit: &WorldObject, slot: i32) -> Option<Item> {
    match unit {
        WorldObject::Player(p) => Inv::of(p).equipped(slot),
        _ => equipment_items(unit).into_iter().find(|i| i.slot == slot),
    }
}

fn holdable_info(server: &GameServer, item: &Item) -> Option<HoldableInfo> {
    let data = &server.game_data.items;
    let t = data.template(item.template_id)?;
    let h = data.holdable_of(t)?;
    Some(HoldableInfo {
        holdable_id: h.id,
        slot_type_id: h.slot_type_id,
        min_range: h.min_range as f32,
        max_range: h.max_range as f32,
        damage_scale: h.damage_scale as f32,
        speed: h.speed as f32,
    })
}

// ---------------------------------------------------------------------------------------------
// skills: ItemSkillHooks
// ---------------------------------------------------------------------------------------------

/// The skills subsystem's view of items (`ItemSkillHooks`).
pub struct EconomyForSkills;

/// `BuffConstants` used by `UpdateGearBonuses`.
mod buff_constants {
    pub const ARMOR_BUFF_TAG: u32 = 145;
    pub const EQUIPMENT_BUFF_TAG: u32 = 156;
    pub const CLOTH_4P: u32 = 713;
    pub const CLOTH_7P: u32 = 714;
    pub const LEATHER_4P: u32 = 715;
    pub const LEATHER_7P: u32 = 716;
    pub const PLATE_4P: u32 = 717;
    pub const PLATE_7P: u32 = 740;
    pub const EQUIP_DUALWIELD: u32 = 4899;
    pub const EQUIP_SHIELD: u32 = 8226;
    pub const EQUIP_TWO_HANDED: u32 = 8227;
}

/// `EquipmentItemSlotType` values the gear buffs check.
mod gear_slot_type {
    pub const BACK: u32 = 9;
    pub const MAINHAND: u32 = 14;
    pub const OFFHAND: u32 = 15;
    pub const TWO_HANDED: u32 = 16;
    pub const ONE_HANDED: u32 = 17;
    pub const SHIELD: u32 = 20;
}

/// `ArmorType`.
mod armor_type {
    pub const CLOTH: u32 = 1;
    pub const LEATHER: u32 = 2;
    pub const METAL: u32 = 3;
}

/// `ItemGrade.Arcane`.
const ITEM_GRADE_ARCANE: u8 = 4;
/// `Item.Dye` category.
const CATEGORY_DYE: i32 = 33;

impl EconomyForSkills {
    /// `new Buff(this, this, SkillCasterUnit(..), template, null, now) { AbLevel }` +
    /// `Buffs.AddBuff`.
    /// `Buffs.AddBuff(new Buff(this, this, skillCaster, template, ...) { AbLevel })` of the gear
    /// code; `caster_obj` is the `SkillCasterUnit` object id (`new SkillCasterUnit()` = 0 for
    /// equip effects and armor buffs, `SkillCasterUnit(ObjId)` for wield and item-set buffs).
    fn add_gear_buff(
        server: &GameServer,
        unit: &WorldObject,
        buff_id: u32,
        ab_level: u32,
        caster_obj: u32,
    ) {
        use crate::systems::skills::buffs;
        let Some(t) = server.game_data.systems.skills.buff(buff_id).cloned() else {
            return;
        };
        let mut b = buffs::new_buff(
            server,
            &t,
            Some(unit),
            &SkillCaster::Unit(caster_obj),
            None,
            crate::world::now_ms(),
        );
        if ab_level > 0 {
            b.ab_level = ab_level;
        }
        buffs::add_buff(server, unit, b, 0, 0);
    }

    fn check_buff(unit: &WorldObject, buff_id: u32) -> bool {
        unit.with_base_unit(|b| b.buffs().check_buff(buff_id))
    }

    /// `ItemGameData.GetItemBuff(templateId, grade) ?? GetBuffTemplate(Template.BuffId)`.
    fn item_buff(server: &GameServer, item: &Item) -> Option<u32> {
        let econ = &server.game_data.systems.economy.item_effects;
        econ.item_grade_buff(item.template_id, item.grade)
            .or_else(|| {
                server
                    .game_data
                    .items
                    .template(item.template_id)
                    .map(|t| t.buff_id)
            })
            .filter(|id| *id != 0 && server.game_data.systems.skills.buff(*id).is_some())
    }

    /// The charged buff part of `ApplyEquipEffects` (`RechargeBuffId`).
    fn charge_buff_active(server: &GameServer, item: &Item) -> Option<u32> {
        let t = server.game_data.items.template(item.template_id)?;
        let e = t.equip()?;
        if e.recharge_buff_id == 0 {
            return None;
        }
        let bind_on_unpack = t.bind_type & BIND_ON_UNPACK != 0;
        let mut check_expire = if bind_on_unpack {
            item.unpack_time
        } else {
            item.charge_start_time
        };
        check_expire += i64::from(e.charge_lifetime) * 60;
        let now = chrono::Utc::now().timestamp();
        let mut add = false;
        if e.charge_lifetime > 0 && check_expire > now {
            add = true;
        }
        if e.charge_count > 0 && item.charge_count > 0 {
            add = true;
        }
        if bind_on_unpack && !item.has_flag(ITEM_FLAG_UNPACKED) {
            add = false;
        }
        add.then_some(e.recharge_buff_id)
    }

    fn is_destroyed(server: &GameServer, item: &Item) -> bool {
        item.is_equip() && !item.is_not_destroyed(&server.game_data.items)
    }

    /// `Unit.ApplyEquipEffects(itemAdded, itemRemoved)`.
    fn apply_equip_effects(
        server: &GameServer,
        unit: &WorldObject,
        items: &[Item],
        added: Option<&Item>,
        removed: Option<&Item>,
    ) {
        use crate::systems::skills::buffs;
        let data = &server.game_data.items;
        if let Some(r) = removed {
            if let Some(id) = Self::item_buff(server, r) {
                if Self::check_buff(unit, id) {
                    buffs::remove_buff(server, unit, id);
                }
            }
            if let Some(e) = data.template(r.template_id).and_then(|t| t.equip()) {
                if e.recharge_buff_id > 0 && Self::check_buff(unit, e.recharge_buff_id) {
                    buffs::remove_buff(server, unit, e.recharge_buff_id);
                }
            }
        }
        if let Some(a) = added {
            if !Self::is_destroyed(server, a) {
                let level = data.template(a.template_id).map_or(0, |t| t.level) as u32;
                if let Some(id) = Self::item_buff(server, a) {
                    Self::add_gear_buff(server, unit, id, level, 0);
                }
                if let Some(id) = Self::charge_buff_active(server, a) {
                    Self::add_gear_buff(server, unit, id, level, 0);
                }
            }
        }
        if added.is_none() && removed.is_none() {
            // This is the first load check to apply buffs for equipped items.
            buffs::remove_buffs_by_tag(server, unit, buff_constants::EQUIPMENT_BUFF_TAG, 20);
            for item in items {
                let Some(t) = data.template(item.template_id) else {
                    continue;
                };
                let level = t.level as u32;
                if t.buff_id != 0 {
                    if let Some(id) = Self::item_buff(server, item) {
                        Self::add_gear_buff(server, unit, id, level, 0);
                    }
                }
                if let Some(id) = Self::charge_buff_active(server, item) {
                    Self::add_gear_buff(server, unit, id, level, 0);
                }
            }
        }
    }

    /// `Unit.GetWeaponWieldKind()` + `ApplyWeaponWieldBuff()`.
    fn apply_weapon_wield_buff(server: &GameServer, unit: &WorldObject) {
        use crate::systems::skills::buffs;
        buffs::remove_buff(server, unit, buff_constants::EQUIP_DUALWIELD);
        buffs::remove_buff(server, unit, buff_constants::EQUIP_SHIELD);
        buffs::remove_buff(server, unit, buff_constants::EQUIP_TWO_HANDED);
        let slot_type_of = |slot: i32| {
            equipped_by_slot(unit, slot)
                .and_then(|i| holdable_info(server, &i))
                .map(|h| h.slot_type_id)
        };
        let main = slot_type_of(equip_slot::MAINHAND);
        let off = slot_type_of(equip_slot::OFFHAND);
        let buff = match main {
            Some(gear_slot_type::TWO_HANDED) => Some(buff_constants::EQUIP_TWO_HANDED),
            Some(gear_slot_type::ONE_HANDED | gear_slot_type::MAINHAND)
                if matches!(
                    off,
                    Some(gear_slot_type::ONE_HANDED | gear_slot_type::OFFHAND)
                ) =>
            {
                Some(buff_constants::EQUIP_DUALWIELD)
            }
            // None / OneHanded: a shield in the offhand
            _ => (off == Some(gear_slot_type::SHIELD)).then_some(buff_constants::EQUIP_SHIELD),
        };
        if let Some(id) = buff {
            Self::add_gear_buff(server, unit, id, 0, unit.obj_id());
        }
    }

    /// `Unit.ApplyArmorGradeBuff(itemAdded, itemRemoved)`.
    fn apply_armor_grade_buff(
        server: &GameServer,
        unit: &WorldObject,
        items: &[Item],
        added: Option<&Item>,
        removed: Option<&Item>,
    ) {
        use crate::systems::skills::buffs;
        let data = &server.game_data.items;
        let is_armor = |i: Option<&Item>| i.is_some_and(|i| i.class == ItemClass::Armor);
        if (added.is_some() || removed.is_some()) && !is_armor(added) && !is_armor(removed) {
            return;
        }
        if added.is_some_and(|a| Self::is_destroyed(server, a)) {
            // Destroyed item, ignore
            return;
        }
        // Clear any existing armor grade buffs
        buffs::remove_buffs_by_tag(server, unit, buff_constants::ARMOR_BUFF_TAG, 10);
        // Get armor pieces by kind (insertion order of the kinds, as the C# dictionary)
        let mut kinds: Vec<(u32, Vec<&Item>)> = Vec::new();
        for item in items {
            if item.class != ItemClass::Armor {
                continue;
            }
            let Some(t) = data.template(item.template_id) else {
                continue;
            };
            if !matches!(t.kind, TemplateKind::Armor(_)) {
                continue;
            }
            // armorTemplate.SlotTemplate.SlotTypeId == Back
            if t.equip()
                .is_some_and(|e| e.slot_type_id == gear_slot_type::BACK)
            {
                continue;
            }
            let Some(kind) = data.wearable_kind_of(t).map(|k| k.type_id) else {
                continue;
            };
            match kinds.iter_mut().find(|(k, _)| *k == kind) {
                Some((_, v)) => v.push(item),
                None => kinds.push((kind, vec![item])),
            }
        }
        // Get kind with most pieces
        let Some(mut best) = kinds.first() else {
            return;
        };
        for k in &kinds {
            if k.1.len() > best.1.len() {
                best = k;
            }
        }
        let pieces = &best.1;
        if pieces.len() < 4 {
            return;
        }
        // finalArmorTemplate.WearableTemplate.TypeId
        let Some(armor_type) = data
            .template(pieces[0].template_id)
            .and_then(|t| data.wearable_of(t))
            .map(|k| k.type_id)
        else {
            return;
        };
        let set_buff = match (pieces.len() == 7, armor_type) {
            (true, armor_type::CLOTH) => Some(buff_constants::CLOTH_7P),
            (true, armor_type::LEATHER) => Some(buff_constants::LEATHER_7P),
            (true, armor_type::METAL) => Some(buff_constants::PLATE_7P),
            (false, armor_type::CLOTH) => Some(buff_constants::CLOTH_4P),
            (false, armor_type::LEATHER) => Some(buff_constants::LEATHER_4P),
            (false, armor_type::METAL) => Some(buff_constants::PLATE_4P),
            _ => None,
        };
        if let Some(id) = set_buff {
            Self::add_gear_buff(server, unit, id, 0, 0);
        }
        // Get only pieces >= arcane
        let above: Vec<&&Item> = pieces
            .iter()
            .filter(|p| p.grade >= ITEM_GRADE_ARCANE)
            .collect();
        if above.len() < 4 {
            return;
        }
        let total_level: i32 = above
            .iter()
            .map(|p| data.template(p.template_id).map_or(0, |t| t.level))
            .sum();
        // This const was calculated by hand, it might make no sense.
        let ab_level = total_level as f32 * 0.406_705_54_f32;
        let grade_buff_ab_level = ab_level * ab_level / 15.0 + 30.0;
        let lowest_grade = above.iter().map(|p| p.grade).min().unwrap_or(0);
        let Some(grade_buff) = data.armor_grade_buff(armor_type, u32::from(lowest_grade)) else {
            // C#: NullReferenceException on GetArmorGradeBuff(..).BuffId
            tracing::error!(
                "ApplyArmorGradeBuff: no armor grade buff for type {armor_type} grade {lowest_grade}"
            );
            return;
        };
        Self::add_gear_buff(
            server,
            unit,
            grade_buff.buff_id,
            grade_buff_ab_level as u32,
            0,
        );
    }

    /// `Unit.ApplyEquipItemSetBonuses()`. The item procs of the set bonuses are not registered:
    /// `UnitProcs.RollProcsForKind` skips every proc whose cooldown has elapsed (and
    /// `LastProc` starts at `DateTime.MinValue`), so a C# proc never fires.
    fn apply_equip_item_set_bonuses(server: &GameServer, unit: &WorldObject, items: &[Item]) {
        use crate::systems::skills::buffs;
        let data = &server.game_data.items;
        let mut set_pieces: Vec<(u32, i32, u32)> = Vec::new(); // (set, pieces, min level)
        for item in items {
            let Some(t) = data.template(item.template_id) else {
                continue;
            };
            let Some(e) = t.equip() else { continue };
            if e.equip_item_set_id == 0 {
                continue;
            }
            match set_pieces
                .iter_mut()
                .find(|(s, _, _)| *s == e.equip_item_set_id)
            {
                Some((_, n, lvl)) => {
                    *n += 1;
                    if (t.level as u32) < *lvl {
                        *lvl = t.level as u32;
                    }
                }
                None => set_pieces.push((e.equip_item_set_id, 1, t.level as u32)),
            }
        }
        let mut applied: HashSet<u32> = HashSet::new();
        for (set_id, count, level) in set_pieces {
            let Some(set) = data.equip_item_set(set_id) else {
                // C#: NullReferenceException
                tracing::error!("ApplyEquipItemSetBonuses: unknown equip item set {set_id}");
                continue;
            };
            for bonus in &set.bonuses {
                if count >= bonus.num_pieces {
                    if bonus.buff_id != 0 {
                        if Self::check_buff(unit, bonus.buff_id) {
                            applied.insert(bonus.buff_id);
                            continue;
                        }
                        Self::add_gear_buff(server, unit, bonus.buff_id, level, unit.obj_id());
                        applied.insert(bonus.buff_id);
                    }
                } else if bonus.buff_id != 0
                    && Self::check_buff(unit, bonus.buff_id)
                    && !applied.contains(&bonus.buff_id)
                {
                    buffs::remove_buff(server, unit, bonus.buff_id);
                }
            }
        }
    }

    /// `ItemConversion` / `GradeEnchant` ... `SpecialEffect` bodies (`item_effects`), `true` =
    /// handled.
    fn special(server: &GameServer, special_type: i32, args: &EffectArgs<'_>, v: [i32; 4]) -> bool {
        /// `SpecialType` values whose `Execute` body is the economy's.
        mod special {
            pub const GAIN_ITEM: i32 = 27;
            pub const GAIN_ITEM_WITH_EMBLEM_IMPRINT: i32 = 45;
            pub const ITEM_CONVERSION: i32 = 49;
            pub const FISHING_LOOT: i32 = 79;
            pub const GRADE_ENCHANT: i32 = 92;
            pub const CLEANUP_LOOK_CONVERT: i32 = 103;
            pub const ITEM_SOCKETING: i32 = 106;
            pub const ITEM_CAP_SCALE_RESET: i32 = 111;
            pub const ITEM_CAP_SCALE: i32 = 112;
        }
        let caster_item = skill_item(&args.caster_caster);
        let target_item = item_target(&args.target_caster).map(|(id, _)| id);
        let tl = args.source.skill().map_or(0, |s| s.tl_id());
        let mut rng = SharedRandom;
        match special_type {
            special::GAIN_ITEM => {
                if let Some(p) = player_caster(args) {
                    item_effects::gain_item(server, p, v);
                }
            }
            special::GAIN_ITEM_WITH_EMBLEM_IMPRINT => {
                item_effects::gain_item_with_emblem_imprint(
                    server,
                    player_target(args),
                    caster_item,
                    v,
                );
            }
            special::ITEM_CONVERSION => {
                let out = match player_caster(args) {
                    Some(p) => item_effects::item_conversion(server, p, target_item, v, &mut rng),
                    None => Outcome::Cancelled,
                };
                if out == Outcome::Cancelled {
                    cancel_skill(args);
                }
            }
            special::FISHING_LOOT => {
                if let (Some(p), Some(t)) = (player_caster(args), args.target.as_obj()) {
                    item_effects::fishing_loot(server, p, t, v, &mut rng);
                }
            }
            special::GRADE_ENCHANT => {
                if let Some(p) = player_caster(args) {
                    let support = match args.skill_object {
                        SkillObject::ItemGradeEnchantingSupport {
                            support_item_id, ..
                        } if *support_item_id != 0 => Some(*support_item_id),
                        _ => None,
                    };
                    item_effects::grade_enchant(
                        server,
                        p,
                        caster_item,
                        target_item,
                        support,
                        v,
                        tl,
                        &mut rng,
                    );
                }
            }
            special::CLEANUP_LOOK_CONVERT => {
                let out = match player_caster(args) {
                    Some(p) => {
                        item_effects::cleanup_look_convert(server, p, caster_item, target_item, v)
                    }
                    None => Outcome::Cancelled,
                };
                if out == Outcome::Cancelled {
                    cancel_skill(args);
                }
            }
            special::ITEM_SOCKETING => {
                if let Some(p) = player_caster(args) {
                    item_effects::item_socketing(
                        server,
                        p,
                        caster_item,
                        target_item,
                        v,
                        tl,
                        &mut rng,
                    );
                }
            }
            special::ITEM_CAP_SCALE_RESET => {
                if let Some(p) = player_caster(args) {
                    item_effects::item_cap_scale_reset(server, p, caster_item, target_item, v);
                }
            }
            special::ITEM_CAP_SCALE => {
                if let Some(p) = player_caster(args) {
                    let skill_id = args.source.skill().map_or(0, |s| s.template.id);
                    item_effects::item_cap_scale(
                        server,
                        p,
                        caster_item,
                        target_item,
                        skill_id,
                        v,
                        &mut rng,
                    );
                }
            }
            _ => return false,
        }
        true
    }

    /// `RecoverExpEffect.Apply`.
    fn recover_exp(
        server: &GameServer,
        e: &crate::systems::skills::data::effects::RecoverExpEffect,
        args: &EffectArgs<'_>,
    ) {
        let Some(player) = player_caster(args) else {
            return;
        };
        let (name, level, recoverable, labor) = {
            let c = player.data();
            (
                c.unit.name.clone(),
                c.unit.level,
                c.recoverable_exp,
                c.labor_power,
            )
        };
        tracing::debug!("Player {name}");
        let mut out = Out::Direct;
        if recoverable <= 0 {
            // Is this one correct?
            send_error_message_to(
                &mut out,
                player,
                error_message::CANNOT_RECOVER_ALL_NOT_ENOUGH,
            );
            return;
        }
        // TODO (C#): Verify this formula
        let level = i32::from(level);
        let needed = if level <= 50 {
            level
        } else {
            50 + (level - 50) * 20
        };
        if e.need_labor_power && i32::from(labor) < needed {
            send_error_message_to(&mut out, player, error_message::NOT_ENOUGH_LABOR_POWER);
            return;
        }
        // NeedMoney: not used in 1.2
        if e.need_priest {
            let me = WorldObject::from(player);
            let found = server.world.world_of(&me).is_some_and(|w| {
                w.around(&me, |o| o.as_npc().cloned())
                    .into_iter()
                    .filter(|n| {
                        use crate::systems::skills::geometry::{distance_to, Target};
                        let t = Target::Obj(WorldObject::from(n));
                        distance_to(server, &Target::Obj(me.clone()), &t, false) <= 10.0
                    })
                    .any(|n| {
                        let tid = n.data().unit.template_id;
                        server
                            .game_data
                            .systems
                            .npcs
                            .npc
                            .template(tid)
                            .is_some_and(|t| t.priest)
                    })
            });
            if !found {
                send_error_message_to(&mut out, player, error_message::TOO_FAR_AWAY);
                return;
            }
        }
        // Use labor and recover exp (not ChangeLabor: no labor exp)
        player.data().labor_power -= needed as i16;
        player.send(
            crate::systems::skills::progress::SCCharacterLaborPowerChangedPacket {
                amount: -needed,
                action: 0,
                point: 0,
                step: 0,
            },
        );
        player.send(crate::systems::skills::progress::SCRecoverableExpPacket {
            obj_id: player.obj_id,
            recoverable_exp: 0,
            penaltied_exp: 0,
            reason: 1,
        });
        crate::systems::skills::progress::add_exp(server, player, recoverable, false);
        player.data().recoverable_exp = 0;
    }

    /// The non-house, non-shipyard part of `CraftEffect.Apply` (housing and vehicles take their
    /// targets first): `Craft` / `Collect` / default -> `Craft.EndCraft()`, then
    /// `DoDoodadInteractionEvents(character, character, target.TemplateId)`.
    fn craft_effect(server: &GameServer, world_interaction: u32, args: &EffectArgs<'_>) -> bool {
        use crate::systems::npcs::area_trigger::world_interaction_group as g;
        let Some(character) = player_caster(args) else {
            return false;
        };
        let Some(target) = args.target.as_obj() else {
            return false;
        };
        tracing::debug!("CraftEffect, {world_interaction}");
        let wi_group = server
            .game_data
            .systems
            .npcs
            .area_trigger
            .get_world_interaction_group(world_interaction);
        match wi_group {
            Some(g::CRAFT | g::COLLECT) => super::craft::end_craft(server, character),
            _ => {
                tracing::warn!(
                    "CraftEffect, {world_interaction} does not have a wi group ({wi_group:?})"
                );
                super::craft::end_craft(server, character);
            }
        }
        let template_id = target.with_base_unit(|b| b.template_id());
        crate::systems::quests::events::on_doodad_interaction(
            server,
            character,
            character,
            template_id,
        );
        true
    }
}

impl ItemSkillHooks for EconomyForSkills {
    fn drop_trade_pack_to_floor(&self, server: &GameServer, player: &PlayerRef) {
        item_effects::drop_trade_pack_to_floor(server, player);
    }

    fn gear_stats(&self, server: &GameServer, player: &PlayerRef) -> GearStats {
        let data = &server.game_data.items;
        let formulas = &server.game_data.formulas;
        let items = lock(&Inv::of(player).equipment()).items.clone();
        let mut g = GearStats::default();
        for item in &items {
            if item.is_equip() && item.is_not_destroyed(data) {
                let st = |w| f64::from(item.stat(data, w).unwrap_or(0));
                g.str_ += st(EquipStat::Str);
                g.dex += st(EquipStat::Dex);
                g.sta += st(EquipStat::Sta);
                g.int += st(EquipStat::Int);
                g.spi += st(EquipStat::Spi);
            }
            match item.class {
                ItemClass::Armor | ItemClass::Accessory => {
                    g.armor += item.base_armor(data, formulas).unwrap_or(0);
                    g.magic_resistance += item.base_magic_resistance(data, formulas).unwrap_or(0);
                }
                ItemClass::Weapon => g.armor += item.weapon_armor(data).unwrap_or(0),
                _ => {}
            }
        }
        let weapon = |slot: i32| {
            items
                .iter()
                .find(|i| i.slot == slot && i.class == ItemClass::Weapon)
        };
        if let Some(w) = weapon(equip_slot::MAINHAND) {
            g.mainhand_dps = w.dps(data).unwrap_or(0.0);
            g.mainhand_mdps = w.mdps(data).unwrap_or(0.0) as f32;
            g.mainhand_hdps = w.hdps(data).unwrap_or(0.0) as f32;
        }
        if let Some(w) = weapon(equip_slot::OFFHAND) {
            g.offhand_dps = w.dps(data).unwrap_or(0.0);
        }
        if let Some(w) = weapon(equip_slot::RANGED) {
            g.ranged_dps = w.dps(data).unwrap_or(0.0);
        }
        g
    }

    fn weapon_holdable(
        &self,
        server: &GameServer,
        unit: &WorldObject,
        slot: i32,
    ) -> Option<HoldableInfo> {
        let item = equipped_by_slot(unit, slot)?;
        holdable_info(server, &item)
    }

    fn weapon_attack_anim(
        &self,
        server: &GameServer,
        holdable_id: u32,
        index: i32,
        left_hand: bool,
    ) -> u32 {
        server
            .game_data
            .items
            .holdable(holdable_id)
            .map_or(crate::data::items::Holdable::DEFAULT_FIST_ANIM_ID, |h| {
                h.attack_anim_id(index, left_hand)
            })
    }

    fn has_equipped(&self, _server: &GameServer, player: &PlayerRef, slot: i32) -> bool {
        Inv::of(player).equipped(slot).is_some()
    }

    fn item_count(&self, _server: &GameServer, player: &PlayerRef, template_id: u32) -> i32 {
        Inv::of(player).items_count(template_id, -1)
    }

    fn owned_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        item_id: u64,
    ) -> Option<(u32, i32)> {
        server
            .items
            .item_by_id(item_id)
            .filter(|i| i.owner_id == u64::from(player.character_id))
            .map(|i| (i.template_id, i.count))
    }

    fn use_skill_as_reagent(&self, server: &GameServer, template_id: u32) -> bool {
        server
            .game_data
            .items
            .template(template_id)
            .is_some_and(|t| t.use_skill_as_reagent)
    }

    fn item_use_skill(&self, server: &GameServer, template_id: u32) -> Option<(u32, bool)> {
        server.game_data.items.template(template_id).map(|t| {
            (
                t.use_skill_id,
                t.bind_type == crate::data::items::bind_type::BIND_ON_PICKUP,
            )
        })
    }

    fn consume_item_by_id(
        &self,
        server: &GameServer,
        _player: &PlayerRef,
        item_id: u64,
        amount: i32,
    ) {
        let Some(c) = server.items.holding_container(item_id) else {
            return;
        };
        let mut txn = ItemTxn::begin(server);
        let template_id = lock(&c).get_item_by_id(item_id).map(|i| i.template_id);
        if let Some(t) = template_id {
            lock(&c).consume_item(
                task_type::SKILL_REAGENTS,
                t,
                amount,
                Some(item_id),
                &mut txn.env,
            );
        }
    }

    fn consume_item_template(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        template_id: u32,
        amount: i32,
    ) {
        let mut txn = ItemTxn::begin(server);
        inventory::consume_item(
            &mut txn,
            &Inv::of(player),
            None,
            task_type::SKILL_EFFECT_CONSUMPTION,
            template_id,
            amount,
            None,
        );
    }

    fn acquire_item(&self, server: &GameServer, player: &PlayerRef, template_id: u32, amount: i32) {
        let mut txn = ItemTxn::begin(server);
        lock(&Inv::of(player).bag()).acquire_default_item(
            task_type::SKILL_EFFECT_GAIN_ITEM,
            template_id,
            amount,
            -1,
            0,
            &mut txn.env,
        );
    }

    fn item_set(&self, server: &GameServer, item_set_id: u32) -> Vec<(u32, i32)> {
        server
            .game_data
            .items
            .item_set(item_set_id)
            .map(|s| s.items.iter().map(|i| (i.item_id, i.count)).collect())
            .unwrap_or_default()
    }

    fn durability_loss(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        amount: i32,
        targets: u8,
        rate: f32,
    ) {
        use super::repair::DurabilityLossTargets as T;
        let t = match targets {
            0 => T::All,
            1 => T::AllArmor,
            2 => T::AllWeapons,
            3 => T::AllMainWeapons,
            4 => T::PrimaryWeapon,
            5 => T::SecondaryWeapon,
            6 => T::RangedWeapon,
            7 => T::Shield,
            _ => T::Feet,
        };
        super::repair::apply_durability_loss_to_equipment(
            server,
            player,
            amount.clamp(0, i32::from(u8::MAX)) as u8,
            t,
            rate,
            &mut SharedRandom,
        );
    }

    fn unlock_item(&self, server: &GameServer, player: &PlayerRef, item_id: u64) {
        // SCItemTaskSuccessPacket(ItemUnlock, ItemUpdate(skillItem.SkillSourceItem))
        match server.items.item_by_id(item_id) {
            Some(item) => player.send(task_packet(
                task_type::ITEM_UNLOCK,
                vec![ItemTask::update(&item)],
            )),
            None => tracing::error!(
                "Plot: SkillSourceItem {item_id} no longer exists (NullReferenceException)"
            ),
        }
    }

    fn apply_gear_buffs(
        &self,
        server: &GameServer,
        unit: &WorldObject,
        added: Option<&Item>,
        removed: Option<&Item>,
    ) {
        let items = equipment_items(unit);
        Self::apply_equip_effects(server, unit, &items, added, removed);
        Self::apply_weapon_wield_buff(server, unit);
        Self::apply_armor_grade_buff(server, unit, &items, added, removed);
        Self::apply_equip_item_set_bonuses(server, unit, &items);
    }

    fn apply_effect(
        &self,
        server: &GameServer,
        kind: EffectKind,
        template: &EffectTemplate,
        args: &EffectArgs<'_>,
    ) -> bool {
        match (kind, template) {
            (EffectKind::GainLootPackItemEffect, EffectTemplate::GainLootPackItem(e)) => {
                // Ignore if not a player
                if let Some(p) = player_caster(args) {
                    let effect = item_effects::GainLootPackItemEffect {
                        loot_pack_id: e.loot_pack_id,
                        consume_source_item: e.consume_source_item,
                        consume_item_id: e.consume_item_id,
                        consume_count: e.consume_count,
                        inherit_grade: e.inherit_grade,
                    };
                    item_effects::gain_loot_pack_item(
                        server,
                        p,
                        &effect,
                        skill_item(&args.caster_caster),
                        &mut SharedRandom,
                    );
                }
                true
            }
            (EffectKind::ImprintUccEffect, EffectTemplate::ImprintUcc { .. }) => {
                tracing::trace!("ImprintUccEffect");
                let out = match player_caster(args).filter(|caster| {
                    player_target(args).is_some_and(|target| target.obj_id == caster.obj_id)
                }) {
                    Some(player) => item_effects::imprint_ucc(
                        server,
                        Some(player),
                        skill_item(&args.caster_caster),
                        item_target(&args.target_caster).map(|(id, _)| id),
                    ),
                    None => Outcome::Cancelled,
                };
                if out == Outcome::Cancelled {
                    cancel_skill(args);
                }
                true
            }
            (EffectKind::CleanupUccEffect, EffectTemplate::CleanupUcc { .. }) => {
                tracing::trace!("CleanupUccEffect");
                let out = match player_caster(args).filter(|caster| {
                    player_target(args).is_some_and(|target| target.obj_id == caster.obj_id)
                }) {
                    Some(player) => item_effects::cleanup_ucc(
                        server,
                        Some(player),
                        skill_item(&args.caster_caster),
                        item_target(&args.target_caster).map(|(id, _)| id),
                    ),
                    None => Outcome::Cancelled,
                };
                if out == Outcome::Cancelled {
                    cancel_skill(args);
                }
                true
            }
            (EffectKind::ConversionEffect, EffectTemplate::Conversion(_)) => {
                item_effects::conversion_effect();
                true
            }
            (
                EffectKind::PutDownBackpackEffect,
                EffectTemplate::PutDownBackpack {
                    backpack_doodad_id, ..
                },
            ) => {
                if let Some(p) = player_caster(args) {
                    item_effects::put_down_backpack(
                        server,
                        p,
                        *backpack_doodad_id,
                        skill_item(&args.caster_caster),
                    );
                }
                true
            }
            (EffectKind::RecoverExpEffect, EffectTemplate::RecoverExp(e)) => {
                Self::recover_exp(server, e, args);
                true
            }
            (
                EffectKind::CraftEffect,
                EffectTemplate::Craft {
                    world_interaction, ..
                },
            ) => Self::craft_effect(server, *world_interaction, args),
            // Trace-only effects in C#.
            (EffectKind::TrainCraftEffect, _) => {
                tracing::trace!("TrainCraftEffect");
                true
            }
            (EffectKind::AccountAttributeEffect, _) => {
                tracing::trace!("AccountAttributeEffect");
                true
            }
            _ => false,
        }
    }

    fn try_add_new_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task_type: u8,
        template_id: u32,
        count: i32,
        grade: u8,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        inventory::try_add_new_item(
            &mut txn,
            &Inv::of(player),
            task_type,
            template_id,
            count,
            i32::from(grade),
            0,
        )
    }

    fn item_template_exists(&self, server: &GameServer, template_id: u32) -> bool {
        server.game_data.items.template(template_id).is_some()
    }

    fn acquire_default_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task_type: u8,
        template_id: u32,
        amount: i32,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        lock(&Inv::of(player).bag()).acquire_default_item(
            task_type,
            template_id,
            amount,
            -1,
            0,
            &mut txn.env,
        )
    }

    fn consume_bag_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task_type: u8,
        template_id: u32,
        amount: i32,
    ) -> i32 {
        let mut txn = ItemTxn::begin(server);
        lock(&Inv::of(player).bag()).consume_item(
            task_type,
            template_id,
            amount,
            None,
            &mut txn.env,
        )
    }

    fn ucc_create_stamp(&self, server: &GameServer, player: &PlayerRef, source_item_id: u64) {
        if let Some(source) = server.items.item_by_id(source_item_id) {
            item_effects::create_stamp(server, player, &source);
        }
    }

    fn item_conversion(&self, server: &GameServer, player: &PlayerRef, item_id: u64) -> bool {
        item_effects::item_conversion(server, player, Some(item_id), [0; 4], &mut SharedRandom)
            == Outcome::Cancelled
    }

    fn dye_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        dye_item_id: u64,
        dye_template_id: u32,
        target_item_id: u64,
    ) {
        let mut txn = ItemTxn::begin(server);
        let inv = Inv::of(player);
        // Skill.Apply consumes its source item after effects, unless this flag cancels it.
        // Reject before editing equipment so an invalid target also keeps its dye.
        let reject = || player.data().skill_char.skill_cancelled = true;
        let Some(dye_holder) = inv.holder_of(dye_item_id) else {
            reject();
            return;
        };
        let dye = lock(&dye_holder).get_item_by_id(dye_item_id).cloned();
        if !dye.is_some_and(|i| i.template_id == dye_template_id && i.count > 0) {
            reject();
            return;
        }
        let Some(holder) = inv.holder_of(target_item_id) else {
            tracing::error!(
                "Dyeing: target item {target_item_id} not found (NullReferenceException)"
            );
            reject();
            return;
        };
        // Check the item used is in the Dye (33) category.
        let category = server
            .game_data
            .items
            .template(dye_template_id)
            .map_or(0, |t| t.category_id);
        if category != CATEGORY_DYE {
            tracing::warn!(
                "{} tried to dye with a non-dye item - item id {dye_item_id} and template id {dye_template_id}",
                player.data().unit.name
            );
            reject();
            return;
        }
        let target = lock(&holder).get_item_by_id(target_item_id).cloned();
        if !target.is_some_and(|i| {
            i.is_equip()
                && server
                    .game_data
                    .items
                    .default_dye_ids
                    .contains_key(&i.template_id)
        }) {
            reject();
            return;
        }
        let after = {
            let mut g = lock(&holder);
            let Some(it) = g.get_item_by_id_mut(target_item_id) else {
                drop(g);
                reject();
                return;
            };
            it.details.dye_item_id = dye_template_id;
            it.dirty = true;
            it.clone()
        };
        txn.out().send(
            player,
            task_packet(task_type::DYEING, vec![ItemTask::update(&after)]),
        );
    }

    fn give_fishing_loot(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _zone_group_id: u32,
        _land: bool,
    ) -> Option<bool> {
        // `FishingLoot` is handled whole by `apply_special_effect`.
        None
    }

    fn cleanup_look_convert(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        target_item_id: u64,
        powder_item_id: Option<u64>,
    ) -> ItemActionResult {
        let powder = powder_item_id.and_then(|id| {
            server.items.item_by_id(id).map(|i| SkillItem {
                item_id: id,
                item_template_id: i.template_id,
            })
        });
        match item_effects::cleanup_look_convert(
            server,
            player,
            powder,
            Some(target_item_id),
            [0; 4],
        ) {
            Outcome::Done => ItemActionResult::Done,
            Outcome::Cancelled => ItemActionResult::Cancel,
        }
    }

    fn skinize(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        target_item_id: u64,
        powder_item_id: Option<u64>,
    ) -> ItemActionResult {
        let mut txn = ItemTxn::begin(server);
        let inv = Inv::of(player);
        let Some(holder) = inv.holder_of(target_item_id) else {
            return ItemActionResult::Cancel;
        };
        let skinized = lock(&holder)
            .get_item_by_id(target_item_id)
            .is_some_and(|i| i.has_flag(ITEM_FLAG_SKINIZED));
        if skinized {
            // Already an image item
            // TODO (C#): Get the correct error as this is likely not correct
            return ItemActionResult::CancelWithError(
                error_message::ITEM_LOOK_CONVERT_AS_NOT_USE_AS_SKIN,
            );
        }
        let Some(powder_id) = powder_item_id else {
            return ItemActionResult::Cancel;
        };
        let Some(powder) = inv.item_by_id(powder_id) else {
            return ItemActionResult::Cancel;
        };
        if powder.count < 1 {
            return ItemActionResult::Cancel;
        }
        let after = {
            let mut g = lock(&holder);
            let Some(it) = g.get_item_by_id_mut(target_item_id) else {
                return ItemActionResult::Cancel;
            };
            it.flags |= ITEM_FLAG_SKINIZED;
            it.dirty = true;
            it.clone()
        };
        txn.out().send(
            player,
            task_packet(task_type::SKINIZE, vec![ItemTask::update_bits(&after)]),
        );
        if let Some(c) = inv.holder_of_any(powder.id) {
            lock(&c).consume_item(
                task_type::SKINIZE,
                powder.template_id,
                1,
                Some(powder.id),
                &mut txn.env,
            );
        }
        ItemActionResult::Done
    }

    fn item_socketing(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        gem_item_id: u64,
        target_item_id: u64,
    ) -> bool {
        // Handled whole by `apply_special_effect` (which sends the skill end); this path only
        // runs without the special-effect routing.
        let gem = server.items.item_by_id(gem_item_id).map(|i| SkillItem {
            item_id: gem_item_id,
            item_template_id: i.template_id,
        });
        item_effects::item_socketing(
            server,
            player,
            gem,
            Some(target_item_id),
            [0; 4],
            0,
            &mut SharedRandom,
        );
        false
    }

    fn grade_enchant(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        request: &GradeEnchantRequest,
    ) -> bool {
        item_effects::grade_enchant(
            server,
            player,
            Some(SkillItem {
                item_id: request.scroll_item_id,
                item_template_id: request.scroll_template_id,
            }),
            Some(request.target_item_id),
            (request.support_item_id != 0).then_some(request.support_item_id),
            [i32::from(request.is_lucky), 0, request.item_type, 0],
            0,
            &mut SharedRandom,
        );
        false
    }

    fn equipped_item_category(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        slot: i32,
    ) -> Option<u32> {
        let item = Inv::of(player).equipped(slot)?;
        server
            .game_data
            .items
            .template(item.template_id)
            .map(|t| t.category_id as u32)
    }

    fn create_sheet_music(&self, server: &GameServer, player: &PlayerRef, item_id: u64) -> bool {
        // MusicManager.CreateSheetMusic is a DB call: it runs as a task here (deviation: the
        // C# blocks and returns its result; failures are logged by the music manager).
        let (Some(srv), Ok(handle)) = (server.self_arc(), tokio::runtime::Handle::try_current())
        else {
            return false;
        };
        let player = player.clone();
        handle.spawn(async move {
            let ok = srv
                .systems
                .services
                .music
                .create_sheet_music(&srv, &player, item_id, &EconomySheetMusic { server: &srv })
                .await;
            if !ok {
                tracing::error!(
                    "Special effects: UserMusicSaveNotes - Error saving music for {} !",
                    player.data().unit.name
                );
            }
        });
        true
    }

    fn apply_special_effect(
        &self,
        server: &GameServer,
        special_type: i32,
        args: &EffectArgs<'_>,
        values: [i32; 4],
    ) -> bool {
        Self::special(server, special_type, args, values)
    }
}

#[cfg(test)]
#[path = "dye_tests.rs"]
mod dye_tests;

// ---------------------------------------------------------------------------------------------
// services: SheetMusicInventory / CharacterItemOps / WebApi / buy-back wipe
// ---------------------------------------------------------------------------------------------

/// The inventory side of `MusicManager.CreateSheetMusic`.
pub struct EconomySheetMusic<'a> {
    pub server: &'a GameServer,
}

impl crate::systems::services::music::SheetMusicInventory for EconomySheetMusic<'_> {
    fn source_item(&self, player: &PlayerRef, item_id: u64) -> Option<(u32, u32)> {
        let c = Inv::of(player).holder_of_any(item_id)?;
        let g = lock(&c);
        let owner = g.owner_id;
        g.get_item_by_id(item_id).map(|i| (owner, i.template_id))
    }

    fn bag_free_slots(&self, player: &PlayerRef) -> i32 {
        lock(&Inv::of(player).bag()).free_slot_count()
    }

    fn add_sheet_music(&self, player: &PlayerRef, song_id: u32) -> bool {
        let server = self.server;
        let mut txn = ItemTxn::begin(server);
        let Some(mut sheet) = txn.env.create(crate::data::items::ITEM_SHEET_MUSIC, 1, 0) else {
            return false;
        };
        sheet.owner_id = u64::from(player.character_id);
        sheet.made_unit_id = player.character_id;
        sheet.details.song_id = song_id;
        let data = txn.data();
        let bag = Inv::of(player).bag();
        let res = lock(&bag).add_item(
            task_type::SAVE_MUSIC_NOTES,
            sheet,
            -1,
            data,
            &mut txn.env.fx,
        );
        match res {
            Ok(r) => r,
            Err(item) => {
                server.items.add_loose(item);
                false
            }
        }
    }

    fn consume_source(&self, player: &PlayerRef, item_id: u64, template_id: u32) -> i32 {
        let mut txn = ItemTxn::begin(self.server);
        lock(&Inv::of(player).bag()).consume_item(
            task_type::SAVE_MUSIC_NOTES,
            template_id,
            1,
            Some(item_id),
            &mut txn.env,
        )
    }
}

/// The services subsystem's inventory operations (`CHARACTER_ITEM_OPS`) and WebApi economy.
pub struct EconomyForServices;

impl crate::systems::services::item_ops::CharacterItemOps for EconomyForServices {
    fn items_count(&self, _server: &GameServer, player: &PlayerRef, template_id: u32) -> i32 {
        Inv::of(player).items_count_in(slot_type::INVENTORY, template_id, -1)
    }

    fn consume_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        template_id: u32,
        count: i32,
    ) -> i32 {
        let mut txn = ItemTxn::begin(server);
        lock(&Inv::of(player).bag()).consume_item(task, template_id, count, None, &mut txn.env)
    }

    fn replace_hair(&self, server: &GameServer, player: &PlayerRef, hair_model: u32) -> bool {
        let mut txn = ItemTxn::begin(server);
        let equipment = Inv::of(player).equipment();
        let mut g = lock(&equipment);
        // Remove old hair item
        if let Some(old) = g.get_item_by_slot(equip_slot::HAIR).map(|i| i.id) {
            g.remove_item_task(task_type::INVALID, old, true, &mut txn.env);
        }
        // Create new hair item
        let res = g.acquire_default_item_ex(
            task_type::INVALID,
            hair_model,
            1,
            -1,
            player.character_id,
            equip_slot::HAIR,
            &mut txn.env,
        );
        let name = player.data().unit.name.clone();
        match res {
            Some(r) if r.has_tasks => {
                if r.new.len() != 1 {
                    tracing::error!(
                        "Something failed during hairstyle creation for player {name} ({})!",
                        player.character_id
                    );
                }
                true
            }
            _ => false,
        }
    }
}

fn naive(t: crate::models::item::UnixTime) -> chrono::NaiveDateTime {
    chrono::DateTime::from_timestamp(t, 0)
        .unwrap_or_default()
        .naive_utc()
}

fn web_mail_info(
    server: &GameServer,
    m: &super::mail::BaseMail,
) -> crate::systems::services::webapi::hooks::WebApiMailInfo {
    use crate::systems::services::webapi::json::DateKind;
    let _ = server;
    crate::systems::services::webapi::hooks::WebApiMailInfo {
        id: m.id,
        sender_id: m.header.sender_id,
        sender_name: m.header.sender_name.clone(),
        receiver_id: m.header.receiver_id,
        receiver_name: m.receiver_name.clone(),
        is_delivered: m.is_delivered,
        title: m.title.clone(),
        mail_type: m.mail_type,
        send_date: (naive(m.body.send_date), DateKind::Unspecified),
        receive_date: (naive(m.body.recv_date), DateKind::Unspecified),
        open_date: (naive(m.open_date), DateKind::Unspecified),
        attachment_count: i32::from(m.get_total_attachment_count()),
        copper_coins: i64::from(m.body.copper_coins),
    }
}

/// The Newtonsoft form of an `AuctionLot` (the `Item` member is reduced to its stored fields).
fn auction_lot_json(
    lot: &super::auction::AuctionLot,
) -> crate::systems::services::webapi::json::JValue {
    use crate::systems::services::webapi::json::{DateKind, JValue};
    let item = match &lot.item {
        Some(i) => JValue::Object(vec![
            ("Id".into(), JValue::UInt(i.id)),
            ("TemplateId".into(), JValue::UInt(u64::from(i.template_id))),
            ("Count".into(), JValue::Int(i64::from(i.count))),
            ("Grade".into(), JValue::UInt(u64::from(i.grade))),
            ("OwnerId".into(), JValue::UInt(i.owner_id)),
            ("SlotType".into(), JValue::UInt(u64::from(i.slot_type))),
            ("Slot".into(), JValue::Int(i64::from(i.slot))),
        ]),
        None => JValue::Null,
    };
    let now = chrono::Utc::now();
    JValue::Object(vec![
        ("Id".into(), JValue::UInt(lot.id)),
        ("Duration".into(), JValue::UInt(u64::from(lot.duration))),
        ("Item".into(), item),
        (
            "EndTime".into(),
            JValue::Date(lot.end_time.naive_utc(), DateKind::Utc),
        ),
        ("TimeLeft".into(), JValue::UInt(lot.time_left(now))),
        ("WorldId".into(), JValue::UInt(u64::from(lot.world_id))),
        ("ClientId".into(), JValue::UInt(u64::from(lot.client_id))),
        ("ClientName".into(), JValue::Str(lot.client_name.clone())),
        ("StartMoney".into(), JValue::Int(i64::from(lot.start_money))),
        (
            "DirectMoney".into(),
            JValue::Int(i64::from(lot.direct_money)),
        ),
        (
            "PostDate".into(),
            JValue::Date(lot.post_date.naive_utc(), DateKind::Utc),
        ),
        (
            "BidWorldId".into(),
            JValue::UInt(u64::from(lot.bid_world_id)),
        ),
        ("BidderId".into(), JValue::UInt(u64::from(lot.bidder_id))),
        ("BidderName".into(), JValue::Str(lot.bidder_name.clone())),
        ("BidMoney".into(), JValue::Int(i64::from(lot.bid_money))),
        ("Extra".into(), JValue::Int(i64::from(lot.extra))),
        ("IsDirty".into(), JValue::Bool(lot.dirty)),
    ])
}

impl crate::systems::services::webapi::hooks::WebApiEconomyHooks for EconomyForServices {
    fn send_mail(
        &self,
        server: &GameServer,
        mail: crate::systems::services::webapi::hooks::WebApiMail,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        let mut m = super::mail::BaseMail {
            mail_type: mail.mail_type,
            title: mail.title,
            receiver_name: mail.receiver_name,
            ..super::mail::BaseMail::default()
        };
        m.header.sender_id = mail.sender_id;
        m.header.sender_name = mail.sender_name;
        m.header.receiver_id = mail.receiver_id;
        m.header.extra = mail.extra;
        m.body.text = mail.text;
        m.body.send_date = mail.send_date.and_utc().timestamp();
        m.body.recv_date = mail.recv_date.and_utc().timestamp();
        m.body.copper_coins = mail.copper_coins;
        m.body.billing_amount = mail.billing_amount;
        for item in mail.attachments {
            m.body.attachments.push(item.id);
            server.items.add_loose(item);
        }
        super::mail::send(&mut txn, m)
    }

    fn current_mail_list(
        &self,
        server: &GameServer,
        character_id: u32,
    ) -> Vec<crate::systems::services::webapi::hooks::WebApiMailInfo> {
        let ids = super::mail::get_current_mail_list_for(server, character_id);
        ids.into_iter()
            .filter_map(|id| server.systems.economy.mail.get_mail_by_id(id))
            .map(|m| web_mail_info(server, &m))
            .collect()
    }

    fn mail_by_id(
        &self,
        server: &GameServer,
        mail_id: i64,
    ) -> Option<crate::systems::services::webapi::hooks::WebApiMailInfo> {
        server
            .systems
            .economy
            .mail
            .get_mail_by_id(mail_id)
            .map(|m| web_mail_info(server, &m))
    }

    fn delete_mail(&self, server: &GameServer, mail_id: i64, trash_items: bool) -> bool {
        let mut txn = ItemTxn::begin(server);
        super::mail::delete_mail_trash(&mut txn, mail_id, trash_items)
    }

    fn auction_lots(
        &self,
        server: &GameServer,
    ) -> anyhow::Result<Vec<crate::systems::services::webapi::hooks::AuctionLotView>> {
        Ok(server
            .systems
            .economy
            .auction
            .all_lots()
            .iter()
            .map(
                |lot| crate::systems::services::webapi::hooks::AuctionLotView {
                    json: auction_lot_json(lot),
                    template_id: lot.item.as_ref().map_or(0, |i| i.template_id),
                    client_name: lot.client_name.clone(),
                    stack_size: lot.item.as_ref().map_or(0, |i| i.count.max(0) as u32),
                    direct_money: lot.direct_money,
                    bid_money: lot.bid_money,
                    bidder_name: Some(lot.bidder_name.clone()),
                },
            )
            .collect())
    }

    fn create_auction_lot(
        &self,
        server: &GameServer,
        lot: crate::systems::services::webapi::hooks::NewAuctionLot,
    ) -> anyhow::Result<crate::systems::services::webapi::json::JValue> {
        let new = super::auction::AuctionManager::create_auction_lot(
            server,
            lot.client_id,
            &lot.client_name,
            lot.item,
            lot.start_price,
            lot.buyout_price,
            lot.duration,
            chrono::Utc::now(),
        )
        .ok_or_else(|| anyhow::anyhow!("CreateAuctionLot failed"))?;
        let json = auction_lot_json(&new);
        server.systems.economy.auction.add_auction_lot(new);
        Ok(json)
    }

    fn acquire_auction_items(
        &self,
        server: &GameServer,
        character_id: u32,
        template_id: u32,
        quantity: i32,
        grade: u8,
    ) -> anyhow::Result<Option<Vec<Item>>> {
        let mut txn = ItemTxn::begin(server);
        let c = server.items.container_for_character(
            character_id,
            slot_type::AUCTION,
            0,
            &server.ids.container,
        );
        let res = lock(&c).acquire_default_item_ex(
            task_type::INVALID,
            template_id,
            quantity,
            i32::from(grade),
            0,
            -1,
            &mut txn.env,
        );
        let Some(res) = res.filter(|r| r.has_tasks) else {
            return Ok(None);
        };
        let g = lock(&c);
        Ok(Some(
            res.new
                .iter()
                .filter_map(|id| g.get_item_by_id(*id).cloned())
                .collect(),
        ))
    }
}

// ---------------------------------------------------------------------------------------------
// npcs: EconomyBridge
// ---------------------------------------------------------------------------------------------

/// The npcs subsystem's view of items, loot, money and coffers (`EconomyBridge`).
pub struct EconomyForNpcs;

impl crate::systems::npcs::hooks::EconomyBridge for EconomyForNpcs {
    fn on_npc_loot(&self, server: &GameServer, npc: &NpcRef, killer: Option<&PlayerRef>) -> bool {
        let owner = WorldObject::from(npc);
        let killer = killer.map(WorldObject::from);
        super::looting::LootManager::generate_loot(server, &owner, killer.as_ref());
        server.systems.economy.loot.item_count(npc.obj_id) > 0
    }

    fn forget_loot(&self, server: &GameServer, obj_id: u32) {
        server.systems.economy.loot.forget(obj_id);
    }

    fn item_template_and_ucc(&self, server: &GameServer, item_id: u64) -> Option<(u32, u64)> {
        server
            .items
            .item_by_id(item_id)
            .map(|i| (i.template_id, i.ucc_id))
    }

    fn acquire_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        template_id: u32,
        count: i32,
        grade: i32,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        inventory::try_add_new_item(
            &mut txn,
            &Inv::of(player),
            task,
            template_id,
            count,
            grade,
            0,
        )
    }

    fn consume_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        template_id: u32,
        count: i32,
    ) -> i32 {
        let mut txn = ItemTxn::begin(server);
        inventory::consume_item(
            &mut txn,
            &Inv::of(player),
            None,
            task,
            template_id,
            count,
            None,
        )
    }

    fn item_count(&self, _server: &GameServer, player: &PlayerRef, template_id: u32) -> i32 {
        Inv::of(player).items_count(template_id, -1)
    }

    fn give_loot_pack(&self, server: &GameServer, player: &PlayerRef, loot_pack_id: u32) -> bool {
        let Some(pack) = server.game_data.systems.economy.loot.pack(loot_pack_id) else {
            return false;
        };
        let mut txn = ItemTxn::begin(server);
        super::loot::give_loot_pack(
            &mut txn,
            player,
            pack,
            item_effects::actability_type::NONE,
            task_type::LOOT_ALL,
            None,
            None,
            &mut SharedRandom,
        )
    }

    fn change_labor(&self, server: &GameServer, player: &PlayerRef, change: i16, actability: i32) {
        crate::systems::skills::progress::change_labor(server, player, change, actability);
    }

    fn change_money(&self, server: &GameServer, player: &PlayerRef, amount: i64, task: u8) -> bool {
        let mut txn = ItemTxn::begin(server);
        let amount = amount.clamp(i64::from(i32::MIN), i64::from(i32::MAX)) as i32;
        if amount < 0 {
            if i64::from(-amount) > player.data().money {
                send_error_message_to(&mut txn.out(), player, error_message::NOT_ENOUGH_MONEY);
                return false;
            }
            inventory::subtract_money(&mut txn.out(), player, slot_type::INVENTORY, -amount, task)
        } else {
            inventory::add_money(&mut txn.out(), player, slot_type::INVENTORY, amount, task)
        }
    }

    fn npc_has_loot_items(&self, server: &GameServer, npc: &NpcRef) -> bool {
        server.systems.economy.loot.item_count(npc.obj_id) > 0
    }

    fn make_loot_public_tick(&self, server: &GameServer, world_id: u32) {
        let Some(world) = server.world.worlds().into_iter().find(|w| w.id == world_id) else {
            return;
        };
        let npcs: Vec<u32> = world
            .all_of_kind(crate::world::ObjectKind::Npc)
            .iter()
            .map(WorldObject::obj_id)
            .collect();
        for id in server.systems.economy.loot.npcs_to_make_public(&npcs) {
            super::looting::LootManager::make_loot_public(server, id);
        }
    }

    fn send_specialty_mail(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _mail: &crate::systems::npcs::specialty::SpecialtyMail,
        _for_crafter: bool,
    ) -> bool {
        // The specialty sale is the economy's (`store::sell_specialty`), which sends its mails
        // itself; the npcs copy of the flow is not registered.
        false
    }

    fn consume_equipment_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        template_id: u32,
        count: i32,
        preferred_item_id: u64,
    ) -> i32 {
        let mut txn = ItemTxn::begin(server);
        lock(&Inv::of(player).equipment()).consume_item(
            task,
            template_id,
            count,
            (preferred_item_id != 0).then_some(preferred_item_id),
            &mut txn.env,
        )
    }

    fn is_auto_equip_trade_pack(&self, server: &GameServer, template_id: u32) -> bool {
        inventory::is_auto_equip_trade_pack(&server.game_data.items, template_id)
    }

    fn takeoff_backpack(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        gliders_only: bool,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        inventory::take_off_backpack(&mut txn, &Inv::of(player), task, gliders_only)
    }

    fn can_replace_glider_in_backpack_slot(&self, server: &GameServer, player: &PlayerRef) -> bool {
        inventory::can_replace_glider_in_backpack_slot(&server.game_data.items, &Inv::of(player))
    }

    fn create_item_in_backpack_slot(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        template_id: u32,
        count: i32,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        let Some(item) = txn.env.create(template_id, count, 0) else {
            return false;
        };
        let data = txn.data();
        let res = lock(&Inv::of(player).equipment()).add_item(
            task,
            item,
            equip_slot::BACKPACK,
            data,
            &mut txn.env.fx,
        );
        match res {
            Ok(r) => r,
            Err(item) => {
                server.items.add_loose(item);
                false
            }
        }
    }

    fn move_item_to_backpack_slot(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        item_id: u64,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        let target = Inv::of(player).equipment();
        super::mail::add_or_move_existing_item(
            &mut txn,
            &target,
            task,
            item_id,
            equip_slot::BACKPACK,
        )
    }

    fn move_item_to_bag(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        item_id: u64,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        let target = Inv::of(player).bag();
        super::mail::add_or_move_existing_item(&mut txn, &target, task, item_id, -1)
    }

    fn move_item_to_system_container(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        item_id: u64,
    ) -> bool {
        move_to_system_container(server, player, task, item_id)
    }

    fn remove_equipment_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        item_id: u64,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        lock(&Inv::of(player).equipment())
            .remove_item_task(task, item_id, true, &mut txn.env)
            .is_some()
    }

    fn bag_space_left_for_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        template_id: u32,
    ) -> i32 {
        let money = player.data().money;
        lock(&Inv::of(player).bag()).space_left_for_template(
            template_id,
            &server.game_data.items,
            money,
        )
    }

    fn bag_consume_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        template_id: u32,
        count: i32,
    ) -> i32 {
        let mut txn = ItemTxn::begin(server);
        lock(&Inv::of(player).bag()).consume_item(task, template_id, count, None, &mut txn.env)
    }

    fn try_equip_new_backpack(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        template_id: u32,
        count: i32,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        inventory::try_equip_new_backpack(
            &mut txn,
            &Inv::of(player),
            task,
            template_id,
            count,
            -1,
            0,
        )
    }

    fn give_doodad_loot_pack(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        loot_pack_id: u32,
        skill_id: u32,
        task: u8,
    ) -> Option<bool> {
        let pack = server.game_data.systems.economy.loot.pack(loot_pack_id)?;
        // SkillManager.GetSkillActAbility(skillId)
        let act = server
            .game_data
            .systems
            .skills
            .skill(skill_id)
            .map_or(0, |s| s.actability_group_id.max(0) as u8);
        let mut rng = SharedRandom;
        let contents =
            super::loot::generate_pack_for_player(server, pack, player, act, None, &mut rng)
                .ok()?;
        if lock(&Inv::of(player).bag()).free_slot_count() < contents.len() as i32 {
            return Some(false);
        }
        let mut txn = ItemTxn::begin(server);
        let given = super::loot::give_loot_pack(
            &mut txn,
            player,
            pack,
            act,
            task,
            Some(contents),
            None,
            &mut rng,
        );
        if given {
            // GET_LOOTPACK (deferred until the item transaction is released).
            crate::systems::report_achievement(
                server,
                player,
                AchievementInput::Increment {
                    kind_id: char_record_kind::GET_LOOTPACK,
                    amount: 1,
                    matcher: RecordMatch::Value1(loot_pack_id),
                },
            );
        }
        Some(true)
    }

    fn try_sell_equipped_fish(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        backpack_id: u64,
        backpack_template_id: u32,
        buy_func_id: u32,
    ) -> bool {
        super::fishing::sell_equipped_fish(
            server,
            player,
            backpack_id,
            backpack_template_id,
            buy_func_id,
        )
    }

    fn try_convert_equipped_fish(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        backpack_id: u64,
        backpack_template_id: u32,
        convert_func_id: u32,
        task: u8,
    ) -> bool {
        super::fishing::convert_equipped_fish(
            server,
            player,
            backpack_id,
            backpack_template_id,
            convert_func_id,
            task,
        )
    }

    fn give_convert_fish_trophy(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        backpack_template_id: u32,
        task: u8,
    ) {
        let mut rng = SharedRandom;
        let Some(trophies) = super::loot::loot_convert_fish(server, backpack_template_id, &mut rng)
        else {
            return;
        };
        // TODO (C#): use only the first item
        let Some(trophy) = trophies.into_iter().next() else {
            return;
        };
        let mut txn = ItemTxn::begin(server);
        let econ = &server.game_data.systems.economy;
        let Some(fish) = econ.items.create_big_fish(
            &server.game_data.items,
            &trophy,
            server.config.id,
            &mut rng,
            chrono::Utc::now().timestamp(),
        ) else {
            tracing::error!(
                "DoodadFuncConvertFish: no fish details for {} (KeyNotFoundException)",
                trophy.made_unit_id
            );
            return;
        };
        let caught = (fish.details.fish_length, fish.details.fish_weight);
        let data = txn.data();
        let res = lock(&Inv::of(player).bag()).add_item(task, fish, -1, data, &mut txn.env.fx);
        if let Err(item) = res {
            server.items.add_loose(item);
        }
        super::fishing::record_catch(server, player, caught.0, caught.1);
    }

    fn coffer_chest_type(&self, server: &GameServer, container_id: u64) -> u8 {
        server
            .items
            .container_by_id(container_id)
            .map_or(0, |c| lock(&c).coffer_type)
    }

    fn family_of_character(&self, server: &GameServer, character_id: u32) -> u32 {
        server
            .systems
            .social
            .families
            .family_of_character(character_id)
    }

    fn expedition_of_character(&self, server: &GameServer, character_id: u32) -> u32 {
        server.expeditions.expedition_of_character(character_id)
    }

    fn magical_enchant(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        target_item_id: u64,
        rune_template_id: u32,
    ) {
        item_effects::magical_enchant(
            server,
            player,
            Some(SkillItem {
                item_id: 0,
                item_template_id: rune_template_id,
            }),
            Some(target_item_id),
        );
    }

    fn doodad_source_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        item_id: u64,
        bag_only: bool,
    ) -> Option<crate::systems::npcs::hooks::DoodadSourceItem> {
        let inv = Inv::of(player);
        let item = if bag_only {
            lock(&inv.bag()).get_item_by_id(item_id).cloned()
        } else {
            inv.item_by_id(item_id)
        }?;
        let t = server.game_data.items.template(item.template_id);
        Some(crate::systems::npcs::hooks::DoodadSourceItem {
            item_id: item.id,
            template_id: item.template_id,
            max_count: t.map_or(1, |t| t.max_count),
            use_skill_id: t.map_or(0, |t| t.use_skill_id),
        })
    }

    fn item_ids_from_doodad(&self, server: &GameServer, doodad_template_id: u32) -> Vec<u32> {
        server
            .game_data
            .items
            .item_ids_from_doodad(doodad_template_id)
            .to_vec()
    }

    fn consume_doodad_source_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        template_id: u32,
        preferred_item_id: Option<u64>,
    ) {
        if let Some(id) = preferred_item_id {
            // character.ItemUse(preferredItem)
            if let Some(item) = server.items.item_by_id(id) {
                crate::systems::quests::events::on_item_use(server, player, item.template_id);
            }
        }
        let mut txn = ItemTxn::begin(server);
        inventory::consume_item(
            &mut txn,
            &Inv::of(player),
            Some(&[slot_type::INVENTORY]),
            task_type::DOODAD_CREATE,
            template_id,
            1,
            preferred_item_id,
        );
    }

    fn initialize_coffer(&self, server: &GameServer, doodad: &DoodadRef, owner_character_id: u32) {
        let c = server
            .items
            .new_coffer_container(owner_character_id, &server.ids.container);
        let mut d = doodad.data();
        let Some(coffer) = d.coffer.as_mut() else {
            return;
        };
        let mut g = lock(&c);
        g.set_container_size(coffer.capacity);
        coffer.container_id = g.container_id;
    }

    fn try_consume_doodad_source_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        doodad_template_id: u32,
        item_id: u64,
        labor_cost: i16,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        let inv = Inv::of(player);
        let bag = inv.bag();
        let mut bag = lock(&bag);
        let Some(item) = bag.get_item_by_id(item_id).cloned() else {
            return false;
        };
        if item.count < 1
            || labor_cost < 0
            || !server
                .game_data
                .items
                .item_ids_from_doodad(doodad_template_id)
                .contains(&item.template_id)
            || player.data().labor_power < labor_cost
        {
            return false;
        }
        let preserve = server
            .game_data
            .items
            .template(item.template_id)
            .is_some_and(|t| t.max_count == 1);
        if preserve {
            if item.count != 1 {
                return false;
            }
            drop(bag);
            let Some(system) = inv.get(slot_type::SYSTEM) else {
                return false;
            };
            if !super::mail::add_or_move_existing_item(
                &mut txn,
                &system,
                task_type::DOODAD_CREATE,
                item_id,
                -1,
            ) {
                return false;
            }
        } else {
            if bag.consume_item(
                task_type::DOODAD_CREATE,
                item.template_id,
                1,
                Some(item_id),
                &mut txn.env,
            ) != 1
            {
                return false;
            }
            drop(bag);
        }
        if labor_cost > 0 {
            inventory::change_labor(&mut txn.out(), server, player, -labor_cost, 0);
        }
        drop(txn);
        crate::systems::quests::events::on_item_use(server, player, item.template_id);
        true
    }

    fn remove_expired_doodad_item(&self, server: &GameServer, item_id: u64) {
        let Some(c) = server.items.holding_container(item_id) else {
            return;
        };
        let mut txn = ItemTxn::begin(server);
        let mut g = lock(&c);
        if matches!(g.slot_type, slot_type::NONE | slot_type::SYSTEM) {
            g.remove_item_task(task_type::INVALID, item_id, true, &mut txn.env);
        }
    }

    fn release_deleted_doodad_items(&self, server: &GameServer, item_id: u64, container_id: u64) {
        // item._holdingContainer = null; itemManager.ReleaseId(item.Id)
        if item_id != 0 && server.items.item_by_id(item_id).is_some() {
            if let Some(c) = server.items.holding_container(item_id) {
                lock(&c).remove_item(item_id);
            }
            server.items.release_id(item_id, &server.ids.item);
        }
        // itemManager.DeleteItemContainer(container)
        if container_id != 0 && server.items.forget_container(container_id) {
            if let Ok(rt) = tokio::runtime::Handle::try_current() {
                let pool = server.db.clone();
                rt.spawn(async move {
                    if let Err(e) = crate::managers::item::ItemManager::delete_container_row(
                        &pool,
                        container_id,
                    )
                    .await
                    {
                        tracing::error!("DeleteItemContainer {container_id}: {e}");
                    }
                });
            }
        }
    }
}

/// `Inventory.SystemContainer.AddOrMoveExistingItem(task, item)`.
fn move_to_system_container(
    server: &GameServer,
    player: &PlayerRef,
    task: u8,
    item_id: u64,
) -> bool {
    let mut txn = ItemTxn::begin(server);
    let Some(target) = Inv::of(player).get(slot_type::SYSTEM) else {
        tracing::error!("character {} has no system container", player.character_id);
        return false;
    };
    super::mail::add_or_move_existing_item(&mut txn, &target, task, item_id, -1)
}

// ---------------------------------------------------------------------------------------------
// quests: QuestHooks (item / money / mail part)
// ---------------------------------------------------------------------------------------------

/// The item / money / mail part of the quest `QuestHooks` (installed into
/// `server.systems.quests.economy_hooks`). These run while the quest code holds the character
/// lock: the item operations use an unlocked [`ItemTxn`] on the character's containers, and
/// their effects (item task packets, quest item events) are flushed after the lock is released
/// (`quests::events::after_unlock`).
pub struct EconomyForQuests;

impl EconomyForQuests {
    fn inv_of(c: &crate::models::character::Character) -> Inv {
        Inv {
            character_id: c.id,
            containers: c.inventory.containers.clone(),
        }
    }

    /// Flushes `fx` once the character lock is released.
    fn defer(server: &GameServer, character_id: u32, fx: ItemFxList) {
        if fx.is_empty() {
            return;
        }
        crate::systems::quests::events::after_unlock(server, character_id, move |s| {
            super::fx::flush(s, fx);
        });
    }
}

impl crate::systems::quests::hooks::QuestHooks for EconomyForQuests {
    fn change_money(
        &self,
        server: &GameServer,
        _player: Option<&PlayerRef>,
        c: &mut crate::models::character::Character,
        amount: i32,
    ) {
        // ChangeMoney(SlotType.None, SlotType.Inventory, amount) (DepositMoney)
        c.money += i64::from(amount);
        let mut fx = ItemFxList::default();
        fx.send(
            c.id,
            task_packet(
                task_type::DEPOSIT_MONEY,
                vec![ItemTask::MoneyChange(amount)],
            ),
        );
        Self::defer(server, c.id, fx);
    }

    fn acquire_default_item(
        &self,
        server: &GameServer,
        _player: Option<&PlayerRef>,
        c: &mut crate::models::character::Character,
        task: u8,
        template_id: u32,
        count: i32,
        grade_id: u8,
    ) -> bool {
        let mut txn = ItemTxn::unlocked(server);
        let inv = Self::inv_of(c);
        let ok = lock(&inv.bag()).acquire_default_item(
            task,
            template_id,
            count,
            i32::from(grade_id),
            0,
            &mut txn.env,
        );
        Self::defer(server, c.id, txn.into_fx());
        ok
    }

    fn try_equip_new_backpack(
        &self,
        server: &GameServer,
        _player: Option<&PlayerRef>,
        c: &mut crate::models::character::Character,
        task: u8,
        template_id: u32,
        count: i32,
        grade_id: u8,
    ) -> bool {
        let mut txn = ItemTxn::unlocked(server);
        let inv = Self::inv_of(c);
        let ok = inventory::try_equip_new_backpack(
            &mut txn,
            &inv,
            task,
            template_id,
            count,
            i32::from(grade_id),
            0,
        );
        Self::defer(server, c.id, txn.into_fx());
        ok
    }

    fn consume_item(
        &self,
        server: &GameServer,
        _player: Option<&PlayerRef>,
        c: &mut crate::models::character::Character,
        task: u8,
        template_id: u32,
        count: i32,
        preferred_item_id: Option<u64>,
    ) -> i32 {
        let mut txn = ItemTxn::unlocked(server);
        let inv = Self::inv_of(c);
        let n = inventory::consume_item(
            &mut txn,
            &inv,
            None,
            task,
            template_id,
            count,
            preferred_item_id,
        );
        Self::defer(server, c.id, txn.into_fx());
        n
    }

    fn send_quest_reward_mails(
        &self,
        server: &GameServer,
        player: Option<&PlayerRef>,
        c: &mut crate::models::character::Character,
        quest_template_id: u32,
        items: &[crate::systems::quests::model::ItemCreationDefinition],
        copper: i32,
    ) -> usize {
        let Some(container) = c
            .inventory
            .containers
            .iter()
            .find(|(kind, _)| *kind == slot_type::MAIL)
            .map(|(_, v)| v.clone())
        else {
            return 1;
        };
        if player.is_none() {
            return 1;
        }
        let defs: Vec<_> = items
            .iter()
            .map(|i| {
                super::mail_kinds::ItemCreationDefinition::new(
                    i.template_id,
                    i.count,
                    i32::from(i.grade_id),
                )
            })
            .collect();
        let mut txn = ItemTxn::unlocked(server);
        let Some(mails) = super::mail_kinds::create_quest_reward_mail_bodies(
            &mut txn,
            c.id,
            &c.unit.name,
            &container,
            quest_template_id,
            &defs,
            copper,
        ) else {
            Self::defer(server, c.id, txn.into_fx());
            return 1;
        };
        let ok = super::mail::store_quest_rewards_locked(&mut txn, c, mails);
        Self::defer(server, c.id, txn.into_fx());
        usize::from(!ok)
    }

    fn is_auto_equip_trade_pack(&self, server: &GameServer, template_id: u32) -> bool {
        inventory::is_auto_equip_trade_pack(&server.game_data.items, template_id)
    }

    fn can_replace_glider_in_backpack_slot(
        &self,
        server: &GameServer,
        c: &crate::models::character::Character,
    ) -> bool {
        inventory::can_replace_glider_in_backpack_slot(&server.game_data.items, &Self::inv_of(c))
    }
}

// ---------------------------------------------------------------------------------------------
// housing: HousingEconomyHooks
// ---------------------------------------------------------------------------------------------

/// The housing subsystem's view of items, money and mail (`HousingEconomyHooks`).
pub struct EconomyForHousing;

fn house_item(server: &GameServer, item: &Item) -> crate::systems::housing::hooks::HouseItem {
    crate::systems::housing::hooks::HouseItem {
        item_id: item.id,
        template_id: item.template_id,
        owner_id: item.owner_id,
        count: item.count,
        max_count: server
            .game_data
            .items
            .template(item.template_id)
            .map_or(1, |t| t.max_count),
        soul_bound: item.has_flag(crate::models::item::ITEM_FLAG_SOUL_BOUND),
        slot_type: item.slot_type,
        ucc_id: item.ucc_id,
        big_fish: (item.class == ItemClass::BigFish)
            .then_some((item.details.fish_weight, item.details.fish_length)),
    }
}

/// A `BaseMail` from a housing mail (without attachments).
fn base_mail(m: &crate::systems::housing::hooks::HouseMail) -> super::mail::BaseMail {
    let mut mail = super::mail::BaseMail {
        mail_type: m.mail_type,
        title: m.title.clone(),
        receiver_name: m.receiver_name.clone().unwrap_or_default(),
        ..super::mail::BaseMail::default()
    };
    mail.header.receiver_id = m.receiver_id;
    mail.header.sender_id = m.sender_id;
    mail.header.sender_name = m.sender_name.clone();
    mail.header.extra = m.extra;
    mail.header.status = m.status;
    mail.body.text = m.text.clone();
    mail.body.send_date = m.send_date_ms / 1000;
    mail.body.recv_date = m.recv_date_ms / 1000;
    mail.body.copper_coins = m.copper_coins;
    mail.body.billing_amount = m.billing_amount;
    mail
}

impl crate::systems::housing::hooks::HousingEconomyHooks for EconomyForHousing {
    fn pay_house_sale_certificates(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        count: i32,
    ) -> bool {
        use crate::systems::housing::hooks::item_task;
        use crate::systems::housing::manager::APPRAISAL_CERTIFICATE;
        let mut txn = ItemTxn::begin(server);
        let bag = Inv::of(player).bag();
        let mut bag = lock(&bag);
        if count <= 0 || bag.items_by_template(APPRAISAL_CERTIFICATE, -1).1 < count {
            return false;
        }
        bag.consume_item(
            item_task::BUY_HOUSE,
            APPRAISAL_CERTIFICATE,
            count,
            None,
            &mut txn.env,
        ) == count
    }

    fn pay_house_construction(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        design: crate::systems::housing::hooks::HouseItem,
        total: i32,
        tax_item: bool,
    ) -> Result<(), crate::systems::housing::hooks::HouseConstructionError> {
        if let Some(effects) =
            self.pay_house_construction_deferred(server, player, design, total, tax_item)?
        {
            super::fx::flush(server, effects);
        }
        Ok(())
    }

    fn pay_house_construction_deferred(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        design: crate::systems::housing::hooks::HouseItem,
        total: i32,
        tax_item: bool,
    ) -> Result<
        Option<crate::models::item_task::ItemFxList>,
        crate::systems::housing::hooks::HouseConstructionError,
    > {
        use crate::systems::housing::hooks::{
            construction_certificates, item_task, HouseConstructionError,
        };
        use crate::systems::housing::manager::{BOUND_TAX_CERTIFICATE, TAX_CERTIFICATE};

        // ItemTxn serializes all economy operations. Effects are flushed only after the bag
        // and character locks are released, so a quest hook cannot change the checked items.
        let mut txn = ItemTxn::begin(server);
        let inv = Inv::of(player);
        let bag = inv.bag();
        let (bound, unbound) = {
            let bag = lock(&bag);
            let Some(item) = bag.get_item_by_id(design.item_id) else {
                return Err(HouseConstructionError::InvalidDesign);
            };
            if item.owner_id != u64::from(player.character_id)
                || item.template_id != design.template_id
                || item.count < 1
                || item.slot_type != slot_type::INVENTORY
            {
                return Err(HouseConstructionError::InvalidDesign);
            }
            let bound = bag.items_by_template(BOUND_TAX_CERTIFICATE, -1).1.max(0);
            let unbound = bag.items_by_template(TAX_CERTIFICATE, -1).1.max(0);
            (bound, unbound)
        };
        if total < 0 {
            return Err(HouseConstructionError::InsufficientPayment);
        }
        let certificates = construction_certificates(total);
        if tax_item {
            if i64::from(bound) + i64::from(unbound) < i64::from(certificates) {
                return Err(HouseConstructionError::InsufficientPayment);
            }
        } else {
            if player.data().money < i64::from(total) {
                return Err(HouseConstructionError::InsufficientPayment);
            }
            if !inventory::subtract_money(
                &mut txn.out(),
                player,
                slot_type::INVENTORY,
                total,
                item_task::HOUSE_CREATION,
            ) {
                return Err(HouseConstructionError::InsufficientPayment);
            }
        }
        let mut bag = lock(&bag);
        if tax_item {
            let from_bound = bound.min(certificates);
            if from_bound > 0 {
                bag.consume_item(
                    item_task::HOUSE_CREATION,
                    BOUND_TAX_CERTIFICATE,
                    from_bound,
                    None,
                    &mut txn.env,
                );
            }
            let remainder = certificates - from_bound;
            if remainder > 0 {
                bag.consume_item(
                    item_task::HOUSE_CREATION,
                    TAX_CERTIFICATE,
                    remainder,
                    None,
                    &mut txn.env,
                );
            }
        }
        bag.consume_item(
            item_task::HOUSE_BUILDING,
            design.template_id,
            1,
            Some(design.item_id),
            &mut txn.env,
        );
        drop(bag);
        Ok(Some(txn.into_fx()))
    }

    fn inventory_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        item_id: u64,
    ) -> Option<crate::systems::housing::hooks::HouseItem> {
        let c = Inv::of(player).holder_of_any(item_id)?;
        let item = lock(&c).get_item_by_id(item_id).cloned()?;
        Some(house_item(server, &item))
    }

    fn item(
        &self,
        server: &GameServer,
        item_id: u64,
    ) -> Option<crate::systems::housing::hooks::HouseItem> {
        server
            .items
            .item_by_id(item_id)
            .map(|i| house_item(server, &i))
    }

    fn inventory_item_count(
        &self,
        _server: &GameServer,
        player: &PlayerRef,
        template_id: u32,
    ) -> i32 {
        Inv::of(player).items_count_in(slot_type::INVENTORY, template_id, -1)
    }

    fn bag_consume_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        template_id: u32,
        count: i32,
        preferred_item_id: Option<u64>,
    ) -> i32 {
        let mut txn = ItemTxn::begin(server);
        lock(&Inv::of(player).bag()).consume_item(
            task,
            template_id,
            count,
            preferred_item_id,
            &mut txn.env,
        )
    }

    fn subtract_money(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        amount: i32,
        task: u8,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        inventory::subtract_money(&mut txn.out(), player, slot_type::INVENTORY, amount, task)
    }

    fn move_item_to_system_container(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        task: u8,
        item_id: u64,
    ) -> bool {
        move_to_system_container(server, player, task, item_id)
    }

    fn destroy_item(&self, server: &GameServer, item_id: u64) {
        let Some(c) = server.items.holding_container(item_id) else {
            return;
        };
        let mut txn = ItemTxn::begin(server);
        let mut g = lock(&c);
        let Some((template_id, count)) =
            g.get_item_by_id(item_id).map(|i| (i.template_id, i.count))
        else {
            return;
        };
        g.consume_item(
            task_type::INVALID,
            template_id,
            count,
            Some(item_id),
            &mut txn.env,
        );
    }

    fn take_coffer_items(
        &self,
        server: &GameServer,
        owner_id: u32,
        container_id: u64,
    ) -> Vec<crate::systems::housing::hooks::HouseItem> {
        let Some(coffer) = server.items.container_by_id(container_id) else {
            return Vec::new();
        };
        let system = server.items.container_for_character(
            owner_id,
            slot_type::SYSTEM,
            0,
            &server.ids.container,
        );
        let mut txn = ItemTxn::begin(server);
        let data = txn.data();
        let mut ids: Vec<(i32, u64)> = lock(&coffer).items.iter().map(|i| (i.slot, i.id)).collect();
        ids.sort_by_key(|(slot, _)| std::cmp::Reverse(*slot));
        let mut out = Vec::new();
        for (_, id) in ids {
            let (mut sys, src) = inventory::lock_pair(&system, &coffer);
            let Some(mut src) = src else { break };
            if sys.move_item_from(&mut src, task_type::INVALID, id, -1, data, &mut txn.env.fx) {
                if let Some(item) = sys.get_item_by_id(id) {
                    out.push(house_item(server, item));
                }
            }
        }
        out
    }

    fn send_mail_with_new_items(
        &self,
        server: &GameServer,
        owner: &PlayerRef,
        mail: crate::systems::housing::hooks::HouseMail,
        template_id: u32,
        count: i32,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        let Some(c) = super::mail::mail_container(owner) else {
            return false;
        };
        let res = lock(&c).acquire_default_item_ex(
            task_type::INVALID,
            template_id,
            count,
            -1,
            0,
            -1,
            &mut txn.env,
        );
        let Some(res) = res.filter(|r| r.has_tasks) else {
            return false;
        };
        let mut m = base_mail(&mail);
        m.body.attachments.extend(res.new);
        super::mail::send(&mut txn, m);
        true
    }

    fn send_mail(
        &self,
        server: &GameServer,
        mail: crate::systems::housing::hooks::HouseMail,
    ) -> bool {
        use crate::systems::housing::hooks::MailAttachment;
        let mut txn = ItemTxn::begin(server);
        let mut m = base_mail(&mail);
        for a in &mail.attachments {
            match *a {
                MailAttachment::NewItem {
                    template_id,
                    count,
                    grade,
                    owner_id,
                } => {
                    let Some(mut item) = txn.env.create(template_id, count, 0) else {
                        continue;
                    };
                    item.grade = grade;
                    item.owner_id = u64::from(owner_id);
                    item.slot_type = slot_type::MAIL;
                    m.body.attachments.push(item.id);
                    server.items.add_loose(item);
                }
                MailAttachment::Existing {
                    item_id,
                    owner_id,
                    add_count,
                } => {
                    if add_count != 0 {
                        super::mail::mutate_item(server, item_id, |i| {
                            i.count += add_count;
                            i.dirty = true;
                        });
                    }
                    let target = server
                        .world
                        .character_by_id(owner_id)
                        .and_then(|p| super::mail::mail_container(&p));
                    match target {
                        Some(c) => {
                            super::mail::add_or_move_existing_item(
                                &mut txn,
                                &c,
                                task_type::INVALID,
                                item_id,
                                -1,
                            );
                        }
                        None => {
                            super::mail::mutate_item(server, item_id, |i| {
                                i.slot_type = slot_type::MAIL;
                                i.dirty = true;
                            });
                        }
                    }
                    m.body.attachments.push(item_id);
                }
            }
        }
        super::mail::send(&mut txn, m)
    }

    fn coffer_items(
        &self,
        server: &GameServer,
        container_id: u64,
    ) -> Vec<crate::systems::housing::hooks::HouseItem> {
        let Some(container) = server.items.container_by_id(container_id) else {
            return Vec::new();
        };
        let mut items = lock(&container).items.clone();
        items.sort_by_key(|item| std::cmp::Reverse(item.slot));
        items.iter().map(|item| house_item(server, item)).collect()
    }

    fn send_return_mails(
        &self,
        server: &GameServer,
        mails: Vec<crate::systems::housing::hooks::HouseMail>,
        payment: Option<(&PlayerRef, i32)>,
    ) -> bool {
        use crate::systems::housing::hooks::{item_task, MailAttachment};
        let mut txn = ItemTxn::begin(server);
        let mut prepared = Vec::new();
        let mut new_items = Vec::new();
        let mut existing = Vec::new();
        let mut reserved_mails = Vec::new();
        let mut seen_existing = HashSet::new();
        let staged: Result<(), ()> = (|| {
            if let Some((buyer, amount)) = payment {
                if amount < 0 || buyer.data().money < i64::from(amount) {
                    return Err(());
                }
            }
            for housing_mail in &mails {
                let mut mail = base_mail(housing_mail);
                let name = server
                    .names
                    .get_character_name(mail.header.receiver_id)
                    .ok_or(())?;
                if !name.eq_ignore_ascii_case(&mail.receiver_name)
                    || server.names.get_character_id(&mail.receiver_name) != mail.header.receiver_id
                    || housing_mail.attachments.len() > super::mail::MAX_MAIL_ATTACHMENTS
                {
                    return Err(());
                }
                let mail_id = server.ids.mail.get_next_id().map_err(|_| ())?;
                reserved_mails.push(mail_id);
                mail.id = i64::from(mail_id);
                for attachment in &housing_mail.attachments {
                    match *attachment {
                        MailAttachment::NewItem {
                            template_id,
                            count,
                            grade,
                            owner_id,
                        } => {
                            if count <= 0 || owner_id != mail.header.receiver_id {
                                return Err(());
                            }
                            // Reserve without ItemManager::new_id: an aborted batch must
                            // keep pending deletions for a previously used item ID intact.
                            let mut item = server
                                .items
                                .create_detached(
                                    template_id,
                                    count,
                                    0,
                                    txn.env.world_id,
                                    txn.env.data,
                                    txn.env.now,
                                )
                                .ok_or(())?;
                            item.id = u64::from(server.ids.item.get_next_id().map_err(|_| ())?);
                            item.grade = grade;
                            item.owner_id = u64::from(owner_id);
                            item.slot_type = slot_type::MAIL;
                            item.slot = mail.body.attachments.len() as i32;
                            mail.body.attachments.push(item.id);
                            new_items.push(item);
                        }
                        MailAttachment::Existing {
                            item_id,
                            owner_id,
                            add_count,
                        } => {
                            let item = server.items.item_by_id(item_id).ok_or(())?;
                            if owner_id != mail.header.receiver_id
                                || add_count < 0
                                || item.count.checked_add(add_count).is_none()
                                || !seen_existing.insert(item_id)
                            {
                                return Err(());
                            }
                            existing.push((
                                item_id,
                                owner_id,
                                add_count,
                                mail.body.attachments.len() as i32,
                            ));
                            mail.body.attachments.push(item_id);
                        }
                    }
                }
                mail.header.attachments = mail.get_total_attachment_count();
                prepared.push((mail, name));
            }
            Ok(())
        })();
        if staged.is_err() {
            for item in new_items {
                server.ids.item.release_id(item.id as u32);
            }
            for id in reserved_mails {
                server.ids.mail.release_id(id);
            }
            return false;
        }
        let mut state = server.systems.economy.mail.state();
        if prepared
            .iter()
            .any(|(mail, _)| state.all.contains_key(mail.id))
        {
            for item in new_items {
                server.ids.item.release_id(item.id as u32);
            }
            for id in reserved_mails {
                server.ids.mail.release_id(id);
            }
            return false;
        }
        // Every fallible check has completed while ItemTxn excludes other economy changes.
        if let Some((buyer, amount)) = payment {
            if !inventory::subtract_money(
                &mut txn.out(),
                buyer,
                slot_type::INVENTORY,
                amount,
                item_task::BUY_HOUSE,
            ) {
                for item in new_items {
                    server.ids.item.release_id(item.id as u32);
                }
                for id in reserved_mails {
                    server.ids.mail.release_id(id);
                }
                return false;
            }
        }
        for (id, owner, add_count, slot) in existing {
            let mut item = match server.items.holding_container(id) {
                Some(source) => lock(&source).remove_item(id),
                None => server.items.take_loose(id),
            }
            .expect("attachment was checked under the economy operation lock");
            item.owner_id = u64::from(owner);
            item.count += add_count;
            item.slot_type = slot_type::MAIL;
            item.slot = slot;
            item.dirty = true;
            server.items.add_loose(item);
        }
        for item in new_items {
            server.items.add_loose(item);
        }
        for (mail, name) in prepared {
            let id = mail.id;
            state.clear_deletion(id);
            let added = state.all.add(id, mail);
            debug_assert!(added, "mail IDs were reserved and checked before commit");
            if let Some(mail) = state.all.get_mut(id) {
                super::mail::notify_new_mail_by_name_if_online(
                    server,
                    &mut txn.env.fx,
                    mail,
                    &name,
                );
            }
        }
        true
    }

    fn delete_house_mails(&self, server: &GameServer, house_id: u32) {
        let mut txn = ItemTxn::begin(server);
        super::mail::delete_house_mails(&mut txn, house_id);
    }

    fn house_mail_ids(&self, server: &GameServer, house_id: u32) -> Vec<i64> {
        server.systems.economy.mail.get_my_house_mails(house_id)
    }

    fn update_tax_mail(
        &self,
        server: &GameServer,
        mail_id: i64,
        update: &crate::systems::housing::hooks::TaxMailUpdate,
    ) {
        server.systems.economy.mail.with_mail_mut(mail_id, |m| {
            if let Some((id, name)) = &update.receiver {
                m.header.receiver_id = *id;
                m.receiver_name = name.clone();
            }
            if let Some(t) = &update.title {
                m.title = t.clone();
            }
            if let Some((text, billing, extra)) = &update.body {
                m.body.text = text.clone();
                m.body.billing_amount = *billing;
                m.header.extra = *extra;
                m.header.status = crate::systems::housing::hooks::MAIL_STATUS_UNPAID;
            }
            m.is_dirty = true;
        });
    }
}

// ---------------------------------------------------------------------------------------------
// vehicles: VehicleItemHooks
// ---------------------------------------------------------------------------------------------

/// `character.Craft.EndCraft()` for the shipyard branches of `CraftEffect`.
pub struct EconomyForVehicles;

impl crate::systems::vehicles::hooks::VehicleItemHooks for EconomyForVehicles {
    fn end_craft(&self, server: &GameServer, player: &PlayerRef) {
        super::craft::end_craft(server, player);
    }
}

// ---------------------------------------------------------------------------------------------
// economy's own slots: MailHousingHooks, CraftHooks, ItemEffectHooks
// ---------------------------------------------------------------------------------------------

/// The economy hooks that compose the other subsystems directly (housing, npcs, skills,
/// quests).
pub struct EconomyComposite;

impl super::mail::MailHousingHooks for EconomyComposite {
    fn house_exists(&self, server: &GameServer, house_id: u32) -> bool {
        crate::systems::housing::house_by_id(server, house_id).is_some()
    }

    fn house_owner_id(&self, server: &GameServer, house_id: u32) -> Option<u32> {
        crate::systems::housing::house_by_id(server, house_id).map(|h| h.data().unit.owner_id)
    }

    fn pay_weekly_tax(&self, server: &GameServer, house_id: u32) -> bool {
        crate::systems::housing::pay_weekly_tax(server, house_id)
    }

    fn pay_weekly_tax_for(&self, server: &GameServer, house_id: u32, payer_id: u32) -> bool {
        crate::systems::housing::house_by_id(server, house_id).is_some_and(|h| {
            crate::systems::housing::manager::pay_weekly_tax_for(server, &h, Some(payer_id))
        })
    }
}

impl super::craft::CraftHooks for EconomyComposite {
    fn start_craft_cast(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        skill_id: u32,
        doodad_obj_id: u32,
        cast_time_multiplier: f32,
    ) {
        use crate::systems::skills::state::SkillRun;
        let Some(t) = server.game_data.systems.skills.skill(skill_id).cloned() else {
            tracing::error!("Craft: unknown skill {skill_id}");
            return;
        };
        let skill = SkillRun::new(t, 1);
        skill.state().cast_time_multiplier = cast_time_multiplier;
        let me = WorldObject::from(player);
        let mut target = SkillCastTarget::Doodad(doodad_obj_id);
        crate::systems::skills::skill::use_skill(
            server,
            &skill,
            &me,
            SkillCaster::Unit(player.obj_id),
            &mut target,
            None,
            false,
        );
    }

    fn cancel_craft_skill(&self, server: &GameServer, player: &PlayerRef) {
        let me = WorldObject::from(player);
        let task = me
            .with_unit(|u| u.unit().skill_state.skill_task.clone())
            .flatten();
        if let Some(t) = task {
            t.skill.state().cancelled = true;
        }
        crate::systems::skills::skill::interrupt_skills(server, &me);
    }

    fn doodad_func_permission(&self, server: &GameServer, doodad: &DoodadRef) -> u8 {
        let group = doodad.data().func_group_id;
        server
            .game_data
            .systems
            .npcs
            .doodad
            .doodad_funcs(group)
            .first()
            .map_or(super::craft::doodad_func_permission::ANY, |f| {
                f.perm_id as u8
            })
    }

    fn account_house_zone_ids(&self, server: &GameServer, account_id: u32) -> Vec<u32> {
        server
            .systems
            .housing
            .manager
            .by_account(account_id)
            .iter()
            .map(|h| h.data().unit.transform.zone_id)
            .collect()
    }

    fn house_actability_bonus(
        &self,
        server: &GameServer,
        x: f32,
        y: f32,
        actability_group_id: i32,
    ) -> u32 {
        crate::systems::housing::act_ability_bonus_at(server, actability_group_id, x, y)
    }

    fn house_actability_bonus_in_world(
        &self,
        server: &GameServer,
        instance_id: u32,
        x: f32,
        y: f32,
        actability_group_id: i32,
    ) -> u32 {
        crate::systems::housing::act_ability_bonus_at_in_world(
            server,
            instance_id,
            actability_group_id,
            x,
            y,
        )
    }

    fn on_craft(&self, server: &GameServer, player: &PlayerRef, craft_id: u32) {
        crate::systems::quests::events::on_craft(server, player, craft_id);
    }
}

impl item_effects::ItemEffectHooks for EconomyComposite {
    fn in_public_farm(&self, server: &GameServer, player: &PlayerRef) -> bool {
        let me = WorldObject::from(player);
        let Some(world) = server.world.world_of(&me) else {
            return false;
        };
        let p = world.world_pos_rot(&me);
        crate::systems::npcs::public_farm::in_public_farm(server, &world.template, (p.x, p.y, p.z))
    }

    fn house_at(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        x: f32,
        y: f32,
    ) -> Option<item_effects::HouseAtLocation> {
        let instance_id = player.data().unit.transform.instance_id;
        let house = crate::systems::housing::house_at_location_in_world(server, instance_id, x, y)?;
        let house_id = house.data().id;
        Some(item_effects::HouseAtLocation {
            house_id,
            house_obj_id: house.obj_id,
            allowed_to_interact: crate::systems::housing::allowed_to_interact(
                server, &house, player,
            ),
        })
    }

    fn spawn_backpack_doodad(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        spawn: &item_effects::BackpackDoodadSpawn,
    ) -> bool {
        use crate::systems::npcs::doodad::manager;
        let me = WorldObject::from(player);
        let Some(world) = server.world.world_of(&me) else {
            return false;
        };
        let Some(doodad) =
            manager::create(server, &world, 0, spawn.doodad_template_id, Some(&me), true)
        else {
            tracing::warn!(
                "Doodad {}, from BackpackDoodadId could not be created",
                spawn.doodad_template_id
            );
            return false;
        };
        let zone_id = player.data().unit.transform.zone_id;
        {
            let mut d = doodad.data();
            d.is_persistent = true;
            d.transform.instance_id = world.id;
            d.transform.zone_id = zone_id;
            let [rx, ry, rz] = spawn.rot;
            d.transform
                .set_position(spawn.x, spawn.y, spawn.z, rx, ry, rz);
            d.attach_point = crate::models::unit::attach_point::NONE;
            d.item_id = spawn.item_id;
            d.item_template_id = spawn.item_template_id;
            d.ucc_id = spawn.ucc_id;
            d.scale = 1.0;
            d.plant_time = chrono::Utc::now().timestamp();
        }
        if let Some(h) = spawn.house {
            if let Some(house) = world
                .house(h.house_obj_id)
                .filter(|house| house.data().id == h.house_id)
            {
                {
                    let mut d = doodad.data();
                    d.owner_db_id = h.house_id;
                    d.owner_type = crate::models::doodad::doodad_owner_type::HOUSING;
                    d.parent_obj_id = h.house_obj_id;
                }
                // BackpackDoodadSpawn supplies world coordinates. Save stores the local
                // transform, so convert before parenting just as player crop placement does.
                crate::systems::npcs::doodad::funcs::world::attach_keeping_world(
                    &world,
                    &WorldObject::from(&house),
                    &WorldObject::from(&doodad),
                );
            }
        }
        manager::init_doodad(server, &world, &doodad);
        manager::spawn(server, &world, &doodad);
        crate::systems::npcs::doodad::db::save(server, &doodad);
        true
    }
}

// ---------------------------------------------------------------------------------------------
// Installation
// ---------------------------------------------------------------------------------------------

/// Installs the economy's implementations of the other subsystems' hooks and the composite
/// hooks of its own slots.
pub fn install(server: &GameServer) {
    let s = &server.systems;
    s.economy.loot.install_release_hook(&server.ids.object);
    s.skills.ext.items.install(Arc::new(EconomyForSkills));
    s.npcs.hooks.economy.install(Arc::new(EconomyForNpcs));
    s.quests.economy_hooks.install(Arc::new(EconomyForQuests));
    s.housing.hooks.install(Arc::new(EconomyForHousing));
    s.vehicles.hooks.items.install(Arc::new(EconomyForVehicles));
    let composite = Arc::new(EconomyComposite);
    s.economy.mail.hooks.housing.install(composite.clone());
    s.economy.craft.hooks.install(composite.clone());
    s.economy.item_effects.hooks.install(composite);
    s.services
        .webapi_economy_hooks
        .install(Arc::new(EconomyForServices));
    crate::systems::services::item_ops::CHARACTER_ITEM_OPS.install(Arc::new(EconomyForServices));
    let wipe: Arc<crate::systems::services::shutdown::BuyBackWipe> =
        Arc::new(|server: &GameServer| {
            // character.BuyBackItems.Wipe() for every online character
            for p in server.world.all_characters() {
                server
                    .systems
                    .economy
                    .store
                    .wipe_buyback(server, p.character_id);
            }
        });
    crate::systems::services::shutdown::BUYBACK_WIPE.install(wipe);
}
