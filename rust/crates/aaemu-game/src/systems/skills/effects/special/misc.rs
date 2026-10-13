//! Special effect actions on progression, points, portals, doodads, mates / slaves, crime and
//! account UI: `AddExp`, `AddLaborPower`, `GiveLivingPoint`, `GiveHonorPoint`,
//! `GiveCrimePoint`, `EnterBeautyshop`, `ExitArchemall`, `GiveAppellation`, `SavePortal`,
//! `SpawnDoodad`, `RemoveDoodad`, `SpawnPet`, `SpawnSlave`, `AttachTo`, `EscapeMySlave`,
//! `DeclareDominion`, `ReportBot`, `ReportBotExpired`. The managers they reach into (doodads,
//! mates, slaves, portals, instances, crime, dominions) belong to other subsystems and are
//! called through `ext` hooks.

use std::collections::HashMap;

use crate::data::formulas::FormulaKind;
use crate::managers::chat::chat_type;
use crate::models::transform::PositionAndRotation;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::world::WorldObject;

use super::super::super::broadcast;
use super::super::super::geometry::{
    around_radius, instance_and_zone, world_of, world_pos, Target,
};
use super::super::super::packets_special::{
    SCBotSuspectReportedPacket, SCToggleBeautyshopResponsePacket,
};
use super::super::super::progress;
use super::super::super::types::{SkillCastTarget, SkillObject, SkillTargetSelection};
use super::super::EffectArgs;
use super::movement::{add_distance_to_front_deg, rad_to_deg};
use super::{caster_is_player, log_values};

/// `ErrorMessageType.InvalidTarget`.
const ERROR_INVALID_TARGET: i16 = 18;

/// `AddExp`: `value1` exp (or, at level 50+ with `value1 == 0`, the `ExpBySkillEffect` formula
/// times `value3 / 10`) to a character, or to a mate unless it is at its max level (then the
/// skill is cancelled and the caster gets `InvalidTarget`).
pub fn add_exp(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "AddExp", v);
    let Some(unit) = args.target.as_obj().filter(|o| o.is_unit()) else {
        return;
    };
    let level = unit.with_unit(|u| u.unit().level).unwrap_or(0);
    let mut exp_to_add = v[0];
    if exp_to_add == 0 && level >= 50 {
        // Experia
        let Some(formula) = server
            .game_data
            .formulas
            .get_formula_by_kind(FormulaKind::ExpBySkillEffect)
        else {
            tracing::error!(
                "Special effects: AddExp has no ExpBySkillEffect formula (NullReferenceException)"
            );
            return;
        };
        let params = HashMap::from([("pc_level", f64::from(level))]);
        let res = formula.evaluate(&params);
        exp_to_add = (res * f64::from(v[2] as f32 / 10.0)) as i32;
    }
    match unit {
        WorldObject::Mate(mate) => {
            let npcs = server.systems.skills.ext.npcs();
            if npcs.mate_is_max_level(server, mate) {
                // Cancel the skill and don't consume the item/reagent (e.g. Companion Crust)
                match args.source.skill() {
                    Some(s) => s.state().cancelled = true,
                    None => {
                        tracing::error!(
                            "Special effects: AddExp without a skill (NullReferenceException)"
                        );
                        return;
                    }
                }
                if let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) {
                    p.send(SCErrorMsgPacket::new(ERROR_INVALID_TARGET, 0, true));
                }
            } else {
                npcs.mate_add_exp(server, mate, exp_to_add);
            }
        }
        WorldObject::Player(p) => progress::add_exp(server, p, exp_to_add, true),
        _ => {}
    }
}

/// `AddLaborPower`: `ChangeLabor((short)value1, 0)`.
pub fn add_labor_power(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    if let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) {
        progress::change_labor(server, p, v[0] as i16, 0);
        log_values(args, "AddLaborPower", v);
    }
}

/// `GiveLivingPoint`: `round(VocationRate * value1)` vocation points.
pub fn give_living_point(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "GiveLivingPoint", v);
    let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) else {
        return;
    };
    let rate = server.systems.skills.config().vocation_rate;
    let points = (rate * f64::from(v[0])).round_ties_even() as i32;
    progress::change_game_points(server, p, progress::GAME_POINT_VOCATION, points);
}

/// `GiveHonorPoint`: `round(HonorRate * value1)` honor points.
pub fn give_honor_point(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    if caster_is_player(args) {
        tracing::debug!(
            "Special effects: GiveHonorPoint amount {}, value2 {}, value3 {}, value4 {}",
            v[0],
            v[1],
            v[2],
            v[3]
        );
    }
    let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) else {
        return;
    };
    let rate = server.systems.skills.config().honor_rate;
    let points = (rate * f64::from(v[0])).round_ties_even() as i32;
    progress::change_game_points(server, p, progress::GAME_POINT_HONOR, points);
}

/// `GiveCrimePoint` (only logs in the C#).
pub fn give_crime_point(_server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "GiveCrimePoint", v);
}

/// `EnterBeautyshop`: `SCToggleBeautyshopResponsePacket(1)`.
pub fn enter_beautyshop(_server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    if let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) {
        log_values(args, "EnterBeautyshop", v);
        p.send(SCToggleBeautyshopResponsePacket { state: 1 });
    }
}

/// `ExitArchemall`: `IndunManager.RequestLeaveInstance(character)`.
pub fn exit_archemall(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "ExitArchemall", v);
    if let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) {
        server.systems.skills.ext.npcs().leave_instance(server, p);
    }
}

/// `GiveAppellation`: `character.Appellations.Add(value1)`.
pub fn give_appellation(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    if let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) {
        tracing::debug!("Special effects: GiveAppellation value1 {}", v[0]);
        server
            .systems
            .skills
            .ext
            .social()
            .give_appellation(server, p, v[0] as u32);
    }
}

/// `SavePortal`: a new private portal at the character's position (`SkillObjectSavePortalInfo`
/// id 0) or a rename of portal `id`.
pub fn save_portal(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let Some(caster) = &args.caster else {
        return;
    };
    let Some(p) = caster.as_player() else {
        return;
    };
    log_values(args, "SavePortal", v);
    let SkillObject::SavePortalInfo { id, name } = args.skill_object else {
        return;
    };
    let npcs = server.systems.skills.ext.npcs();
    if *id == 0 {
        let pos = world_pos(server, &Target::Obj(caster.clone()));
        let zone_id = p.data().unit.transform.zone_id;
        npcs.add_private_portal(server, p, pos.x, pos.y, pos.z, pos.rot_z, zone_id, name);
    } else {
        npcs.rename_private_portal(server, p, *id as u32, name);
    }
}

/// `SpawnDoodad`: doodad `value1` one metre in front of the caster (or the target for
/// `TargetSelection.Target` skills), facing like the target, spawned after `value2` ms.
///
/// Position and orientation use the selected source/target frame, in radians.
/// `value3` (create trade pack) is unused, as in the C#. The
/// doodad itself (`DoodadManager.Create`, `GeoData.GetHeight`, `InitDoodad`, `Spawn`) is the
/// npc subsystem's.
pub fn spawn_doodad(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let [doodad_id, delay, create_trade_pack, value4] = v;
    let Some(caster) = &args.caster else {
        tracing::warn!(
            "Special effects: SpawnDoodad has no caster defined, doodadId {doodad_id}, delay {delay}, createTradePack {create_trade_pack}, value4 {value4}"
        );
        return;
    };
    if caster.as_player().is_some() {
        tracing::debug!(
            "Special effects: SpawnDoodad doodadId {doodad_id}, delay {delay}, createTradePack {create_trade_pack}, value4 {value4}"
        );
    }
    let caster_t = Target::Obj(caster.clone());
    let target_pos = world_pos(server, &args.target);
    let selection = args
        .source
        .skill()
        .and_then(|s| SkillTargetSelection::from_raw(i64::from(s.template.target_selection)));
    let (base, zone_id) = match selection {
        Some(SkillTargetSelection::Target) => (target_pos, instance_and_zone(&args.target).1),
        _ => (world_pos(server, &caster_t), instance_and_zone(&caster_t).1),
    };
    let (xx, yy) = add_distance_to_front_deg(1.0, base.x, base.y, rad_to_deg(base.rot_z) + 90.0);
    let pos = PositionAndRotation::new(xx, yy, base.z, base.rot_x, base.rot_y, base.rot_z);
    server.systems.skills.ext.npcs().spawn_doodad(
        server,
        caster,
        doodad_id as u32,
        zone_id,
        pos,
        delay,
    );
}

/// Delete matching doodads through their manager; world visibility sends the removal once.
pub fn remove_doodad(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "RemoveDoodad", v);
    let Some(caster) = &args.caster else {
        tracing::error!("Special effects: RemoveDoodad without a caster (NullReferenceException)");
        return;
    };
    let npcs = server.systems.skills.ext.npcs();
    for o in around_radius(server, &Target::Obj(caster.clone()), 10.0, false) {
        let Some(d) = o.as_doodad() else {
            continue;
        };
        let template_id = d.data().template_id;
        if i64::from(template_id) == i64::from(v[0]) {
            npcs.delete_doodad(server, o.obj_id());
        }
    }
}

/// `SpawnPet`: `owner.Mates.SpawnMount(skillItem)` (the `value1` switch is empty in the C#).
pub fn spawn_pet(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "SpawnPet", v);
    let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) else {
        tracing::error!(
            "Special effects: SpawnPet caster is not a Character (InvalidCastException)"
        );
        return;
    };
    if !args.caster_caster.is_item() {
        tracing::error!(
            "Special effects: SpawnPet casterObj is not a SkillItem (InvalidCastException)"
        );
        return;
    }
    server
        .systems
        .skills
        .ext
        .npcs()
        .spawn_mount(server, p, &args.caster_caster);
}

/// `SpawnSlave`: `SlaveManager.Create(owner, skillItem)`.
pub fn spawn_slave(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "SpawnSlave", v);
    let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) else {
        tracing::error!(
            "Special effects: SpawnSlave caster is not a Character (InvalidCastException)"
        );
        return;
    };
    if !args.caster_caster.is_item() {
        tracing::error!(
            "Special effects: SpawnSlave casterObj is not a SkillItem (InvalidCastException)"
        );
        return;
    }
    server
        .systems
        .skills
        .ext
        .npcs()
        .spawn_slave(server, p, &args.caster_caster);
}

/// `AttachTo`: a character binds the target slave at attach point `value1`.
pub fn attach_to(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "AttachTo", v);
    let Some(slave) = args.target.as_obj().filter(|t| t.as_slave().is_some()) else {
        return;
    };
    let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) else {
        return;
    };
    server
        .systems
        .skills
        .ext
        .npcs()
        .bind_slave(server, p, slave.obj_id(), v[0]);
}

/// `EscapeMySlave`: `SlaveManager.RidersEscape(player, positionTarget)`.
pub fn escape_my_slave(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) else {
        return;
    };
    if !matches!(args.target_caster, SkillCastTarget::Position { .. }) {
        return;
    }
    tracing::debug!(
        "Special effects: EscapeMySlave value1 {}, value2 {}, value3 {}, value4 {}",
        v[0],
        v[1],
        v[2],
        v[3]
    );
    server
        .systems
        .skills
        .ext
        .npcs()
        .riders_escape(server, p, &args.target_caster);
}

/// `DeclareDominion`: an expedition member claims the lodestone (house) target; the dominion
/// data / packet / backpack consumption are the social subsystem's.
pub fn declare_dominion(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "DeclareDominion", v);
    let Some(caster) = &args.caster else {
        tracing::error!(
            "Special effects: DeclareDominion without a caster (NullReferenceException)"
        );
        return;
    };
    let Some(expedition_id) = caster.with_unit(|u| u.unit().expedition_id) else {
        tracing::error!(
            "Special effects: DeclareDominion caster is not a Unit (InvalidCastException)"
        );
        return;
    };
    if expedition_id == 0 {
        return;
    }
    // Check target is not already claimed
    let Some(lodestone) = args.target.as_obj().filter(|t| t.as_house().is_some()) else {
        return;
    };
    server
        .systems
        .skills
        .ext
        .social()
        .declare_dominion(server, caster, expedition_id, lodestone);
}

/// `ReportBot`: a character reports the target character as a bot (`CrimeManager.ReportBot`);
/// on success `SCBotSuspectReportedPacket` is broadcast around the reporter.
pub fn report_bot(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let Some(caster) = &args.caster else {
        return;
    };
    let Some(reporter) = caster.as_player() else {
        // Only players can report other players
        return;
    };
    tracing::debug!(
        "Special effects: ReportBot value1 {}, value2 {}, value3 {}, value4 {}",
        v[0],
        v[1],
        v[2],
        v[3]
    );
    let SkillCastTarget::Unit(target_obj_id) = args.target_caster else {
        tracing::debug!(
            "Special effects: ReportBot target is not a Unit, ObjId: {}",
            args.target_caster.obj_id()
        );
        return;
    };
    let Some(suspect) = world_of(server, caster).and_then(|w| w.character_by_obj_id(target_obj_id))
    else {
        tracing::debug!(
            "Special effects: ReportBot target is not a player, ObjId: {target_obj_id}"
        );
        return;
    };
    let msg = match args.skill_object {
        SkillObject::Text(t) => t.as_str(),
        _ => "",
    };
    let reporter_name = reporter.data().unit.name.clone();
    let suspect_name = suspect.data().unit.name.clone();
    if !server
        .systems
        .skills
        .ext
        .social()
        .report_bot(server, &suspect, reporter, msg)
    {
        tracing::warn!(
            "Special effects: ReportBot target {suspect_name} failed to get reported by {reporter_name} (possible multiple reports)"
        );
        return;
    }
    broadcast(
        server,
        caster,
        &SCBotSuspectReportedPacket {
            reporter: reporter_name,
            suspect: suspect_name,
        },
        true,
    );
}

/// `ReportBotExpired`: `CrimeManager.ReportBotExpired(player)` (suspect buffs removed).
pub fn report_bot_expired(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) else {
        // Only applies to players
        return;
    };
    tracing::debug!(
        "Special effects: ReportBotExpired value1 {}, value2 {}, value3 {}, value4 {}",
        v[0],
        v[1],
        v[2],
        v[3]
    );
    if server
        .systems
        .skills
        .ext
        .social()
        .report_bot_expired(server, p)
    {
        let (name, id) = {
            let c = p.data();
            (c.unit.name.clone(), c.id)
        };
        tracing::info!(
            "Special effects: ReportBotExpired, {name} ({id}) removed their suspect buffs"
        );
    }
}

/// `Character.SendMessage(message)` (`ChatType.System`).
pub(crate) fn send_system_message(p: &crate::world::PlayerRef, message: &str) {
    crate::world::session::send_message(p, chat_type::SYSTEM, message);
}
