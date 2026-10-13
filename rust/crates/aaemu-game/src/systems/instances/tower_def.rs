//! Tower defense event lifecycle for the 1.2 compact tables. The C# port supplied only
//! data and packets. Controllers use authored spawn positions; their summons retain normal
//! NPC skill/AI behavior. No client opcodes or rewards are synthesized here.

use std::collections::{BTreeMap, HashMap, HashSet};
use std::sync::{Arc, Mutex};

use anyhow::{anyhow, bail, Context};
use tokio::time::Instant;

use crate::net::packet::ServerPacket;
use crate::server::GameServer;
use crate::systems::npcs::doodad::spawner::DoodadSpawner;
use crate::systems::npcs::npc::spawner::{NpcSpawner, NpcSpawnerInit, NpcSpawnerShared};
use crate::systems::npcs::spawn_manager;
use crate::world::{DoodadRef, NpcRef, PlayerRef, WorldObject};

use super::config::TowerDefenseEventConfig;
use super::data::{TowerDef, TowerDefProg};
use super::packets::{
    SCTowerDefEndPacket, SCTowerDefListPacket, SCTowerDefStartPacket, SCTowerDefWaveStartPacket,
    TowerDefInfo, TowerDefKey,
};

/// Client identity plus world instance (two instances may run the same definition).
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct EventKey {
    pub world_id: u32,
    pub id: u32,
    pub zone_group_id: u16,
}

impl EventKey {
    fn wire(self) -> TowerDefKey {
        TowerDefKey {
            tower_def_id: self.id,
            zone_group_id: self.zone_group_id,
        }
    }

    pub fn at_player(server: &GameServer, player: &PlayerRef, id: u32) -> anyhow::Result<Self> {
        let t = player.data().unit.transform;
        let zone = server
            .game_data
            .zones
            .zone_by_key(t.zone_id)
            .context("Your current zone is unknown")?;
        let zone_group_id =
            u16::try_from(zone.group_id).context("Zone group exceeds the 1.2 wire limit")?;
        Ok(Self {
            world_id: t.instance_id,
            id,
            zone_group_id,
        })
    }
}

impl From<&TowerDefenseEventConfig> for EventKey {
    fn from(c: &TowerDefenseEventConfig) -> Self {
        Self {
            world_id: c.world_id,
            id: c.id,
            zone_group_id: c.zone_group_id,
        }
    }
}

#[derive(Debug)]
struct OwnedNpc {
    npc: NpcRef,
    step: u32,
    despawn_on_next: bool,
    is_target: bool,
}

#[derive(Debug)]
struct Event {
    def: TowerDef,
    zone_id: u32,
    started: Instant,
    wave_started: Instant,
    step: u32,
    plans: BTreeMap<u32, Vec<Arc<NpcSpawner>>>,
    doodad_plans: BTreeMap<u32, Vec<Arc<DoodadSpawner>>>,
    doodads: Vec<(DoodadRef, u32, bool)>,
    owned: HashMap<u32, OwnedNpc>,
    kills: HashMap<u32, u32>,
    end_kills: u32,
    // Keep the handle as well as the id: duplicate death callbacks must not count twice,
    // while a new NPC reusing the old id must count normally.
    deaths: HashMap<u32, NpcRef>,
}

#[derive(Debug, Default)]
struct State {
    active: BTreeMap<EventKey, Event>,
    ever_started: bool,
    previous_hour: Option<f32>,
    day: u64,
    scheduled_days: HashMap<EventKey, u64>,
    war_active: HashSet<EventKey>,
}

/// Mutations are serialized with the NPC subsystem's reentrant spawn lock. The state
/// mutex is released before every world/NPC operation, including spawn callbacks.
#[derive(Debug, Default)]
pub struct TowerDefenseManager {
    state: Mutex<State>,
}

fn manager(server: &GameServer) -> &TowerDefenseManager {
    &server.systems.instances.tower_defs
}

fn definition(server: &GameServer, id: u32) -> anyhow::Result<&TowerDef> {
    server
        .game_data
        .systems
        .instances
        .tower_defs
        .tower_defs
        .get(&id)
        .ok_or_else(|| anyhow!("Unknown tower defense {id}"))
}

fn prog<'a>(server: &'a GameServer, event: &Event) -> Option<&'a TowerDefProg> {
    event
        .step
        .checked_sub(1)
        .and_then(|i| event.def.progs.get(i as usize))
        .and_then(|id| server.game_data.systems.instances.tower_defs.progs.get(id))
}

fn valid_seconds(seconds: f32) -> bool {
    seconds.is_finite() && (0.0..=604_800.0).contains(&seconds)
}

fn prepare(server: &GameServer, key: EventKey, zone_id: u32) -> anyhow::Result<Event> {
    let def = definition(server, key.id)?.clone();
    let world = server
        .world
        .world(key.world_id)
        .context("World instance is unavailable")?;
    let zone = server
        .game_data
        .zones
        .zone_by_key(zone_id)
        .context("Unknown event zone")?;
    if zone.group_id != u32::from(key.zone_group_id) || !world.template.zone_keys.contains(&zone_id)
    {
        bail!("Event zone does not belong to this world and zone group");
    }
    if !valid_seconds(def.first_wave_after)
        || !valid_seconds(def.force_end_time)
        || def.force_end_time == 0.0
    {
        bail!("Tower defense {} has invalid event timers", key.id);
    }
    let data = &server.game_data.systems.instances.tower_defs;
    let mut spawn_ids = HashSet::from([def.target_npc_spawn_id]);
    let mut doodad_ids = HashSet::new();
    if def.target_npc_spawn_id == 0 {
        bail!("Tower defense {} has no target NPC spawner", key.id);
    }
    for id in &def.progs {
        let p = data
            .progs
            .get(id)
            .with_context(|| format!("Missing wave {id}"))?;
        if p.tower_def_id != key.id || !valid_seconds(p.cond_to_next_time) {
            bail!("Invalid tower defense wave {id}");
        }
        for target in &p.spawn_targets {
            match target.spawn_target_type.as_str() {
                "NpcSpawner" => {
                    spawn_ids.insert(target.spawn_target_id);
                }
                "DoodadAlmighty" => {
                    doodad_ids.insert(target.spawn_target_id);
                }
                _ => bail!(
                    "Wave {id}: unsupported spawn target {} ({})",
                    target.spawn_target_type,
                    target.spawn_target_id
                ),
            }
        }
        for target in &p.kill_targets {
            if target.kill_target_type != "Npc"
                || target.kill_count == 0
                || !server
                    .game_data
                    .systems
                    .npcs
                    .npc
                    .exist(target.kill_target_id)
            {
                bail!(
                    "Wave {id}: unsupported or missing kill target {} ({})",
                    target.kill_target_type,
                    target.kill_target_id
                );
            }
        }
    }
    if def.kill_npc_id != 0
        && def.kill_npc_count > 0
        && !server.game_data.systems.npcs.npc.exist(def.kill_npc_id)
    {
        bail!("Missing event end NPC {}", def.kill_npc_id);
    }
    let spawns = server
        .systems
        .npcs
        .spawns
        .get(key.world_id)
        .context("World spawns are not loaded")?;
    let mut plans = BTreeMap::new();
    let mut doodad_plans = BTreeMap::new();
    for id in doodad_ids {
        if !server.game_data.systems.npcs.doodad.exist(id) {
            bail!("Missing doodad template {id}");
        }
        let sources: Vec<_> = spawns
            .get_doodad_spawners_by_unit_id(id)
            .into_iter()
            .filter(|s| {
                server
                    .game_data
                    .zones
                    .zone_by_key(s.position.zone_id)
                    .is_some_and(|z| z.group_id == u32::from(key.zone_group_id))
            })
            .collect();
        if sources.is_empty() {
            bail!(
                "Missing doodad {id} positions in zone group {}",
                key.zone_group_id
            );
        }
        let mut copies = Vec::new();
        for source in sources {
            let p = source.position;
            if ![p.x, p.y, p.z, p.yaw, p.pitch, p.roll, source.scale()]
                .iter()
                .all(|v| v.is_finite())
            {
                bail!("Doodad {id}: invalid authored position");
            }
            copies.push(source.event_copy(key.world_id));
        }
        doodad_plans.insert(id, copies);
    }
    for id in spawn_ids {
        let sources: Vec<_> = spawns
            .tower_def_spawners(id)
            .into_iter()
            .filter(|s| {
                server
                    .game_data
                    .zones
                    .zone_by_key(s.position().zone_id)
                    .is_some_and(|z| z.group_id == u32::from(key.zone_group_id))
            })
            .collect();
        if sources.is_empty() {
            bail!(
                "Missing NPC spawner {id} positions in zone group {}",
                key.zone_group_id
            );
        }
        let mut copies = Vec::new();
        for s in sources {
            let position = s.position();
            if ![
                position.x,
                position.y,
                position.z,
                position.yaw,
                position.pitch,
                position.roll,
            ]
            .iter()
            .all(|v| v.is_finite())
            {
                bail!("Spawner {id}: invalid controller position");
            }
            if !server.game_data.systems.npcs.npc.exist(s.unit_id) {
                bail!("Spawner {id}: missing NPC {}", s.unit_id);
            }
            // A fresh spawner for each event avoids shared populations, stale corpses and
            // respawn state carrying into the next run.
            copies.push(Arc::new(NpcSpawner::new(
                NpcSpawnerInit {
                    id,
                    spawner_id: id,
                    unit_id: s.unit_id,
                    follow_path: s.follow_path.clone(),
                    follow_npc: s.follow_npc,
                    respawn_time: 0,
                    despawn_time: s.despawn_time,
                    special_link: s.special_link.clone(),
                    count: s.count,
                    npc_spawner_ids: vec![id],
                    parent_world: Some(key.world_id),
                    template: s.template(),
                    spawnable_npcs: Vec::new(),
                },
                NpcSpawnerShared::new(position),
            )));
        }
        plans.insert(id, copies);
    }
    let now = spawn_manager::now();
    Ok(Event {
        def,
        zone_id,
        started: now,
        wave_started: now,
        step: 0,
        plans,
        doodad_plans,
        doodads: Vec::new(),
        owned: HashMap::new(),
        kills: HashMap::new(),
        end_kills: 0,
        deaths: HashMap::new(),
    })
}

/// Preflight all waves before sending a start packet or creating an NPC.
pub fn validate(server: &GameServer, key: EventKey, zone_id: u32) -> anyhow::Result<()> {
    prepare(server, key, zone_id).map(|_| ())
}

pub fn initialize(server: &GameServer) -> anyhow::Result<()> {
    let config = &server.config.dungeons.tower_defense;
    if config.enabled {
        let mut seen = HashSet::new();
        for c in &config.events {
            let key = EventKey::from(c);
            if !seen.insert(key) {
                bail!("Duplicate tower defense schedule {}", c.id);
            }
            validate(server, key, c.zone_id)
                .with_context(|| format!("Tower defense schedule {}", c.id))?;
            let def = definition(server, c.id)?;
            if !def.time_of_day.is_finite()
                || !(0.0..24.0).contains(&def.time_of_day)
                || def.time_of_day_day_interval == 0
            {
                bail!("Tower defense {} has an invalid game-time schedule", c.id);
            }
        }
    }
    manager(server)
        .state
        .lock()
        .expect("tower defense poisoned")
        .previous_hour = Some(server.time.get_time());
    Ok(())
}

pub fn start(server: &GameServer, key: EventKey, zone_id: u32) -> anyhow::Result<()> {
    let _serial = server.systems.npcs.spawns.spawn_lock();
    let event = prepare(server, key, zone_id)?;
    let first_wave_after = event.def.first_wave_after;
    let target = event.def.target_npc_spawn_id;
    {
        let mut state = manager(server)
            .state
            .lock()
            .expect("tower defense poisoned");
        if state.active.contains_key(&key) {
            bail!("Tower defense {} is already running", key.id);
        }
        if state.active.iter().any(|(k, e)| {
            k.world_id == key.world_id
                && k.zone_group_id == key.zone_group_id
                && e.plans.keys().any(|id| event.plans.contains_key(id))
        }) {
            bail!("Another active event uses these NPC spawners");
        }
        state.ever_started = true;
        state.active.insert(key, event);
    }
    if let Err(error) = spawn_controllers(server, key, target) {
        end(server, key)?;
        return Err(error);
    }
    broadcast(
        server,
        key.world_id,
        SCTowerDefStartPacket {
            key: key.wire(),
            event_zone_id: zone_id,
        },
    );
    send_world_list(server, key.world_id);
    if first_wave_after == 0.0 && !definition(server, key.id)?.progs.is_empty() {
        next_wave(server, key, 1)?;
    }
    tracing::info!(
        id = key.id,
        world = key.world_id,
        group = key.zone_group_id,
        "Tower defense started"
    );
    Ok(())
}

fn spawn_controllers(server: &GameServer, key: EventKey, id: u32) -> anyhow::Result<()> {
    let spawners = manager(server)
        .state
        .lock()
        .expect("tower defense poisoned")
        .active
        .get(&key)
        .and_then(|e| e.plans.get(&id))
        .cloned()
        .context("Event spawner is unavailable")?;
    for s in spawners {
        s.do_event_spawn(server);
        if s.spawned_npcs().values().all(Vec::is_empty) {
            bail!("NPC spawner {id} failed to spawn");
        }
    }
    Ok(())
}

/// Advance exactly one wave. GM requests cannot skip waves or replay an active wave.
pub fn next_wave(server: &GameServer, key: EventKey, step: u32) -> anyhow::Result<()> {
    let _serial = server.systems.npcs.spawns.spawn_lock();
    let (zone_id, targets, removed, removed_doodads) = {
        let mut state = manager(server)
            .state
            .lock()
            .expect("tower defense poisoned");
        let e = state
            .active
            .get_mut(&key)
            .context("Tower defense is not running")?;
        if step != e.step + 1 || step as usize > e.def.progs.len() {
            bail!(
                "Next wave must be {} ({} waves total)",
                e.step + 1,
                e.def.progs.len()
            );
        }
        let mut removed = Vec::new();
        e.owned.retain(|_, npc| {
            if npc.despawn_on_next && npc.step == e.step {
                removed.push(npc.npc.clone());
                false
            } else {
                true
            }
        });
        let mut removed_doodads = Vec::new();
        e.doodads.retain(|(doodad, spawned_step, remove)| {
            if *remove && *spawned_step == e.step {
                removed_doodads.push(doodad.clone());
                false
            } else {
                true
            }
        });
        e.step = step;
        e.wave_started = spawn_manager::now();
        e.kills.clear();
        let targets = prog(server, e)
            .context("Missing wave data")?
            .spawn_targets
            .clone();
        (e.zone_id, targets, removed, removed_doodads)
    };
    cleanup_npcs(server, key.world_id, removed, true);
    cleanup_doodads(server, key.world_id, removed_doodads);
    for target in targets {
        let result = if target.spawn_target_type == "DoodadAlmighty" {
            spawn_doodads(
                server,
                key,
                target.spawn_target_id,
                step,
                target.despawn_on_next_step,
            )
        } else {
            spawn_controllers(server, key, target.spawn_target_id)
        };
        if let Err(error) = result {
            end(server, key)?;
            return Err(error);
        }
    }
    broadcast(
        server,
        key.world_id,
        SCTowerDefWaveStartPacket {
            key: key.wire(),
            event_zone_id: zone_id,
            step,
        },
    );
    send_world_list(server, key.world_id);
    tracing::info!(
        id = key.id,
        world = key.world_id,
        group = key.zone_group_id,
        step,
        "Tower defense wave started"
    );
    Ok(())
}

pub fn end(server: &GameServer, key: EventKey) -> anyhow::Result<()> {
    end_event(server, key, true)
}

fn end_event(server: &GameServer, key: EventKey, keep_corpses: bool) -> anyhow::Result<()> {
    let _serial = server.systems.npcs.spawns.spawn_lock();
    let event = manager(server)
        .state
        .lock()
        .expect("tower defense poisoned")
        .active
        .remove(&key)
        .context("Tower defense is not running")?;
    cleanup_doodads(
        server,
        key.world_id,
        event.doodads.into_iter().map(|(d, _, _)| d).collect(),
    );
    cleanup_npcs(
        server,
        key.world_id,
        event.owned.into_values().map(|n| n.npc).collect(),
        keep_corpses,
    );
    broadcast(
        server,
        key.world_id,
        SCTowerDefEndPacket {
            key: key.wire(),
            event_zone_id: event.zone_id,
        },
    );
    send_world_list(server, key.world_id);
    tracing::info!(id = key.id, world = key.world_id, "Tower defense ended");
    Ok(())
}

fn cleanup_npcs(server: &GameServer, world_id: u32, npcs: Vec<NpcRef>, keep_corpses: bool) {
    for npc in npcs {
        let manager = server.systems.npcs.spawns.get(world_id);
        // Corpse timers may have deleted this NPC already and recycled its id.
        if !server
            .world
            .world(world_id)
            .and_then(|w| w.npc(npc.obj_id))
            .is_some_and(|n| Arc::ptr_eq(&n, &npc))
        {
            if let Some(m) = manager {
                m.cancel_npc_timers(&npc);
            }
            continue;
        }
        let spawner = server.systems.npcs.spawns.npc_spawner_of(npc.obj_id);
        if let Some(s) = &spawner {
            s.set_respawn_time(0);
        }
        // DoDie publishes the kill before generating loot and scheduling corpse removal.
        // Keep every dead NPC on its normal timer, even if loot is not generated yet.
        let is_corpse = {
            let n = npc.data();
            n.dead_time_ms != 0 && n.unit.is_dead()
        };
        if keep_corpses && is_corpse {
            if let Some(m) = manager {
                m.cancel_npc_respawn(&npc);
            }
            continue;
        }
        if let Some(m) = manager {
            m.cancel_npc_timers(&npc);
        }
        if let Some(s) = spawner {
            s.despawn(server, &npc);
        } else {
            spawn_manager::delete_object(server, &WorldObject::from(&npc), Some(world_id));
        }
        server.ids.object.release_id(npc.obj_id);
        spawn_manager::forget_object(server, npc.obj_id);
    }
}

fn spawn_doodads(
    server: &GameServer,
    key: EventKey,
    id: u32,
    step: u32,
    remove: bool,
) -> anyhow::Result<()> {
    let spawners = manager(server)
        .state
        .lock()
        .expect("tower defense poisoned")
        .active
        .get(&key)
        .and_then(|e| e.doodad_plans.get(&id))
        .cloned()
        .context("Event doodad spawner is unavailable")?;
    for spawner in spawners {
        let doodad = spawner
            .spawn(server, 0)
            .with_context(|| format!("Doodad {id} failed to spawn"))?;
        if let Some(event) = manager(server)
            .state
            .lock()
            .expect("tower defense poisoned")
            .active
            .get_mut(&key)
        {
            event.doodads.push((doodad, step, remove));
        }
    }
    Ok(())
}

fn cleanup_doodads(server: &GameServer, world_id: u32, doodads: Vec<DoodadRef>) {
    let Some(world) = server.world.world(world_id) else {
        return;
    };
    for doodad in doodads {
        if !world
            .doodad(doodad.obj_id)
            .is_some_and(|current| Arc::ptr_eq(&current, &doodad))
        {
            continue;
        }
        if let Some(spawner) = server.systems.npcs.spawns.doodad_spawner_of(doodad.obj_id) {
            spawner.despawn(server, &doodad);
        } else {
            crate::systems::npcs::doodad::manager::delete(server, &world, &doodad);
            server.ids.object.release_id(doodad.obj_id);
            spawn_manager::forget_object(server, doodad.obj_id);
        }
    }
}

/// Own direct controllers and descendants summoned by them, without touching unrelated NPCs.
pub fn on_spawn(server: &GameServer, npc: &NpcRef) {
    let _serial = server.systems.npcs.spawns.spawn_lock();
    let (world_id, owner_id) = {
        let n = npc.data();
        (n.unit.transform.instance_id, n.unit.owner_id)
    };
    let spawner = server.systems.npcs.spawns.npc_spawner_of(npc.obj_id);
    let owner = server.world.world(world_id).and_then(|w| w.npc(owner_id));
    let mut owned = false;
    {
        let mut state = manager(server)
            .state
            .lock()
            .expect("tower defense poisoned");
        for (key, e) in &mut state.active {
            if key.world_id != world_id {
                continue;
            }
            let source = spawner.as_ref().and_then(|s| {
                e.plans
                    .get(&s.id)
                    .filter(|plans| plans.iter().any(|p| Arc::ptr_eq(p, s)))
                    .map(|_| s.id)
            });
            let ownership = if let Some(id) = source {
                Some((
                    e.step,
                    prog(server, e).is_some_and(|p| {
                        p.spawn_targets
                            .iter()
                            .any(|t| t.spawn_target_id == id && t.despawn_on_next_step)
                    }),
                ))
            } else {
                e.owned
                    .get(&owner_id)
                    .filter(|parent| owner.as_ref().is_some_and(|n| Arc::ptr_eq(&parent.npc, n)))
                    .map(|parent| (parent.step, parent.despawn_on_next))
            };
            if let Some((step, despawn_on_next)) = ownership {
                e.owned.insert(
                    npc.obj_id,
                    OwnedNpc {
                        npc: npc.clone(),
                        step,
                        despawn_on_next,
                        is_target: source == Some(e.def.target_npc_spawn_id),
                    },
                );
                owned = true;
                break;
            }
        }
    }
    if owned {
        if let Some(s) = spawner {
            s.set_respawn_time(0);
        }
    }
}

/// SpawnEffect/NpcSpawnerSpawnEffect leave Unit.OwnerId at zero in the 1.2 port. Carry
/// event ownership explicitly so their NPCs participate in wave/end cleanup as well.
pub fn on_summoned(server: &GameServer, summoner: &WorldObject, npcs: &[NpcRef]) {
    let Some(parent) = summoner.as_npc() else {
        return;
    };
    let _serial = server.systems.npcs.spawns.spawn_lock();
    let mut adopted = Vec::new();
    {
        let mut state = manager(server)
            .state
            .lock()
            .expect("tower defense poisoned");
        let key = state.active.iter().find_map(|(key, event)| {
            event
                .owned
                .get(&parent.obj_id)
                .filter(|n| Arc::ptr_eq(&n.npc, parent))
                .map(|_| *key)
        });
        let Some(key) = key else {
            return;
        };
        let candidates: Vec<_> = npcs
            .iter()
            .filter(|npc| {
                !state.active.values().any(|e| {
                    e.owned
                        .get(&npc.obj_id)
                        .is_some_and(|n| Arc::ptr_eq(&n.npc, npc))
                })
            })
            .cloned()
            .collect();
        if let Some(event) = state.active.get_mut(&key) {
            let owner = &event.owned[&parent.obj_id];
            let (step, despawn_on_next) = (owner.step, owner.despawn_on_next);
            for npc in candidates {
                event.owned.insert(
                    npc.obj_id,
                    OwnedNpc {
                        npc: npc.clone(),
                        step,
                        despawn_on_next,
                        is_target: false,
                    },
                );
                adopted.push(npc);
            }
        }
    }
    for npc in adopted {
        if let Some(s) = server.systems.npcs.spawns.npc_spawner_of(npc.obj_id) {
            s.set_respawn_time(0);
        }
    }
}

/// Called only from DoDie, never from the duplicated dungeon ReduceHp callback.
pub fn on_death(server: &GameServer, victim: &WorldObject) {
    let Some(npc) = victim.as_npc() else {
        return;
    };
    let _serial = server.systems.npcs.spawns.spawn_lock();
    let (world_id, zone_id, template) = {
        let n = npc.data();
        (
            n.unit.transform.instance_id,
            n.unit.transform.zone_id,
            n.unit.template_id,
        )
    };
    let group = server
        .game_data
        .zones
        .zone_by_key(zone_id)
        .map(|z| z.group_id);
    let mut state = manager(server)
        .state
        .lock()
        .expect("tower defense poisoned");
    for (key, e) in &mut state.active {
        if key.world_id != world_id || group != Some(u32::from(key.zone_group_id)) {
            continue;
        }
        if e.deaths
            .get(&npc.obj_id)
            .is_some_and(|n| Arc::ptr_eq(n, npc))
        {
            continue;
        }
        e.deaths.insert(npc.obj_id, npc.clone());
        if prog(server, e)
            .is_some_and(|p| p.kill_targets.iter().any(|t| t.kill_target_id == template))
        {
            let kills = e.kills.entry(template).or_default();
            *kills = kills.saturating_add(1);
        }
        if e.def.kill_npc_count > 0
            && (e.def.kill_npc_id == template
                || (e.def.kill_npc_id == 0
                    && e.owned
                        .get(&npc.obj_id)
                        .is_some_and(|n| n.is_target && Arc::ptr_eq(&n.npc, npc))))
        {
            e.end_kills = e.end_kills.saturating_add(1);
        }
    }
}

fn wave_complete(p: &TowerDefProg, kills: &HashMap<u32, u32>, elapsed: f32) -> bool {
    let mut conditions = (p.cond_to_next_time > 0.0)
        .then_some(elapsed >= p.cond_to_next_time)
        .into_iter()
        .chain(
            p.kill_targets
                .iter()
                .map(|t| kills.get(&t.kill_target_id).copied().unwrap_or(0) >= t.kill_count),
        );
    if p.cond_comp_by_and {
        conditions.all(|v| v)
    } else {
        conditions.any(|v| v)
    }
}

pub fn tick(server: &GameServer) {
    let _serial = server.systems.npcs.spawns.spawn_lock();
    schedule_tick(server);
    let now = spawn_manager::now();
    let actions: Vec<_> = {
        let state = manager(server)
            .state
            .lock()
            .expect("tower defense poisoned");
        state
            .active
            .iter()
            .filter_map(|(key, e)| {
                if !server.world.has_world(key.world_id)
                    || now.duration_since(e.started).as_secs_f32() >= e.def.force_end_time
                    || (e.def.kill_npc_count > 0 && e.end_kills >= e.def.kill_npc_count)
                {
                    return Some((*key, None));
                }
                if e.step == 0 {
                    return (now.duration_since(e.started).as_secs_f32() >= e.def.first_wave_after
                        && !e.def.progs.is_empty())
                    .then_some((*key, Some(1)));
                }
                prog(server, e)
                    .filter(|p| {
                        wave_complete(
                            p,
                            &e.kills,
                            now.duration_since(e.wave_started).as_secs_f32(),
                        )
                    })
                    .map(|_| {
                        (
                            *key,
                            ((e.step as usize) < e.def.progs.len()).then_some(e.step + 1),
                        )
                    })
            })
            .collect()
    };
    for (key, step) in actions {
        let result = match step {
            Some(step) => next_wave(server, key, step),
            None => end(server, key),
        };
        if let Err(error) = result {
            tracing::error!(id = key.id, %error, "Tower defense tick failed");
        }
    }
}

fn schedule_tick(server: &GameServer) {
    let config = &server.config.dungeons.tower_defense;
    if !config.enabled {
        return;
    }
    let hour = server.time.get_time();
    if !hour.is_finite() || !(0.0..=24.0).contains(&hour) {
        return;
    }
    let mut starts = Vec::new();
    let mut stops = Vec::new();
    {
        let mut state = manager(server)
            .state
            .lock()
            .expect("tower defense poisoned");
        let previous = state.previous_hour.replace(hour).unwrap_or(hour);
        let wrapped = hour < previous;
        if wrapped {
            state.day += 1;
        }
        for c in &config.events {
            let key = EventKey::from(c);
            let Some(def) = server
                .game_data
                .systems
                .instances
                .tower_defs
                .tower_defs
                .get(&c.id)
            else {
                continue;
            };
            let war = server
                .game_data
                .zones
                .conflicts
                .iter()
                .any(|z| z.zone_group_id == c.zone_group_id && z.war_tower_def_id == c.id);
            if war {
                let in_war = server
                    .systems
                    .social
                    .conflicts
                    .get(u32::from(c.zone_group_id))
                    .is_some_and(|s| {
                        s.current_state == crate::data::world_data::conflict_type::WAR
                    });
                if in_war && state.war_active.insert(key) {
                    starts.push((key, c.zone_id));
                }
                if !in_war && state.war_active.remove(&key) && state.active.contains_key(&key) {
                    stops.push(key);
                }
                continue;
            }
            let occurrence = if wrapped && def.time_of_day > previous {
                Some(state.day - 1)
            } else if (wrapped || previous < def.time_of_day) && def.time_of_day <= hour {
                Some(state.day)
            } else {
                None
            };
            if let Some(day) = occurrence {
                if def.time_of_day_day_interval > 0
                    && day.is_multiple_of(u64::from(def.time_of_day_day_interval))
                    && state.scheduled_days.get(&key) != Some(&day)
                {
                    state.scheduled_days.insert(key, day);
                    if !state.active.contains_key(&key) {
                        starts.push((key, c.zone_id));
                    }
                }
            }
        }
    }
    for key in stops {
        if let Err(error) = end(server, key) {
            tracing::error!(%error, "Stopping war event failed");
        }
    }
    for (key, zone) in starts {
        if let Err(error) = start(server, key, zone) {
            tracing::error!(%error, "Starting scheduled tower defense failed");
        }
    }
}

pub fn infos(server: &GameServer, world_id: u32) -> Vec<TowerDefInfo> {
    let snapshots: Vec<_> = manager(server)
        .state
        .lock()
        .expect("tower defense poisoned")
        .active
        .iter()
        .filter(|(k, _)| k.world_id == world_id)
        .map(|(k, e)| {
            let target = e
                .owned
                .values()
                .filter(|n| n.is_target)
                .min_by_key(|n| n.npc.obj_id)
                .map(|n| n.npc.clone());
            (
                TowerDefInfo {
                    key: k.wire(),
                    zone_id: e.zone_id,
                    spot_id: e.def.target_npc_spawn_id,
                    current_step: e.step,
                    ..Default::default()
                },
                target,
            )
        })
        .collect();
    snapshots
        .into_iter()
        .map(|(mut info, target)| {
            if let Some(npc) = target {
                let t = npc.data().unit.transform;
                info.position = (t.x, t.y, t.z);
                if server
                    .world
                    .world(world_id)
                    .and_then(|w| w.npc(npc.obj_id))
                    .is_some_and(|n| Arc::ptr_eq(&n, &npc))
                {
                    info.target_obj_id = npc.obj_id;
                }
            }
            info
        })
        .collect()
}

pub fn send_list(server: &GameServer, player: &PlayerRef) {
    let world_id = player.data().unit.transform.instance_id;
    player.send(SCTowerDefListPacket {
        infos: infos(server, world_id),
    });
}

/// Preserve ordinary zone-entry flows before any event has run. After the first event,
/// also send empty lists so switching worlds clears an event's old client map marker.
pub fn send_zone_list(server: &GameServer, player: &PlayerRef) {
    let started = manager(server)
        .state
        .lock()
        .expect("tower defense poisoned")
        .ever_started;
    if started {
        send_list(server, player);
    }
}

fn send_world_list(server: &GameServer, world_id: u32) {
    broadcast(
        server,
        world_id,
        SCTowerDefListPacket {
            infos: infos(server, world_id),
        },
    );
}

fn broadcast(server: &GameServer, world_id: u32, packet: impl ServerPacket + Clone + 'static) {
    for player in server.world.all_characters() {
        let t = player.data().unit.transform;
        if t.instance_id == world_id {
            player.send(packet.clone());
        }
    }
}

/// End events before an instance is removed, while its NPCs are still reachable.
pub fn cleanup_world(server: &GameServer, world_id: u32) {
    let _serial = server.systems.npcs.spawns.spawn_lock();
    let keys: Vec<_> = manager(server)
        .state
        .lock()
        .expect("tower defense poisoned")
        .active
        .keys()
        .filter(|k| k.world_id == world_id)
        .copied()
        .collect();
    for key in keys {
        let _ = end_event(server, key, false);
    }
}

#[cfg(test)]
mod tests;
