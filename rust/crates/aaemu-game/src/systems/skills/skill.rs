//! `Skill`: `Use`, `GetInitialTarget` / `SetInitialTarget`, `Cast`, `StartChanneling`,
//! `EndChanneling`, `ScheduleEffects`, `ApplyEffects`, `EndSkill`, `Stop`, `StopSkill`,
//! `RollCombatDice`, `ManaCost` / `ConsumeMana`, plus `Unit.InterruptSkills`, `Unit.UseSkill`,
//! `GetAbLevel`, auto attack (`StartAutoSkill` / `UseAutoAttackSkillTask`,
//! `SkillManager.GetAttackDelay`) and the cast / channel / apply tasks.

use std::sync::Arc;

use rand::Rng;

use crate::models::transform::PositionAndRotation;
use crate::models::unit::SkillAttribute;
use crate::server::GameServer;
use crate::world::{now_ms, WorldObject};

use super::buffs;
use super::combat as unit_combat;
use super::data::{EffectKind, EffectTemplate, SkillEffect, SkillTemplate};
use super::effects::{self, EffectArgs, EffectSource};
use super::geometry::{distance_to, is_front, world_of, world_pos, PosUnit, Target};
use super::packets::{
    CompressedGamePackets, SCCastingStoppedPacket, SCSkillCooldownResetPacket, SCSkillEndedPacket,
    SCSkillFiredPacket, SCSkillStartedPacket, SCSkillStoppedPacket,
};
use super::relation::{can_attack, filter_with_relation, owner_character, relation_to};
use super::state::{AutoAttackSlot, CancelToken, SkillRef, SkillRun, SkillTaskKind, SkillTaskSlot};
use super::stats::{equip_slot, slot_type, stat, stats, StatKind};
use super::types::{
    skill_result, ApplicationMethod, BuffRemoveOn, CastAction, DamageType, RelationState,
    SkillCastTarget, SkillCaster, SkillHitType, SkillObject, SkillTargetRelation,
    SkillTargetSelection, SkillTargetType,
};
use super::{broadcast, unit_reqs};

/// `SkillsEnum.DigUpTreasureChestMarkedOnMap`.
pub const DIG_UP_TREASURE_SKILL: u32 = 18_349;

/// The arguments a cast carries through its tasks.
#[derive(Debug, Clone)]
pub struct CastCtx {
    pub caster: WorldObject,
    pub caster_caster: SkillCaster,
    pub target: Target,
    pub target_caster: SkillCastTarget,
    pub skill_object: SkillObject,
}

/// `GetAbLevel(ability)`: characters from their ability exp, other units their level (or the
/// owner-provided override).
pub fn ab_level(server: &GameServer, unit: &WorldObject, ability: u8) -> i32 {
    match unit {
        WorldObject::Player(p) => {
            let c = p.data();
            crate::db_character::ab_level(server, &c, ability)
        }
        o => o
            .with_unit(|u| {
                i32::from(
                    u.unit()
                        .skill_state
                        .ab_level_override
                        .unwrap_or(u.unit().level),
                )
            })
            .unwrap_or(0),
    }
}

/// `new Skill(template, owner)`.
pub fn new_skill_for(
    server: &GameServer,
    template: Arc<SkillTemplate>,
    owner: Option<&WorldObject>,
) -> SkillRef {
    let level = match owner {
        Some(o) => SkillRun::level_for(&template, ab_level(server, o, template.ability_id)),
        None => 1,
    };
    SkillRun::new(template, level)
}

/// `Skill.ManaCost(caster)`.
pub fn mana_cost(server: &GameServer, skill: &SkillRun, caster: &WorldObject) -> i32 {
    let t = &skill.template;
    let base = (f64::from(ab_level(server, caster, t.ability_id) - 1) * 1.6 + 8.0) * 3.0 / 3.65;
    let cost2 = base * f64::from(t.mana_level_md) + f64::from(t.mana_cost);
    let tags = server.game_data.systems.skills.skill_tags(t.id).to_vec();
    caster
        .with_unit(|u| {
            u.unit()
                .skill_modifiers
                .apply(t.id, &tags, SkillAttribute::ManaCost, cost2)
        })
        .unwrap_or(cost2) as i32
}

/// `BaseUnit.ApplySkillModifiers(skill, attribute, value)`.
pub fn apply_skill_modifiers(
    server: &GameServer,
    skill: &SkillRun,
    caster: &WorldObject,
    attr: SkillAttribute,
    value: f64,
) -> f64 {
    let tags = server
        .game_data
        .systems
        .skills
        .skill_tags(skill.template.id)
        .to_vec();
    caster
        .with_unit(|u| {
            u.unit()
                .skill_modifiers
                .apply(skill.template.id, &tags, attr, value)
        })
        .unwrap_or(value)
}

/// `Helper.CheckInterval(caller, interval)` for the two call sites of `Skill.Use`.
fn check_interval(server: &GameServer, obj_id: u32, site: u8, interval_ms: i64) -> bool {
    let now = now_ms();
    let mut m = server
        .systems
        .skills
        .intervals
        .lock()
        .expect("intervals poisoned");
    let next = m.get(&(obj_id, site)).copied().unwrap_or(now);
    if next > now {
        return false;
    }
    m.insert((obj_id, site), now + interval_ms);
    true
}

fn lookup_base_unit(server: &GameServer, caster: &WorldObject, obj_id: u32) -> Option<WorldObject> {
    world_of(server, caster).and_then(|w| w.object(obj_id))
}

/// `Skill.SetInitialTarget(caster, targetCaster)`: a position unit.
fn set_initial_target(
    server: &GameServer,
    caster: &WorldObject,
    target_caster: &SkillCastTarget,
) -> Target {
    let caster_t = Target::Obj(caster.clone());
    let mut p = super::geometry::pos_unit_from(server, &caster_t);
    match *target_caster {
        SkillCastTarget::Position {
            x, y, z, obj_id1, ..
        } => {
            let npc_target = caster
                .as_npc()
                .map(|n| n.data().unit.current_target)
                .filter(|t| *t != 0)
                .and_then(|t| server.world.object(t));
            if let Some(t) = npc_target {
                let l = t.transform();
                p.pos.x = l.x;
                p.pos.y = l.y;
                p.pos.z = l.z;
            } else if obj_id1 != 0 {
                match lookup_base_unit(server, caster, obj_id1) {
                    Some(basis) => {
                        let bw = world_pos(server, &Target::Obj(basis.clone()));
                        let scale = basis.scale();
                        let local = [x * scale, y * scale, z * scale];
                        let r = bw.to_quaternion().rotate(local);
                        p.pos.x = r[0] + bw.x;
                        p.pos.y = r[1] + bw.y;
                        p.pos.z = r[2] + bw.z;
                    }
                    None => {
                        let w = world_pos(server, &caster_t);
                        p.pos.x = w.x;
                        p.pos.y = w.y;
                        p.pos.z = w.z;
                    }
                }
            } else {
                p.pos.x = x;
                p.pos.y = y;
                p.pos.z = z;
            }
        }
        SkillCastTarget::Position2 { x, y, z, .. } | SkillCastTarget::Position3 { x, y, z, .. } => {
            p.pos.x = x;
            p.pos.y = y;
            p.pos.z = z;
        }
        _ => {}
    }
    Target::Pos(p)
}

/// `Skill.GetInitialTarget(caster, skillCaster, targetCaster)` (`None` = null).
fn initial_target(
    server: &GameServer,
    skill: &SkillRun,
    caster: &WorldObject,
    skill_caster: &SkillCaster,
    target_caster: &mut SkillCastTarget,
) -> Option<Target> {
    if !caster.is_unit() {
        return None;
    }
    let mut target: Option<WorldObject> = Some(caster.clone());
    if matches!(
        skill_caster,
        SkillCaster::Mount { .. } | SkillCaster::Unit(_)
    ) {
        target = world_of(server, caster).and_then(|w| w.unit(skill_caster.obj_id()));
    }
    let tt = SkillTargetType::from_raw(i64::from(skill.template.target_type));
    let caster_t = Target::Obj(caster.clone());
    let lookup = |tc: &mut SkillCastTarget| -> Option<WorldObject> {
        let found = if tc.obj_id() > 0 {
            lookup_base_unit(server, caster, tc.obj_id())
        } else {
            Some(caster.clone())
        };
        if let Some(f) = &found {
            tc.set_obj_id(f.obj_id());
        }
        found
    };
    use SkillTargetType as T;
    match tt {
        Some(T::SelfTarget) => {
            if target_caster.is_unit_or_doodad() {
                if let Some(t) = &target {
                    target_caster.set_obj_id(t.obj_id());
                }
            }
        }
        Some(T::Friendly) => {
            if target_caster.is_unit_or_doodad() {
                target = lookup(target_caster);
            }
            if let Some(t) = &target {
                let r = relation_to(server, &caster_t, &Target::Obj(t.clone()));
                if r != RelationState::Friendly && r != RelationState::Neutral {
                    return None;
                }
            }
        }
        Some(T::Hostile) => {
            if target_caster.is_unit_or_doodad() {
                target = lookup(target_caster);
            }
            if let Some(t) = &target {
                let tt = Target::Obj(t.clone());
                let r = relation_to(server, &caster_t, &tt);
                if r != RelationState::Hostile
                    && r != RelationState::Neutral
                    && !can_attack(server, &caster_t, &tt)
                {
                    return None;
                }
            }
        }
        Some(T::AnyUnit | T::Doodad) => {
            if target_caster.is_unit_or_doodad() {
                target = lookup(target_caster);
            }
        }
        Some(T::Item) => {}
        Some(T::Others | T::Building) => {
            if target_caster.is_unit_or_doodad() {
                target = lookup(target_caster);
            }
            if target.as_ref().is_some_and(|t| t.same(caster)) {
                return None;
            }
        }
        Some(T::FriendlyOthers) => {
            if target_caster.is_unit_or_doodad() {
                target = lookup(target_caster);
            }
            let t = target.as_ref()?;
            if t.same(caster) {
                return None;
            }
            let r = relation_to(server, &caster_t, &Target::Obj(t.clone()));
            if r != RelationState::Friendly && r != RelationState::Neutral {
                return None;
            }
        }
        Some(T::Pos | T::BallisticPos | T::ArtilleryPos) => {
            return Some(set_initial_target(server, caster, target_caster));
        }
        Some(T::RelativePos) => {
            if matches!(
                target_caster,
                SkillCastTarget::Position { .. }
                    | SkillCastTarget::Position2 { .. }
                    | SkillCastTarget::Position3 { .. }
            ) {
                return Some(set_initial_target(server, caster, target_caster));
            }
        }
        Some(
            T::Party | T::Raid | T::Line | T::Pet | T::SummonPos | T::SourcePos | T::CursorPos,
        ) => {}
        None => {
            tracing::error!(
                "SkillTargetType not supported {} (NotSupportedException)",
                skill.template.target_type
            );
            return None;
        }
    }
    target.map(Target::Obj)
}

/// `Skill.Use(caster, casterCaster, targetCaster, skillObject, bypassGcd, out value)`:
/// (`SkillResult`, extra value).
pub fn use_skill(
    server: &GameServer,
    skill: &SkillRef,
    caster: &WorldObject,
    caster_caster: SkillCaster,
    target_caster: &mut SkillCastTarget,
    skill_object: Option<SkillObject>,
    bypass_gcd: bool,
) -> (u8, u32) {
    if !caster.is_unit() {
        return (skill_result::INVALID_SOURCE, 0);
    }
    let template = skill.template.clone();
    // The 1.2 fishing reactions require the caster's own spawned NPC. OwnerId is
    // the character id; using an object id here would accept unrelated NPCs or
    // reject the fisherman. Resolve this before cast state, GCD or buff mutations.
    let owned_target = if template.target_my_npc {
        let Some(player) = caster.as_player() else {
            return (unit_reqs::skill_result_for_key("skill_not_my_npc"), 0);
        };
        let target = initial_target(server, skill, caster, &caster_caster, target_caster);
        let owner = target
            .as_ref()
            .and_then(Target::as_obj)
            .and_then(WorldObject::as_npc)
            .map(|npc| npc.data().unit.owner_id);
        if player.character_id == 0 || owner != Some(player.character_id) {
            return (unit_reqs::skill_result_for_key("skill_not_my_npc"), 0);
        }
        target
    } else {
        None
    };
    let character = caster.as_player().cloned();
    let cooldown_owner = character
        .clone()
        .or_else(|| owner_character(server, caster));
    if let Some(c) = &character {
        c.data().skill_char.skill_cancelled = false;
    }
    caster.with_unit(|u| u.unit_mut().skill_state.condition_chance = true);

    let req = unit_reqs::can_use_skill(server, &template, caster, &caster_caster);
    if !req.is_ok() {
        if let Some(c) = &character {
            let c = c.data();
            tracing::warn!(
                "{} ({}) failed requirements to use skill {} - {}",
                c.unit.name,
                c.id,
                template.id,
                req.key
            );
        }
        skill.state().cancelled = true;
        return (unit_reqs::skill_result_for_key(req.key), req.value);
    }
    // Resolve buff restrictions before cooldown writes or StartSkill buff removal:
    // removing a restrictive buff first would let the forbidden skill bypass it.
    let checked_target = if !server
        .game_data
        .systems
        .skills
        .buff_skill_requirements
        .is_empty()
    {
        let target = owned_target
            .or_else(|| initial_target(server, skill, caster, &caster_caster, target_caster));
        if let Some(req) = super::buff_reqs::failed_requirement(
            server,
            &template,
            caster,
            target.as_ref().and_then(Target::as_obj),
        ) {
            skill.state().cancelled = true;
            return (skill_result::SKILL_REQ_FAIL, req);
        }
        target
    } else {
        owned_target
    };
    let now = now_ms();
    if template.cooldown_time > 0 {
        if let Some(o) = &cooldown_owner {
            let ignore = o.data().skill_char.ignore_skill_cooldowns;
            let on_cd = caster
                .with_unit(|u| u.unit_mut().cooldowns.check_cooldown(template.id, now))
                .unwrap_or(false);
            if !ignore && on_cd {
                return (skill_result::COOLDOWN_TIME, 0);
            }
        }
    }
    skill.state().bypass_gcd = bypass_gcd;
    if !bypass_gcd {
        let delay: i64 = if matches!(skill.id, 2..=4) {
            if character.is_some() {
                500
            } else {
                1500
            }
        } else {
            150
        };
        let (last_used, gcd) = caster
            .with_unit(|u| (u.unit().skill_last_used_ms, u.unit().global_cooldown_ms))
            .unwrap_or((0, 0));
        if last_used + delay > now && !check_interval(server, caster.obj_id(), 0, delay) {
            return (skill_result::COOLDOWN_TIME, 0);
        }
        if gcd >= now
            && !template.ignore_global_cooldown
            && !check_interval(server, caster.obj_id(), 1, delay)
        {
            return (skill_result::COOLDOWN_TIME, 0);
        }
        caster.with_unit(|u| u.unit_mut().skill_last_used_ms = now);
    }
    if template.cancel_ongoing_buffs {
        if caster.as_mate().is_some() {
            buffs::trigger_remove_on(
                server,
                caster,
                BuffRemoveOn::UseSkill,
                template.cancel_ongoing_buff_exception_tag_id,
            );
        }
        buffs::trigger_remove_on(
            server,
            caster,
            BuffRemoveOn::StartSkill,
            template.cancel_ongoing_buff_exception_tag_id,
        );
    }
    let skill_object = skill_object.unwrap_or_default();
    let Some(target) = checked_target
        .or_else(|| initial_target(server, skill, caster, &caster_caster, target_caster))
    else {
        if let Some(n) = caster.as_npc() {
            server
                .systems
                .skills
                .ext
                .npcs()
                .npc_no_aggro_target(server, n);
        }
        tracing::trace!(
            "Skill: SkillResult.NoTarget! - Skill {}, Caster {}",
            template.id,
            caster.obj_id()
        );
        skill.state().initial_target = 0;
        return (skill_result::NO_TARGET, 0);
    };
    skill.state().initial_target = target.obj_id();
    // Unmount character if skill asks for it (vehicles subsystem)
    if template.unmount {
        if let Some(p) = caster.as_player() {
            crate::systems::vehicles::mates::unmount_riding_character(server, p, true);
        }
    }
    let mp = caster.with_unit(|u| u.unit().mp).unwrap_or(0);
    if mana_cost(server, skill, caster) > mp {
        return (skill_result::LACK_MANA, 0);
    }
    let tl = server.systems.skills.tl_ids.next_id();
    skill.state().tl_id = tl;
    tracing::debug!(
        "Created SkillTlId {tl} for Skill {}, Caster {} with target {}",
        template.id,
        caster.obj_id(),
        target.obj_id()
    );
    let ctx = CastCtx {
        caster: caster.clone(),
        caster_caster,
        target: target.clone(),
        target_caster: *target_caster,
        skill_object: skill_object.clone(),
    };
    if let Some(plot_id) = template.plot_id {
        super::plots::run_plot(server, plot_id, ctx.clone(), skill.clone());
        if template.plot_only {
            return (skill_result::SUCCESS, 0);
        }
    }
    // Range checks
    let skill_range = apply_skill_modifiers(
        server,
        skill,
        caster,
        SkillAttribute::Range,
        f64::from(template.max_range),
    );
    let caster_t = Target::Obj(caster.clone());
    let target_dist = f64::from(distance_to(server, &caster_t, &target, true));
    let mut min_range = f64::from(template.min_range);
    let mut max_range = skill_range;
    if template.target_type == SkillTargetType::Doodad as i32 && template.min_range >= 100 {
        min_range = f64::from(template.min_range) / 100.0;
    }
    if template.weapon_slot_for_range_id > 0 {
        let (mn, mx) = server
            .systems
            .skills
            .ext
            .items()
            .weapon_holdable(server, caster, template.weapon_slot_for_range_id)
            .map_or((0.0, 3.0), |h| (h.min_range, h.max_range));
        min_range = f64::from(mn);
        max_range = f64::from(mx);
    }
    if target_dist < min_range {
        server.systems.skills.tl_ids.release(tl);
        skill.state().tl_id = 0;
        tracing::info!("TooCloseRange targetDist={target_dist}, minRangeCheck={min_range}");
        return (skill_result::TOO_CLOSE_RANGE, 0);
    }
    let target_is_doodad_or_slave = target
        .as_obj()
        .is_some_and(|o| o.as_doodad().is_some() || o.as_slave().is_some());
    if target_dist > max_range && !target_is_doodad_or_slave {
        server.systems.skills.tl_ids.release(tl);
        skill.state().tl_id = 0;
        tracing::info!("TooFarRange targetDist={target_dist}, maxRangeCheck={max_range}");
        return (skill_result::TOO_FAR_RANGE, 0);
    }
    if let Some(c) = &character {
        if let Some(r) = portal_zone_check(server, &template, c) {
            return (r, 0);
        }
    }
    // Casting time
    let mut cast_time = 0i32;
    if template.casting_time > 0 {
        let mul = stat(server, caster, StatKind::CastTimeMul);
        cast_time = (mul
            * apply_skill_modifiers(
                server,
                skill,
                caster,
                SkillAttribute::CastTime,
                f64::from(template.casting_time),
            )) as i32;
    }
    let ctm = skill.state().cast_time_multiplier;
    cast_time = (f64::from(cast_time) * f64::from(ctm)).round_ties_even() as i32;
    if cast_time > 0 {
        let mut p = SCSkillStartedPacket::new(
            skill.id,
            tl,
            ctx.caster_caster,
            ctx.target_caster,
            ctx.skill_object.clone(),
        );
        p.base_cast_time_div10 = (cast_time / 10) as u16;
        p.real_cast_time_div10 = (cast_time / 10) as u16;
        broadcast(server, caster, &p, true);
        let cancel = CancelToken::new();
        caster.with_unit(|u| {
            u.unit_mut().skill_state.skill_task = Some(SkillTaskSlot {
                kind: SkillTaskKind::Cast,
                skill: skill.clone(),
                cancel: cancel.clone(),
            })
        });
        let skill2 = skill.clone();
        super::tasks::schedule(server, i64::from(cast_time), move |srv| {
            // CastTask.Execute
            if cancel.is_cancelled() || skill2.cancelled() {
                return;
            }
            cast(&srv, &skill2, &ctx);
        });
    } else {
        cast(server, skill, &ctx);
    }
    (skill_result::SUCCESS, 0)
}

/// The `AccessLevel < 100` checks of `Skill.Use` for `Return` / `OpenPortalEffect` skills.
fn portal_zone_check(
    server: &GameServer,
    template: &SkillTemplate,
    c: &crate::world::PlayerRef,
) -> Option<u8> {
    let (access, district, faction, instance) = {
        let d = c.data();
        (
            d.access_level,
            d.return_district_id,
            d.unit.faction_id,
            d.unit.transform.instance_id,
        )
    };
    if access >= 100 {
        return None;
    }
    let data = &server.game_data.systems.skills;
    let first = template.effects.first()?;
    let (kind, id) = first.template?;
    let mut zone_id: Option<u32> = None;
    match data.effect(kind, id)? {
        EffectTemplate::Special(s) if s.special_effect_type_id == 25 => {
            if s.value1 > 0 {
                // PortalManager.GetWorldGatesById: world gates are not loaded (portals subsystem)
            } else {
                let rp = server
                    .game_data
                    .portals
                    .district_return_point(district, faction);
                zone_id = server
                    .game_data
                    .portals
                    .recalls
                    .values()
                    .flatten()
                    .find(|p| p.id == rp)
                    .map(|p| p.zone_id);
            }
        }
        EffectTemplate::OpenPortal { .. } if instance != 0 => {
            return Some(skill_result::INVALID_LOCATION);
        }
        // character.Portals.GetPortalInfo(portalInfo.Id): portals subsystem
        _ => {}
    }
    let zone_id = zone_id?;
    match server.game_data.zones.zone_by_key(zone_id) {
        None => Some(skill_result::NO_PERM),
        Some(z) if z.closed => Some(skill_result::NO_PERM),
        _ => None,
    }
}

/// `Skill.Cast(caster, casterCaster, target, targetCaster, skillObject)`.
pub fn cast(server: &GameServer, skill: &SkillRef, ctx: &CastCtx) {
    let caster = &ctx.caster;
    if !caster.is_unit() {
        return;
    }
    let t = &skill.template;
    let now = now_ms();
    if !skill.state().bypass_gcd {
        let mut gcd = t.custom_gcd;
        if t.default_gcd {
            gcd = if caster.as_npc().is_some() {
                1500
            } else {
                1000
            };
        }
        let mul = stat(server, caster, StatKind::GlobalCooldownMul) as f32;
        let ms = (gcd as f32 * (mul / 100.0)) as i64;
        caster.with_unit(|u| u.unit_mut().global_cooldown_ms = now + ms);
    }
    if caster.as_npc().is_some() && t.skill_controller_id != 0 {
        if let Some(sc) = server
            .game_data
            .systems
            .skills
            .skill_controller(t.skill_controller_id)
        {
            let dist = super::geometry::raw_distance(
                &world_pos(server, &Target::Obj(caster.clone())),
                &world_pos(server, &ctx.target),
                true,
            );
            if dist >= t.min_range as f32 && dist <= t.max_range as f32 {
                if let Some(sc) =
                    super::controllers::create(server, sc, caster, &ctx.target, 0.0, 0.0, 0.0, 0.0)
                {
                    super::controllers::activate(server, &sc);
                }
            }
        }
    }
    caster.with_unit(|u| u.unit_mut().skill_state.skill_task = None);
    consume_mana(server, skill, caster);
    caster.with_unit(|u| {
        u.unit_mut()
            .cooldowns
            .add_cooldown(t.id, t.cooldown_time as u32, now)
    });
    // Validate the cast item
    if let (
        Some(p),
        SkillCaster::Item {
            item_id,
            item_template_id,
            ..
        },
    ) = (caster.as_player(), ctx.caster_caster)
    {
        let items = server.systems.skills.ext.items();
        if items.use_skill_as_reagent(server, item_template_id) {
            match items.owned_item(server, p, item_id) {
                None => {
                    tracing::warn!(
                        "SkillItem does not exists {item_id} (templateId: {item_template_id})"
                    );
                    return;
                }
                Some((tid, _)) => {
                    if items.item_count(server, p, tid) < 1 {
                        tracing::warn!("SkillItem, player does not own enough of {item_id}");
                        return;
                    }
                }
            }
        }
    }
    if t.channeling_time > 0 {
        start_channeling(server, skill, ctx);
    } else {
        schedule_effects(server, skill, ctx);
    }
}

/// `Skill.StartChanneling`.
pub fn start_channeling(server: &GameServer, skill: &SkillRef, ctx: &CastCtx) {
    let caster = &ctx.caster;
    let t = &skill.template;
    let data = &server.game_data.systems.skills;
    if t.channeling_buff_id != 0 {
        if let Some(b) = data.buff(t.channeling_buff_id).cloned() {
            if let Some(target) = ctx.target.as_obj() {
                buffs::apply_template(
                    server,
                    &b,
                    Some(caster),
                    &ctx.caster_caster,
                    target,
                    Some(skill),
                );
            }
        }
    }
    if t.channeling_target_buff_id != 0 {
        if let Some(b) = data.buff(t.channeling_target_buff_id).cloned() {
            if let Some(target) = ctx.target.as_obj() {
                buffs::apply_template(
                    server,
                    &b,
                    Some(caster),
                    &ctx.caster_caster,
                    target,
                    Some(skill),
                );
            }
        }
    }
    let doodad = if t.channeling_doodad_id > 0 {
        server
            .systems
            .skills
            .ext
            .npcs()
            .spawn_channel_doodad(server, caster, t.channeling_doodad_id)
            .unwrap_or(0)
    } else {
        0
    };
    let tl = skill.tl_id();
    broadcast(
        server,
        caster,
        &SCSkillFiredPacket {
            id: skill.id,
            tl,
            caster: ctx.caster_caster,
            target: ctx.target_caster,
            skill_object: ctx.skill_object.clone(),
            computed_delay: 0,
            channeling_time: t.channeling_time,
            fire_anim_id: t.fire_anim_id,
        },
        true,
    );
    let cancel = CancelToken::new();
    caster.with_unit(|u| {
        u.unit_mut().skill_state.skill_task = Some(SkillTaskSlot {
            kind: SkillTaskKind::EndChanneling {
                channel_doodad: doodad,
            },
            skill: skill.clone(),
            cancel: cancel.clone(),
        })
    });
    let skill2 = skill.clone();
    let ctx2 = ctx.clone();
    super::tasks::schedule(server, i64::from(t.channeling_time), move |srv| {
        if cancel.is_cancelled() {
            return;
        }
        end_channeling(&srv, &skill2, &ctx2.caster, doodad, &ctx2.caster_caster);
    });
}

/// `Skill.EndChanneling(caster, channelDoodad, casterCaster)`.
pub fn end_channeling(
    server: &GameServer,
    skill: &SkillRef,
    caster: &WorldObject,
    channel_doodad: u32,
    caster_caster: &SkillCaster,
) {
    if !caster.is_unit() {
        return;
    }
    caster.with_unit(|u| u.unit_mut().skill_state.skill_task = None);
    let t = &skill.template;
    if t.channeling_buff_id != 0 {
        buffs::remove_effect_by_skill(server, caster, t.channeling_buff_id, t.id);
    }
    if t.channeling_target_buff_id != 0 {
        let initial = skill.state().initial_target;
        match server.world.object(initial) {
            Some(target) => {
                buffs::remove_effect_by_skill(server, &target, t.channeling_target_buff_id, t.id)
            }
            None => tracing::error!(
                "EndChanneling: InitialTarget {initial} is gone (NullReferenceException in C#)"
            ),
        }
    }
    if channel_doodad != 0 {
        server
            .systems
            .skills
            .ext
            .npcs()
            .delete_doodad(server, channel_doodad);
    }
    end_skill(server, skill, caster);
    if !skill.cancelled() {
        if let (
            SkillCaster::Item {
                item_id,
                item_template_id,
                ..
            },
            Some(p),
        ) = (caster_caster, caster.as_player())
        {
            if *item_template_id > 0 {
                server
                    .systems
                    .skills
                    .ext
                    .quests()
                    .item_use(server, p, *item_id);
            }
        }
    }
    // Events.OnChannelingCancel: no listeners in the ported code.
}

/// `Skill.ScheduleEffects`.
pub fn schedule_effects(server: &GameServer, skill: &SkillRef, ctx: &CastCtx) {
    let caster = &ctx.caster;
    let t = &skill.template;
    let data = &server.game_data.systems.skills;
    if t.toggle_buff_id != 0 {
        if let (Some(b), Some(target)) = (data.buff(t.toggle_buff_id).cloned(), ctx.target.as_obj())
        {
            buffs::apply_template(
                server,
                &b,
                Some(caster),
                &ctx.caster_caster,
                target,
                Some(skill),
            );
        }
    }
    let mut total_delay = 0i32;
    if t.effect_delay > 0 {
        total_delay += t.effect_delay;
    }
    if t.effect_speed > 0.0 {
        let d = distance_to(server, &Target::Obj(caster.clone()), &ctx.target, false);
        total_delay += (d / t.effect_speed * 1000.0) as i32;
    }
    if t.fire_anim_id != 0 && t.use_anim_time {
        if let Some(anim) = data.anims.get(t.fire_anim_id) {
            let mul = stat(server, caster, StatKind::GlobalCooldownMul) as f32;
            total_delay += (anim.combat_sync_time as f32 * (mul / 100.0)) as i32;
        }
    }
    let weapon_anim = weapon_attack_anim_id(server, skill, caster);
    let mut fired = SCSkillFiredPacket {
        id: skill.id,
        tl: skill.tl_id(),
        caster: ctx.caster_caster,
        target: ctx.target_caster,
        skill_object: ctx.skill_object.clone(),
        computed_delay: total_delay as i16,
        channeling_time: t.channeling_time,
        fire_anim_id: t.fire_anim_id,
    };
    if weapon_anim > 0 {
        fired.fire_anim_id = weapon_anim;
    }
    broadcast(server, caster, &fired, true);
    if total_delay > 0 {
        let skill2 = skill.clone();
        let ctx2 = ctx.clone();
        super::tasks::schedule(server, i64::from(total_delay), move |srv| {
            // ApplySkillTask
            apply_effects(&srv, &skill2, &ctx2);
            end_skill(&srv, &skill2, &ctx2.caster);
        });
    } else {
        apply_effects(server, skill, ctx);
        end_skill(server, skill, caster);
    }
}

/// `Skill.GetWeaponAttackAnimId(caster)`.
fn weapon_attack_anim_id(server: &GameServer, skill: &SkillRun, caster: &WorldObject) -> u32 {
    if !matches!(skill.template.id, 2..=4) {
        return 0;
    }
    let idx = {
        let mut s = skill.state();
        let i = s.auto_attack_index;
        s.auto_attack_index += 1;
        i
    };
    if caster.as_npc().is_some() {
        return (idx % 2 + 1) as u32;
    }
    if caster.as_player().is_none() {
        skill.state().auto_attack_index -= 1;
        return 0;
    }
    let slot = match skill.template.id {
        3 => equip_slot::OFFHAND,
        4 => equip_slot::RANGED,
        _ => equip_slot::MAINHAND,
    };
    let items = server.systems.skills.ext.items();
    match items.weapon_holdable(server, caster, slot) {
        Some(h) => items.weapon_attack_anim(server, h.holdable_id, idx, skill.template.id == 3),
        None => {
            if idx % 2 == 0 {
                1
            } else {
                2
            }
        }
    }
}

/// `Skill.RollCombatDice(attacker, target)`.
pub fn roll_combat_dice(
    server: &GameServer,
    skill: &SkillRun,
    attacker: &WorldObject,
    target: &WorldObject,
) -> SkillHitType {
    let damage_type = DamageType::from_raw(i64::from(skill.template.damage_type_id));
    let roll = || rand::thread_rng().gen::<f32>() * 100.0;
    let attacker_unit = attacker.is_unit();
    let target_unit = target.is_unit();
    let a = stats(
        server,
        attacker,
        &[
            StatKind::BullsEye,
            StatKind::MeleeAccuracy,
            StatKind::SpellAccuracy,
            StatKind::RangedAccuracy,
        ],
    );
    let mut always_hit = false;
    if attacker_unit {
        let bulls_eye_mod = a[0] as i32 as f32 / 1000.0 * 3.0 / 100.0;
        let ap = world_pos(server, &Target::Obj(attacker.clone()));
        let tp = world_pos(server, &Target::Obj(target.clone()));
        if !is_front(&ap, &tp) {
            always_hit = true;
        } else if target_unit {
            let tstat = stats(
                server,
                target,
                &[
                    StatKind::DodgeRate,
                    StatKind::BlockRate,
                    StatKind::MeleeParryRate,
                    StatKind::RangedParryRate,
                ],
            );
            if roll() < tstat[0] as f32 - bulls_eye_mod {
                match damage_type {
                    Some(DamageType::Melee) => return SkillHitType::MeleeDodge,
                    Some(DamageType::Ranged) => return SkillHitType::RangedDodge,
                    _ => {}
                }
            }
            if roll() < tstat[1] as f32 - bulls_eye_mod {
                match damage_type {
                    Some(DamageType::Melee) => return SkillHitType::MeleeBlock,
                    Some(DamageType::Ranged) => return SkillHitType::RangedBlock,
                    _ => {}
                }
            }
            if roll() < tstat[2] as f32 - bulls_eye_mod {
                if damage_type == Some(DamageType::Melee) {
                    return SkillHitType::MeleeParry;
                }
                if damage_type == Some(DamageType::Ranged) {
                    let dual = target.with_base_unit(|b| {
                        b.buffs()
                            .check_buff(super::types::buff_constants::EQUIP_DUALWIELD)
                            && b.buffs()
                                .check_buff(super::types::buff_constants::DUALWIELD_PROFICIENCY)
                    });
                    if dual {
                        return SkillHitType::MeleeParry;
                    }
                }
            }
            if roll() < tstat[3] as f32 - bulls_eye_mod && damage_type == Some(DamageType::Ranged) {
                return SkillHitType::RangedParry;
            }
        }
    }
    let _ = always_hit;
    match damage_type {
        Some(DamageType::Melee) => {
            if attacker_unit && roll() < a[1] as f32 {
                SkillHitType::MeleeHit
            } else {
                SkillHitType::MeleeMiss
            }
        }
        Some(DamageType::Magic) => {
            if attacker_unit && roll() < a[2] as f32 {
                SkillHitType::SpellHit
            } else {
                SkillHitType::SpellMiss
            }
        }
        Some(DamageType::Ranged) => {
            if attacker_unit && roll() < a[3] as f32 {
                SkillHitType::RangedHit
            } else {
                SkillHitType::RangedMiss
            }
        }
        Some(DamageType::Siege) => SkillHitType::RangedHit,
        _ => SkillHitType::Invalid,
    }
}

fn level_rule_hit(skill: &SkillRun) -> SkillHitType {
    match DamageType::from_raw(i64::from(skill.template.damage_type_id)) {
        Some(DamageType::Melee) => SkillHitType::MeleeHit,
        Some(DamageType::Magic | DamageType::Heal) => SkillHitType::SpellHit,
        Some(DamageType::Siege | DamageType::Ranged) => SkillHitType::RangedHit,
        None => SkillHitType::Invalid,
    }
}

/// `ShipSiegeAoEHit.AppendHostileShipsHitBySiegeHullAoE`: a siege AoE cast on a position also
/// hits the boats (with ship physics) whose mass box the siege circle touches, although their
/// pivot is outside the circle (`systems::ai::physics::siege_point_hits_ship`).
fn append_hostile_ships_hit_by_siege_hull_aoe(
    server: &GameServer,
    t: &super::data::SkillTemplate,
    caster: &Target,
    target_self: &Target,
    ctx: &CastCtx,
    possible: &mut Vec<Target>,
) {
    use crate::systems::ai::physics::{siege_point_hits_ship, SIEGE_PIVOT_QUERY_SLACK_METERS};
    if t.target_area_radius <= 0 {
        return;
    }
    if DamageType::from_raw(i64::from(t.damage_type_id)) != Some(DamageType::Siege) {
        return;
    }
    if !matches!(
        ctx.target_caster,
        SkillCastTarget::Position { .. } | SkillCastTarget::Position2 { .. }
    ) {
        return;
    }
    let radius = t.target_area_radius as f32;
    let p = super::geometry::world_pos(server, target_self);
    let relation = SkillTargetRelation::from_raw(i64::from(t.target_relation))
        .unwrap_or(SkillTargetRelation::Any);
    let nearby = super::geometry::around_radius(
        server,
        target_self,
        radius + SIEGE_PIVOT_QUERY_SLACK_METERS,
        false,
    );
    for o in nearby {
        let Some(ship) = o.as_slave() else {
            continue;
        };
        if possible
            .iter()
            .any(|x| x.as_obj().is_some_and(|y| y.same(&o)))
        {
            continue;
        }
        let target = Target::Obj(o.clone());
        if !super::relation::is_relation_valid(server, relation, caster, &target) {
            continue;
        }
        // IsABoat() + RigidBody / ShipModel + the mass-box test (physics snapshot).
        if !siege_point_hits_ship(server, ship.obj_id, p.x, p.y, radius) {
            continue;
        }
        possible.push(target);
    }
}

/// `Skill.ApplyEffects(caster, casterCaster, targetSelf, targetCaster, skillObject)`.
pub fn apply_effects(server: &GameServer, skill: &SkillRef, ctx: &CastCtx) {
    let caster = &ctx.caster;
    if !caster.is_unit() {
        return;
    }
    let t = skill.template.clone();
    let data = &server.game_data.systems.skills;
    let player = caster.as_player().cloned();
    let caster_t = Target::Obj(caster.clone());
    let target_self = &ctx.target;
    let selection = SkillTargetSelection::from_raw(i64::from(t.target_selection));
    let mut possible: Vec<Target> = Vec::new();
    if t.target_siege
        && selection == Some(SkillTargetSelection::Source)
        && caster.as_slave().is_some()
    {
        possible.push(target_self.clone());
    } else if t.target_area_radius > 0 {
        let mut units =
            super::geometry::around_radius(server, target_self, t.target_area_radius as f32, true);
        if selection == Some(SkillTargetSelection::Source) {
            if let Some(o) = target_self.as_obj() {
                units.push(o.clone());
            }
        }
        let relation = SkillTargetRelation::from_raw(i64::from(t.target_relation))
            .unwrap_or(SkillTargetRelation::Any);
        let units = filter_with_relation(server, relation, &caster_t, units);
        possible.extend(units.into_iter().map(Target::Obj));
        if selection == Some(SkillTargetSelection::Source) && target_self.as_obj().is_none() {
            possible.push(target_self.clone());
        }
    } else {
        possible.push(target_self.clone());
    }
    append_hostile_ships_hit_by_siege_hull_aoe(
        server,
        &t,
        &caster_t,
        target_self,
        ctx,
        &mut possible,
    );
    // Distinct
    let mut distinct: Vec<Target> = Vec::new();
    for p in possible {
        if !distinct.iter().any(|d| d.same(&p)) {
            distinct.push(p);
        }
    }
    let mut possible = distinct;
    if possible.is_empty() && matches!(ctx.target_caster, SkillCastTarget::Position { .. }) {
        possible.push(caster_t.clone());
    }
    if t.target_area_count > 0 && possible.len() > t.target_area_count as usize {
        let mut with_d: Vec<(f32, Target)> = possible
            .into_iter()
            .map(|p| (distance_to(server, &p, target_self, false), p))
            .collect();
        with_d.sort_by(|a, b| a.0.partial_cmp(&b.0).unwrap_or(std::cmp::Ordering::Equal));
        possible = with_d
            .into_iter()
            .take(t.target_area_count as usize)
            .map(|(_, p)| p)
            .collect();
    }
    let hostile = t.target_type == SkillTargetType::Hostile as i32;
    for target in &possible {
        let Some(o) = target.as_obj() else { continue };
        if o.is_unit() && hostile {
            let mut dice = roll_combat_dice(server, skill, caster, o);
            if t.level_rule_no_consideration {
                dice = level_rule_hit(skill);
            }
            skill.state().hit_types.entry(o.obj_id()).or_insert(dice);
            if let Some(tp) = o.as_player() {
                let cfg = server.systems.skills.config();
                let items = server.systems.skills.ext.items();
                let mut rate = if caster.as_player().is_some() {
                    cfg.pvp_durability_loss_rate
                } else {
                    cfg.pve_durability_loss_rate
                };
                rate *= items.durability_decrement_chance(server);
                if rate > 0.0 {
                    use super::combat::durability_targets as d;
                    match dice {
                        SkillHitType::MeleeBlock | SkillHitType::RangedBlock => {
                            items.durability_loss(server, tp, 1, d::SHIELD, rate)
                        }
                        SkillHitType::RangedParry | SkillHitType::MeleeParry => {
                            items.durability_loss(server, tp, 1, d::ALL_MAIN_WEAPONS, rate)
                        }
                        _ => {}
                    }
                }
            }
        } else if o.as_doodad().is_some() {
            server
                .systems
                .skills
                .ext
                .npcs()
                .doodad_skill_hit(server, o, caster, t.id);
        }
    }

    let caster_level = caster.with_unit(|u| u.unit().level).unwrap_or(0);
    let force_attack = caster.with_unit(|u| u.unit().force_attack).unwrap_or(false);
    let caster_pos = world_pos(server, &caster_t);
    let mut to_apply: Vec<(Target, &SkillEffect)> = Vec::new();
    let mut last_applied: Option<&SkillEffect> = None;
    for effect in &t.effects {
        let effected: Vec<Target> =
            match ApplicationMethod::from_raw(i64::from(effect.application_method)) {
                Some(ApplicationMethod::Target | ApplicationMethod::SourceToPos) => {
                    possible.clone()
                }
                Some(ApplicationMethod::Source) => vec![caster_t.clone()],
                Some(ApplicationMethod::SourceOnce) => {
                    let mate_owner_mark = matches!(ctx.caster_caster, SkillCaster::Mount { .. })
                        && target_self.as_obj().is_some_and(|o| o.as_mate().is_some());
                    let slave = target_self.as_obj().is_some_and(|o| o.as_slave().is_some());
                    if mate_owner_mark || slave {
                        possible.clone()
                    } else {
                        vec![caster_t.clone()]
                    }
                }
                None => Vec::new(),
            };
        for target in effected {
            let relation = relation_to(server, &caster_t, &target);
            if effect.start_level > caster_level || effect.end_level < caster_level {
                continue;
            }
            if effect.friendly && !effect.non_friendly && relation != RelationState::Friendly {
                continue;
            }
            if !effect.friendly
                && effect.non_friendly
                && relation != RelationState::Hostile
                && ((relation == RelationState::Friendly && !force_attack)
                    || caster_t.obj_id() == target.obj_id())
            {
                continue;
            }
            let tp = world_pos(server, &target);
            if effect.front && !effect.back && !is_front(&caster_pos, &tp) {
                continue;
            }
            if !effect.front && effect.back && is_front(&caster_pos, &tp) {
                continue;
            }
            if effect.source_buff_tag_id > 0
                && !buffs::check_buffs_by_tag(server, caster, effect.source_buff_tag_id)
            {
                continue;
            }
            if effect.source_no_buff_tag_id > 0
                && buffs::check_buffs_by_tag(server, caster, effect.source_no_buff_tag_id)
            {
                continue;
            }
            if let Some(o) = target.as_obj() {
                if effect.target_buff_tag_id > 0
                    && !buffs::check_buffs_by_tag(server, o, effect.target_buff_tag_id)
                {
                    continue;
                }
                if effect.target_no_buff_tag_id > 0
                    && buffs::check_buffs_by_tag(server, o, effect.target_no_buff_tag_id)
                {
                    continue;
                }
            }
            if effect.target_npc_tag_id > 0 {
                let ok = target.as_obj().and_then(|o| o.as_npc()).is_some_and(|n| {
                    let tid = n.data().unit.template_id;
                    data.npcs_by_tag(effect.target_npc_tag_id).contains(&tid)
                });
                if !ok {
                    continue;
                }
            }
            if effect.chance < 100 && rand::thread_rng().gen_range(0..100) > effect.chance {
                continue;
            }
            if let (Some(last), Some((EffectKind::NpcSpawnerSpawnEffect, id))) =
                (last_applied, effect.template)
            {
                let spawner =
                    |e: &SkillEffect| match e.template.and_then(|(k, i)| data.effect(k, i)) {
                        Some(EffectTemplate::NpcSpawnerSpawn(s)) => Some(s.spawner_id),
                        _ => None,
                    };
                if last.effect_id == effect.effect_id && spawner(last) == spawner(effect) {
                    let _ = id;
                    continue;
                }
            }
            to_apply.push((target, effect));
            last_applied = Some(effect);
        }
    }

    let items = server.systems.skills.ext.items();
    let mut consumed_items: Vec<(u64, i32)> = Vec::new();
    let mut consumed_templates: Vec<(u32, i32)> = Vec::new();
    if let Some(last) = last_applied {
        if let (
            SkillCaster::Item {
                item_id,
                item_template_id,
                ..
            },
            Some(_),
        ) = (ctx.caster_caster, &player)
        {
            if last.consume_source_item || items.use_skill_as_reagent(server, item_template_id) {
                consumed_items.push((item_id, last.consume_item_count));
            }
        }
        if let Some(p) = &player {
            if last.consume_item_id != 0 && last.consume_item_count > 0 {
                // source item consumption, or enough of the item in the bag / equipment
                if last.consume_source_item
                    || items.item_count(server, p, last.consume_item_id) >= last.consume_item_count
                {
                    consumed_templates.push((last.consume_item_id, last.consume_item_count));
                }
            }
        }
    }
    let reagents = data.reagents_for(t.id);
    let products = data.products_for(t.id);
    if !reagents.is_empty() || !products.is_empty() {
        if let Some(p) = &player {
            if !reagents.is_empty() {
                let mut found = false;
                for r in &reagents {
                    if items.item_count(server, p, r.item_id) >= i32::from(r.amount) {
                        consumed_templates.push((r.item_id, i32::from(r.amount)));
                        found = true;
                        if t.first_reagent_only {
                            break;
                        }
                    } else if !t.first_reagent_only {
                        skill.state().cancelled = true;
                        return;
                    }
                }
                if !found {
                    skill.state().cancelled = true;
                    return;
                }
            }
            for prod in &products {
                items.acquire_item(server, p, prod.item_id, i32::from(prod.amount));
            }
        }
    }

    // Weighted selection
    let weighted_total: i32 = to_apply.iter().map(|(_, e)| e.weight).sum();
    let mut selected = -1i32;
    if weighted_total > 0 {
        selected = rand::thread_rng().gen_range(0..weighted_total);
    }
    let mut current_weight = 0;
    let mut packets = CompressedGamePackets::new();
    let tl = skill.tl_id();
    for (target, effect) in &to_apply {
        if effect.weight > 0 {
            if selected == -1 {
                continue;
            }
            current_weight += effect.weight;
            if selected >= current_weight {
                continue;
            }
            selected = -1;
        }
        let Some((kind, id)) = effect.template else {
            tracing::error!(
                "Template not found for Skill[{}] Effect[{}]",
                t.id,
                effect.effect_id
            );
            continue;
        };
        let Some(template) = data.effect(kind, id) else {
            continue;
        };
        let this_target_caster = if target.obj_id() == ctx.target_caster.obj_id() {
            ctx.target_caster
        } else {
            SkillCastTarget::Unit(target.obj_id())
        };
        let mut args = EffectArgs {
            caster: Some(caster.clone()),
            caster_caster: ctx.caster_caster,
            target: target.clone(),
            target_caster: this_target_caster,
            cast: CastAction::Skill {
                skill_id: t.id,
                tl_id: tl,
            },
            source: EffectSource::from_skill(skill.clone()),
            skill_object: &ctx.skill_object,
        };
        if let EffectTemplate::KillNpcWithoutCorpse(k) = template {
            let npc = world_of(server, caster).and_then(|w| w.npc_by_template_id(k.npc_id));
            let effective = npc
                .clone()
                .or_else(|| target.as_obj().and_then(|o| o.as_npc().cloned()));
            if effective.as_ref().is_some_and(|n| n.data().unit.is_dead()) {
                // skip: KillNpcWithoutCorpse happens before death
            } else {
                if let Some(n) = npc {
                    args.caster = Some(WorldObject::from(n));
                }
                effects::apply(server, kind, template, &args, Some(&mut packets));
            }
        } else {
            effects::apply(server, kind, template, &args, Some(&mut packets));
            if player
                .as_ref()
                .is_some_and(|p| p.data().skill_char.skill_cancelled)
            {
                skill.state().cancelled = true;
            }
        }
        if effect.item_set_id > 0 {
            consumed_templates.extend(items.item_set(server, effect.item_set_id));
        }
    }
    if let SkillCaster::Item {
        item_id,
        item_template_id,
        ..
    } = ctx.caster_caster
    {
        let chance = caster
            .with_unit(|u| u.unit().skill_state.condition_chance)
            .unwrap_or(false);
        if chance {
            let Some(p) = &player else {
                return;
            };
            server
                .systems
                .skills
                .ext
                .quests()
                .item_use(server, p, item_id);
            if items.use_skill_as_reagent(server, item_template_id)
                && reagents.is_empty()
                && products.is_empty()
                && consumed_items.is_empty()
                && t.effects.is_empty()
            {
                consumed_items.push((item_id, 1));
            }
        }
    }
    if !packets.is_empty() {
        broadcast(server, caster, &packets, true);
    }
    // Hack to consume TreasureMap items (as the C#).
    if let Some(p) = player.as_ref().filter(|_| t.id == DIG_UP_TREASURE_SKILL) {
        match unit_reqs::treasure_map_nearby(server, Some(p), unit_reqs::TREASURE_MAP_DIG_RANGE) {
            Some(map_id) => consumed_items.push((map_id, 1)),
            None => {
                let (name, id) = {
                    let c = p.data();
                    (c.unit.name.clone(), c.id)
                };
                tracing::error!(
                    "Unable to find a treasure map to take from user {name} ({id}) when digging up treasure"
                );
            }
        }
    }
    if !skill.cancelled() {
        if let Some(p) = &player {
            for (item, amount) in consumed_items {
                items.consume_item_by_id(server, p, item, amount);
            }
            for (tid, amount) in consumed_templates {
                items.consume_item_template(server, p, tid, amount);
            }
        }
    }
}

/// `Skill.EndSkill(caster)`.
pub fn end_skill(server: &GameServer, skill: &SkillRef, caster: &WorldObject) {
    if !caster.is_unit() {
        return;
    }
    let t = &skill.template;
    if let Some(p) = caster.as_player() {
        let cancelled = skill.cancelled();
        let labor = t.consume_labor_power;
        let mut cost = labor;
        let step = p
            .data()
            .actability
            .actabilities
            .iter()
            .find(|a| a.id == t.actability_group_id as u8 as u32)
            .map(|a| a.step);
        if let Some(step) = step {
            cost = (f64::from(cost) * f64::from(super::progress::labor_cost_multiplier(step)))
                .round_ties_even() as i32;
        }
        if labor > 0 && cost < 1 {
            cost = 1;
        }
        let have = i32::from(p.data().labor_power);
        if cost > 0 && !cancelled && have >= cost {
            super::progress::change_labor(server, p, -(cost as i16), t.actability_group_id);
        }
        if t.gain_life_point > 0 && !cancelled {
            let rate = server.systems.skills.config().vocation_rate;
            super::progress::change_game_points(
                server,
                p,
                super::progress::GAME_POINT_VOCATION,
                (rate * f64::from(t.gain_life_point)).ceil() as i32,
            );
        }
    }
    skill.invoke_callback();
    // Unit.OnSkillEnd: no-op for every ported kind (npc AI hooks it).
    let tl = skill.tl_id();
    broadcast(server, caster, &SCSkillEndedPacket { tl }, true);
    server.systems.skills.tl_ids.release(tl);
    skill.state().tl_id = 0;
    reset_cooldown_if_ignored(server, skill, caster);
}

fn reset_cooldown_if_ignored(server: &GameServer, skill: &SkillRun, caster: &WorldObject) {
    if let Some(o) = owner_character(server, caster) {
        if o.data().skill_char.ignore_skill_cooldowns {
            o.send(SCSkillCooldownResetPacket {
                obj_id: o.obj_id,
                skill_id: skill.template.id,
                tag_id: 0,
                gcd: false,
            });
            caster.with_unit(|u| u.unit_mut().cooldowns.remove_cooldown(skill.template.id));
        }
    }
}

/// `Skill.Stop(caster, channelDoodad, casterCaster)`.
pub fn stop(server: &GameServer, skill: &SkillRef, caster: &WorldObject, channel_doodad: u32) {
    if !caster.is_unit() {
        return;
    }
    let t = &skill.template;
    if t.channeling_time > 0 {
        end_channeling(
            server,
            skill,
            caster,
            channel_doodad,
            &SkillCaster::Unit(caster.obj_id()),
        );
    }
    if t.toggle_buff_id != 0 {
        buffs::remove_effect_by_skill(server, caster, t.toggle_buff_id, t.id);
    }
    let tl = skill.tl_id();
    broadcast(
        server,
        caster,
        &SCCastingStoppedPacket { tl, duration: 0 },
        true,
    );
    broadcast(server, caster, &SCSkillEndedPacket { tl }, true);
    skill.invoke_callback();
    caster.with_unit(|u| u.unit_mut().skill_state.skill_task = None);
    skill.state().cancelled = true;
    server.systems.skills.tl_ids.release(tl);
    skill.state().tl_id = 0;
    reset_cooldown_if_ignored(server, skill, caster);
}

/// `Skill.StopSkill(caster)` (base melee / ranged skills).
pub fn stop_skill(server: &GameServer, skill: &SkillRef, caster: &WorldObject) {
    if !caster.is_unit() {
        return;
    }
    if let Some(a) = caster
        .with_unit(|u| u.unit().skill_state.auto_attack.clone())
        .flatten()
    {
        a.cancel.cancel();
    }
    let tl = skill.tl_id();
    broadcast(server, caster, &SCSkillEndedPacket { tl }, true);
    broadcast(
        server,
        caster,
        &SCSkillStoppedPacket {
            obj_id: caster.obj_id(),
            skill_id: skill.id,
        },
        true,
    );
    server.systems.skills.tl_ids.release(tl);
    skill.state().tl_id = 0;
}

/// `Skill.ConsumeMana(caster)`.
pub fn consume_mana(server: &GameServer, skill: &SkillRun, caster: &WorldObject) {
    if !caster.is_unit() {
        return;
    }
    let cost = mana_cost(server, skill, caster);
    unit_combat::reduce_current_mp(server, caster, cost);
    if let Some(p) = caster.as_player() {
        let mut c = p.data();
        c.skill_char.last_cast_ms = now_ms();
        c.skill_char.is_in_post_cast = true;
    }
}

/// `Unit.InterruptSkills()`.
pub fn interrupt_skills(server: &GameServer, unit: &WorldObject) {
    let Some((plot, task)) = unit.with_unit(|u| {
        let s = &u.unit().skill_state;
        (s.active_plot.clone(), s.skill_task.clone())
    }) else {
        return;
    };
    if let Some(p) = plot {
        p.0.request_cancellation();
    }
    let Some(task) = task else {
        return;
    };
    match task.kind {
        SkillTaskKind::EndChanneling { channel_doodad } => {
            stop(server, &task.skill, unit, channel_doodad)
        }
        SkillTaskKind::Cast => stop(server, &task.skill, unit, 0),
    }
}

/// `Unit.UseSkill(skillId, target)`.
pub fn unit_use_skill(
    server: &GameServer,
    unit: &WorldObject,
    skill_id: u32,
    target: &WorldObject,
) -> u8 {
    let Some(t) = server.game_data.systems.skills.skill(skill_id).cloned() else {
        tracing::error!("UseSkill: skill {skill_id} has no template (NullReferenceException)");
        return skill_result::INVALID_SKILL;
    };
    let skill = SkillRun::new(t, 1);
    let mut sct = if target.as_doodad().is_some() {
        SkillCastTarget::Doodad(target.obj_id())
    } else {
        SkillCastTarget::Unit(target.obj_id())
    };
    use_skill(
        server,
        &skill,
        unit,
        SkillCaster::Unit(unit.obj_id()),
        &mut sct,
        None,
        true,
    )
    .0
}

/// `SkillManager.GetAttackDelay(template, caster, includeCooldown, additionalDelay)`.
pub fn attack_delay(
    server: &GameServer,
    t: &SkillTemplate,
    caster: &WorldObject,
    include_cooldown: bool,
    additional: f64,
) -> f64 {
    let gcd_mul = stat(server, caster, StatKind::GlobalCooldownMul);
    if matches!(t.id, 2..=4) && caster.as_player().is_some() {
        let (slot, fallback) = match t.id {
            2 => (equip_slot::MAINHAND, 1500.0),
            3 => (equip_slot::OFFHAND, 1500.0),
            _ => (equip_slot::RANGED, 1800.0),
        };
        let speed = server
            .systems
            .skills
            .ext
            .items()
            .weapon_holdable(server, caster, slot)
            .filter(|h| h.speed > 0.0)
            .map_or(fallback, |h| f64::from(h.speed));
        return (speed * (gcd_mul / 100.0)).clamp(400.0, 5000.0);
    }
    let cast_mul = stat(server, caster, StatKind::CastTimeMul);
    let cast = f64::from(t.casting_time) * cast_mul;
    let cd = if include_cooldown {
        f64::from(t.cooldown_time) * (gcd_mul / 100.0)
    } else {
        0.0
    };
    cast + cd + additional * (gcd_mul / 100.0)
}

/// `Unit.StartAutoSkill(skill)` (characters only).
pub fn start_auto_skill(server: &GameServer, caster: &WorldObject, skill: &SkillRef) {
    let Some(p) = caster.as_player() else { return };
    if p.data().unit.skill_state.auto_attack.is_some() {
        return;
    }
    let cancel = CancelToken::new();
    p.data().unit.skill_state.auto_attack = Some(AutoAttackSlot {
        skill: skill.clone(),
        cancel: cancel.clone(),
    });
    let delay = attack_delay(server, &skill.template, caster, true, 1000.0) as i64;
    let obj_id = p.obj_id;
    let skill = skill.clone();
    schedule_auto_attack(server, obj_id, skill, cancel, delay);
}

fn schedule_auto_attack(
    server: &GameServer,
    obj_id: u32,
    skill: SkillRef,
    cancel: CancelToken,
    delay: i64,
) {
    super::tasks::schedule(server, delay, move |srv| {
        if cancel.is_cancelled() {
            return;
        }
        let Some(p) = srv.world.character_by_obj_id(obj_id) else {
            return;
        };
        let next = auto_attack_tick(&srv, &p, &skill, &cancel);
        if let Some(next) = next {
            schedule_auto_attack(&srv, obj_id, skill, cancel, next);
        }
    });
}

/// `UseAutoAttackSkillTask.Execute`: `Some(next delay)` while it keeps running.
fn auto_attack_tick(
    server: &GameServer,
    p: &crate::world::PlayerRef,
    skill: &SkillRef,
    cancel: &CancelToken,
) -> Option<i64> {
    let me = WorldObject::from(p);
    let (hp, target_id, has_task, gcd) = {
        let c = p.data();
        (
            c.unit.hp,
            c.unit.current_target,
            c.unit.skill_state.skill_task.is_some(),
            c.unit.global_cooldown_ms,
        )
    };
    let target = (target_id != 0)
        .then(|| server.world.object(target_id))
        .flatten()
        .filter(WorldObject::is_unit);
    let target_hp = target
        .as_ref()
        .and_then(|t| t.with_unit(|u| u.unit().hp))
        .unwrap_or(0);
    if hp <= 0
        || target.is_none()
        || target_hp <= 0
        || target_id == p.obj_id
        || cancel.is_cancelled()
    {
        // StopAutoAttack
        cancel.cancel();
        let mut c = p.data();
        c.unit.is_auto_attack = false;
        c.unit.skill_state.auto_attack = None;
        return None;
    }
    let target = target?;
    let delay = attack_delay(server, &skill.template, &me, true, 1000.0) as i64;
    if has_task || gcd >= now_ms() {
        return Some(delay);
    }
    let me_t = Target::Obj(me.clone());
    let tt = Target::Obj(target.clone());
    if !can_attack(server, &me_t, &tt) {
        return Some(delay);
    }
    let items = server.systems.skills.ext.items();
    let slot = if skill.template.id == 4 {
        equip_slot::RANGED
    } else {
        equip_slot::MAINHAND
    };
    let max_range = items
        .weapon_holdable(server, &me, slot)
        .filter(|h| h.max_range > 0.0)
        .map(|h| h.max_range)
        .unwrap_or(if skill.template.max_range > 0 {
            skill.template.max_range as f32
        } else {
            3.0
        });
    if distance_to(server, &me_t, &tt, false) > max_range {
        return Some(delay);
    }
    let mut sct = SkillCastTarget::Unit(target.obj_id());
    use_skill(
        server,
        skill,
        &me,
        SkillCaster::Unit(me.obj_id()),
        &mut sct,
        Some(SkillObject::None),
        true,
    );
    if skill.template.id == 2 {
        let off = items.weapon_holdable(server, &me, equip_slot::OFFHAND);
        if let Some(h) = off {
            if h.slot_type_id != slot_type::SHIELD {
                if let Some(t3) = server.game_data.systems.skills.skill(3).cloned() {
                    let off_skill = SkillRun::new(t3, 1);
                    let mut sct = SkillCastTarget::Unit(target.obj_id());
                    use_skill(
                        server,
                        &off_skill,
                        &me,
                        SkillCaster::Unit(me.obj_id()),
                        &mut sct,
                        Some(SkillObject::None),
                        true,
                    );
                }
            }
        }
    }
    Some(delay)
}

/// The world position as a `PositionAndRotation` (re-export for callers).
pub fn pos(server: &GameServer, t: &Target) -> PositionAndRotation {
    world_pos(server, t)
}

/// A position unit target (for callers outside the module).
pub fn pos_target(p: PosUnit) -> Target {
    Target::Pos(p)
}

#[cfg(test)]
#[path = "owned_npc_target_tests.rs"]
mod owned_npc_target_tests;
