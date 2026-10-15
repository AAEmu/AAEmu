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
    SCAppliedToInstantGamePacket, SCCancelInstantGamePacket, SCInstantGameAddPointPacket,
    SCInstantGameEndPacket, SCInstantGameInviteTimeoutPacket, SCInstantGameJoinedPacket,
    SCInstantGameKillPacket, SCInstantGameKillstreakPacket, SCInstantGameStartPacket,
    SCInstantGameStatePacket, SCInviteToInstantGamePacket, SCSkillCooldownResetPacket,
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
/// `BattlefieldEndingReason.AchievementKillCount`.
pub const ENDING_ACHIEVEMENT_KILL_COUNT: u8 = 5;
/// `BattlefieldEndingReason.AchievementKillCorpsHead`.
pub const ENDING_ACHIEVEMENT_KILL_CORPS_HEAD: u8 = 7;

/// Bits of `InstantGameTeamMember.BonusSet` (the scoreboard medal icons, in the order the
/// 1.2 scoreboard Lua lists them: victory, bravery, destroyer, immortality).
pub mod bonus_set {
    // Bit order from the 1.2 client's bonus-condition mapper: 0 "winner", 1 "no_death",
    // 2 "top_enemy_pc_kill", 3 "top_enemy_non_pc_kill", 4 "loser".
    pub const VICTORY: u32 = 1 << 0;
    pub const NO_DEATH: u32 = 1 << 1;
    pub const TOP_PC_KILL: u32 = 1 << 2;
    pub const TOP_NON_PC_KILL: u32 = 1 << 3;
    pub const LOSER: u32 = 1 << 4;
}

/// `ui_texts` 336: "Failed the level requirements for this arena."
pub const ERROR_LEVEL_REQUIREMENTS: u16 = 336;

/// Seconds an invitation may go unanswered before it expires. No 1.2 value survives in any
/// source; 60 matches the C# "game expire after 60 seconds" TODO and the Gladiator rule
/// set's `time_unearned_win`.
pub const INVITE_TIMEOUT_SECS: u64 = 60;
/// `BattlefieldEndingReason.TimeoverCompareKillCount` (the only time-over reason the 1.2
/// battlefield Lua names: `BFER_TIMEOVER_COMPARE_KILL_COUNT`).
pub const ENDING_TIMEOVER_COMPARE_KILL_COUNT: u8 = 1;

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
    /// Non-PC kills (server-side only; the wire carries no such count, it only decides the
    /// `bonus_top_enemy_non_pc_kill` medal).
    pub npc_kills: u16,
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
    /// The result (bonuses, honor, rank points) has been awarded; a second `send_result`
    /// re-sends the packet but never pays twice.
    pub results_awarded: bool,
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
    // An unanswered invitation expires (previously the player stayed invited forever and
    // the game could never fill its roster).
    if let Some(s) = server.self_arc() {
        let g = game.clone();
        let pl = p.clone();
        tokio::spawn(async move {
            tokio::time::sleep(Duration::from_secs(INVITE_TIMEOUT_SECS)).await;
            expire_invite(&s, &g, &pl);
        });
    }
}

/// Drops a still-unanswered invitation: the timeout packet first, then the normal
/// withdrawal (which clears the queue UI and requeues the game).
fn expire_invite(server: &GameServer, game: &Arc<InstantGame>, p: &PlayerRef) {
    let mgr = &server.systems.instances.instant_games;
    match mgr.current(p) {
        Some(cur) if Arc::ptr_eq(&cur, game) => {}
        _ => return,
    }
    if !game.has_player(p) || game.st().member_index(p).is_some() {
        return;
    }
    let Some(assigned) = game.st().corps_of(p) else {
        return;
    };
    p.send(SCInstantGameInviteTimeoutPacket {
        zone_instance_id: game.zone_instance_id,
        ruleset_id: game.rule_set.id,
        corps: assigned,
    });
    // Mirror a decline without the Cancel clear: the 1.2 client's timeout handler returns
    // it to applied/queued (state 2), so the player goes straight back into the queue.
    withdraw_inner(server, p, false);
    apply_to_battlefield(server, game.battlefield.id, corps::ANY, p);
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
        withdraw_inner(server, p, false);
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
            npc_kills: 0,
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
///
/// Deviation: the C# timed path sent both corps as `Lose` with reason `AchievementScore`
/// even when nobody reached the victory score (every Drill Camp game, whose rule set has
/// `victory_score = 0`). The timed result now decides win / lose / draw by kill count
/// (score breaks a kill tie) with `TimeoverCompareKillCount`.
pub async fn end_game(server: &Arc<GameServer>, game: &Arc<InstantGame>) {
    finish_timeover(server, game);
    let ending = Duration::from_millis(
        u64::try_from(i64::from(game.rule_set.time_ending) * 60 * 1000).unwrap_or(0),
    );
    tokio::time::sleep(ending).await;
    destroy_game(server, game);
}

/// The timed end of a game: decide the winner by kill count (score breaks a kill tie,
/// equal score is a draw), then send the result.
pub fn finish_timeover(server: &GameServer, game: &Arc<InstantGame>) {
    {
        let mut st = game.st();
        if st.corps1.state != victory_state::WIN && st.corps2.state != victory_state::WIN {
            let k1 = st.result_kills(corps::CORPS1);
            let k2 = st.result_kills(corps::CORPS2);
            let s1 = st.result_score(corps::CORPS1);
            let s2 = st.result_score(corps::CORPS2);
            let (c1, c2) = if (k1, s1) > (k2, s2) {
                (victory_state::WIN, victory_state::LOSE)
            } else if (k2, s2) > (k1, s1) {
                (victory_state::LOSE, victory_state::WIN)
            } else {
                (victory_state::DRAW, victory_state::DRAW)
            };
            st.corps1.state = c1;
            st.corps2.state = c2;
        }
    }
    send_result(server, game, ENDING_TIMEOVER_COMPARE_KILL_COUNT);
}

/// One member's end-of-match award, computed under the game lock by [`award_results`] and
/// paid out by [`send_result`] after the lock is released.
struct MemberAward {
    player: PlayerRef,
    honor: i32,
    rank_points: i32,
    rank_wins: i32,
}

/// Fills every member's `bonus` / `bonus_set` from the rule set's `bonus_*` columns and
/// returns what each member is owed (honor + arena ranking). Runs once per game.
///
/// Bonuses (all C# versions left them at zero, so the scoreboard medal column was always
/// empty): the winning corps gets `bonus_winner`, the losing one `bonus_loser`, a member
/// with no deaths gets `bonus_no_death`, and the top PC / non-PC killers (ties share) get
/// `bonus_top_enemy_pc_kill` / `bonus_top_enemy_non_pc_kill`. The sum is the honor credited.
fn award_results(game: &InstantGame) -> Vec<MemberAward> {
    let mut st = game.st();
    if st.results_awarded {
        return Vec::new();
    }
    st.results_awarded = true;
    let r = &game.rule_set;
    let state1 = st.corps1.state;
    let state2 = st.corps2.state;
    let max_kills = st.members.iter().map(|m| m.kills).max().unwrap_or(0);
    let max_npc_kills = st.members.iter().map(|m| m.npc_kills).max().unwrap_or(0);
    let mut awards = Vec::with_capacity(st.members.len());
    for m in &mut st.members {
        let state = if m.result == corps::CORPS1 {
            state1
        } else {
            state2
        };
        let (mut bonus, mut set, rank_points, rank_wins) = match state {
            victory_state::WIN => (r.bonus_winner, bonus_set::VICTORY, r.rank_win_point, 1),
            victory_state::LOSE => (r.bonus_loser, bonus_set::LOSER, r.rank_lose_point, 0),
            victory_state::DRAW => (0, 0, r.rank_draw_point, 0),
            _ => (0, 0, r.rank_invalid_point, 0),
        };
        if m.deaths == 0 {
            bonus = bonus.wrapping_add(r.bonus_no_death);
            set |= bonus_set::NO_DEATH;
        }
        if max_kills > 0 && m.kills == max_kills {
            bonus = bonus.wrapping_add(r.bonus_top_enemy_pc_kill);
            set |= bonus_set::TOP_PC_KILL;
        }
        if max_npc_kills > 0 && m.npc_kills == max_npc_kills {
            bonus = bonus.wrapping_add(r.bonus_top_enemy_non_pc_kill);
            set |= bonus_set::TOP_NON_PC_KILL;
        }
        m.bonus = bonus;
        m.bonus_set = set;
        awards.push(MemberAward {
            player: m.player.clone(),
            honor: bonus,
            rank_points,
            rank_wins,
        });
    }
    awards
}

/// `SendResult()`: award the bonuses / honor / rank points, then broadcast the result.
pub fn send_result(server: &GameServer, game: &Arc<InstantGame>, reason: u8) {
    let awards = award_results(game);
    let packet = {
        let st = game.st();
        SCInstantGameEndPacket {
            zone_instance_id: game.zone_instance_id,
            reason,
            corps1: st.result_wire(corps::CORPS1),
            corps2: st.result_wire(corps::CORPS2),
        }
    };
    pay_awards(server, game, &awards);
    game.broadcast(&packet);
}

/// Credits the honor of every award and records the result on the arena ranking board
/// (`RankingState::record` with a [`RankEvent::Arena`](crate::systems::social::ranking_runtime::RankEvent::Arena);
/// the board's own zone-group check keeps non-Gladiator battlefields off it).
fn pay_awards(server: &GameServer, game: &InstantGame, awards: &[MemberAward]) {
    use crate::systems::skills::progress::{change_game_points, GAME_POINT_HONOR};
    use crate::systems::social::ranking_data::RankKind;
    use crate::systems::social::ranking_runtime::{RankEvent, RankHolder};

    let ranking = &server.game_data.systems.social.ranking;
    let board = ranking
        .supported_ranks()
        .find(|b| b.kind() == Some(RankKind::Arena));
    let zone_group_id = server
        .game_data
        .systems
        .instances
        .zone_order
        .zones
        .iter()
        .find(|(key, _)| *key == game.battlefield.zone_key)
        .map_or(0, |(_, group)| *group);
    let now = chrono::Utc::now();
    for award in awards {
        if award.honor != 0 {
            change_game_points(server, &award.player, GAME_POINT_HONOR, award.honor);
        }
        if let Some(board) = board {
            if award.rank_points != 0 || award.rank_wins != 0 {
                let mut c = award.player.data();
                // The game server carries no account login name; the character name is
                // the fallback the client's ranking row uses for want of it.
                let holder = RankHolder {
                    account_id: c.account_id,
                    account: c.unit.name.clone(),
                    character_id: c.id,
                    world_id: c.server_id as u8,
                };
                c.ranking_state.record(
                    board,
                    &holder,
                    RankEvent::Arena {
                        points: award.rank_points,
                        wins: award.rank_wins,
                    },
                    zone_group_id,
                    now,
                );
            }
        }
    }
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
    // Non-PC victim: count it for the top-non-PC-kill bonus, and killing this corps' target
    // Polestar (`victory_kill_corps*_head`) wins the game outright.
    if let Some(npc) = victim.as_npc().cloned() {
        let template_id = npc.data().unit.template_id;
        let won = {
            let mut st = game.st();
            let Some(ki) = st.member_index(killer) else {
                return;
            };
            st.members[ki].npc_kills = st.members[ki].npc_kills.wrapping_add(1);
            let Some(kc) = st.corps_of(killer) else {
                return;
            };
            let target = game.rule_set.corps_head_target(kc);
            if target != 0 && template_id == target {
                if kc == corps::CORPS1 {
                    st.corps1.state = victory_state::WIN;
                } else {
                    st.corps2.state = victory_state::WIN;
                }
                true
            } else {
                false
            }
        };
        if won {
            finish_game(server, game, ENDING_ACHIEVEMENT_KILL_CORPS_HEAD);
        }
        return;
    }
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

/// `AddScore(corps, scorer, score)`. Returns `true` when the corps reached a victory
/// condition (the game ends).
///
/// Deviations from the C#, all client-visible on 1.2: the point packet named `Corps1` with
/// `score2 = 0` whoever scored (the mini scoreboard could never show corps 2's score);
/// `result.Score >= VictoryScore` with the Drill Camp rule set's `victory_score = 0` ended
/// the game on the first kill; and `victory_kill_count` was never checked. The packet now
/// names the scoring corps with both corps' totals, a zero victory score / kill count
/// disables that condition, and the kill-count condition ends with
/// `AchievementKillCount`.
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
    let (packet, reason) = {
        let mut st = game.st();
        st.members[scorer].score = st.members[scorer].score.wrapping_add(score);
        let total = st.result_score(which);
        let kills = st.result_kills(which);
        let name = st.members[scorer].player.data().unit.name.clone();
        let reason = if game.rule_set.victory_score > 0 && total >= game.rule_set.victory_score {
            Some(ENDING_ACHIEVEMENT_SCORE)
        } else if game.rule_set.victory_kill_count > 0
            && u32::try_from(kills).unwrap_or(0) >= game.rule_set.victory_kill_count
        {
            Some(ENDING_ACHIEVEMENT_KILL_COUNT)
        } else {
            None
        };
        if reason.is_some() {
            if which == corps::CORPS1 {
                st.corps1.state = victory_state::WIN;
            } else {
                st.corps2.state = victory_state::WIN;
            }
        }
        let score1 = st.result_score(corps::CORPS1);
        let score2 = st.result_score(corps::CORPS2);
        (
            SCInstantGameAddPointPacket {
                zone_instance_id: game.zone_instance_id,
                corps: which,
                points: score,
                score1,
                score2,
                char_name: name,
            },
            reason,
        )
    };
    game.broadcast(&packet);
    if let Some(reason) = reason {
        finish_game(server, game, reason);
    }
    reason.is_some()
}

/// Ends a game right now with `reason`: cancels the timed end, sends the result (which pays
/// the awards) and schedules the teardown after `TimeEnding`.
fn finish_game(server: &GameServer, game: &Arc<InstantGame>, reason: u8) {
    if let Some(t) = game.end_task.lock().expect("end task poisoned").take() {
        t.abort();
    }
    // `EndGame()` runs synchronously up to its first await: the result is sent now.
    send_result(server, game, reason);
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

// ---------------------------------------------------------------------------------------------
// InstantGameManager
// ---------------------------------------------------------------------------------------------

/// `MatchmakingApplicant`.
#[derive(Debug, Clone)]
pub struct Applicant {
    pub time_applied_ms: i64,
    pub player: PlayerRef,
    /// The corps requested on apply (`InstantCorps` byte) — the State packet counts the
    /// queue by this preference (corps 1 / corps 2 / any).
    pub corps: u8,
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
    // `level_min` gate (never enforced by any C# version, so a level-1 character could queue
    // for the level-50 Gladiator Arena): refuse with the client's own message and no queue
    // entry.
    if let Some(rs) = server
        .game_data
        .systems
        .instances
        .battlefields
        .battlefield(battlefield_id)
        .and_then(|bf| bf.rule_set)
    {
        if rs.level_min > 0 && u32::from(p.data().unit.level) < rs.level_min {
            tracing::trace!(
                "[Matchmaking] {} is below level {} for battlefield {battlefield_id}",
                p.data().unit.name,
                rs.level_min
            );
            p.send(SCAppliedToInstantGamePacket {
                battlefield_id,
                corps,
                error_message_id: ERROR_LEVEL_REQUIREMENTS,
            });
            return;
        }
    }
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
            corps,
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
    broadcast_battlefield_state(server, battlefield_id);
}

/// Sends `SCInstantGameStatePacket` to everyone queued for this battlefield: the per-corps
/// size plus how many applicants requested corps 1 / corps 2 / any. The client only applies
/// it while applied (state 2) for the same battlefield and refreshes its joined counts.
pub fn broadcast_battlefield_state(server: &GameServer, battlefield_id: u32) {
    let Some(rs) = server
        .game_data
        .systems
        .instances
        .battlefields
        .battlefield(battlefield_id)
        .and_then(|bf| bf.rule_set)
    else {
        return;
    };
    let mgr = &server.systems.instances.instant_games;
    let (mut recipients, mut counts) = (Vec::new(), [0u16; 3]);
    {
        let st = mgr.lock();
        if let Some((_, applicants)) = st.queue.iter().find(|(id, _)| *id == battlefield_id) {
            for a in applicants {
                let i = match a.corps {
                    corps::CORPS1 => 0,
                    corps::CORPS2 => 1,
                    _ => 2,
                };
                counts[i] = counts[i].saturating_add(1);
                recipients.push(a.player.clone());
            }
        }
    }
    let packet = SCInstantGameStatePacket {
        battlefield_id,
        corps_size: u8::try_from(rs.corps_size).unwrap_or(u8::MAX),
        corps1_count: counts[0],
        corps2_count: counts[1],
        any_count: counts[2],
    };
    let mut seen: Vec<u32> = Vec::new();
    for p in recipients {
        if seen.contains(&p.character_id) {
            continue;
        }
        seen.push(p.character_id);
        p.send(packet);
    }
}

/// `WithdrawFromBattlefield(character)`: leaves the queue, or declines an invitation (the game
/// goes back to the queue list).
pub fn withdraw_from_battlefield(server: &GameServer, p: &PlayerRef) {
    withdraw_inner(server, p, true);
}

/// As above; with `notify_cancel = false` for decline/invite-timeout, where the player is
/// re-queued right away: retail sent no Cancel there, and the 1.2 client's cancel handler
/// would reset it to not-applied (state 0).
fn withdraw_inner(server: &GameServer, p: &PlayerRef, notify_cancel: bool) {
    let mgr = &server.systems.instances.instant_games;
    let mut st = mgr.lock();
    for (battlefield_id, applicants) in st.queue.iter_mut() {
        if let Some(i) = applicants.iter().position(|a| Arc::ptr_eq(&a.player, p)) {
            applicants.remove(i);
            tracing::trace!(
                "[Matchmaking] Removing {} from matchmaking.",
                p.data().unit.name
            );
            // The entrance UI waits for `INSTANT_GAME_JOIN_CANCEL`; without this packet it
            // stayed in the applied/waiting state after a cancel.
            let battlefield_id = *battlefield_id;
            drop(st);
            if notify_cancel {
                p.send(SCCancelInstantGamePacket::clear_queue());
            }
            broadcast_battlefield_state(server, battlefield_id);
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
            let battlefield_id = game.battlefield_id();
            if !st.queue_list.iter().any(|g| Arc::ptr_eq(g, &game)) {
                st.queue_list.push(game);
                tracing::trace!("[Matchmaking] Adding game to queue list.");
            }
            drop(st);
            if notify_cancel {
                p.send(SCCancelInstantGamePacket::clear_queue());
            }
            broadcast_battlefield_state(server, battlefield_id);
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
