//! Original 1.2 dominion snapshots and the social edge of persistent castle authority.

use aaemu_commons::helpers::convert_coord_to_long;
use aaemu_commons::{Marshal, PacketStream};
use chrono::{DateTime, Utc};

use crate::models::container::{equip_slot, slot_type};
use crate::models::item_task::task_type;
use crate::server::GameServer;
use crate::systems::economy::inventory::{Inv, ItemTxn};
use crate::systems::housing::dominion::runtime::is_live_object;
use crate::systems::housing::dominion::runtime::{apply_tower_state, tower_identity};
use crate::systems::housing::dominion::state::{
    DominionError, DominionState, SiegeSide, SiegeStatus,
};
use crate::systems::skills::effects::EffectArgs;
use crate::systems::skills::types::{CastAction, SkillCastTarget, SkillCaster};
use crate::world::{PlayerRef, WorldObject};

use super::packets::misc::{
    SCDominionDataPacket, SCDominionOwnerChangedPacket, SCDominionTaxRatePacket,
    SCSiegeAlertPacket, SCSiegeDeclaredPacket, SCSiegeMemberPacket, SCSiegeStatePacket,
};

const DECLARATION_SKILL: u32 = 13_661;
const PURIFYING_ARCHEUM: u32 = 16_493;

/// Original stock DominionGuardTowerStateNoticeKey indices, not fortification/model stages.
pub mod tower_alert {
    pub const FIRST_ATTACKED: u8 = 0;
    pub const BELOW_75_PERCENT: u8 = 1;
    pub const BELOW_50_PERCENT: u8 = 2;
    pub const ENGRAVABLE: u8 = 3;
    pub const ENGRAVING_STARTED: u8 = 4;
    pub const ENGRAVING_STOPPED: u8 = 5;
    pub const ENGRAVING_SUCCEEDED: u8 = 6;
}

/// Emit only actual roster differences, removing the previous expedition entry before adding
/// a replacement. Call while the housing operation serializes the old/new transition, before
/// the full state snapshot. Repeated registration returns old==new and emits no duplicates.
pub fn broadcast_participant_changes(
    server: &GameServer,
    old: &DominionState,
    new: &DominionState,
) {
    if old.zone_group_id != new.zone_group_id || old.tower != new.tower {
        return;
    }
    let Some(data) = snapshot(server, new, Utc::now().timestamp()) else {
        return;
    };
    let empty = std::collections::BTreeMap::new();
    let old_roster = old.siege.as_ref().map_or(&empty, |s| &s.participants);
    let new_roster = new.siege.as_ref().map_or(&empty, |s| &s.participants);
    for participant in old_roster
        .values()
        .filter(|p| new_roster.get(&p.character_id) != Some(*p))
    {
        server.world.broadcast_to_server(&SCSiegeMemberPacket {
            id: data.zone_id,
            expedition_id: participant.expedition_id,
            character_id: participant.character_id,
            added: false,
        });
    }
    for participant in new_roster
        .values()
        .filter(|p| old_roster.get(&p.character_id) != Some(*p))
    {
        server.world.broadcast_to_server(&SCSiegeMemberPacket {
            id: data.zone_id,
            expedition_id: participant.expedition_id,
            character_id: participant.character_id,
            added: true,
        });
    }
}

/// The offense declaration notification for a newly accepted explicit siege schedule.
pub fn broadcast_siege_declared(server: &GameServer, state: &DominionState) {
    let Some(data) = snapshot(server, state, Utc::now().timestamp()) else {
        return;
    };
    if data.siege_timers.unk2_data.id == 0 {
        return;
    }
    server.world.broadcast_to_server(&SCSiegeDeclaredPacket {
        id: data.zone_id,
        offense: data.siege_timers.unk2_data,
    });
}

/// Tower alerts omit their zone from the native Lua event. Limit delivery to online players
/// in this instance who belong to an involved expedition or are physically in this territory.
pub fn send_tower_alert(
    server: &GameServer,
    state: &DominionState,
    alert: u8,
    actor: Option<&PlayerRef>,
) {
    if alert > tower_alert::ENGRAVING_SUCCEEDED
        || snapshot(server, state, Utc::now().timestamp()).is_none()
    {
        return;
    }
    let (character_id, name) = actor
        .filter(|p| p.character_id != 0 && is_live_object(server, &WorldObject::from(*p)))
        .map(|p| {
            let name = p.data().unit.name.clone();
            let name = if name.is_empty() {
                server
                    .names
                    .get_character_name(p.character_id)
                    .unwrap_or_default()
            } else {
                name
            };
            (p.character_id, name)
        })
        .unwrap_or_default();
    let packet = SCSiegeAlertPacket {
        id: state.zone_group_id as u16,
        alert,
        character_id,
        name,
    };
    let mut expeditions = vec![state.owner_expedition_id];
    if let Some(siege) = &state.siege {
        expeditions.extend([siege.attacker_expedition_id, siege.defender_expedition_id]);
    }
    for player in server.world.all_characters() {
        let (online, transform) = {
            let c = player.data();
            (c.is_online, c.unit.transform)
        };
        if !online || transform.instance_id != state.tower.instance_id {
            continue;
        }
        let expedition = server
            .expeditions
            .expedition_of_character(player.character_id);
        let in_expedition = expedition != 0 && expeditions.contains(&expedition);
        let in_territory = server
            .game_data
            .zones
            .zone_by_key(transform.zone_id)
            .is_some_and(|z| z.group_id == state.zone_group_id);
        if in_expedition || in_territory {
            player.send(packet.clone());
        }
    }
}

/// Build only snapshots backed by the same live lodestone and authored 1.2 setting.
pub fn snapshot(server: &GameServer, state: &DominionState, now: i64) -> Option<DominionData> {
    state.validate().ok()?;
    let house = server.systems.housing.manager.by_id(state.tower.house_id)?;
    let (zone, identity) = tower_identity(server, &house)?;
    let obj = WorldObject::from(&house);
    if zone != state.zone_group_id || identity != state.tower || !is_live_object(server, &obj) {
        return None;
    }
    let setting = server
        .game_data
        .systems
        .housing
        .castles
        .guard_tower_settings
        .get(&identity.guard_tower_setting_id)?;
    let pos = server.world.world_of(&obj)?.world_pos_rot(&obj);
    if !pos.x.is_finite() || !pos.y.is_finite() || !pos.z.is_finite() {
        return None;
    }
    let epoch = DateTime::from_timestamp(0, 0)?;
    let mut timers = DominionSiegeTimers {
        started: epoch,
        fixed: epoch,
        // Native 1.2: 0=no_dominion, 1=auction, 2=declare, 3=warmup, 4=siege, 5=peace.
        siege_period: if state.owner_expedition_id == 0 { 0 } else { 5 },
        ..Default::default()
    };
    if let Some(siege) = state
        .siege
        .as_ref()
        .filter(|s| s.status != SiegeStatus::Finished && now < s.ends_at)
    {
        // The five native duration slots are auction, declare, warmup, siege and peace,
        // in seconds. This first version has an explicit GM schedule and no auction.
        timers.durations = [
            0,
            0,
            i32::try_from(siege.starts_at.checked_sub(siege.scheduled_at)?).ok()?,
            i32::try_from(siege.ends_at.checked_sub(siege.starts_at)?).ok()?,
            0,
        ];
        timers.started = DateTime::from_timestamp(siege.scheduled_at, 0)?;
        timers.siege_period = if siege.active_at(now) { 4 } else { 3 };
        // The native owner setter and team getters identify these records by expedition.
        timers.unk_data.id = siege.defender_expedition_id;
        timers.unk2_data.id = siege.attacker_expedition_id;
        for (side, team) in [
            (SiegeSide::Defender, &mut timers.unk_data),
            (SiegeSide::Attacker, &mut timers.unk2_data),
        ] {
            if let Some(hq) = server
                .systems
                .housing
                .headquarters
                .snapshot(server, state, side)
            {
                team.obj_id = hq.obj_id;
                team.x = hq.position.x;
                team.y = hq.position.y;
                team.z = hq.position.z;
            }
        }
        for participant in siege.participants.values() {
            let team = match participant.side {
                SiegeSide::Defender => &mut timers.unk_data,
                SiegeSide::Attacker => &mut timers.unk2_data,
            };
            team.unk_ids.push(participant.character_id);
        }
        // Explicit GM rosters have no automatic reinforcement stage. Native ni/nr are
        // initial/final limits; making both equal the current limit avoids a false pending
        // reinforcement indicator. 100 is the native array capacity, not a rank entitlement.
        for team in [&mut timers.unk_data, &mut timers.unk2_data] {
            team.ni = 100;
            team.nr = 100;
            team.limit = 100;
        }
        if timers.unk_data.unk_ids.len() > 100 || timers.unk2_data.unk_ids.len() > 100 {
            return None;
        }
    }
    Some(DominionData {
        zone_id: u16::try_from(zone).ok()?,
        expedition_id: state.owner_expedition_id,
        house: identity.house_id,
        tax_rate: state.tax_rate,
        x: pos.x,
        y: pos.y,
        z: pos.z,
        cur_house_tax_money: state.treasury.cur_house_tax_money,
        cur_hunt_tax_money: state.treasury.cur_hunt_tax_money,
        peace_tax_money: state.treasury.peace_tax_money,
        cur_house_tax_aa_point: state.treasury.cur_house_tax_aa_point,
        peace_tax_aa_point: state.treasury.peace_tax_aa_point,
        last_paid_time: DateTime::from_timestamp(state.last_paid_at, 0)?,
        last_siege_end_time: DateTime::from_timestamp(state.last_siege_end_at, 0)?,
        reign_start_time: DateTime::from_timestamp(state.reign_start_at, 0)?,
        last_tax_rate_changed_time: DateTime::from_timestamp(state.last_tax_rate_changed_at, 0)?,
        last_national_tax_rate_changed_time: DateTime::from_timestamp(
            state.last_national_tax_rate_changed_at,
            0,
        )?,
        national_tax_rate: state.national_tax_rate,
        obj_id: house.obj_id,
        territory_data: DominionTerritoryData {
            id: setting.id,
            id2: setting.initial_buff_id,
            max_gates: u8::try_from(setting.max_gates).ok()?,
            max_walls: u8::try_from(setting.max_walls).ok()?,
            radius_declare: i16::try_from(setting.radius_declare).ok()?,
            radius_dominion: u16::try_from(setting.radius_dominion).ok()?,
            radius_offense_hq: i16::try_from(setting.radius_offense_hq).ok()?,
            radius_siege: i16::try_from(setting.radius_siege).ok()?,
        },
        siege_timers: timers,
        non_pvp_start: epoch,
        ..Default::default()
    })
}

pub fn broadcast_snapshot(server: &GameServer, state: &DominionState, newly_declared: bool) {
    if let Some(dominion) = snapshot(server, state, Utc::now().timestamp()) {
        server.world.broadcast_to_server(&SCDominionDataPacket {
            dominion,
            newly_declared,
            final_data_by_request: true,
        });
    }
}

/// Announce an actual phase transition before replacing the native cached snapshot.
/// OnSiegeState emits its phase event even for unchanged phases, so roster and tax updates
/// must use broadcast_snapshot instead.
pub fn broadcast_phase_change(server: &GameServer, state: &DominionState) {
    let Some(dominion) = snapshot(server, state, Utc::now().timestamp()) else {
        return;
    };
    broadcast_siege_state(server, &dominion, 0);
    server.world.broadcast_to_server(&SCDominionDataPacket {
        dominion,
        newly_declared: false,
        final_data_by_request: true,
    });
}

/// Ownership delta followed by the authoritative snapshot: the native delta resets national
/// tax to 500 independently, which must not replace the server's persisted value.
pub fn broadcast_owner_change(server: &GameServer, state: &DominionState) {
    let Some(dominion) = snapshot(server, state, Utc::now().timestamp()) else {
        return;
    };
    // Native OnSiegeState needs the preceding cached phase (4 at capture) and copies the
    // new timers before emitting DOMINION_SIEGE_PERIOD_CHANGED. A snapshot first loses it.
    broadcast_siege_state(server, &dominion, state.owner_expedition_id);
    server
        .world
        .broadcast_to_server(&SCDominionOwnerChangedPacket {
            id: dominion.zone_id,
            unk_id: state.owner_expedition_id,
            rst: state.reign_start_at as u64,
            bestowed: false,
        });
    server.world.broadcast_to_server(&SCDominionDataPacket {
        dominion,
        newly_declared: false,
        final_data_by_request: true,
    });
}

fn broadcast_siege_state(server: &GameServer, dominion: &DominionData, new_owner: u32) {
    server.world.broadcast_to_server(&SCSiegeStatePacket {
        id: dominion.zone_id,
        territory_data: dominion.territory_data,
        siege_timers: dominion.siege_timers.clone(),
        new_owner,
        reign_start_time: dominion.reign_start_time,
    });
}

/// 1.2 has no request-dominion-data opcode; resend the persistent cache at world entry.
pub fn send_snapshots(server: &GameServer, player: &PlayerRef) {
    let now = Utc::now().timestamp();
    let snapshots: Vec<_> = server
        .systems
        .housing
        .dominions
        .snapshots()
        .iter()
        .filter_map(|state| snapshot(server, state, now))
        .collect();
    let count = snapshots.len();
    for (index, dominion) in snapshots.into_iter().enumerate() {
        player.send(SCDominionDataPacket {
            dominion,
            newly_declared: false,
            final_data_by_request: index + 1 == count,
        });
    }
}

fn is_expedition_leader(server: &GameServer, player: &PlayerRef, expedition: u32) -> bool {
    expedition != 0
        && player.data().unit.expedition_id == expedition
        && server
            .expeditions
            .expedition_of_character(player.character_id)
            == expedition
        && server
            .expeditions
            .get(expedition)
            .is_some_and(|e| !e.is_disbanded && e.faction.owner_id == player.character_id)
}

/// Match the original native IsDominionOwner gate and its inclusive 0..50 slider.
pub fn update_tax_rate(server: &GameServer, player: &PlayerRef, zone: u16, tax_rate: i32) {
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    let Some(state) = server.systems.housing.dominions.get(u32::from(zone)) else {
        return;
    };
    if !(0..=50).contains(&tax_rate)
        || !is_expedition_leader(server, player, state.owner_expedition_id)
    {
        return;
    }
    let state = match server.systems.housing.dominions.set_tax_rate(
        u32::from(zone),
        state.owner_expedition_id,
        tax_rate,
        Utc::now().timestamp(),
    ) {
        Ok(state) => state,
        Err(DominionError::TaxRateChangeLater) => {
            super::util::send_error(
                player,
                crate::systems::economy::error_message::TAXRATE_CHANGE_LATER,
            );
            return;
        }
        Err(_) => return,
    };
    server
        .world
        .broadcast_to_server(&SCDominionTaxRatePacket { id: zone, tax_rate });
    crate::systems::housing::dominion::tax::refresh_bills(server, u32::from(zone));
    broadcast_snapshot(server, &state, false);
}

/// Initial unowned claim, only from the accepted original 20-second skill and exact carried
/// Purifying Archeum. Ownership assignment and pack consumption share one authority operation.
pub fn declare_effect(server: &GameServer, args: &EffectArgs<'_>) {
    let (Some(caster), Some(source), Some(tower)) = (
        args.caster.as_ref(),
        args.source.skill.as_ref(),
        args.target.as_obj().and_then(WorldObject::as_house),
    ) else {
        return;
    };
    let Some(player) = caster.as_player() else {
        return;
    };
    let Some(canonical) = server.game_data.systems.skills.skill(DECLARATION_SKILL) else {
        return;
    };
    let (accepted, target_id) = {
        let state = source.state();
        (
            state.generation != 0 && !state.cancelled,
            state.initial_target,
        )
    };
    if !std::sync::Arc::ptr_eq(canonical, &source.template)
        || source.id != DECLARATION_SKILL
        || args.source.buff.is_some()
        || args.source.is_trigger
        || !accepted
        || target_id != tower.obj_id
        || !matches!(args.cast, CastAction::Skill { skill_id: DECLARATION_SKILL, tl_id } if tl_id == source.tl_id())
        || !matches!(args.caster_caster, SkillCaster::Unit(id) if id == player.obj_id)
        || !matches!(args.target_caster, SkillCastTarget::Unit(id) if id == tower.obj_id)
    {
        return;
    }
    let operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .expect("housing poisoned");
    let obj = WorldObject::from(tower);
    let Some((zone, identity)) = tower_identity(server, tower) else {
        return;
    };
    // Original 1.2 lodestones have a single authored claim action at build step 0
    // (housing139/step395/model893/skill13661), before becoming their main model.
    let Some(template) = server
        .game_data
        .systems
        .housing
        .template(tower.data().unit.template_id)
    else {
        return;
    };
    let initial_claim = template.build_steps.len() == 1
        && template.build_steps.get(&0).is_some_and(|step| {
            step.step == 0 && step.skill_id == DECLARATION_SKILL && step.num_actions == 1
        });
    let at_initial_action = {
        let h = tower.data();
        h.current_step == 0 && h.base_action == 0 && h.num_action == 0 && h.unit.owner_id == 0
    };
    if !initial_claim || !at_initial_action {
        return;
    }
    if !server
        .systems
        .housing
        .manager
        .by_id(identity.house_id)
        .is_some_and(|h| std::sync::Arc::ptr_eq(&h, tower))
        || !is_live_object(server, caster)
        || !is_live_object(server, &obj)
        || caster.transform().instance_id != identity.instance_id
        || tower.data().unit.hp <= 0
        || !server
            .game_data
            .systems
            .housing
            .castles
            .guard_tower_settings
            .contains_key(&identity.guard_tower_setting_id)
    {
        return;
    }
    let (expedition, alive) = {
        let c = player.data();
        (
            c.unit.expedition_id,
            c.unit.hp > 0 && !c.skill_char.skill_cancelled,
        )
    };
    if !alive || !is_expedition_leader(server, player, expedition) {
        return;
    }
    let Some(world) = server.world.world_of(caster) else {
        return;
    };
    let c = world.world_pos_rot(caster);
    let t = world.world_pos_rot(&obj);
    let distance2 = (c.x - t.x).powi(2) + (c.y - t.y).powi(2) + (c.z - t.z).powi(2);
    let range = canonical.max_range as f32;
    if !distance2.is_finite() || !range.is_finite() || range <= 0.0 || distance2 > range * range {
        return;
    }
    let mut effects = None;
    let claim = server.systems.housing.dominions.declare_unowned_with(
        zone,
        identity,
        expedition,
        Utc::now().timestamp(),
        || {
            let mut txn = ItemTxn::begin(server);
            let inv = Inv::of(player);
            let Some(equipment) = inv.get(slot_type::EQUIPMENT) else {
                return false;
            };
            let mut equipment = equipment.lock().expect("equipment poisoned");
            let pack = equipment
                .get_item_by_slot(equip_slot::BACKPACK)
                .filter(|item| {
                    item.template_id == PURIFYING_ARCHEUM
                        && item.count == 1
                        && item.owner_id == u64::from(player.character_id)
                        && equipment.owner_id == player.character_id
                })
                .map(|item| item.id);
            let consumed = pack.is_some_and(|id| {
                equipment.consume_item(
                    task_type::SKILL_REAGENTS,
                    PURIFYING_ARCHEUM,
                    1,
                    Some(id),
                    &mut txn.env,
                ) == 1
            });
            drop(equipment);
            effects = Some(txn.into_fx());
            consumed
        },
    );
    // Special50 has no separate CraftEffect. Advance only after the authority and exact
    // pack consumption succeeded, while other housing operations remain serialized.
    if claim.is_ok() {
        crate::systems::housing::manager::add_build_action(server, tower);
    }
    drop(operation);
    if let Some(effects) = effects {
        crate::systems::economy::fx::flush(server, effects);
    }
    if let Ok(state) = claim {
        let progress = {
            let h = tower.data();
            crate::systems::housing::packets::SCHouseBuildProgressPacket {
                tl: h.unit.tl_id,
                model_id: h.unit.model_id,
                all_step: h.all_action,
                cur_step: h.all_action,
            }
        };
        // Native 1.2 clamps UnitPoints against its current construction model's HP cap.
        // Install the finished model before sending the tower's completed health.
        crate::world::broadcast(&world, tower, &progress, true);
        apply_tower_state(server, &state);
        for doodad in server.systems.housing.manager.attached(tower.obj_id) {
            crate::systems::npcs::doodad::manager::spawn(server, &world, &doodad);
        }
        broadcast_snapshot(server, &state, true);
    }
}

/// `DominionData`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DominionData {
    pub zone_id: u16,
    pub expedition_id: u32,
    /// `House` (TODO in C#: an id?).
    pub house: u32,
    pub tax_rate: i32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub cur_house_tax_money: i32,
    pub cur_hunt_tax_money: i32,
    pub peace_tax_money: i32,
    pub cur_house_tax_aa_point: i32,
    pub peace_tax_aa_point: i32,
    pub last_paid_time: DateTime<Utc>,
    pub last_siege_end_time: DateTime<Utc>,
    pub reign_start_time: DateTime<Utc>,
    pub last_tax_rate_changed_time: DateTime<Utc>,
    /// `LastNationalTaxRateChagedTime` (sic).
    pub last_national_tax_rate_changed_time: DateTime<Utc>,
    pub national_tax_rate: u16,
    pub national_monument_db_id: i64,
    pub national_monument_x: f32,
    pub national_monument_y: f32,
    pub national_monument_z: f32,
    pub obj_id: u32,
    pub territory_data: DominionTerritoryData,
    pub siege_timers: DominionSiegeTimers,
    pub non_pvp_start: DateTime<Utc>,
    pub non_pvp_duration: u16,
}

impl Marshal for DominionData {
    fn write(&self, s: &mut PacketStream) {
        s.write_u16(self.zone_id)
            .write_u32(self.expedition_id)
            .write_u32(self.house)
            .write_i32(self.tax_rate)
            .write_i64(convert_coord_to_long(self.x))
            .write_i64(convert_coord_to_long(self.y))
            .write_f32(self.z)
            .write_i32(self.cur_house_tax_money)
            .write_i32(self.cur_hunt_tax_money)
            .write_i32(self.peace_tax_money)
            .write_i32(self.cur_house_tax_aa_point)
            .write_i32(self.peace_tax_aa_point)
            .write_date_time(self.last_paid_time)
            .write_date_time(self.last_siege_end_time)
            .write_date_time(self.reign_start_time)
            .write_date_time(self.last_tax_rate_changed_time)
            .write_date_time(self.last_national_tax_rate_changed_time)
            .write_u16(self.national_tax_rate)
            .write_i64(self.national_monument_db_id)
            .write_i64(convert_coord_to_long(self.national_monument_x))
            .write_i64(convert_coord_to_long(self.national_monument_y))
            .write_f32(self.national_monument_z)
            .write_bc(self.obj_id)
            .write(&self.territory_data)
            .write(&self.siege_timers)
            .write_date_time(self.non_pvp_start)
            .write_u16(self.non_pvp_duration);
    }
}

/// `DominionTerritoryData`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct DominionTerritoryData {
    pub id: u32,
    pub id2: u32,
    pub max_gates: u8,
    pub max_walls: u8,
    pub radius_declare: i16,
    pub radius_dominion: u16,
    pub radius_offense_hq: i16,
    pub radius_siege: i16,
}

impl Marshal for DominionTerritoryData {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_u32(self.id2)
            .write_u8(self.max_gates)
            .write_u8(self.max_walls)
            .write_i16(self.radius_declare)
            .write_u16(self.radius_dominion)
            .write_i16(self.radius_offense_hq)
            .write_i16(self.radius_siege);
    }
}

/// `DominionSiegeTimers` (TODO in C#: the names may be wrong).
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DominionSiegeTimers {
    pub durations: [i32; 5],
    pub started: DateTime<Utc>,
    pub fixed: DateTime<Utc>,
    pub bdm: i32,
    pub siege_period: u8,
    pub unk_data: DominionUnkData,
    pub unk2_data: DominionUnkData,
}

impl Marshal for DominionSiegeTimers {
    fn write(&self, s: &mut PacketStream) {
        for d in self.durations {
            s.write_i32(d);
        }
        s.write_date_time(self.started)
            .write_date_time(self.fixed)
            .write_i32(self.bdm)
            .write_u8(self.siege_period)
            .write(&self.unk_data)
            .write(&self.unk2_data);
    }
}

/// `DominionUnkData`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DominionUnkData {
    /// `Id` (TODO in C#: the expedition id).
    pub id: u32,
    pub obj_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub ni: u8,
    pub nr: u8,
    pub limit: u8,
    /// `UnkIds` (the count is written as a byte).
    pub unk_ids: Vec<u32>,
}

impl Marshal for DominionUnkData {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_bc(self.obj_id)
            .write_i64(convert_coord_to_long(self.x))
            .write_i64(convert_coord_to_long(self.y))
            .write_f32(self.z)
            .write_u8(self.ni)
            .write_u8(self.nr)
            .write_u8(self.limit)
            .write_u8(self.unk_ids.len() as u8);
        for id in &self.unk_ids {
            s.write_u32(*id);
        }
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use chrono::TimeZone;

    fn t(secs: i64) -> DateTime<Utc> {
        Utc.timestamp_opt(secs, 0).unwrap()
    }

    fn long(v: f32) -> i64 {
        ((v * 4096.0) as i64) << 32
    }

    pub(crate) fn sample() -> DominionData {
        DominionData {
            zone_id: 0x0102,
            expedition_id: 3,
            house: 4,
            tax_rate: 5,
            x: 100.5,
            y: 200.25,
            z: 30.0,
            cur_house_tax_money: 6,
            cur_hunt_tax_money: 7,
            peace_tax_money: 8,
            cur_house_tax_aa_point: 9,
            peace_tax_aa_point: 10,
            last_paid_time: t(11),
            last_siege_end_time: t(12),
            reign_start_time: t(13),
            last_tax_rate_changed_time: t(14),
            last_national_tax_rate_changed_time: t(15),
            national_tax_rate: 16,
            national_monument_db_id: 17,
            national_monument_x: 18.0,
            national_monument_y: 19.0,
            national_monument_z: 20.0,
            obj_id: 0x0A0B0C,
            territory_data: DominionTerritoryData {
                id: 21,
                id2: 22,
                max_gates: 23,
                max_walls: 24,
                radius_declare: -25,
                radius_dominion: 26,
                radius_offense_hq: 27,
                radius_siege: 28,
            },
            siege_timers: DominionSiegeTimers {
                durations: [1, 2, 3, 4, 5],
                started: t(29),
                fixed: t(30),
                bdm: 31,
                siege_period: 32,
                unk_data: DominionUnkData {
                    id: 33,
                    obj_id: 34,
                    x: 1.0,
                    y: 2.0,
                    z: 3.0,
                    ni: 4,
                    nr: 5,
                    limit: 6,
                    unk_ids: vec![7, 8],
                },
                unk2_data: DominionUnkData::default(),
            },
            non_pvp_start: t(35),
            non_pvp_duration: 36,
        }
    }

    /// The expected `Write` of [`sample`], spelled out with the C# primitives.
    pub(crate) fn sample_bytes() -> Vec<u8> {
        let mut e = PacketStream::new();
        e.write_u16(0x0102)
            .write_u32(3)
            .write_u32(4)
            .write_i32(5)
            .write_i64(long(100.5))
            .write_i64(long(200.25))
            .write_f32(30.0);
        for v in 6..=10 {
            e.write_i32(v);
        }
        for v in 11..=15 {
            e.write_i64(v);
        }
        e.write_u16(16)
            .write_i64(17)
            .write_i64(long(18.0))
            .write_i64(long(19.0))
            .write_f32(20.0)
            .write_bc(0x0A0B0C)
            // territory
            .write_u32(21)
            .write_u32(22)
            .write_u8(23)
            .write_u8(24)
            .write_i16(-25)
            .write_u16(26)
            .write_i16(27)
            .write_i16(28);
        // siege timers
        for v in 1..=5 {
            e.write_i32(v);
        }
        e.write_i64(29).write_i64(30).write_i32(31).write_u8(32);
        e.write_u32(33)
            .write_bc(34)
            .write_i64(long(1.0))
            .write_i64(long(2.0))
            .write_f32(3.0)
            .write_u8(4)
            .write_u8(5)
            .write_u8(6)
            .write_u8(2)
            .write_u32(7)
            .write_u32(8);
        e.write_u32(0)
            .write_bc(0)
            .write_i64(0)
            .write_i64(0)
            .write_f32(0.0)
            .write_raw(&[0, 0, 0, 0]);
        e.write_i64(35).write_u16(36);
        e.into_bytes()
    }

    #[test]
    fn dominion_data_write_is_byte_exact() {
        let mut s = PacketStream::new();
        sample().write(&mut s);
        assert_eq!(s.into_bytes(), sample_bytes());
    }
}
#[cfg(test)]
mod authority_tests {
    use std::collections::BTreeMap;
    use std::sync::Arc;

    use super::*;
    use crate::data::items::{ItemTemplate, TemplateKind};
    use crate::data::world_data::{SystemFaction, Zone};
    use crate::data::GameData;
    use crate::models::house::House;
    use crate::models::item::Item;
    use crate::net::registry::ClientPacket;
    use crate::opcodes::sc;
    use crate::systems::housing::data::{GuardTowerSetting, HousingBuildStep, HousingTemplate};
    use crate::systems::housing::dominion::state::{SiegeOutcome, SiegeParticipant, SiegeState};
    use crate::systems::skills::data::SkillTemplate;
    use crate::systems::skills::effects::EffectSource;
    use crate::systems::skills::geometry::Target;
    use crate::systems::skills::state::{SkillRef, SkillRun};
    use crate::systems::skills::types::SkillObject;
    use crate::systems::social::expedition::{self, Expedition};
    use crate::systems::social::packets::misc::CSUpdateDominionTaxRatePacket;
    use crate::test_support::{
        spawn_player, test_character, test_server_with, TestClient, TEST_ZONE,
    };
    use crate::world::{Entity, HouseRef};

    const ZONE: u32 = 25;
    const EXPEDITION: u32 = 77;
    const SETTING: u32 = 7;
    const DESIGN: u32 = 139;
    const WRONG_PACK: u32 = 26_672;

    struct Fixture {
        server: GameServer,
        client: TestClient,
        member_client: TestClient,
        leader: PlayerRef,
        member: PlayerRef,
        house: HouseRef,
        skill: SkillRef,
    }

    fn fixture() -> Fixture {
        let mut data = GameData::empty();
        data.zones.zones.insert(
            TEST_ZONE,
            Zone {
                id: 1,
                zone_key: TEST_ZONE,
                group_id: ZONE,
                ..Default::default()
            },
        );
        data.zones.zones.insert(
            TEST_ZONE + 1,
            Zone {
                id: 2,
                zone_key: TEST_ZONE + 1,
                group_id: ZONE + 1,
                ..Default::default()
            },
        );
        data.systems.housing.add_template(HousingTemplate {
            id: DESIGN,
            main_model_id: 894,
            hp: 1000,
            guard_tower_setting_id: SETTING,
            build_steps: BTreeMap::from([(
                0,
                HousingBuildStep {
                    id: 395,
                    housing_id: DESIGN,
                    step: 0,
                    model_id: 893,
                    skill_id: DECLARATION_SKILL,
                    num_actions: 1,
                },
            )]),
            ..Default::default()
        });
        data.systems.housing.castles.guard_tower_settings.insert(
            SETTING,
            GuardTowerSetting {
                id: SETTING,
                initial_buff_id: 4771,
                max_gates: 2,
                max_walls: 100,
                radius_declare: 450,
                radius_dominion: 200,
                radius_siege: 450,
                radius_offense_hq: 120,
                ..Default::default()
            },
        );
        data.systems.skills.add_skill(SkillTemplate {
            id: DECLARATION_SKILL,
            max_range: 10,
            casting_time: 20_000,
            ..Default::default()
        });
        for id in [PURIFYING_ARCHEUM, WRONG_PACK] {
            data.items.templates.insert(
                id,
                ItemTemplate {
                    kind: TemplateKind::Backpack { backpack_type: 1 },
                    ..ItemTemplate::plain(id, 1)
                },
            );
        }
        let server = test_server_with(data);
        let mut client = TestClient::new(&server, 1, 1001);
        let leader = spawn_player(
            &server,
            &client,
            test_character(1, "Lord", 100.0, 100.0),
            0x100,
        );
        let mut member_client = TestClient::new(&server, 2, 1002);
        let member = spawn_player(
            &server,
            &member_client,
            test_character(2, "Member", 100.0, 100.0),
            0x101,
        );
        for p in [&leader, &member] {
            p.data().unit.hp = 100;
            p.data().unit.expedition_id = EXPEDITION;
        }
        server.expeditions.add(Expedition {
            faction: SystemFaction {
                id: EXPEDITION,
                owner_id: 1,
                name: "Castle".into(),
                ..Default::default()
            },
            members: vec![
                expedition::member_from_character(&server, EXPEDITION, &leader, true),
                expedition::member_from_character(&server, EXPEDITION, &member, false),
            ],
            policies: Vec::new(),
            removed_members: Vec::new(),
            is_disbanded: false,
        });
        let mut h = House::new(13, DESIGN);
        h.current_step = 0;
        h.build_step_count = 1;
        h.all_action = 1;
        h.unit.model_id = 893;
        h.unit.hp = 1000;
        h.unit.max_hp = 1000;
        h.unit.tl_id = 12;
        h.unit.transform.set_xyz(105.0, 100.0, 100.0);
        h.unit.transform.zone_id = TEST_ZONE;
        let house = Entity::new(0x200, h);
        assert!(server.systems.housing.manager.insert(&house));
        crate::systems::housing::manager::spawn_house(&server, &house);
        let (_, identity) = tower_identity(&server, &house).unwrap();
        server
            .systems
            .housing
            .dominions
            .register_tower(ZONE, identity)
            .unwrap();
        let skill = SkillRun::new(
            server
                .game_data
                .systems
                .skills
                .skill(DECLARATION_SKILL)
                .unwrap()
                .clone(),
            1,
        );
        skill.state().generation = 1;
        skill.state().initial_target = house.obj_id;
        skill.state().tl_id = 5;
        client.conn.active_char = Some(leader.clone());
        client.drain();
        member_client.drain();
        Fixture {
            server,
            client,
            member_client,
            leader,
            member,
            house,
            skill,
        }
    }

    fn args<'a>(f: &Fixture, object: &'a SkillObject) -> EffectArgs<'a> {
        EffectArgs {
            caster: Some(WorldObject::from(&f.leader)),
            caster_caster: SkillCaster::Unit(f.leader.obj_id),
            target: Target::Obj(WorldObject::from(&f.house)),
            target_caster: SkillCastTarget::Unit(f.house.obj_id),
            cast: CastAction::Skill {
                skill_id: DECLARATION_SKILL,
                tl_id: 5,
            },
            source: EffectSource::from_skill(f.skill.clone()),
            skill_object: object,
        }
    }

    fn give_pack(f: &Fixture, id: u64, template_id: u32) {
        give_pack_for(f, &f.leader, id, template_id);
    }

    fn give_pack_for(f: &Fixture, player: &PlayerRef, id: u64, template_id: u32) {
        let item = Item::create(
            id,
            f.server.game_data.items.template(template_id).unwrap(),
            1,
            0,
            1,
            &f.server.game_data.items,
            0,
        );
        let equipment = Inv::of(player).equipment();
        equipment
            .lock()
            .unwrap()
            .add_new_item(item, equip_slot::BACKPACK, &f.server.game_data.items)
            .unwrap();
        f.server.items.add_container(equipment);
    }

    fn owned(f: &Fixture) -> DominionState {
        let (_, identity) = tower_identity(&f.server, &f.house).unwrap();
        f.server
            .systems
            .housing
            .dominions
            .declare_unowned_with(ZONE, identity, EXPEDITION, 90, || true)
            .unwrap()
    }

    #[tokio::test]
    async fn snapshot_uses_live_tower_authored_limits_and_native_timer_seconds() {
        let f = fixture();
        let mut state = owned(&f);
        let data = snapshot(&f.server, &state, 100).unwrap();
        assert_eq!(
            (data.zone_id, data.expedition_id, data.house, data.obj_id),
            (25, 77, 13, 0x200)
        );
        assert_eq!((data.x, data.y, data.z), (105.0, 100.0, 100.0));
        assert_eq!(
            data.territory_data,
            DominionTerritoryData {
                id: SETTING,
                id2: 4771,
                max_gates: 2,
                max_walls: 100,
                radius_declare: 450,
                radius_dominion: 200,
                radius_offense_hq: 120,
                radius_siege: 450,
            }
        );
        assert_eq!(
            (
                data.cur_house_tax_money,
                data.cur_hunt_tax_money,
                data.peace_tax_money,
                data.cur_house_tax_aa_point,
                data.peace_tax_aa_point,
                data.national_tax_rate
            ),
            (0, 0, 0, 0, 0, 0)
        );
        assert_eq!(data.reign_start_time.timestamp(), 90);
        assert_eq!(data.siege_timers.siege_period, 5);
        state.last_siege_id = 1;
        state.siege = Some(SiegeState {
            siege_id: 1,
            attacker_expedition_id: 88,
            defender_expedition_id: EXPEDITION,
            ownership_generation: state.ownership_generation,
            status: SiegeStatus::Scheduled,
            outcome: SiegeOutcome::Pending,
            scheduled_at: 100,
            starts_at: 120,
            ends_at: 420,
            ended_at: 0,
            participants: BTreeMap::from([
                (
                    1,
                    SiegeParticipant {
                        character_id: 1,
                        expedition_id: 77,
                        side: SiegeSide::Defender,
                    },
                ),
                (
                    2,
                    SiegeParticipant {
                        character_id: 2,
                        expedition_id: 88,
                        side: SiegeSide::Attacker,
                    },
                ),
            ]),
        });
        let warmup = snapshot(&f.server, &state, 119).unwrap().siege_timers;
        assert_eq!(warmup.siege_period, 3);
        assert_eq!(warmup.durations, [0, 0, 20, 300, 0]);
        assert_eq!(warmup.started.timestamp(), 100);
        assert_eq!((warmup.unk_data.id, warmup.unk2_data.id), (77, 88));
        assert_eq!((warmup.unk_data.limit, warmup.unk2_data.limit), (100, 100));
        assert_eq!((warmup.unk_data.ni, warmup.unk_data.nr), (100, 100));
        assert_eq!((warmup.unk2_data.ni, warmup.unk2_data.nr), (100, 100));
        assert_eq!(warmup.unk_data.unk_ids, vec![1]);
        assert_eq!(warmup.unk2_data.unk_ids, vec![2]);
        assert_eq!(
            snapshot(&f.server, &state, 120)
                .unwrap()
                .siege_timers
                .siege_period,
            4
        );
        assert_eq!(
            snapshot(&f.server, &state, 420)
                .unwrap()
                .siege_timers
                .siege_period,
            5
        );
        state.tower.instance_id = 99;
        assert!(snapshot(&f.server, &state, 200).is_none());
    }

    #[tokio::test]
    async fn declaration_consumes_only_exact_equipped_pack_once_and_restores_snapshot() {
        let mut f = fixture();
        let object = SkillObject::None;
        f.house.data().unit.hp = 500;
        give_pack(&f, 100, PURIFYING_ARCHEUM);
        declare_effect(&f.server, &args(&f, &object));
        let state = f.server.systems.housing.dominions.get(ZONE).unwrap();
        assert_eq!(state.owner_expedition_id, EXPEDITION);
        assert!(Inv::of(&f.leader).equipped(equip_slot::BACKPACK).is_none());
        assert_eq!(f.house.data().unit.expedition_id, EXPEDITION);
        assert_eq!(f.house.data().current_step, -1);
        assert_eq!(f.house.data().unit.model_id, 894);
        assert_eq!(f.house.data().unit.owner_id, 0);
        assert_eq!(f.house.data().unit.hp, 1000);
        let packets = f.client.drain();
        let progress = packets
            .iter()
            .position(|(op, _)| *op == sc::SCHouseBuildProgressPacket)
            .unwrap();
        let points = packets
            .iter()
            .position(|(op, body)| *op == sc::SCUnitPointsPacket && body.starts_with(&[0, 2, 0]))
            .unwrap();
        let snapshot = packets
            .iter()
            .position(|(op, _)| *op == sc::SCDominionDataPacket)
            .unwrap();
        assert!(progress < points && points < snapshot);
        assert_eq!(
            u32::from_le_bytes(packets[progress].1[2..6].try_into().unwrap()),
            894
        );
        assert_eq!(
            i32::from_le_bytes(packets[points].1[3..7].try_into().unwrap()),
            100_000
        );
        let snapshots: Vec<_> = packets
            .into_iter()
            .filter(|(op, _)| *op == sc::SCDominionDataPacket)
            .collect();
        assert_eq!(snapshots.len(), 1);
        assert_eq!(&snapshots[0].1[snapshots[0].1.len() - 2..], &[1, 1]);
        give_pack(&f, 101, PURIFYING_ARCHEUM);
        declare_effect(&f.server, &args(&f, &object));
        assert_eq!(
            Inv::of(&f.leader)
                .equipped(equip_slot::BACKPACK)
                .unwrap()
                .id,
            101
        );
        assert_eq!(f.server.systems.housing.dominions.get(ZONE).unwrap(), state);
        // Reload the authority cache, then world entry must send the same persistent owner.
        f.server
            .systems
            .housing
            .dominions
            .restore(vec![state.clone()])
            .unwrap();
        send_snapshots(&f.server, &f.leader);
        let packets = f.client.drain();
        assert_eq!(packets.len(), 1);
        assert_eq!(packets[0].0, sc::SCDominionDataPacket);
        assert_eq!(&packets[0].1[packets[0].1.len() - 2..], &[0, 1]);
        assert_eq!(
            u32::from_le_bytes(packets[0].1[2..6].try_into().unwrap()),
            EXPEDITION
        );
    }

    #[tokio::test]
    async fn declaration_rejects_completed_or_mismatched_authored_claim_action() {
        let object = SkillObject::None;
        for (step, base_action, num_action, owner) in [
            (-1, 0, 0, 0),
            (1, 0, 0, 0),
            (0, 1, 0, 0),
            (0, 0, 1, 0),
            (0, 0, 0, 1),
        ] {
            let mut f = fixture();
            give_pack(&f, 100, PURIFYING_ARCHEUM);
            {
                let mut h = f.house.data();
                h.current_step = step;
                h.base_action = base_action;
                h.num_action = num_action;
                h.unit.owner_id = owner;
            }
            declare_effect(&f.server, &args(&f, &object));
            assert_eq!(
                Inv::of(&f.leader)
                    .equipped(equip_slot::BACKPACK)
                    .unwrap()
                    .id,
                100
            );
            assert_eq!(
                f.server
                    .systems
                    .housing
                    .dominions
                    .get(ZONE)
                    .unwrap()
                    .owner_expedition_id,
                0
            );
            assert_eq!(f.house.data().current_step, step);
            assert_eq!(f.house.data().unit.model_id, 893);
            assert!(f.client.drain().is_empty());
        }
        let mut f = fixture();
        give_pack(&f, 100, PURIFYING_ARCHEUM);
        let mut template = f
            .server
            .game_data
            .systems
            .housing
            .template(DESIGN)
            .unwrap()
            .clone();
        template.build_steps.get_mut(&0).unwrap().skill_id = 13_660;
        Arc::get_mut(&mut f.server.game_data)
            .unwrap()
            .systems
            .housing
            .add_template(template);
        declare_effect(&f.server, &args(&f, &object));
        assert_eq!(
            Inv::of(&f.leader)
                .equipped(equip_slot::BACKPACK)
                .unwrap()
                .id,
            100
        );
        assert_eq!(
            f.server
                .systems
                .housing
                .dominions
                .get(ZONE)
                .unwrap()
                .owner_expedition_id,
            0
        );
        assert_eq!(f.house.data().current_step, 0);
        assert!(f.client.drain().is_empty());
    }

    #[tokio::test]
    async fn rejected_declarations_keep_pack_and_owner() {
        let f = fixture();
        let object = SkillObject::None;
        give_pack(&f, 100, WRONG_PACK);
        declare_effect(&f.server, &args(&f, &object));
        assert_eq!(
            Inv::of(&f.leader)
                .equipped(equip_slot::BACKPACK)
                .unwrap()
                .template_id,
            WRONG_PACK
        );
        assert_eq!(
            f.server
                .systems
                .housing
                .dominions
                .get(ZONE)
                .unwrap()
                .owner_expedition_id,
            0
        );

        let f = fixture();
        give_pack(&f, 100, PURIFYING_ARCHEUM);
        give_pack_for(&f, &f.member, 200, PURIFYING_ARCHEUM);
        let original = args(&f, &object);
        crate::systems::skills::ext::SocialSkillHooks::declare_dominion(
            &crate::systems::social::bridges::SocialForSkills,
            &f.server,
            &WorldObject::from(&f.leader),
            EXPEDITION,
            &WorldObject::from(&f.house),
        );
        let mut rejected = original.clone();
        rejected.caster = Some(WorldObject::from(&f.member));
        rejected.caster_caster = SkillCaster::Unit(f.member.obj_id);
        declare_effect(&f.server, &rejected);
        assert_eq!(
            Inv::of(&f.member)
                .equipped(equip_slot::BACKPACK)
                .unwrap()
                .id,
            200
        );
        rejected = original.clone();
        rejected.source.is_trigger = true;
        declare_effect(&f.server, &rejected);
        rejected = original.clone();
        let forged = SkillRun::new(Arc::new(f.skill.template.as_ref().clone()), 1);
        forged.state().generation = 1;
        forged.state().initial_target = f.house.obj_id;
        forged.state().tl_id = 5;
        rejected.source.skill = Some(forged);
        declare_effect(&f.server, &rejected);
        rejected = original.clone();
        rejected.target_caster = SkillCastTarget::Unit(0x201);
        declare_effect(&f.server, &rejected);
        f.skill.state().initial_target = 0x201;
        declare_effect(&f.server, &original);
        f.skill.state().initial_target = f.house.obj_id;
        f.house.data().unit.transform.set_xyz(111.0, 100.0, 100.0);
        declare_effect(&f.server, &original);
        f.house.data().unit.transform.set_xyz(105.0, 100.0, 100.0);
        f.skill.state().cancelled = true;
        declare_effect(&f.server, &original);
        assert_eq!(
            Inv::of(&f.leader)
                .equipped(equip_slot::BACKPACK)
                .unwrap()
                .id,
            100
        );
        assert_eq!(
            f.server
                .systems
                .housing
                .dominions
                .get(ZONE)
                .unwrap()
                .owner_expedition_id,
            0
        );
    }

    #[tokio::test]
    async fn tax_packet_requires_current_owner_leader_and_native_range() {
        let mut f = fixture();
        owned(&f);
        let now = Utc::now().timestamp();
        f.server
            .systems
            .housing
            .dominions
            .schedule_siege(ZONE, 88, now + 300, now + 900, now)
            .unwrap();
        f.client.conn.active_char = Some(f.member.clone());
        CSUpdateDominionTaxRatePacket {
            id: ZONE as u16,
            tax_rate: 25,
        }
        .handle(&f.server, &mut f.client.conn)
        .await
        .unwrap();
        f.client.conn.active_char = Some(f.leader.clone());
        for rate in [-1, 51, i32::MAX] {
            CSUpdateDominionTaxRatePacket {
                id: ZONE as u16,
                tax_rate: rate,
            }
            .handle(&f.server, &mut f.client.conn)
            .await
            .unwrap();
        }
        assert_eq!(
            f.server
                .systems
                .housing
                .dominions
                .get(ZONE)
                .unwrap()
                .tax_rate,
            0
        );
        assert!(f.client.drain().is_empty());
        CSUpdateDominionTaxRatePacket {
            id: ZONE as u16,
            tax_rate: 50,
        }
        .handle(&f.server, &mut f.client.conn)
        .await
        .unwrap();
        let state = f.server.systems.housing.dominions.get(ZONE).unwrap();
        assert_eq!(state.tax_rate, 50);
        assert!(state.last_tax_rate_changed_at > 0);
        let packets = f.client.drain();
        assert_eq!(
            packets.iter().map(|p| p.0).collect::<Vec<_>>(),
            vec![sc::SCDominionTaxRatePacket, sc::SCDominionDataPacket]
        );
        assert_eq!(packets[0].1, vec![25, 0, 50, 0, 0, 0]);
        CSUpdateDominionTaxRatePacket {
            id: ZONE as u16,
            tax_rate: 25,
        }
        .handle(&f.server, &mut f.client.conn)
        .await
        .unwrap();
        assert_eq!(f.server.systems.housing.dominions.get(ZONE).unwrap(), state);
        assert_eq!(
            f.client.drain(),
            vec![(
                sc::SCErrorMsgPacket,
                crate::systems::social::packets::body(
                    &crate::packets::world::SCErrorMsgPacket::new(
                        crate::systems::economy::error_message::TAXRATE_CHANGE_LATER,
                        0,
                        true,
                    )
                ),
            )]
        );
        CSUpdateDominionTaxRatePacket {
            id: ZONE as u16,
            tax_rate: 50,
        }
        .handle(&f.server, &mut f.client.conn)
        .await
        .unwrap();
        assert_eq!(f.server.systems.housing.dominions.get(ZONE).unwrap(), state);
        let repeated = f.client.drain();
        assert_eq!(
            repeated.iter().map(|p| p.0).collect::<Vec<_>>(),
            vec![sc::SCDominionTaxRatePacket, sc::SCDominionDataPacket]
        );
        f.leader.data().unit.expedition_id = 0;
        CSUpdateDominionTaxRatePacket {
            id: ZONE as u16,
            tax_rate: 0,
        }
        .handle(&f.server, &mut f.client.conn)
        .await
        .unwrap();
        assert_eq!(f.server.systems.housing.dominions.get(ZONE).unwrap(), state);
    }

    #[tokio::test]
    async fn participant_notifications_diff_rosters_remove_before_add_and_skip_repeated_rpc() {
        let mut f = fixture();
        owned(&f);
        let now = Utc::now().timestamp();
        let old = f
            .server
            .systems
            .housing
            .dominions
            .schedule_siege(ZONE, 88, now + 300, now + 900, now)
            .unwrap();
        let next = f
            .server
            .systems
            .housing
            .dominions
            .register_participant(ZONE, 1, 77, SiegeSide::Defender, now)
            .unwrap();
        broadcast_participant_changes(&f.server, &old, &next);
        assert_eq!(
            f.client.drain(),
            vec![(
                sc::SCSiegeMemberPacket,
                vec![25, 0, 77, 0, 0, 0, 1, 0, 0, 0, 1]
            )]
        );
        let repeated = f
            .server
            .systems
            .housing
            .dominions
            .register_participant(ZONE, 1, 77, SiegeSide::Defender, now)
            .unwrap();
        broadcast_participant_changes(&f.server, &next, &repeated);
        assert!(f.client.drain().is_empty());
        broadcast_snapshot(&f.server, &repeated, false);
        let packets = f.client.drain();
        assert_eq!(
            packets.iter().map(|p| p.0).collect::<Vec<_>>(),
            vec![sc::SCDominionDataPacket]
        );
        assert_eq!(&packets[0].1[packets[0].1.len() - 2..], &[0, 1]);
        let changed = f
            .server
            .systems
            .housing
            .dominions
            .register_participant(ZONE, 1, 88, SiegeSide::Attacker, now)
            .unwrap();
        broadcast_participant_changes(&f.server, &repeated, &changed);
        assert_eq!(
            f.client.drain(),
            vec![
                (
                    sc::SCSiegeMemberPacket,
                    vec![25, 0, 77, 0, 0, 0, 1, 0, 0, 0, 0]
                ),
                (
                    sc::SCSiegeMemberPacket,
                    vec![25, 0, 88, 0, 0, 0, 1, 0, 0, 0, 1]
                ),
            ]
        );
        assert!(f
            .server
            .systems
            .housing
            .dominions
            .unregister_participant(ZONE, 1)
            .unwrap());
        let removed = f.server.systems.housing.dominions.get(ZONE).unwrap();
        broadcast_participant_changes(&f.server, &changed, &removed);
        assert_eq!(
            f.client.drain(),
            vec![(
                sc::SCSiegeMemberPacket,
                vec![25, 0, 88, 0, 0, 0, 1, 0, 0, 0, 0]
            )]
        );
        broadcast_siege_declared(&f.server, &removed);
        let packets = f.client.drain();
        assert_eq!(packets.len(), 1);
        assert_eq!(packets[0].0, sc::SCSiegeDeclaredPacket);
        assert_eq!(&packets[0].1[..6], &[25, 0, 88, 0, 0, 0]);
        assert_eq!(&packets[0].1[29..33], &[100, 100, 100, 0]);
    }

    #[tokio::test]
    async fn tower_alerts_target_involved_guild_or_local_territory_in_same_instance() {
        let mut f = fixture();
        let state = owned(&f);
        f.leader.data().is_online = true;
        f.leader.data().unit.transform.zone_id = TEST_ZONE + 1; // Owning guild still receives it.
        f.member.data().is_online = true;
        f.member.data().unit.transform.instance_id = 99; // Owning guild in another instance does not.
        let mut clients = Vec::new();
        let mut players = Vec::new();
        for id in 3..=5 {
            let mut client = TestClient::new(&f.server, id, 1000 + id);
            let player = spawn_player(
                &f.server,
                &client,
                test_character(id, &format!("Observer{id}"), 120.0 + id as f32, 100.0),
                0x100 + id,
            );
            player.data().is_online = id != 5;
            client.conn.active_char = Some(player.clone());
            clients.push(client);
            players.push(player);
        }
        players[1].data().unit.transform.zone_id = TEST_ZONE + 1;
        f.client.drain();
        f.member_client.drain();
        for client in &mut clients {
            client.drain();
        }
        send_tower_alert(
            &f.server,
            &state,
            tower_alert::FIRST_ATTACKED,
            Some(&f.leader),
        );
        let expected = (
            sc::SCSiegeAlertPacket,
            vec![25, 0, 0, 1, 0, 0, 0, 4, 0, b'L', b'o', b'r', b'd'],
        );
        assert_eq!(f.client.drain(), vec![expected.clone()]);
        assert_eq!(clients[0].drain(), vec![expected]);
        assert!(f.member_client.drain().is_empty());
        assert!(clients[1].drain().is_empty());
        assert!(clients[2].drain().is_empty());
        send_tower_alert(&f.server, &state, 7, Some(&f.leader));
        assert!(f.client.drain().is_empty());
        send_tower_alert(&f.server, &state, tower_alert::ENGRAVABLE, None);
        assert_eq!(
            f.client.drain(),
            vec![(sc::SCSiegeAlertPacket, vec![25, 0, 3, 0, 0, 0, 0])]
        );
    }

    #[tokio::test]
    async fn scheduled_phase_notice_precedes_snapshot_without_a_reign_field() {
        let mut f = fixture();
        owned(&f);
        let now = Utc::now().timestamp();
        let state = f
            .server
            .systems
            .housing
            .dominions
            .schedule_siege(ZONE, 88, now + 300, now + 900, now)
            .unwrap();
        broadcast_phase_change(&f.server, &state);
        let packets = f.client.drain();
        assert_eq!(
            packets.iter().map(|p| p.0).collect::<Vec<_>>(),
            vec![sc::SCSiegeStatePacket, sc::SCDominionDataPacket]
        );
        assert_eq!(packets[0].1.len(), 127);
        assert_eq!(packets[0].1[60], 3);
        assert_eq!(&packets[0].1[123..127], &[0, 0, 0, 0]);
        assert_eq!(&packets[1].1[packets[1].1.len() - 2..], &[0, 1]);
    }

    #[tokio::test]
    async fn owner_delta_precedes_snapshot_that_restores_authoritative_national_tax() {
        let mut f = fixture();
        let state = owned(&f);
        broadcast_owner_change(&f.server, &state);
        let packets = f.client.drain();
        assert_eq!(
            packets.iter().map(|p| p.0).collect::<Vec<_>>(),
            vec![
                sc::SCSiegeStatePacket,
                sc::SCDominionOwnerChangedPacket,
                sc::SCDominionDataPacket
            ]
        );
        let mut expected = PacketStream::new();
        expected
            .write_u16(25)
            .write_u32(77)
            .write_u64(90)
            .write_bool(false);
        assert_eq!(packets[1].1, expected.into_bytes());
        let data = snapshot(&f.server, &state, 100).unwrap();
        assert_eq!(data.national_tax_rate, 0);
        let mut expected = PacketStream::new();
        expected.write(&data).write_bool(false).write_bool(true);
        assert_eq!(packets[2].1, expected.into_bytes());
    }
}
