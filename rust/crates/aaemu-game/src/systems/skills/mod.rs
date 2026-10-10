//! Phase-3 subsystem: skills / buffs / effects / plots / combat.
//!
//! C# scope: `Core/Managers/SkillManager` (effects, plots, tags, modifiers, combat buffs),
//! `PlotManager`, `EffectTaskManager`, `ManaRegenManager`, `AnimationManager`,
//! `Models/Game/Skills/**`, the behaviour half of `Models/Game/Units/Buffs`
//! (`BuffTemplate.Start/Dispel`, tick / dispel tasks, tolerance, triggers, `TriggerRemoveOn`),
//! `Unit` combat (`ReduceCurrentHp`, `DoDie`, regen, aggro, `UseSkill`), cooldown / GCD handling,
//! character progression used by skills (exp, labor, skill learning) and the skill packets.
//!
//! Layout:
//! * [`data`] — every table (`server.game_data.systems.skills`);
//! * [`types`] — wire types (`SkillCaster`, `SkillCastTarget`, `SkillObject`, `CastAction`, ...);
//! * [`packets`] / [`handlers`] — S->C / C->S packets;
//! * [`skill`] — `Skill` (use, cast, channel, apply, end, stop) and auto attack;
//! * [`buffs`] — buff behaviour, triggers, tolerance; [`effects`] — effect templates;
//! * [`plots`] — plot trees; [`combat`] — hp / mp / death / aggro / states; [`regen`] — ticks;
//! * [`stats`] — attribute getters; [`relation`] / [`geometry`] — targeting helpers;
//! * [`ext`] — hooks into the npcs / economy / quests / social subsystems.

pub mod buffs;
pub mod combat;
pub mod data;
pub mod effects;
pub mod ext;
pub mod geometry;
pub mod handlers;
pub mod packets;
pub mod plots;
pub mod progress;
pub mod regen;
pub mod relation;
pub mod skill;
pub mod state;
pub mod stats;
pub mod tasks;
pub mod types;
pub mod unit_reqs;

#[cfg(test)]
mod tests;

use std::collections::{HashMap, HashSet};
use std::sync::{Arc, Mutex, OnceLock, RwLock, Weak};

use crate::models::buff::BuffEvent;
use crate::net::packet::{ServerPacket, LEVEL_PLAIN};
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::server::GameServer;
use crate::systems::{SaveStage, UnitHooks};
use crate::world::{PlayerRef, WorldObject};

pub use data::Data;

/// The `AppConfiguration.World` values the skill code reads (defaults = the shipped
/// `Configurations/World.json`).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SkillsConfig {
    pub god_mode: bool,
    pub pve_durability_loss_rate: f32,
    pub pvp_durability_loss_rate: f32,
    pub vocation_rate: f64,
    pub actability_rate: f64,
}

impl Default for SkillsConfig {
    fn default() -> Self {
        Self {
            god_mode: false,
            pve_durability_loss_rate: 1.0,
            pvp_durability_loss_rate: 1.0,
            vocation_rate: 1.0,
            actability_rate: 1.0,
        }
    }
}

/// Managers of this subsystem (`server.systems.skills`).
#[derive(Debug, Default)]
pub struct System {
    server: OnceLock<Weak<GameServer>>,
    /// Hooks into the other subsystems.
    pub ext: ext::ExtHooks,
    /// `SkillTlIdManager`.
    pub tl_ids: tasks::TlIdManager,
    /// Subscribed buff triggers (`BuffTriggersHandler`).
    pub triggers: Mutex<Vec<buffs::TriggerSub>>,
    /// Abuser obj id -> obj ids of the units that have it in their aggro table
    /// (`OnAbuserHealed` / `OnAbuserDied` subscriptions).
    pub aggro_watch: Mutex<HashMap<u32, HashSet<u32>>>,
    /// `ManaRegenManager.Registrations` (character id -> entry).
    pub mana_regen: Mutex<HashMap<u32, regen::ManaRegenEntry>>,
    /// `Helper.CheckInterval` state ((obj id, call site) -> next allowed time).
    pub intervals: Mutex<HashMap<(u32, u8), i64>>,
    config: RwLock<SkillsConfig>,
}

impl System {
    /// Startup after the phase-1/2 managers: hook installation, timers.
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        bind(server);
        regen::start_tickers(server);
        Ok(())
    }

    /// This subsystem's part of `SaveManager.DoSave` (buffs are saved with the characters).
    pub async fn save(
        &self,
        _server: &GameServer,
        _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        _stage: SaveStage,
    ) -> crate::Result<usize> {
        Ok(0)
    }

    /// The bound server (tasks / plots need an owned handle).
    pub fn server(&self) -> Option<Arc<GameServer>> {
        self.server.get().and_then(Weak::upgrade)
    }

    pub fn config(&self) -> SkillsConfig {
        *self.config.read().expect("config poisoned")
    }

    pub fn set_config(&self, c: SkillsConfig) {
        *self.config.write().expect("config poisoned") = c;
    }
}

/// The skills subsystem's `UnitHooks`.
struct SkillUnitHooks;

impl UnitHooks for SkillUnitHooks {
    fn on_buff_event(&self, server: &GameServer, owner: &WorldObject, event: &BuffEvent) {
        buffs::on_buff_event(server, owner, event);
    }

    fn on_unit_zone_change(
        &self,
        server: &GameServer,
        unit: &WorldObject,
        last_zone: u32,
        new_zone: u32,
    ) {
        combat::on_zone_change(server, unit, last_zone, new_zone);
    }
}

/// Binds the server handle (tasks) and installs the unit hooks. Idempotent.
pub fn bind(server: &Arc<GameServer>) {
    let _ = server.systems.skills.server.set(Arc::downgrade(server));
    server.systems.hooks.unit.install(Arc::new(SkillUnitHooks));
}

/// `GameObject.BroadcastPacket(packet, self)` for any object (players around, plus the object
/// itself when it is a player and `include_self`).
pub fn broadcast(
    server: &GameServer,
    obj: &WorldObject,
    packet: &dyn ServerPacket,
    include_self: bool,
) {
    match geometry::world_of(server, obj) {
        Some(w) => crate::world::broadcast(&w, obj, packet, include_self),
        None => {
            if include_self {
                if let Some(p) = obj.as_player() {
                    p.send_dyn(packet);
                }
            }
        }
    }
}

/// `SendPacketToPlayers(units, packet)`: the packet goes to every player among `units`.
pub fn send_to_players(_server: &GameServer, units: &[WorldObject], packet: &dyn ServerPacket) {
    for u in units {
        if let Some(p) = u.as_player() {
            p.send_dyn(packet);
        }
    }
}

/// The part of `Character.Load()` / world entry that needs the buff behaviour: the buffs added
/// while loading are started (bonuses, modifier caches, timers), the cached stats are
/// recomputed with them, and `RestoreSavedHpMp` clamps to the exact `MaxHp` / `MaxMp`.
pub fn on_character_loaded(server: &GameServer, player: &PlayerRef) {
    let obj = WorldObject::from(player);
    crate::systems::dispatch_buff_events(server, &obj);
    stats::update_gear_bonuses(server, player, None, None);
    stats::refresh_cached_stats(server, &obj);
    let mut c = player.data();
    c.unit.hp = c.saved_hp.min(c.unit.max_hp);
    c.unit.mp = c.saved_mp.min(c.unit.max_mp);
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    use handlers::*;
    reg.register::<CSStartSkillPacket>(LEVEL_PLAIN, cs::CSStartSkillPacket);
    reg.register::<CSStopCastingPacket>(LEVEL_PLAIN, cs::CSStopCastingPacket);
    reg.register::<CSRemoveBuffPacket>(LEVEL_PLAIN, cs::CSRemoveBuffPacket);
    reg.register::<CSLearnSkillPacket>(LEVEL_PLAIN, cs::CSLearnSkillPacket);
    reg.register::<CSLearnBuffPacket>(LEVEL_PLAIN, cs::CSLearnBuffPacket);
    reg.register::<CSResetSkillsPacket>(LEVEL_PLAIN, cs::CSResetSkillsPacket);
    reg.register::<CSSwapAbilityPacket>(LEVEL_PLAIN, cs::CSSwapAbilityPacket);
    reg.register::<CSSkillControllerStatePacket>(LEVEL_PLAIN, cs::CSSkillControllerStatePacket);
    reg.register::<CSCreateSkillControllerPacket>(LEVEL_PLAIN, cs::CSCreateSkillControllerPacket);
    reg.register::<CSSetForceAttackPacket>(LEVEL_PLAIN, cs::CSSetForceAttackPacket);
    reg.register::<CSChangeTargetPacket>(LEVEL_PLAIN, cs::CSChangeTargetPacket);
    reg.register::<CSBuyPriestBuffPacket>(LEVEL_PLAIN, cs::CSBuyPriestBuffPacket);
}
