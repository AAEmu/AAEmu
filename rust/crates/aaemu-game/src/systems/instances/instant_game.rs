//! `Core/Managers/InstantGameManager.cs` and `Models/Game/InstantGame/*`: battlefield
//! matchmaking queues, the instant game (arena) instances, teams, kills, scores and results.

use std::sync::{Arc, Mutex, MutexGuard, OnceLock, Weak};
use std::time::Duration;

use crate::data::world_templates::WorldSpawnPosition;
use crate::packets::world::SCUnitPointsPacket;
use crate::server::GameServer;
use crate::systems::npcs::portal::packets::SCLoadInstancePacket;
use crate::systems::skills::packets::CompressedGamePackets;
use crate::systems::skills::packets_special::{SCCharacterResurrectedPacket, SCTeleportUnitPacket};
use crate::world::{Player, PlayerRef, WorldObject};

use super::data::{Battlefield, GameRuleSet, Point};
use super::events::{CharEvent, Handler};
use super::packets::{
    SCAppliedToInstantGamePacket, SCInstantGameAddPointPacket, SCInstantGameEndPacket,
    SCInstantGameJoinedPacket, SCInstantGameKillPacket, SCInstantGameKillstreakPacket,
    SCInstantGameStartPacket, SCInviteToInstantGamePacket, SCSkillCooldownResetPacket,
    TeamMemberWire, TeamResultWire, ZoneInstanceId,
};
use super::worlds;

/// `InstantCorps` (as the byte written to the wire).
pub mod corps {
    /// `Invalid = -1` (`(byte)-1`).
    pub const INVALID: u8 = 0xFF;
    pub const CORPS1: u8 = 0;
    pub const CORPS2: u8 = 1;
    pub const ANY: u8 = 2;
}

/// `InstantGameType`.
pub mod game_type {
    pub const DRILL_CAMP: u32 = 7;
    pub const GLADIATOR: u32 = 11;
}

/// `VictoryState`.
pub mod victory_state {
    pub const INVALID: u8 = 0;
    pub const LOSE: u8 = 1;
    pub const WIN: u8 = 2;
    pub const DRAW: u8 = 3;
}

/// `BattlefieldEndingReason.AchievementScore`.
pub const ENDING_ACHIEVEMENT_SCORE: u8 = 8;

/// `Character.ResetAllSkillCooldowns`: `playerSkillsTag`.
const PLAYER_SKILLS_TAG: u32 = 378;

/// `Environment.TickCount & int.MaxValue` (milliseconds of a monotonic clock; the C# counts from
/// the machine's boot, here from the first use).
pub fn tick_count() -> u32 {
    static START: OnceLock<std::time::Instant> = OnceLock::new();
    let start = START.get_or_init(std::time::Instant::now);
    (start.elapsed().as_millis() as u64 & u64::from(i32::MAX as u32)) as u32
}

fn now_ms() -> i64 {
    chrono::Utc::now().timestamp_millis()
}

// ---------------------------------------------------------------------------------------------
// InstantGame
// ---------------------------------------------------------------------------------------------

/// `InstantGameTeamMember`.
#[derive(Debug, Clone)]
pub struct TeamMember {
    pub player: PlayerRef,
    pub score: i32,
    pub bonus: i32,
    pub bonus_set: u32,
    pub killstreak: u16,
    pub kills: u16,
    pub assists: u16,
    pub deaths: u16,
    /// `Corps` (which result: [`corps::CORPS1`] / [`corps::CORPS2`]).
    pub result: u8,
}

/// `InstantGameTeamResult` (members are indices into the game's member list).
#[derive(Debug, Clone, Default)]
pub struct TeamResult {
    pub state: u8,
    pub faction_id: u32,
    pub members: Vec<usize>,
}

/// The mutable members of an [`InstantGame`].
#[derive(Debug, Default)]
pub struct GameState {
    /// `_players`.
    pub players: Vec<PlayerRef>,
    /// `_corps` (faction id -> players).
    pub corps: Vec<(u32, Vec<PlayerRef>)>,
    /// `_characterCorps`.
    pub character_corps: Vec<(PlayerRef, u8)>,
    /// `_members` (insertion order).
    pub members: Vec<TeamMember>,
    pub corps1: TeamResult,
    pub corps2: TeamResult,
}

impl GameState {
    fn corps_of(&self, p: &PlayerRef) -> Option<u8> {
        self.character_corps
            .iter()
            .find(|(x, _)| Arc::ptr_eq(x, p))
            .map(|(_, c)| *c)
    }

    fn member_index(&self, p: &PlayerRef) -> Option<usize> {
        self.members.iter().position(|m| Arc::ptr_eq(&m.player, p))
    }

    fn result(&self, which: u8) -> &TeamResult {
        if which == corps::CORPS1 {
            &self.corps1
        } else {
            &self.corps2
        }
    }

    fn result_score(&self, which: u8) -> i32 {
        self.result(which)
            .members
            .iter()
            .fold(0i32, |a, i| a.wrapping_add(self.members[*i].score))
    }

    fn result_kills(&self, which: u8) -> i16 {
        self.result(which)
            .members
            .iter()
            .map(|i| i32::from(self.members[*i].kills))
            .sum::<i32>() as i16
    }

    /// The wire form of a team result (`InstantGameTeamResult.Write`).
    pub fn result_wire(&self, which: u8) -> TeamResultWire {
        let r = self.result(which);
        TeamResultWire {
            state: r.state,
            faction_id: r.faction_id,
            members: r
                .members
                .iter()
                .map(|i| {
                    let m = &self.members[*i];
                    let (id, name) = {
                        let c = m.player.data();
                        (c.id, c.unit.name.clone())
                    };
                    TeamMemberWire {
                        character_id: id,
                        name,
                        score: m.score,
                        bonus: m.bonus,
                        bonus_set: m.bonus_set,
                        killstreak: m.killstreak,
                        kills: m.kills,
                        assists: m.assists,
                        deaths: m.deaths,
                    }
                })
                .collect(),
        }
    }
}

/// `InstantGame`.
#[derive(Debug)]
pub struct InstantGame {
    pub uid: u64,
    /// `_battlefield`.
    pub battlefield: Battlefield,
    /// `_battlefield.RuleSet`.
    pub rule_set: GameRuleSet,
    /// `_world.Id`.
    pub world_id: u32,
    /// `_zoneInstanceId`.
    pub zone_instance_id: ZoneInstanceId,
    st: Mutex<GameState>,
    /// The timed `EndGame` task (`_endGameTokenSource`).
    end_task: Mutex<Option<tokio::task::JoinHandle<()>>>,
}

impl InstantGame {
    pub fn st(&self) -> MutexGuard<'_, GameState> {
        self.st.lock().expect("instant game poisoned")
    }

    /// `BattlefieldId`.
    pub fn battlefield_id(&self) -> u32 {
        self.battlefield.id
    }

    /// `IsFull`.
    pub fn is_full(&self) -> bool {
        self.st().players.len() as u64 == u64::from(self.rule_set.corps_size) * 2
    }

    /// `_players.Contains(character)`.
    pub fn has_player(&self, p: &PlayerRef) -> bool {
        self.st().players.iter().any(|x| Arc::ptr_eq(x, p))
    }

    /// `GetCorps()`.
    pub fn get_corps(&self) -> u8 {
        let st = self.st();
        if self.battlefield.id == game_type::GLADIATOR {
            if !st.character_corps.iter().any(|(_, c)| *c == corps::CORPS1) {
                return corps::CORPS1;
            }
            if !st.character_corps.iter().any(|(_, c)| *c == corps::CORPS2) {
                return corps::CORPS2;
            }
            return corps::INVALID;
        }
        let a = st
            .character_corps
            .iter()
            .filter(|(_, c)| *c == corps::CORPS1)
            .count();
        let b = st
            .character_corps
            .iter()
            .filter(|(_, c)| *c == corps::CORPS2)
            .count();
        if b > a {
            corps::CORPS1
        } else {
            corps::CORPS2
        }
    }

    fn spawn_of(&self, which: u8) -> Option<Point> {
        let s = self.battlefield.spawns?;
        if which == corps::CORPS1 {
            s.corps1_spawn
        } else {
            s.corps2_spawn
        }
    }

    /// `BroadcastPacket(packet)` (every player of the game).
    fn broadcast(&self, packet: &dyn crate::net::packet::ServerPacket) {
        let players = self.st().players.clone();
        let mut out = crate::net::connection::Fanout::new(packet);
        for p in players {
            out.send(&p.conn);
        }
    }
}

/// `new InstantGame(battlefield)`: creates the battlefield's world instance (its spawn files
/// load in the background). `None` where the C# constructor throws (no rule set, no world for
/// the zone key, both corps the same faction).
pub fn create_game(server: &GameServer, battlefield: &Battlefield) -> Option<Arc<InstantGame>> {
    let Some(rule_set) = battlefield.rule_set else {
        tracing::error!(
            "InstantGame: battlefield {} has no rule set",
            battlefield.id
        );
        return None;
    };
    if rule_set.corps1_faction_id == rule_set.corps2_faction_id {
        tracing::error!(
            "InstantGame: battlefield {} uses the same faction for both corps",
            battlefield.id
        );
        return None;
    }
    let Some(template) = server
        .game_data
        .worlds
        .get_world_template_by_zone_key(battlefield.zone_key)
    else {
        tracing::error!(
            "GetWorldByZone(): No world template defined for ZoneId {}",
            battlefield.zone_key
        );
        return None;
    };
    let world = match worlds::create_world_instance(server, template, 0, false, 0, None) {
        Ok(w) => w,
        Err(e) => {
            tracing::error!("InstantGame: CreateWorldInstance failed: {e}");
            return None;
        }
    };
    if let Some(s) = server.self_arc() {
        let w = world.clone();
        tokio::spawn(async move { worlds::load_spawns(&s, &w).await });
    }
    let game = Arc::new(InstantGame {
        uid: server.systems.instances.next_uid(),
        battlefield: battlefield.clone(),
        rule_set,
        world_id: world.id,
        zone_instance_id: ZoneInstanceId {
            zone_id: battlefield.zone_key,
            instance_id: world.id,
        },
        st: Mutex::new(GameState {
            corps: vec![
                (rule_set.corps1_faction_id, Vec::new()),
                (rule_set.corps2_faction_id, Vec::new()),
            ],
            corps1: TeamResult {
                state: victory_state::LOSE,
                faction_id: rule_set.corps1_faction_id,
                members: Vec::new(),
            },
            corps2: TeamResult {
                state: victory_state::LOSE,
                faction_id: rule_set.corps2_faction_id,
                members: Vec::new(),
            },
            ..GameState::default()
        }),
        end_task: Mutex::new(None),
    });
    Some(game)
}

/// `AddPlayer(character, corps)`: the player joins a corps and is invited.
pub fn add_player(server: &GameServer, game: &Arc<InstantGame>, p: &PlayerRef, corps: u8) {
    if game.has_player(p) {
        // Player already exists in game, remove for correction
        remove_player(server, game, p);
    }
    {
        let mut st = game.st();
        st.players.push(p.clone());
        let faction = if corps == corps::CORPS1 {
            game.rule_set.corps1_faction_id
        } else {
            game.rule_set.corps2_faction_id
        };
        if let Some((_, list)) = st.corps.iter_mut().find(|(f, _)| *f == faction) {
            list.push(p.clone());
        }
        st.character_corps.push((p.clone(), corps));
    }
    p.send(SCInviteToInstantGamePacket {
        zone_instance_id: game.zone_instance_id,
        ruleset_id: game.rule_set.id,
        corps,
        qualified_id: 1,
    });
    server
        .systems
        .instances
        .instant_games
        .set_current(p, Some(game.clone()));
}

/// `RemovePlayer(character)`.
///
/// Note (C#): `_corps` is keyed by faction id but looked up with `InstantCorps.Corps1` /
/// `Corps2` (0 / 1), so the corps lists are never cleaned; mirrored.
pub fn remove_player(server: &GameServer, game: &Arc<InstantGame>, p: &PlayerRef) -> bool {
    {
        let mut st = game.st();
        let Some(i) = st.players.iter().position(|x| Arc::ptr_eq(x, p)) else {
            return false;
        };
        st.players.remove(i);
        for key in [u32::from(corps::CORPS1), u32::from(corps::CORPS2)] {
            if let Some((_, list)) = st.corps.iter_mut().find(|(f, _)| *f == key) {
                list.retain(|x| !Arc::ptr_eq(x, p));
            }
        }
        st.character_corps.retain(|(x, _)| !Arc::ptr_eq(x, p));
    }
    server.systems.instances.instant_games.set_current(p, None);
    true
}

/// `PlayerInviteResponse(character, joins, qualifierId)`.
pub fn player_invite_response(server: &GameServer, p: &PlayerRef, join: bool, _qualified_id: u64) {
    let Some(game) = server.systems.instances.instant_games.current(p) else {
        return;
    };
    if !join {
        // Next room, remove from current game then readd to requeue
        withdraw_from_battlefield(server, p);
        apply_to_battlefield(server, game.battlefield.id, corps::ANY, p);
        return;
    }
    let Some(c) = game.st().corps_of(p) else {
        // `_characterCorps[character]`: KeyNotFoundException in C#.
        return;
    };
    let Some(spawn) = game.spawn_of(c) else {
        tracing::error!(
            "InstantGame: battlefield {} has no spawns",
            game.battlefield.id
        );
        return;
    };
    move_character_to_world(
        server,
        &game,
        p,
        game.battlefield.zone_key,
        spawn.x,
        spawn.y,
        spawn.z,
    );
}

/// `MoveCharacterToWorld(character, zoneId, x, y, z)`.
fn move_character_to_world(
    server: &GameServer,
    game: &InstantGame,
    p: &PlayerRef,
    zone_id: u32,
    x: f32,
    y: f32,
    z: f32,
) {
    p.data().disabled_set_position = true;
    if p.data().main_world_position.is_none() {
        let t = worlds::clone_detached(server, p);
        p.data().main_world_position = Some(t);
    }
    let wsp = WorldSpawnPosition {
        world_id: 0,
        zone_id,
        x,
        y,
        z,
        ..WorldSpawnPosition::default()
    };
    worlds::apply_world_spawn_position(server, p, &wsp, game.world_id);
    p.send(SCLoadInstancePacket {
        instance_id: game.world_id,
        zone_id,
        x,
        y,
        z,
        ang_x: 0.0,
        ang_y: 0.0,
        ang_z: 0.0,
    });
}

/// `OnEnterWorld(character, qualifierId)` (`CSEnteredInstantGameWorldPacket`).
pub fn on_enter_world(server: &GameServer, p: &PlayerRef, _qualified_id: u64) {
    let Some(game) = server.systems.instances.instant_games.current(p) else {
        return;
    };
    if p.data().unit.transform.instance_id != game.world_id {
        return;
    }
    if game.st().member_index(p).is_some() {
        return;
    }
    let Some(c) = game.st().corps_of(p) else {
        return;
    };
    p.send(SCInstantGameJoinedPacket {
        zone_instance_id: game.zone_instance_id,
        corps: c,
        rule_set: game.rule_set,
    });
    let faction = if c == corps::CORPS1 {
        game.rule_set.corps1_faction_id
    } else {
        game.rule_set.corps2_faction_id
    };
    set_character_faction(server, p, faction);
    let start = {
        let mut st = game.st();
        if st.member_index(p).is_some() {
            // `_members.Add`: ArgumentException in C# (entered twice).
            return;
        }
        let which = if c == corps::CORPS1 {
            corps::CORPS1
        } else {
            corps::CORPS2
        };
        st.members.push(TeamMember {
            player: p.clone(),
            score: 0,
            bonus: 0,
            bonus_set: 0,
            killstreak: 0,
            kills: 0,
            assists: 0,
            deaths: 0,
            result: which,
        });
        let idx = st.members.len() - 1;
        if which == corps::CORPS1 {
            st.corps1.members.push(idx);
        } else {
            st.corps2.members.push(idx);
        }
        // TODO (C#): This can be done better; game expire after 60 seconds if not enough players
        st.members.len() as u64 == u64::from(game.rule_set.corps_size) * 2
    };
    server.systems.instances.events.characters.subscribe(
        p,
        CharEvent::Kill,
        Handler::Game(game.clone()),
    );
    if start {
        start_game(server, &game);
    }
}

/// `Start()`: the start packet, the 3 s player reset and the timed `EndGame`.
pub fn start_game(server: &GameServer, game: &Arc<InstantGame>) {
    let now = tick_count();
    let start = now.wrapping_add(20000);
    game.broadcast(&SCInstantGameStartPacket {
        zone_instance_id: game.zone_instance_id,
        start,
        now,
    });
    let Some(s) = server.self_arc() else {
        return;
    };
    {
        let (s, g) = (s.clone(), game.clone());
        tokio::spawn(async move {
            tokio::time::sleep(Duration::from_secs(3)).await;
            let players: Vec<PlayerRef> = g
                .st()
                .character_corps
                .iter()
                .map(|(p, _)| p.clone())
                .collect();
            for p in players {
                reset_player(&s, &p);
            }
        });
    }
    let g = game.clone();
    let playing = Duration::from_millis(
        u64::try_from(i64::from(game.rule_set.time_playing) * 60 * 1000).unwrap_or(0),
    );
    let handle = tokio::spawn(async move {
        tokio::time::sleep(playing).await;
        end_game(&s, &g).await;
    });
    *game.end_task.lock().expect("end task poisoned") = Some(handle);
}

/// Full HP / MP (broadcast), `Buffs.RemoveAllEffects()`, `ResetAllSkillCooldowns(false)`.
fn reset_player(server: &GameServer, p: &PlayerRef) {
    let (hp, mp) = {
        let mut c = p.data();
        c.unit.hp = c.unit.max_hp;
        c.unit.mp = c.unit.max_mp;
        (c.unit.hp, c.unit.mp)
    };
    let obj = WorldObject::from(p);
    crate::systems::skills::broadcast(
        server,
        &obj,
        &SCUnitPointsPacket {
            obj_id: p.obj_id,
            hp,
            mp,
        },
        true,
    );
    crate::systems::skills::buffs::remove_all_effects(server, &obj);
    crate::systems::dispatch_buff_events(server, &obj);
    reset_all_skill_cooldowns(server, p, false);
}

/// `Character.ResetAllSkillCooldowns(triggerGcd)`: one `SCSkillCooldownResetPacket` per skill
/// tagged `playerSkillsTag` (378), compressed.
pub fn reset_all_skill_cooldowns(server: &GameServer, p: &PlayerRef, trigger_gcd: bool) {
    let mut packets = CompressedGamePackets::new();
    for skill_id in server
        .game_data
        .systems
        .skills
        .skills_by_tag(PLAYER_SKILLS_TAG)
    {
        packets.add(&SCSkillCooldownResetPacket {
            obj_id: p.obj_id,
            skill_id: *skill_id,
            tag_id: 0,
            gcd: trigger_gcd,
        });
    }
    p.send(packets);
}

/// `EndGame()`: the result, then after `TimeEnding` minutes the game is destroyed.
pub async fn end_game(server: &Arc<GameServer>, game: &Arc<InstantGame>) {
    send_result(game);
    let ending = Duration::from_millis(
        u64::try_from(i64::from(game.rule_set.time_ending) * 60 * 1000).unwrap_or(0),
    );
    tokio::time::sleep(ending).await;
    destroy_game(server, game);
}

/// `SendResult()`.
pub fn send_result(game: &InstantGame) {
    let packet = {
        let st = game.st();
        SCInstantGameEndPacket {
            zone_instance_id: game.zone_instance_id,
            reason: ENDING_ACHIEVEMENT_SCORE,
            corps1: st.result_wire(corps::CORPS1),
            corps2: st.result_wire(corps::CORPS2),
        }
    };
    game.broadcast(&packet);
}

/// `DestroyInstantGame()`: every player leaves, the instance is removed.
///
/// Deviation: the C# only removes the instance from the world list (its finalizer cleans the
/// spawns up and releases the id later); here that happens right away.
pub fn destroy_game(server: &GameServer, game: &Arc<InstantGame>) {
    let players = game.st().players.clone();
    for p in &players {
        leave_instant_game(server, game, p);
    }
    // TODO (C#): Unbind all events from characters
    worlds::cleanup_instance(server, game.world_id);
    worlds::remove_world(server, game.world_id);
    worlds::release_world_id(server, game.world_id);
    server.systems.instances.instant_games.remove_game(game);
}

/// `CSLeaveInstantGamePacket`: `CurrentInstantGame?.LeaveInstantGame(character)`.
pub fn leave_current_game(server: &GameServer, p: &PlayerRef) {
    if let Some(game) = server.systems.instances.instant_games.current(p) {
        leave_instant_game(server, &game, p);
    }
}

/// `LeaveInstantGame(character)`.
pub fn leave_instant_game(server: &GameServer, game: &Arc<InstantGame>, p: &PlayerRef) {
    remove_player(server, game, p);
    let origin = p.data().unit.origin_faction_id;
    if origin == 0 {
        // `character.OriginFaction.Id` on a character whose faction was never changed
        // (NullReferenceException in C#).
        tracing::error!(
            "LeaveInstantGame: {} has no origin faction",
            p.data().unit.name
        );
        return;
    }
    set_character_faction(server, p, origin);
    server
        .systems
        .instances
        .events
        .characters
        .unsubscribe(p, CharEvent::Kill, game.uid);
    p.data().disabled_set_position = true;
    if p.data().main_world_position.is_none() {
        tracing::warn!(
            "Character {} ({}) does not have MainWorldPosition when leaving instant game!",
            p.data().unit.name,
            p.character_id
        );
        return;
    }
    let _ = worlds::return_to_main_world(server, p);
}

/// `Character.SetFaction(factionId)`: `Unit.SetFaction` plus the character part (housing,
/// expedition and party).
///
/// Note (C#): entering a battlefield sets the corps faction, so a guild member whose guild's
/// mother faction differs is kicked from the guild (`ExpeditionManager.Kick(connection, id)`,
/// subject to their role's expel right) and from their party; mirrored by default. The
/// `Dungeons.KickOnBattlefieldFactionChange` switch (`false`) keeps both memberships.
pub fn set_character_faction(server: &GameServer, p: &PlayerRef, faction_id: u32) {
    if p.data().unit.faction_id == faction_id {
        return;
    }
    let kick = server.config.dungeons.kick_on_battlefield_faction_change;
    let obj = WorldObject::from(p);
    crate::systems::skills::combat::set_faction(server, &obj, faction_id);
    crate::systems::dispatch_buff_events(server, &obj);
    let (id, faction, expedition, in_party) = {
        let c = p.data();
        (c.id, c.unit.faction_id, c.unit.expedition_id, c.in_party)
    };
    server
        .systems
        .social
        .hooks
        .get()
        .update_owned_housing_faction(server, id, faction);
    if kick && expedition != 0 {
        let mother = server
            .expeditions
            .get(expedition)
            .map(|e| e.faction.mother_id);
        if mother.is_some_and(|m| m != faction_id) {
            if let Some(s) = server.self_arc() {
                let p = p.clone();
                tokio::spawn(async move {
                    crate::systems::social::expedition::kick(&s, &p, p.character_id).await;
                });
            }
        }
    }
    if kick && in_party {
        server.systems.social.teams.member_remove_from_team(
            server,
            p,
            p,
            crate::systems::social::team::risky_action::KICK,
        );
    }
}

/// `OnKill(sender, args)` (instant game kill scoring; `killer` is the subscribed player).
pub fn on_kill(
    server: &GameServer,
    game: &Arc<InstantGame>,
    killer: &PlayerRef,
    victim: &WorldObject,
) {
    let Some(victim) = victim.as_player().cloned() else {
        return;
    };
    // `_members[killer]` / `_members[victim]` / `_characterCorps[..]` throw KeyNotFound in C#;
    // the updates done before the throw are kept.
    let packet = {
        let mut st = game.st();
        let Some(ki) = st.member_index(killer) else {
            return;
        };
        st.members[ki].kills = st.members[ki].kills.wrapping_add(1);
        st.members[ki].killstreak = st.members[ki].killstreak.wrapping_add(1);
        let Some(vi) = st.member_index(&victim) else {
            return;
        };
        st.members[vi].deaths = st.members[vi].deaths.wrapping_add(1);
        st.members[vi].killstreak = 0;
        let (Some(kc), Some(vc)) = (st.corps_of(killer), st.corps_of(&victim)) else {
            return;
        };
        let (kr, vr) = (st.members[ki].result, st.members[vi].result);
        let killstreak = st.members[ki].killstreak as i8;
        let (killer_id, killer_name) = {
            let c = killer.data();
            (c.id, c.unit.name.clone())
        };
        let (victim_id, victim_name) = {
            let c = victim.data();
            (c.id, c.unit.name.clone())
        };
        (
            SCInstantGameKillPacket {
                zone_instance_id: game.zone_instance_id,
                killer_id,
                victim_id,
                killer_corps: kc,
                victim_corps: vc,
                killer_killstreak: killstreak,
                killer_name,
                victim_name,
                killer_corps_kills: i32::from(st.result_kills(kr)),
                victim_corps_deaths: i32::from(st.result_kills(vr)),
            },
            killstreak,
            kc,
            vc,
            ki,
        )
    };
    let (kill, killstreak, killer_corps, victim_corps, ki) = packet;
    game.broadcast(&kill);
    killer.send(SCInstantGameKillstreakPacket {
        zone_instance_id: game.zone_instance_id,
        killstreak,
        skill_id: 0,
        enabled: true,
    });
    // TODO (C#): Get score from events
    if add_score(server, game, killer_corps, ki, 30) {
        // C#: `EndGame().Start()` throws InvalidOperationException after starting the end of
        // the game, so the rest of OnKill (the respawn) does not run.
        return;
    }
    let victim_spawn = game.spawn_of(victim_corps);
    let killer_spawn = game.spawn_of(if victim_corps == corps::CORPS1 {
        corps::CORPS2
    } else {
        corps::CORPS1
    });
    let Some(s) = server.self_arc() else {
        return;
    };
    let (g, killer) = (game.clone(), killer.clone());
    tokio::spawn(async move {
        tokio::time::sleep(Duration::from_secs(6)).await;
        respawn_after_kill(&s, &g, &killer, &victim, victim_spawn, killer_spawn);
    });
}

/// The delayed part of `OnKill`: the victim is resurrected at its corps spawn; in the gladiator
/// arena the killer is reset at the other spawn.
fn respawn_after_kill(
    server: &GameServer,
    game: &InstantGame,
    killer: &PlayerRef,
    victim: &PlayerRef,
    victim_spawn: Option<Point>,
    killer_spawn: Option<Point>,
) {
    // TODO (C#): Prevent fall damage for both killer and victim on teleport
    let Some(spawn) = victim_spawn else {
        return;
    };
    let vobj = WorldObject::from(victim);
    crate::systems::skills::broadcast(
        server,
        &vobj,
        &SCCharacterResurrectedPacket {
            unit_id: victim.obj_id,
            x: spawn.x,
            y: spawn.y,
            z: spawn.z,
            z_rot: f32::from(spawn.rotation_z),
        },
        true,
    );
    reset_all_skill_cooldowns(server, victim, false);
    crate::systems::skills::buffs::remove_all_effects(server, &vobj);
    crate::systems::dispatch_buff_events(server, &vobj);
    full_points(server, victim);

    if game.battlefield.id != game_type::GLADIATOR {
        return;
    }
    let Some(spawn) = killer_spawn else {
        return;
    };
    let kobj = WorldObject::from(killer);
    let dead = killer.data().unit.hp == 0;
    if dead {
        // Killer somehow died
        crate::systems::skills::broadcast(
            server,
            &kobj,
            &SCCharacterResurrectedPacket {
                unit_id: killer.obj_id,
                x: spawn.x,
                y: spawn.y,
                z: spawn.z,
                z_rot: f32::from(spawn.rotation_z),
            },
            true,
        );
    } else {
        killer.send(SCTeleportUnitPacket {
            reason: 0,
            error_message: 0,
            x: spawn.x,
            y: spawn.y,
            z: spawn.z,
            z2: f32::from(spawn.rotation_z),
        });
    }
    full_points(server, killer);
    crate::systems::skills::buffs::remove_all_effects(server, &kobj);
    crate::systems::dispatch_buff_events(server, &kobj);
    reset_all_skill_cooldowns(server, killer, false);
}

fn full_points(server: &GameServer, p: &PlayerRef) {
    let (hp, mp) = {
        let mut c = p.data();
        c.unit.hp = c.unit.max_hp;
        c.unit.mp = c.unit.max_mp;
        (c.unit.hp, c.unit.mp)
    };
    crate::systems::skills::broadcast(
        server,
        &WorldObject::from(p),
        &SCUnitPointsPacket {
            obj_id: p.obj_id,
            hp,
            mp,
        },
        true,
    );
}

/// `AddScore(corps, scorer, score)`. Returns `true` when the corps reached the victory score
/// (the game ends).
///
/// Note (C#): the point packet always names `Corps1` and sends the scoring corps' total as
/// `score1` with `score2 = 0`; mirrored.
fn add_score(
    server: &GameServer,
    game: &Arc<InstantGame>,
    which: u8,
    scorer: usize,
    score: i32,
) -> bool {
    let which = if which == corps::CORPS1 {
        corps::CORPS1
    } else {
        corps::CORPS2
    };
    let (packet, won) = {
        let mut st = game.st();
        st.members[scorer].score = st.members[scorer].score.wrapping_add(score);
        let total = st.result_score(which);
        let name = st.members[scorer].player.data().unit.name.clone();
        let won = total >= game.rule_set.victory_score;
        if won {
            if which == corps::CORPS1 {
                st.corps1.state = victory_state::WIN;
            } else {
                st.corps2.state = victory_state::WIN;
            }
        }
        (
            SCInstantGameAddPointPacket {
                zone_instance_id: game.zone_instance_id,
                corps: corps::CORPS1,
                points: score,
                score1: total,
                score2: 0,
                char_name: name,
            },
            won,
        )
    };
    game.broadcast(&packet);
    if won {
        if let Some(t) = game.end_task.lock().expect("end task poisoned").take() {
            t.abort();
        }
        // `EndGame()` runs synchronously up to its first await: the result is sent now.
        send_result(game);
        if let Some(s) = server.self_arc() {
            let g = game.clone();
            let ending = Duration::from_millis(
                u64::try_from(i64::from(game.rule_set.time_ending) * 60 * 1000).unwrap_or(0),
            );
            tokio::spawn(async move {
                tokio::time::sleep(ending).await;
                destroy_game(&s, &g);
            });
        }
    }
    won
}

// ---------------------------------------------------------------------------------------------
// InstantGameManager
// ---------------------------------------------------------------------------------------------

/// `MatchmakingApplicant`.
#[derive(Debug, Clone)]
pub struct Applicant {
    pub time_applied_ms: i64,
    pub player: PlayerRef,
}

#[derive(Debug, Default)]
struct ManagerState {
    /// `_matchmakingQueue` (battlefield id -> applicants; insertion order).
    queue: Vec<(u32, Vec<Applicant>)>,
    /// `_instantGames`.
    games: Vec<Arc<InstantGame>>,
    /// `_queueList` (games waiting for more players).
    queue_list: Vec<Arc<InstantGame>>,
}

/// `InstantGameManager` (`server.systems.instances.instant_games`).
#[derive(Debug, Default)]
pub struct InstantGameManager {
    st: Mutex<ManagerState>,
    /// `Character.CurrentInstantGame`.
    current: Mutex<Vec<(Weak<Player>, Arc<InstantGame>)>>,
}

impl InstantGameManager {
    fn lock(&self) -> MutexGuard<'_, ManagerState> {
        self.st.lock().expect("instant games poisoned")
    }

    /// `character.CurrentInstantGame`.
    pub fn current(&self, p: &PlayerRef) -> Option<Arc<InstantGame>> {
        self.current
            .lock()
            .expect("current games poisoned")
            .iter()
            .find(|(w, _)| w.upgrade().is_some_and(|x| Arc::ptr_eq(&x, p)))
            .map(|(_, g)| g.clone())
    }

    /// `character.CurrentInstantGame = game`.
    pub fn set_current(&self, p: &PlayerRef, game: Option<Arc<InstantGame>>) {
        let mut cur = self.current.lock().expect("current games poisoned");
        cur.retain(|(w, _)| w.upgrade().is_some_and(|x| !Arc::ptr_eq(&x, p)));
        if let Some(g) = game {
            cur.push((Arc::downgrade(p), g));
        }
    }

    /// The applicants of a battlefield queue (character ids, queue order).
    pub fn queued(&self, battlefield_id: u32) -> Option<Vec<u32>> {
        self.lock()
            .queue
            .iter()
            .find(|(id, _)| *id == battlefield_id)
            .map(|(_, a)| a.iter().map(|x| x.player.character_id).collect())
    }

    /// `_instantGames`.
    pub fn games(&self) -> Vec<Arc<InstantGame>> {
        self.lock().games.clone()
    }

    /// `_queueList`.
    pub fn queue_list(&self) -> Vec<Arc<InstantGame>> {
        self.lock().queue_list.clone()
    }

    /// `RemoveGame(game)` (the game stays in `_queueList`, as in C#).
    pub fn remove_game(&self, game: &Arc<InstantGame>) {
        self.lock().games.retain(|g| !Arc::ptr_eq(g, game));
    }
}

/// `ApplyToBattlefield(battlefieldId, corps, character)`.
pub fn apply_to_battlefield(server: &GameServer, battlefield_id: u32, corps: u8, p: &PlayerRef) {
    let mgr = &server.systems.instances.instant_games;
    {
        let mut st = mgr.lock();
        let pos = st.queue.iter().position(|(id, _)| *id == battlefield_id);
        let pos = match pos {
            Some(i) => i,
            None => {
                if server
                    .game_data
                    .systems
                    .instances
                    .battlefields
                    .battlefield(battlefield_id)
                    .is_none()
                {
                    tracing::warn!(
                        "[Matchmaking] Failed to add player {} to matchmaking queue for battlefield {battlefield_id}; battlefield does not exist",
                        p.data().unit.name
                    );
                    return;
                }
                st.queue.push((battlefield_id, Vec::new()));
                st.queue.len() - 1
            }
        };
        let applicants = &mut st.queue[pos].1;
        if applicants.iter().any(|a| Arc::ptr_eq(&a.player, p)) {
            return;
        }
        applicants.push(Applicant {
            time_applied_ms: now_ms(),
            player: p.clone(),
        });
    }
    tracing::trace!(
        "[Matchmaking] Added player {} to matchmaking queue for battlefield {battlefield_id}",
        p.data().unit.name
    );
    p.send(SCAppliedToInstantGamePacket {
        battlefield_id,
        corps,
        error_message_id: 0,
    });
}

/// `WithdrawFromBattlefield(character)`: leaves the queue, or declines an invitation (the game
/// goes back to the queue list).
pub fn withdraw_from_battlefield(server: &GameServer, p: &PlayerRef) {
    let mgr = &server.systems.instances.instant_games;
    let mut st = mgr.lock();
    for (_, applicants) in st.queue.iter_mut() {
        if let Some(i) = applicants.iter().position(|a| Arc::ptr_eq(&a.player, p)) {
            applicants.remove(i);
            tracing::trace!(
                "[Matchmaking] Removing {} from matchmaking.",
                p.data().unit.name
            );
            return;
        }
    }
    let games = st.games.clone();
    for game in games {
        if remove_player(server, &game, p) {
            tracing::trace!(
                "[Matchmaking] {} declined arena invitation.",
                p.data().unit.name
            );
            if !st.queue_list.iter().any(|g| Arc::ptr_eq(g, &game)) {
                st.queue_list.push(game);
                tracing::trace!("[Matchmaking] Adding game to queue list.");
            }
            return;
        }
    }
}

/// `MissingPlayersToStart(bfId, queueCount)`.
fn missing_players_to_start(
    server: &GameServer,
    st: &ManagerState,
    battlefield_id: u32,
    queue_count: usize,
) -> bool {
    let Some(bf) = server
        .game_data
        .systems
        .instances
        .battlefields
        .battlefield(battlefield_id)
    else {
        return true;
    };
    let corps_size = bf.rule_set.map_or(0, |r| r.corps_size);
    let minimum = if queue_count > 0 {
        1
    } else {
        u64::from(corps_size) * 2
    };
    // Temporary exception to Drill Camp for debugging purposes.
    if bf.id == game_type::DRILL_CAMP {
        return false;
    }
    let queued = st
        .queue
        .iter()
        .find(|(id, _)| *id == battlefield_id)
        .map_or(0, |(_, a)| a.len()) as u64;
    if queued < minimum {
        tracing::trace!(
            "[Matchmaking] {} player(s) are missing to allow matchmaking for battlefield {battlefield_id}",
            minimum - queued
        );
        return true;
    }
    false
}

/// `CheckMatchmakingQueue(bfId)` (under the manager lock).
fn check_matchmaking_queue(server: &GameServer, st: &mut ManagerState, battlefield_id: u32) {
    let games: Vec<Arc<InstantGame>> = st
        .queue_list
        .iter()
        .filter(|g| g.battlefield_id() == battlefield_id)
        .cloned()
        .collect();
    let Some(pos) = st.queue.iter().position(|(id, _)| *id == battlefield_id) else {
        return;
    };
    // "Remove offline players in the queue list": `CharObj` is never null, nothing is removed.
    tracing::trace!(
        "[Matchmaking] Running matchmaking for battlefield {battlefield_id}... Queue: {}",
        st.queue[pos].1.len()
    );
    if missing_players_to_start(server, st, battlefield_id, games.len()) {
        return;
    }
    let game = if let Some(g) = games.first() {
        tracing::trace!("[Matchmaking] Game found without full players.");
        g.clone()
    } else {
        let Some(bf) = server
            .game_data
            .systems
            .instances
            .battlefields
            .battlefield(battlefield_id)
            .cloned()
        else {
            return;
        };
        let Some(g) = create_game(server, &bf) else {
            return;
        };
        g
    };
    let queue_count = st.queue[pos].1.len();
    for _ in 0..queue_count {
        if game.is_full() {
            tracing::trace!("[Matchmaking] Game is full.");
            if let Some(i) = st.queue_list.iter().position(|g| Arc::ptr_eq(g, &game)) {
                st.queue_list.remove(i);
                tracing::trace!("[Matchmaking] Removing queued game from queueList.");
            }
            break;
        }
        let applicant = st.queue[pos].1.remove(0);
        match server.world.character_by_id(applicant.player.character_id) {
            Some(p) => {
                let c = game.get_corps();
                add_player(server, &game, &p, c);
                tracing::trace!(
                    "[Matchmaking] Adding player to game: {}",
                    p.data().unit.name
                );
            }
            None => {
                // playerCharacter is null.
            }
        }
    }
    if !st.games.iter().any(|g| Arc::ptr_eq(g, &game)) && games.is_empty() {
        tracing::trace!("[Matchmaking] Instant game created.");
        st.games.push(game.clone());
    }
    if game.is_full() {
        tracing::trace!("[Matchmaking] Game is full.");
        if let Some(i) = st.queue_list.iter().position(|g| Arc::ptr_eq(g, &game)) {
            st.queue_list.remove(i);
            tracing::trace!("[Matchmaking] Removing queued game from queueList.");
        }
    } else if battlefield_id != game_type::GLADIATOR
        && !st.queue_list.iter().any(|g| Arc::ptr_eq(g, &game))
    {
        tracing::trace!("[Matchmaking] Adding Drill Camp game to queue list as it is not full.");
        st.queue_list.push(game);
    }
}

/// `BattlefieldTick` (15 s): matchmaking for every queue.
pub fn battlefield_tick(server: &GameServer) {
    let mgr = &server.systems.instances.instant_games;
    let mut st = mgr.lock();
    let ids: Vec<u32> = st.queue.iter().map(|(id, _)| *id).collect();
    for id in ids {
        check_matchmaking_queue(server, &mut st, id);
    }
}

/// `PlayerCanEnter(character)` (not called by the C# server code).
pub fn player_can_enter(server: &GameServer, p: &PlayerRef) -> bool {
    // TODO (C#): Not in jury, not in duel, not in jail conditions
    let c = p.data();
    if c.unit.is_in_battle {
        return false; // In Combat
    }
    if c.unit.transform.instance_id != crate::models::character::DEFAULT_INSTANCE_ID {
        return false; // In an instanced world (Dungeon or Mirage)
    }
    if c.unit.hp == 0 {
        return false; // Is dead
    }
    if c.inventory
        .equipment_slots()
        .get(26)
        .is_some_and(Option::is_some)
    {
        return false; // Tradepack equipped
    }
    if c.unit.buffs.check_buff(2385) {
        return false; // Rebirth trauma is active
    }
    let instance = c.unit.transform.instance_id;
    drop(c);
    // Vehicle or boat is summoned
    let summoned = server.world.world(instance).is_some_and(|w| {
        crate::systems::vehicles::slaves::get_active_slave_by_owner_obj_id(&w, p.obj_id).is_some()
    });
    !summoned
}
