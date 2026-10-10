//! `Models/Game/Indun/Dungeon.cs` and `DungeonLoaderTask.cs`: one dungeon (or system instance
//! such as the Mirage Island / the Library) with its world instance, access list, enter queue,
//! leave requests, room states and ticks.
//!
//! `WorldInstance.DungeonInstance` is `server.systems.instances.dungeons[world id]`. The C#
//! character event subscriptions (`OnTeamJoin`, `OnTeamLeave`, `OnTeamKick`, `OnDungeonLeave`,
//! `OnDisconnect`) live in [`super::events::CharacterEvents`].

use std::collections::{HashMap, HashSet};
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::Duration;

use crate::server::GameServer;
use crate::systems::npcs::portal::packets::SCLoadInstancePacket;
use crate::systems::social::team::Team;
use crate::world::{PlayerRef, WorldInstance};

use super::data::{IndunEventKind, IndunZone};
use super::events::{CharEvent, Handler};
use super::packets::{SCProcessingInstancePacket, ZoneInstanceId};
use super::worlds::{self, deg_to_rad, error_message as err, send_error};

/// `Dungeon`.
#[derive(Debug)]
pub struct Dungeon {
    /// Identity of this object (C# reference identity; event subscriptions use it).
    pub uid: u64,
    /// `_indunZone`.
    pub indun_zone: IndunZone,
    /// `_zoneInstanceId` (first zone key of the zone group, instance id at creation).
    pub zone_instance_id: ZoneInstanceId,
    /// `IsSystem`.
    pub is_system: bool,
    /// `_createTime` (unix ms).
    pub create_time_ms: i64,
    st: Mutex<DungeonState>,
}

/// The mutable members of a [`Dungeon`].
#[derive(Debug, Default)]
pub struct DungeonState {
    /// `World` (`None` once destroyed).
    pub world_id: Option<u32>,
    /// `PlayersWithAccess`.
    pub players_with_access: HashSet<u32>,
    /// `_leaveRequests` (character id -> kick time, unix ms; insertion order).
    pub leave_requests: Vec<(u32, i64)>,
    /// `_characterOwner`.
    pub character_owner: Option<PlayerRef>,
    /// `_ownerTeam` (refreshed from the team manager while the team exists).
    pub owner_team: Option<Team>,
    /// `_isTeamOwned`.
    pub is_team_owned: bool,
    /// `EnterRequests` (players waiting for the dungeon to finish loading).
    pub enter_requests: Vec<PlayerRef>,
    /// `_rooms` (room id -> cleared).
    pub rooms: HashMap<u32, bool>,
    /// `FinishedLoading`.
    pub finished_loading: bool,
    /// `TickManager.OnTick` subscriptions (`AreaClearTick` 1 s, `LeaveDungeonTick` 5 s).
    pub area_clear_tick: bool,
    pub leave_tick: bool,
    /// The `DungeonLoaderTask` (tests await it).
    pub loader: Option<tokio::task::JoinHandle<()>>,
}

fn now_ms() -> i64 {
    chrono::Utc::now().timestamp_millis()
}

impl Dungeon {
    pub fn st(&self) -> MutexGuard<'_, DungeonState> {
        self.st.lock().expect("dungeon poisoned")
    }

    /// `World?.Id`.
    pub fn world_id(&self) -> Option<u32> {
        self.st().world_id
    }

    /// `World` (the live instance).
    pub fn world(&self, server: &GameServer) -> Option<Arc<WorldInstance>> {
        let id = self.world_id()?;
        server.world.worlds().into_iter().find(|w| w.id == id)
    }

    /// `GetZoneGroupId`.
    pub fn zone_group_id(&self) -> u32 {
        self.indun_zone.zone_group_id
    }

    /// `IsTeamOwned`.
    pub fn is_team_owned(&self) -> bool {
        self.st().is_team_owned
    }

    /// `PlayersWithAccess.Contains(id)`.
    pub fn has_access(&self, character_id: u32) -> bool {
        self.st().players_with_access.contains(&character_id)
    }

    /// `EnterRequests.Contains(character)`.
    pub fn is_queued(&self, p: &PlayerRef) -> bool {
        self.st().enter_requests.iter().any(|x| Arc::ptr_eq(x, p))
    }

    /// `IsFull` (`World?.GetCharacterCount() ?? 0) >= MaxPlayers`).
    pub fn is_full(&self, server: &GameServer) -> bool {
        let count = self
            .world(server)
            .map_or(0, |w| worlds::character_count(&w));
        count as u64 >= u64::from(self.indun_zone.max_players)
    }

    /// `IsExpired` (non-system dungeons expire one day after their creation).
    pub fn is_expired(&self) -> bool {
        !self.is_system && self.create_time_ms + 24 * 3600 * 1000 < now_ms()
    }

    /// `IsRoomCleared(roomId)`.
    pub fn is_room_cleared(&self, room_id: u32) -> bool {
        self.st().rooms.get(&room_id).copied().unwrap_or(false)
    }

    /// `SetRoomCleared(roomId)`.
    pub fn set_room_cleared(&self, room_id: u32) {
        self.st().rooms.insert(room_id, true);
    }

    /// `GetDungeonTemplateId()`.
    pub fn dungeon_template_id(&self, server: &GameServer) -> Option<u32> {
        self.world(server).map(|w| w.template.id)
    }
}

/// `new Dungeon(indunZone, character, channelId, team, overrideInstanceId, fixedInstanceId)`.
/// `None` when the zone group has no zone keys or no world (the C# logs and leaves a broken
/// object behind).
#[allow(clippy::too_many_arguments)]
pub fn create(
    server: &GameServer,
    indun_zone: &IndunZone,
    character: Option<&PlayerRef>,
    channel_id: u32,
    team: Option<Team>,
    override_instance_id: bool,
    fixed_instance_id: u32,
    is_system: bool,
) -> Option<Arc<Dungeon>> {
    let zone_keys = server
        .game_data
        .systems
        .instances
        .zone_order
        .zone_keys_in_group(indun_zone.zone_group_id);
    match zone_keys.len() {
        0 => {
            tracing::error!("No Zone Keys found for this zone group id.");
            return None;
        }
        1 => {}
        _ => tracing::info!("There are more than one zone keys for this dungeon?!"),
    }
    let Some(template) = server
        .game_data
        .worlds
        .get_world_template_by_zone_key(zone_keys[0])
    else {
        tracing::error!(
            "GetWorldByZone(): No world template defined for ZoneId {}",
            zone_keys[0]
        );
        return None;
    };
    tracing::info!(
        "[Dungeon] Create system dungeon {} - channel {channel_id}...",
        template.name
    );
    let world = match worlds::create_world_instance(
        server,
        template,
        channel_id,
        override_instance_id,
        fixed_instance_id,
        character,
    ) {
        Ok(w) => w,
        Err(e) => {
            tracing::error!("[Dungeon] CreateWorldInstance failed: {e}");
            return None;
        }
    };
    let sys = &server.systems.instances;
    let dungeon = Arc::new(Dungeon {
        uid: sys.next_uid(),
        indun_zone: indun_zone.clone(),
        zone_instance_id: ZoneInstanceId {
            zone_id: zone_keys[0],
            instance_id: world.id,
        },
        is_system,
        create_time_ms: now_ms(),
        st: Mutex::new(DungeonState {
            world_id: Some(world.id),
            is_team_owned: team.is_some(),
            owner_team: team.clone(),
            character_owner: character.cloned(),
            ..DungeonState::default()
        }),
    });
    // World.DungeonInstance = this
    sys.dungeons.insert(world.id, dungeon.clone());

    // If started by a character, enter them into queue
    if let Some(c) = character {
        dungeon.st().players_with_access.insert(c.character_id);
        queue_player(server, &dungeon, c);
    }
    // Add team members to allow access
    if let Some(team) = &team {
        let mut st = dungeon.st();
        for m in team.members.iter().flatten() {
            st.players_with_access.insert(m.character.character_id);
        }
    }
    dungeon.st().area_clear_tick = true;
    register_indun_events(server, &dungeon);

    // Create a loading task to run the loading async
    if let Some(server) = server.self_arc() {
        let d = dungeon.clone();
        let notify = character.cloned();
        let handle = tokio::spawn(async move {
            tokio::time::sleep(Duration::from_millis(100)).await;
            run_loader(&server, &d, notify.as_ref(), Duration::from_millis(1000)).await;
        });
        dungeon.st().loader = Some(handle);
    } else {
        tracing::error!("[Dungeon] no server handle: the dungeon loader cannot be scheduled");
    }
    dungeon.st().leave_tick = true;
    Some(dungeon)
}

/// `DungeonLoaderTask.Execute`: `SpawnManager.Load` + `SpawnAll` (waited for), the second
/// `RegisterIndunEvents`, `FinishedLoading`, then the queued players enter.
///
/// Deviation: the C# does not await `Task.WhenAll(SpawnTasks)` (the comment says it waits), so
/// whether the doodads exist when the events are registered again is a race; here the spawn
/// tasks are awaited. `SpawnManager.Load` runs here instead of in `CreateWorldInstance`.
pub async fn run_loader(
    server: &Arc<GameServer>,
    dungeon: &Arc<Dungeon>,
    _notify: Option<&PlayerRef>,
    settle: Duration,
) {
    let Some(world) = dungeon.world(server) else {
        return;
    };
    let name = worlds::world_name(&world);
    worlds::load_spawns(server, &world).await;
    tracing::debug!("[{name})] Spawning game objects Npc, Doodad, Slave, Gimmick...");
    if let Some(m) = server.systems.npcs.spawns.get(world.id) {
        m.spawn_all(server);
        m.wait_spawn_tasks().await;
    }
    tracing::debug!("[{name})] Finished spawning game objects Npc, Doodad, Slave, Gimmick...");
    register_indun_events(server, dungeon);
    tracing::info!("[{name})] Dungeon instance ready!");
    dungeon.st().finished_loading = true;
    tokio::time::sleep(settle).await;
    let requests: Vec<PlayerRef> = dungeon.st().enter_requests.clone();
    if !requests.is_empty() {
        tracing::info!("[{name})] Moving players to dungeon instance ...");
        for p in &requests {
            if p.data().is_online {
                add_player(server, dungeon, p);
            }
        }
        dungeon.st().enter_requests.clear();
    }
}

/// `QueuePlayer(character)`: adds a player to the queue while the dungeon is loading (or adds
/// them directly once it is loaded).
pub fn queue_player(server: &GameServer, dungeon: &Arc<Dungeon>, p: &PlayerRef) -> bool {
    if dungeon.is_queued(p) {
        return true;
    }
    // Block players who are part of a court case
    if server
        .systems
        .social
        .trials
        .is_player_in_court(p.character_id)
    {
        send_error(p, err::CANNOT_USE_PORTAL_IN_TRIAL, 0);
        return false;
    }
    if !server.systems.instances.indun.check_entry_attempt_count(
        p.character_id,
        dungeon.zone_group_id(),
        &dungeon.indun_zone,
        true,
    ) {
        tracing::info!(
            "[{}] Player {} did too many dungeon attempts.",
            dungeon.world_id().unwrap_or(0),
            p.data().unit.name
        );
        send_error(p, err::INSTANCE_VISIT_LIMIT, 0);
        return false;
    }
    let finished = {
        let mut st = dungeon.st();
        st.players_with_access.insert(p.character_id);
        st.finished_loading
    };
    if finished {
        add_player(server, dungeon, p);
    } else {
        p.send(SCProcessingInstancePacket {
            zone_id: dungeon.zone_instance_id.zone_id as i32,
        });
        dungeon.st().enter_requests.push(p.clone());
    }
    true
}

/// `AddPlayer(character)`.
pub fn add_player(server: &GameServer, dungeon: &Arc<Dungeon>, p: &PlayerRef) {
    let zid = dungeon.zone_instance_id;
    tracing::info!(
        "[Dungeon] Adding player {} to dungeon {}, {}",
        p.data().unit.name,
        zid.instance_id,
        zid.zone_id
    );
    let Some(world) = dungeon.world(server) else {
        // C#: World.HasCharacter on a destroyed dungeon (NullReferenceException).
        tracing::error!("[Dungeon] AddPlayer on a destroyed dungeon");
        return;
    };
    // `character.ParentWorld` (the world the character comes from)
    let old_instance = p.data().unit.transform.instance_id;
    if !worlds::has_character(&world, p.character_id) {
        world.add_object(p);
    } else {
        tracing::info!(
            "[Dungeon] Player {} already exists in dungeon {}, {}. Most likely an error in logic?",
            p.data().unit.name,
            zid.instance_id,
            zid.zone_id
        );
    }
    // Force despawn all mates of the player in the old world
    crate::systems::vehicles::mates::remove_and_despawn_all_active_owned_mates(
        server,
        old_instance,
        p.character_id,
    );
    move_character(server, dungeon, &world, p);
}

/// `MoveCharacterToSystemInstance` / `MoveCharacterToDungeon` (they differ in the team event
/// subscriptions only).
fn move_character(
    server: &GameServer,
    dungeon: &Arc<Dungeon>,
    world: &Arc<WorldInstance>,
    p: &PlayerRef,
) {
    let zone_key = dungeon.zone_instance_id.zone_id;
    // we take the coordinates of the zone
    let Some(sp) = worlds::zone_spawn_position(server, &world.template, zone_key) else {
        if dungeon.is_system {
            tracing::info!("World #{}, not have default spawn position.", world.id);
        } else {
            tracing::info!("World #{}, does not have default spawn position.", world.id);
        }
        send_error(p, err::NO_SERVER_INSTANCE_RESOURCE, 0);
        return;
    };
    p.data().disabled_set_position = true;
    worlds::apply_world_spawn_position(server, p, &sp, world.id);
    p.send(SCLoadInstancePacket {
        instance_id: world.id,
        zone_id: zone_key,
        x: sp.x,
        y: sp.y,
        z: sp.z,
        ang_x: deg_to_rad(sp.roll),
        ang_y: deg_to_rad(sp.pitch),
        ang_z: deg_to_rad(sp.yaw),
    });
    let ev = &server.systems.instances.events.characters;
    let h = Handler::Dungeon(dungeon.clone());
    if !dungeon.is_system {
        ev.subscribe(p, CharEvent::TeamJoin, h.clone());
        ev.subscribe(p, CharEvent::TeamKick, h.clone());
        ev.subscribe(p, CharEvent::TeamLeave, h.clone());
    }
    ev.subscribe(p, CharEvent::DungeonLeave, h.clone());
    ev.subscribe(p, CharEvent::Disconnect, h);
}

/// `RemovePlayer(character)` (`World.RemoveObject`).
fn remove_player(server: &GameServer, dungeon: &Dungeon, p: &PlayerRef) -> bool {
    match dungeon.world(server) {
        Some(w) => w.remove_object(p.obj_id),
        None => false,
    }
}

fn unsubscribe(server: &GameServer, dungeon: &Arc<Dungeon>, p: &PlayerRef, events: &[CharEvent]) {
    let ev = &server.systems.instances.events.characters;
    for e in events {
        ev.unsubscribe(p, *e, dungeon.uid);
    }
}

/// `LeaveDungeonInstance(character)` / `LeaveSystemInstance(character)`.
fn leave_instance(server: &GameServer, dungeon: &Arc<Dungeon>, p: &PlayerRef, system: bool) {
    if system {
        unsubscribe(
            server,
            dungeon,
            p,
            &[CharEvent::DungeonLeave, CharEvent::Disconnect],
        );
    } else {
        unsubscribe(
            server,
            dungeon,
            p,
            &[
                CharEvent::TeamJoin,
                CharEvent::TeamKick,
                CharEvent::TeamLeave,
                CharEvent::DungeonLeave,
                CharEvent::Disconnect,
            ],
        );
    }
    dungeon
        .st()
        .leave_requests
        .retain(|(id, _)| *id != p.character_id);
    let _ = remove_player(server, dungeon, p);
    if p.data().main_world_position.is_none() {
        let id = dungeon.world_id().unwrap_or(0);
        if system {
            tracing::info!(
                "World #.{id}, did not have a return point set in main world for {} ({}) !",
                p.data().unit.name,
                p.character_id
            );
        } else {
            tracing::info!("World #.{id}, does not have Main World spawn position.");
        }
        return;
    }
    let _ = worlds::return_to_main_world(server, p);
}

/// `OnDungeonLeave(sender, args)`.
pub fn on_dungeon_leave(server: &GameServer, dungeon: &Arc<Dungeon>, p: &PlayerRef) {
    tracing::info!(
        "Player {} ({}) has exited from dungeon {}!",
        p.data().unit.name,
        p.character_id,
        dungeon
            .world(server)
            .map(|w| worlds::world_name(&w))
            .unwrap_or_default()
    );
    // `character.ParentWorld.DungeonInstance`
    let instance = p.data().unit.transform.instance_id;
    let Some(parent) = server.systems.instances.dungeon_of_world(instance) else {
        return;
    };
    leave_instance(server, dungeon, p, parent.is_system);
}

/// `OnTeamJoin(sender, args)` (`team` is `args.Team`, the team the player just joined).
pub fn on_team_join(server: &GameServer, dungeon: &Arc<Dungeon>, p: &PlayerRef, team: &Team) {
    tracing::info!("Player {} has joined a party!", p.data().unit.name);
    let converted = {
        let mut st = dungeon.st();
        if !st.is_team_owned {
            let owner_id = st.character_owner.as_ref().map(|o| o.character_id);
            if owner_id != Some(team.owner_id) {
                return;
            }
            st.owner_team = Some(team.clone());
            st.is_team_owned = true;
            st.character_owner = None;
            true
        } else {
            false
        }
    };
    if converted {
        tracing::info!(
            "[Dungeon] instanceId: {}, zoneId: {}. Converting solo instance into a party instance.",
            dungeon.zone_instance_id.instance_id,
            dungeon.zone_instance_id.zone_id
        );
        return;
    }
    // `PlayerInSameTeam(character)`: GetTeamByObjId(player) is `team` here.
    let same = dungeon.st().owner_team.as_ref().map(|t| t.id) == Some(team.id);
    if same {
        if let Some(world) = dungeon.world(server) {
            if !worlds::has_character(&world, p.character_id) {
                world.add_object(p);
            }
        }
    }
}

/// `OnTeamLeave(sender, args)` (also `OnTeamKick`).
pub fn on_team_leave(server: &GameServer, dungeon: &Arc<Dungeon>, p: &PlayerRef, team_id: u32) {
    tracing::info!(
        "Player {} has left the party {team_id}!",
        p.data().unit.name
    );
    send_error(p, err::INSTANCE_LEAVE_PARTY, 0);
    let Some(world) = dungeon.world(server) else {
        return;
    };
    let instance = p.data().unit.transform.instance_id;
    if worlds::has_character(&world, p.character_id)
        && instance == dungeon.zone_instance_id.instance_id
    {
        let kick = i64::from(server.config.dungeons.auto_team_disband_kick_time) * 1000;
        let mut st = dungeon.st();
        st.players_with_access.remove(&p.character_id);
        // `_leaveRequests.TryAdd` keeps an existing request.
        if !st
            .leave_requests
            .iter()
            .any(|(id, _)| *id == p.character_id)
        {
            st.leave_requests.push((p.character_id, now_ms() + kick));
        }
    }
}

/// `OnDisconnect(sender, args)`.
pub fn on_disconnect(server: &GameServer, dungeon: &Arc<Dungeon>, p: &PlayerRef) {
    tracing::info!(
        "[Dungeon] instanceId={}, zoneId={} player={} disconnected!",
        dungeon.zone_instance_id.instance_id,
        dungeon.zone_instance_id.zone_id,
        p.data().unit.name
    );
    if dungeon.is_system {
        let _ = remove_player(server, dungeon, p);
        unsubscribe(
            server,
            dungeon,
            p,
            &[CharEvent::DungeonLeave, CharEvent::Disconnect],
        );
        return;
    }
    if !p.data().in_party {
        let zone = dungeon.world(server).and_then(|w| {
            w.template
                .zone_keys
                .first()
                .and_then(|k| server.game_data.zones.zone_by_key(*k).cloned())
        });
        super::indun::request_deletion(server, p, zone.as_ref());
    }
    let _ = remove_player(server, dungeon, p);
    unsubscribe(
        server,
        dungeon,
        p,
        &[
            CharEvent::TeamJoin,
            CharEvent::TeamKick,
            CharEvent::TeamLeave,
            CharEvent::DungeonLeave,
            CharEvent::Disconnect,
        ],
    );
}

/// `PlayerInSameTeam(player)`: the owning team is the player's current team. (A player without
/// a team is a NullReferenceException in C#; `false` here.)
pub fn player_in_same_team(server: &GameServer, dungeon: &Dungeon, p: &PlayerRef) -> bool {
    let owner = {
        let st = dungeon.st();
        if !st.is_team_owned {
            return false;
        }
        st.owner_team.as_ref().map(|t| t.id)
    };
    let current = server.systems.social.teams.team_by_obj_id(p.obj_id);
    match (owner, current) {
        (Some(o), Some(t)) => o == t.id,
        _ => false,
    }
}

/// `IsOwner(character)` (unused in C#).
pub fn is_owner(dungeon: &Dungeon, p: &PlayerRef) -> bool {
    let st = dungeon.st();
    !st.is_team_owned
        && st
            .character_owner
            .as_ref()
            .is_some_and(|o| o.character_id == p.character_id)
}

/// `_ownerTeam.MembersOnlineCount()` (the live team while it exists, else the last snapshot,
/// like the C# object reference).
fn owner_team_online_count(server: &GameServer, dungeon: &Dungeon) -> Option<usize> {
    let team_id = dungeon.st().owner_team.as_ref().map(|t| t.id)?;
    if let Some(live) = server.systems.social.teams.active_team(team_id) {
        let n = live.members_online_count();
        dungeon.st().owner_team = Some(live);
        return Some(n);
    }
    let snapshot = dungeon.st().owner_team.clone()?;
    Some(snapshot.members_online_count())
}

/// `LeaveDungeonTick`: kicks players whose leave request expired; removes a team dungeon whose
/// team is completely offline.
pub fn leave_dungeon_tick(server: &GameServer, dungeon: &Arc<Dungeon>) {
    let online = owner_team_online_count(server, dungeon);
    let empty = dungeon.st().leave_requests.is_empty();
    match online {
        Some(n) => {
            if n == 0 && empty {
                let _ = destroy_dungeon(server, dungeon);
            } else if empty {
                return;
            }
        }
        None => {
            if empty {
                return;
            }
        }
    }
    let requests = dungeon.st().leave_requests.clone();
    let world_label = dungeon
        .world(server)
        .map(|w| worlds::world_name(&w))
        .unwrap_or_default();
    let zone = dungeon.zone_instance_id.zone_id;
    for (player_id, time) in requests {
        if now_ms() <= time {
            continue;
        }
        let Some(character) = server.world.character_by_id(player_id) else {
            tracing::warn!(
                "[{world_label}] zoneId={zone}: Player Id {player_id} not found. Removing request."
            );
            dungeon
                .st()
                .leave_requests
                .retain(|(id, _)| *id != player_id);
            return;
        };
        if character.data().in_party && player_in_same_team(server, dungeon, &character) {
            tracing::info!(
                "[{world_label}] zoneId={zone}: {} ({player_id}) rejoined party, aborting.",
                character.data().unit.name
            );
            dungeon
                .st()
                .leave_requests
                .retain(|(id, _)| *id != player_id);
            return;
        }
        tracing::info!(
            "[{world_label}] zoneId={zone}: Removing {} ({player_id}) from instance.",
            character.data().unit.name
        );
        super::events::fire_dungeon_leave(server, &character);
        // QoL update that's different from retail
        let count = dungeon
            .world(server)
            .map_or(0, |w| worlds::character_count(&w));
        if server.config.dungeons.auto_cleanup_after_kick && count == 0 && !dungeon.is_system {
            if dungeon.world_id().is_none() {
                // C#: World.GetCharacterCount() on a destroyed dungeon (NRE).
                return;
            }
            if !destroy_dungeon(server, dungeon) {
                tracing::warn!(
                    "[{world_label}] Failed to removed empty dungeon with no players after kick from dungeon, zoneId={zone}"
                );
            }
        }
    }
}

/// `RegisterIndunEvents()`.
pub fn register_indun_events(server: &GameServer, dungeon: &Dungeon) {
    tracing::info!("Registering Indun Events...");
    let Some(world) = dungeon.world(server) else {
        return;
    };
    let events = server
        .game_data
        .systems
        .instances
        .indun
        .indun_events(dungeon.zone_group_id())
        .to_vec();
    for ev in &events {
        super::events::subscribe(server, ev, &world);
    }
}

/// `UnregisterIndunEvents()`.
fn unregister_indun_events(server: &GameServer, dungeon: &Dungeon, world_id: u32) {
    tracing::info!("Unregistering Indun Events...");
    let events = server
        .game_data
        .systems
        .instances
        .indun
        .indun_events(dungeon.zone_group_id())
        .to_vec();
    for ev in &events {
        super::events::unsubscribe(server, ev, world_id);
    }
}

/// `DestroyDungeon()`: removes every player from the instance (they are not moved out — C#),
/// unregisters the events, cleans the instance up and removes it.
pub fn destroy_dungeon(server: &GameServer, dungeon: &Arc<Dungeon>) -> bool {
    let zid = dungeon.zone_instance_id;
    tracing::info!(
        "[Dungeon] instanceId={}, zoneId={}: Destroying dungeon...",
        zid.instance_id,
        zid.zone_id
    );
    dungeon.st().area_clear_tick = false;
    let Some(world) = dungeon.world(server) else {
        // C#: `World.GetAllCharacters()` on a destroyed dungeon (NullReferenceException).
        return true;
    };
    for p in world.all_characters() {
        let _ = remove_player(server, dungeon, &p);
    }
    unregister_indun_events(server, dungeon, world.id);
    {
        let mut st = dungeon.st();
        st.leave_tick = false;
        st.area_clear_tick = false;
    }
    worlds::cleanup_instance(server, world.id);
    worlds::remove_world(server, world.id);
    worlds::release_world_id(server, world.id);
    server.systems.instances.dungeons.remove(&world.id);
    server.systems.instances.events.remove_world(world.id);
    dungeon.st().world_id = None;
    true
}

/// `AreaClearTick`: for every `IndunEventNoAliveChInRooms` of the zone group, counts the players
/// within the room radius of its center doodad; when the room became empty, the event's actions
/// run.
///
/// Note (C#): a cleared room ends the whole tick (`return` instead of `continue`); mirrored.
pub fn area_clear_tick(server: &GameServer, dungeon: &Arc<Dungeon>) {
    let Some(world) = dungeon.world(server) else {
        return;
    };
    let data = &server.game_data.systems.instances.indun;
    for ev in data.indun_events(dungeon.zone_group_id()) {
        let IndunEventKind::NoAliveChInRooms { room_id } = ev.kind else {
            continue;
        };
        if dungeon.is_room_cleared(room_id) {
            return;
        }
        let Some(room) = data.room(room_id) else {
            tracing::error!("AreaClearTick: room {room_id} does not exist");
            continue;
        };
        let events = &server.systems.instances.events;
        let Some(doodad) = events.room_doodad(ev.id, world.id) else {
            continue;
        };
        let radius = room.radius as f32;
        let count = super::events::players_in_room(server, &doodad, radius);
        tracing::info!("Character:{count} in room:{room_id}");
        if count == 0 && events.room_player_count(ev.id, world.id) != 0 {
            super::indun::do_indun_actions(server, ev.start_action_id, &world);
        }
        events.set_room_player_count(ev.id, world.id, count as u32);
    }
}
