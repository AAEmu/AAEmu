//! Periodic unit ticks: `Unit.OnActiveRegionTick` (`CombatTick` + `RegenTick`, the `Character`
//! overrides with `BreathTick`) for every unit in a region near a player, about once per second,
//! and `ManaRegenManager` (the Dash buff mana drain, every 200 ms).

use std::collections::HashSet;
use std::sync::Arc;
use std::time::Duration;

use crate::packets::world::SCUnitPointsPacket;
use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

use super::packets::{env_source, SCEnvDamagePacket, SCSetBreathPacket};
use super::types::{buff_constants, KillReason};
use super::{broadcast, buffs, combat};

/// `ManaRegenTemplate` (one registration per character id).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct ManaRegenEntry {
    pub obj_id: u32,
    pub tick: f64,
    pub tick_level_mana_cost: f64,
    pub level: u8,
}

/// `ManaRegenManager.Register(player, template)` (an existing entry is kept).
pub fn register_mana_regen(
    server: &GameServer,
    p: &PlayerRef,
    tick: f64,
    tick_level_mana_cost: f32,
) {
    let level = p.data().unit.level;
    server
        .systems
        .skills
        .mana_regen
        .lock()
        .expect("mana regen poisoned")
        .entry(p.character_id)
        .or_insert(ManaRegenEntry {
            obj_id: p.obj_id,
            tick,
            tick_level_mana_cost: f64::from(tick_level_mana_cost),
            level,
        });
}

/// `ManaRegenTemplate.CalculateManaCostPerTick` (formula 13 with `ab_level`).
fn mana_cost_per_tick(server: &GameServer, e: &ManaRegenEntry) -> f64 {
    let params = std::collections::HashMap::from([("ab_level", f64::from(e.level))]);
    let f = server
        .game_data
        .formulas
        .get_formula(13)
        .map_or(0.0, |f| f.evaluate(&params));
    f * e.tick_level_mana_cost
}

/// `ManaRegenManager.Tick`.
pub fn mana_regen_tick(server: &GameServer) {
    let entries: Vec<(u32, ManaRegenEntry)> = server
        .systems
        .skills
        .mana_regen
        .lock()
        .expect("mana regen poisoned")
        .iter()
        .map(|(k, v)| (*k, *v))
        .collect();
    for (char_id, e) in entries {
        let Some(p) = server.world.character_by_obj_id(e.obj_id) else {
            server
                .systems
                .skills
                .mana_regen
                .lock()
                .expect("mana regen poisoned")
                .remove(&char_id);
            continue;
        };
        let obj = WorldObject::from(&p);
        let per_tick = mana_cost_per_tick(server, &e);
        let (has_dash, mp) = {
            let c = p.data();
            (c.unit.buffs.check_buff(buff_constants::DASH), c.unit.mp)
        };
        let ok = if !has_dash {
            false
        } else if f64::from(mp) >= per_tick {
            combat::reduce_current_mp(server, &obj, per_tick as i32);
            true
        } else {
            false
        };
        if !ok {
            server
                .systems
                .skills
                .mana_regen
                .lock()
                .expect("mana regen poisoned")
                .remove(&char_id);
            buffs::remove_buff(server, &obj, buff_constants::DASH);
        }
    }
}

/// `Character.RegenTick`.
fn character_regen(server: &GameServer, p: &PlayerRef) {
    let obj = WorldObject::from(p);
    let s = super::stats::stats(
        server,
        &obj,
        &[
            super::stats::StatKind::MaxHp,
            super::stats::StatKind::MaxMp,
            super::stats::StatKind::HpRegen,
            super::stats::StatKind::MpRegen,
            super::stats::StatKind::PersistentHpRegen,
            super::stats::StatKind::PersistentMpRegen,
        ],
    );
    let (max_hp, max_mp, hp_regen, mp_regen, php, pmp) = (
        s[0] as i32,
        s[1] as i32,
        s[2] as i32,
        s[3] as i32,
        s[4] as i32,
        s[5] as i32,
    );
    let res = {
        let mut c = p.data();
        let drowning = c.breath == 0;
        let post_cast = c.skill_char.is_in_post_cast;
        let u = &mut c.unit;
        if u.is_dead() || !(u.hp < max_hp || u.mp < max_mp) || drowning {
            return;
        }
        let old_hp = u.hp;
        u.hp += if u.is_in_battle { php } else { hp_regen };
        u.mp += if post_cast { pmp } else { mp_regen };
        u.hp = u.hp.min(max_hp);
        u.mp = u.mp.min(max_mp);
        (old_hp, u.hp, u.mp)
    };
    broadcast(
        server,
        &obj,
        &SCUnitPointsPacket {
            obj_id: obj.obj_id(),
            hp: res.1,
            mp: res.2,
        },
        true,
    );
    combat::post_update_current_hp(server, &obj, Some(&obj), res.0, res.1, KillReason::Unknown);
}

/// `Character.BreathTick`.
fn breath_tick(server: &GameServer, p: &PlayerRef) {
    let obj = WorldObject::from(p);
    let (dead, under_water, breath, max_hp) = {
        let c = p.data();
        (
            c.unit.is_dead(),
            c.unit.is_under_water,
            c.breath,
            c.unit.max_hp,
        )
    };
    if dead || !under_water {
        return;
    }
    if breath == 0 {
        let damage = (f64::from(max_hp) * 0.1) as i32;
        combat::reduce_current_hp(server, &obj, Some(&obj), damage, KillReason::Damage);
        p.send(SCEnvDamagePacket {
            source: env_source::DROWNING,
            target: obj.obj_id(),
            amount: damage as u32,
            gimmick_id: 0,
        });
    } else {
        let b = {
            let mut c = p.data();
            c.breath = c.breath.saturating_sub(1000);
            c.breath
        };
        p.send(SCSetBreathPacket { breath: b });
    }
}

/// `OnActiveRegionTick` for every unit around the players of every world.
pub fn active_region_tick(server: &GameServer) {
    let mut seen = HashSet::new();
    for world in server.world.worlds() {
        for p in world.all_characters() {
            let me = WorldObject::from(&p);
            let mut units = vec![me.clone()];
            units.extend(
                world
                    .objects_around(&me)
                    .into_iter()
                    .filter(WorldObject::is_unit),
            );
            for u in units {
                if !seen.insert(u.obj_id()) {
                    continue;
                }
                combat::combat_tick(server, &u);
                if let Some(pp) = u.as_player() {
                    character_regen(server, pp);
                    breath_tick(server, pp);
                }
            }
        }
    }
}

/// Starts the periodic ticks (region tick 1 s, mana regen 200 ms).
pub fn start_tickers(server: &Arc<GameServer>) {
    let weak = Arc::downgrade(server);
    tokio::spawn(async move {
        let mut iv = tokio::time::interval(Duration::from_millis(1000));
        loop {
            iv.tick().await;
            let Some(s) = weak.upgrade() else { break };
            active_region_tick(&s);
        }
    });
    let weak = Arc::downgrade(server);
    tokio::spawn(async move {
        let mut iv = tokio::time::interval(Duration::from_millis(200));
        loop {
            iv.tick().await;
            let Some(s) = weak.upgrade() else { break };
            mana_regen_tick(&s);
        }
    });
}
