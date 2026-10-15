//! `Core/Managers/DuelManager` + `Models/Game/Duels` + `Models/Tasks/Duels`: duel requests,
//! acceptance (combat flag, temporary Red / Blue team factions), the start sequence, the
//! 5-minute end timer, the 1 s distance and HP checks and the end of a duel.
//!
//! Tasks: every C# `TaskManager.Schedule` becomes a [`DuelTimer`]: a tokio task that sleeps and
//! then runs the task's `Execute` in a *separate* detached task, so `Cancel()` (abort) only
//! prevents a task that has not fired yet, like the C# (cancelling the running task from inside
//! its own `Execute` is harmless). A replaced timer that is not cancelled keeps running, as a
//! replaced C# task does. The step functions (`duel_start`, `duel_stop`, `duel_result_check`,
//! `duel_distance_check`, the `*_task_execute` functions) are public so tests can drive them.
//!
//! Locking: the manager mutex and a duel's state mutex (always in that order) are never held
//! across an `.await`, while locking a player's data, or while calling the world / other
//! managers.

use std::collections::HashMap;
use std::future::Future;
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::Duration;

use tokio::task::JoinHandle;

use crate::packets::world_objects::SCDoodadRemovedPacket;
use crate::server::GameServer;
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::systems::social::faction;
use crate::systems::social::packets::duel::*;
use crate::systems::social::util::{self, send_error};
use crate::world::{DoodadRef, PlayerRef, WorldObject};

/// Combat flag doodad template (`UnitId = 5014`).
pub const COMBAT_FLAG_TEMPLATE_ID: u32 = 5014;
/// `FactionsEnum.RedTeam` (the challenger's duel faction).
pub const FACTION_RED_TEAM: u32 = 159;
/// `FactionsEnum.BlueTeam` (the challenged character's duel faction).
pub const FACTION_BLUE_TEAM: u32 = 160;
/// `Delay` (1 s) of the distance and HP checks.
pub const CHECK_DELAY: Duration = Duration::from_millis(1000);
/// `DistanceForSurrender` (75 m from the flag, 3D).
pub const DISTANCE_FOR_SURRENDER: f32 = 75.0;
/// `DuelDurationTime` (5 min).
pub const DUEL_DURATION: Duration = Duration::from_secs(5 * 60);
/// `DuelStartTask` delay (3 s).
pub const DUEL_START_DELAY: Duration = Duration::from_secs(3);
/// `SCAreaChatBubblePacket` type of the duel start.
pub const DUEL_AREA_BUBBLE_TYPE: u32 = 543;

/// `DuelDetType` (the `det` of `SCDuelEndedPacket`).
pub mod duel_det_type {
    pub const LOSE: u8 = 0;
    pub const WIN: u8 = 1;
    /// Fled beyond the flag border.
    pub const SURRENDER: u8 = 2;
    pub const DRAW: u8 = 3;
}

/// `VictoryState` (unused by the 1.2 C#).
pub mod victory_state {
    pub const INVALID: u8 = 0;
    pub const LOSE: u8 = 1;
    pub const WIN: u8 = 2;
    pub const DRAW: u8 = 3;
}

/// `DuelDistance`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(i8)]
pub enum DuelDistance {
    Error = -1,
    Near = 0,
    ChallengerFar = 1,
    ChallengedFar = 2,
}

/// A scheduled duel task (`TaskManager.Schedule`); `cancel` = `Task.Cancel()`.
#[derive(Debug)]
pub struct DuelTimer {
    handle: Option<JoinHandle<()>>,
}

impl DuelTimer {
    /// `Task.Cancel()`: the task does not fire if it has not yet (a running `Execute` is not
    /// interrupted).
    pub fn cancel(self) {
        if let Some(h) = self.handle {
            h.abort();
        }
    }
}

/// `TaskManager.Schedule(task, delay)`: runs `f` after `delay` in its own task. Without the
/// shared server handle (`GameServer::set_self` was not called) the timer never fires.
fn schedule<F, Fut>(server: &GameServer, delay: Duration, f: F) -> DuelTimer
where
    F: FnOnce(Arc<GameServer>) -> Fut + Send + 'static,
    Fut: Future<Output = ()> + Send + 'static,
{
    let Some(arc) = server.self_arc() else {
        tracing::warn!("DuelManager: no shared server handle, the duel task never fires");
        return DuelTimer { handle: None };
    };
    let weak = Arc::downgrade(&arc);
    drop(arc);
    let handle = tokio::spawn(async move {
        tokio::time::sleep(delay).await;
        if let Some(server) = weak.upgrade() {
            // Detached: cancelling the timer from the task's own Execute must not abort it.
            tokio::spawn(f(server));
        }
    });
    DuelTimer {
        handle: Some(handle),
    }
}

/// The mutable members of a `Duel`.
#[derive(Debug, Default)]
pub struct DuelState {
    /// `DuelFlag`.
    pub flag: Option<DoodadRef>,
    /// Center of the distance check when no flag could be spawned (see
    /// `SocialHooks::spawn_doodad`): the challenger's position at the acceptance.
    pub fallback_center: (f32, f32, f32),
    pub start_task: Option<DuelTimer>,
    pub end_timer_task: Option<DuelTimer>,
    pub distance_check_task: Option<DuelTimer>,
    pub result_check_task: Option<DuelTimer>,
    /// `DuelStarted`.
    pub started: bool,
    /// `DuelAllowed` (only ever set to false).
    pub allowed: bool,
}

/// `Duel`.
#[derive(Debug)]
pub struct Duel {
    /// The character who sent the challenge.
    pub challenger: PlayerRef,
    /// The challenged character.
    pub challenged: PlayerRef,
    state: Mutex<DuelState>,
}

pub type DuelRef = Arc<Duel>;

impl Duel {
    pub fn new(challenger: PlayerRef, challenged: PlayerRef) -> Self {
        Self {
            challenger,
            challenged,
            state: Mutex::new(DuelState::default()),
        }
    }

    /// The duel's mutable members (leaf lock).
    pub fn state(&self) -> MutexGuard<'_, DuelState> {
        self.state.lock().expect("duel poisoned")
    }

    /// `SendPacketsBoth`.
    pub fn send_both<P>(&self, packet: P)
    where
        P: crate::net::packet::ServerPacket + Clone + 'static,
    {
        self.challenger.send(packet.clone());
        self.challenged.send(packet);
    }

    /// `Challenger.Hp`, `Challenged.Hp` (one lock at a time).
    fn hps(&self) -> (i32, i32) {
        let a = self.challenger.data().unit.hp;
        let b = self.challenged.data().unit.hp;
        (a, b)
    }
}

/// A snapshot of a duel for other subsystems.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DuelInfo {
    pub challenger_id: u32,
    pub challenged_id: u32,
    pub challenger_obj_id: u32,
    pub challenged_obj_id: u32,
    /// The combat flag's obj id (0 = none).
    pub flag_obj_id: u32,
    pub started: bool,
}

#[derive(Debug, Default)]
struct State {
    /// `_duels` (both participants' character ids).
    duels: HashMap<u32, DuelRef>,
    /// `SaveFactions` (character id -> faction before the duel).
    save_factions: HashMap<u32, u32>,
    /// `_combatFlag.Last`: the flag of the most recently accepted duel (the C# keeps a single
    /// spawner field for every duel).
    last_flag: Option<DoodadRef>,
}

/// The task delays of the duel manager (the C# constants; tests shorten / lengthen them).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DuelDelays {
    /// `DuelStartTask` (3 s).
    pub start: Duration,
    /// `Delay` of the distance / HP check tasks (1 s).
    pub check: Duration,
    /// `DuelEndTimerTask` (`DuelDurationTime`, 5 min).
    pub duration: Duration,
}

impl Default for DuelDelays {
    fn default() -> Self {
        Self {
            start: DUEL_START_DELAY,
            check: CHECK_DELAY,
            duration: DUEL_DURATION,
        }
    }
}

/// `DuelManager`.
#[derive(Debug, Default)]
pub struct DuelManager {
    st: Mutex<State>,
    delays: Mutex<DuelDelays>,
}

impl DuelManager {
    fn st(&self) -> MutexGuard<'_, State> {
        self.st.lock().expect("duel manager poisoned")
    }

    /// The task delays.
    pub fn delays(&self) -> DuelDelays {
        *self.delays.lock().expect("duel delays poisoned")
    }

    /// Replaces the task delays (tests).
    pub fn set_delays(&self, delays: DuelDelays) {
        *self.delays.lock().expect("duel delays poisoned") = delays;
    }

    /// `_duels[id]`.
    pub fn get(&self, character_id: u32) -> Option<DuelRef> {
        self.st().duels.get(&character_id).cloned()
    }

    /// The duel a character takes part in (`_duels.TryGetValue(id)`), as a snapshot.
    pub fn duel_of(&self, character_id: u32) -> Option<DuelInfo> {
        let duel = self.get(character_id)?;
        let st = duel.state();
        Some(DuelInfo {
            challenger_id: duel.challenger.character_id,
            challenged_id: duel.challenged.character_id,
            challenger_obj_id: duel.challenger.obj_id,
            challenged_obj_id: duel.challenged.obj_id,
            flag_obj_id: st.flag.as_ref().map_or(0, |f| f.obj_id),
            started: st.started,
        })
    }

    /// Whether characters `a` and `b` are the two sides of the same duel.
    pub fn is_in_duel_with(&self, a: u32, b: u32) -> bool {
        self.duel_of(a).is_some_and(|d| {
            (d.challenger_id == a && d.challenged_id == b)
                || (d.challenger_id == b && d.challenged_id == a)
        })
    }

    /// `SaveFactions[id]`.
    pub fn saved_faction(&self, character_id: u32) -> Option<u32> {
        self.st().save_factions.get(&character_id).copied()
    }

    /// Number of `_duels` keys.
    pub fn len(&self) -> usize {
        self.st().duels.len()
    }

    pub fn is_empty(&self) -> bool {
        self.st().duels.is_empty()
    }

    /// `DuelAdd`: each participant is keyed only when it has no duel yet.
    fn duel_add(&self, duel: &DuelRef) {
        let mut st = self.st();
        st.duels
            .entry(duel.challenger.character_id)
            .or_insert_with(|| duel.clone());
        st.duels
            .entry(duel.challenged.character_id)
            .or_insert_with(|| duel.clone());
    }

    /// `DuelRemove`: removes both participants' keys (whatever duel they point to).
    fn duel_remove(&self, duel: &DuelRef) {
        let mut st = self.st();
        st.duels.remove(&duel.challenger.character_id);
        st.duels.remove(&duel.challenged.character_id);
    }

    /// `DuelRequest(challenger, challengedId)`. The C# does not check that the challenged
    /// character is online (NullReferenceException); here that is logged and ignored.
    pub fn duel_request(&self, server: &GameServer, challenger: &PlayerRef, challenged_id: u32) {
        let Some(challenged) = server.world.character_by_id(challenged_id) else {
            tracing::warn!(
                "DuelRequest: challenger={}:{}, challenged={challenged_id} not found",
                challenger.character_id,
                challenger.obj_id
            );
            return;
        };
        let duel = Arc::new(Duel::new(challenger.clone(), challenged.clone()));
        self.duel_add(&duel);
        challenged.send(SCDuelChallengedPacket {
            challenger_id: challenger.character_id,
        });
        tracing::warn!(
            "DuelRequest: challenger={}:{}, challenged={challenged_id}:{}:{}",
            challenger.character_id,
            challenger.obj_id,
            challenged.character_id,
            challenged.obj_id
        );
    }

    /// `DuelAccepted(challenged, challengerId)`: spawns the combat flag between both
    /// characters, switches them to the Red / Blue team factions and schedules the start.
    pub async fn duel_accepted(
        &self,
        server: &GameServer,
        challenged: &PlayerRef,
        challenger_id: u32,
    ) {
        let Some(duel) = self.get(challenger_id) else {
            tracing::warn!("DuelAccepted: Id = {challenger_id} not found in duels[]");
            return;
        };
        {
            let mut st = duel.state();
            if st.started {
                drop(st);
                tracing::warn!(
                    "DuelAccepted: Duel with challengerId = {challenger_id} is already started"
                );
                return;
            }
            st.started = true;
        }
        duel.challenger.data().unit.is_in_duel = true;
        duel.challenged.data().unit.is_in_duel = true;

        // Spawn the flag (`challenged.ParentWorld`, the challenger's spawn position with X / Y
        // in the middle of both characters and Z from the height map).
        let instance_id = challenged.data().unit.transform.instance_id;
        let a = world_pos_rot(server, &duel.challenger);
        let b = world_pos_rot(server, &duel.challenged);
        let x = a.x - (a.x - b.x) / 2.0;
        let y = a.y - (a.y - b.y) / 2.0;
        let hooks = server.systems.social.hooks.get();
        // Without a height (no heightmaps) the challenger's Z is kept.
        let z = hooks
            .terrain_height(server, instance_id, x, y)
            .unwrap_or(a.z);
        let flag = hooks.spawn_doodad(
            server,
            instance_id,
            COMBAT_FLAG_TEMPLATE_ID,
            (x, y, z),
            a.rot_z,
        );
        self.st().last_flag = flag.clone();
        {
            let mut st = duel.state();
            st.flag = flag;
            st.fallback_center = (a.x, a.y, a.z);
        }

        // Change the factions temporarily (TODO in C#: pets / vehicles).
        self.set_faction(server, &duel.challenger, FACTION_RED_TEAM)
            .await;
        self.set_faction(server, &duel.challenged, FACTION_BLUE_TEAM)
            .await;

        let start_id = duel.challenger.character_id;
        let timer = schedule(server, self.delays().start, move |srv| async move {
            srv.systems.social.duels.duel_start(&srv, start_id);
        });
        duel.state().start_task = Some(timer);
    }

    /// `SetFaction(owner, factionId)`: remembers the current faction, then
    /// `Character.SetFaction`.
    async fn set_faction(&self, server: &GameServer, owner: &PlayerRef, faction_id: u32) {
        let current = owner.data().unit.faction_id;
        self.st().save_factions.insert(owner.character_id, current);
        faction::set_faction(server, owner, faction_id).await;
    }

    /// `RestoreFaction(owner)`. `false` = no saved faction (the C# throws
    /// `KeyNotFoundException`, which aborts `DuelStop`).
    async fn restore_faction(&self, server: &GameServer, owner: &PlayerRef) -> bool {
        let Some(saved) = self.saved_faction(owner.character_id) else {
            return false;
        };
        faction::set_faction(server, owner, saved).await;
        self.st().save_factions.remove(&owner.character_id);
        true
    }

    /// `DuelStart(id)` (`DuelStartTask`): the start packets, the end timer and the first
    /// distance / HP checks.
    pub fn duel_start(&self, server: &GameServer, id: u32) {
        let Some(duel) = self.get(id) else {
            tracing::warn!("DuelStart: Id = {id} not found in duels[]");
            return;
        };
        let a = duel.challenger.obj_id;
        let b = duel.challenged.obj_id;
        // Deviation: without a flag (no doodad subsystem) the flag obj id is 0 (the C# would
        // throw here).
        let flag_obj_id = duel.state().flag.as_ref().map_or(0, |f| f.obj_id);
        duel.send_both(SCDuelStartedPacket {
            challenger_obj_id: a,
            challenged_obj_id: b,
        });
        duel.send_both(SCAreaChatBubblePacket {
            enter: true,
            unit_obj_id: a,
            bubble_type: DUEL_AREA_BUBBLE_TYPE,
        });
        duel.send_both(SCDuelStartCountdownPacket {});
        duel.send_both(SCDuelStatePacket {
            challenger_obj_id: a,
            flag_obj_id,
        });
        duel.send_both(SCDuelStatePacket {
            challenger_obj_id: b,
            flag_obj_id,
        });
        // Make the flag flutter in the wind (`_combatFlag.Last`: the last accepted duel's flag).
        let last_flag = self.st().last_flag.clone();
        if let Some(f) = last_flag {
            let packet = {
                let d = f.data();
                SCDoodadPhaseChangedPacket::new(f.obj_id, &d, util::now_ms())
            };
            duel.challenger.send(packet);
        }
        // The players can be attacked.
        duel.send_both(SCCombatEngagedPacket { obj_id: a });
        duel.send_both(SCCombatEngagedPacket { obj_id: b });

        let challenger_id = duel.challenger.character_id;
        let task_duel = duel.clone();
        let timer = schedule(server, self.delays().duration, move |srv| async move {
            srv.systems
                .social
                .duels
                .end_timer_task_execute(&srv, &task_duel, challenger_id)
                .await;
        });
        duel.state().end_timer_task = Some(timer);

        // The return values are ignored (a duel that starts out of range never ends by
        // distance, as in C#).
        let _ = self.duel_distance_check(server, challenger_id);
        let _ = self.duel_result_check(server, challenger_id);
    }

    /// `DuelCancel(challengerId, errorMessage)`.
    pub fn duel_cancel(&self, _server: &GameServer, challenger_id: u32, error_message: i16) {
        let Some(duel) = self.get(challenger_id) else {
            tracing::warn!("DuelCancel: Id={challenger_id} not found in duels[]");
            return;
        };
        duel.state().allowed = false;
        if error_message != 0 {
            send_error(&duel.challenger, error_message);
        }
        tracing::warn!(
            "DuelCancel: Duel with challengerId={challenger_id} canceled, error={error_message}"
        );
        self.duel_clean_up(challenger_id);
    }

    /// `DuelCleanUp(id)`: clears `IsInDuel`, cancels the start / end tasks (not the check
    /// tasks, as in C#) and removes the duel.
    pub fn duel_clean_up(&self, id: u32) {
        let Some(duel) = self.get(id) else {
            tracing::warn!("CleanUpDuel: Id={id} not found in duels[]");
            return;
        };
        duel.challenger.data().unit.is_in_duel = false;
        duel.challenged.data().unit.is_in_duel = false;
        let (start, end) = {
            let mut st = duel.state();
            (st.start_task.take(), st.end_timer_task.take())
        };
        if let Some(t) = start {
            t.cancel();
        }
        if let Some(t) = end {
            t.cancel();
        }
        self.duel_remove(&duel);
    }

    /// `DuelStop(id, det, loseId)`: the result packets, flag removal, faction restore.
    pub async fn duel_stop(&self, server: &GameServer, id: u32, det: u8, lose_id: u32) {
        tracing::warn!("DuelStop: Duel ended");
        let Some(duel) = self.get(id) else {
            tracing::warn!("DuelStop: Id={id} not found in duels[]");
            return;
        };
        duel.state().allowed = false;
        let (a_id, b_id) = (duel.challenger.character_id, duel.challenged.character_id);
        let (a_obj, b_obj) = (duel.challenger.obj_id, duel.challenged.obj_id);
        let ended = |w_id, l_id, w_obj, l_obj| SCDuelEndedPacket {
            challenger_id: w_id,
            challenged_id: l_id,
            challenger_obj_id: w_obj,
            challenged_obj_id: l_obj,
            det,
        };
        if det == duel_det_type::DRAW {
            duel.challenged.send(ended(a_id, b_id, a_obj, b_obj));
            duel.challenger.send(ended(b_id, a_id, b_obj, a_obj));
            tracing::warn!("DuelStop: Draw!");
        } else if lose_id != 0 {
            if lose_id == a_id {
                duel.send_both(ended(b_id, a_id, b_obj, a_obj));
                tracing::warn!("DuelStop: Challenger:{a_id} Lose, Challenged:{b_id} Win!");
                report_duel_win(server, &duel.challenged);
            } else if lose_id == b_id {
                duel.send_both(ended(a_id, b_id, a_obj, b_obj));
                tracing::warn!("DuelStop: Challenger:{a_id} Win, Challenged:{b_id} Lose!");
                report_duel_win(server, &duel.challenger);
            }
        }
        // Duel state: ended.
        duel.send_both(SCDuelStatePacket {
            challenger_obj_id: b_obj,
            flag_obj_id: 0,
        });
        duel.send_both(SCDuelStatePacket {
            challenger_obj_id: a_obj,
            flag_obj_id: 0,
        });

        let flag = duel.state().flag.clone();
        if let Some(f) = flag {
            // `DuelFlag.Delete()` (the flag stays referenced by the duel, as in C#).
            let obj = WorldObject::from(&f);
            if let Some(w) = server.world.world_of(&obj) {
                w.delete(&obj);
            }
            duel.send_both(SCDoodadRemovedPacket { obj_id: f.obj_id });
        }

        // Restore the factions (a missing entry aborts the rest, like the C# exception).
        if !self.restore_faction(server, &duel.challenger).await
            || !self.restore_faction(server, &duel.challenged).await
        {
            tracing::warn!("DuelStop: Id={id} has no saved faction");
            return;
        }

        // The players cannot be attacked any more.
        duel.challenger.data().unit.is_in_battle = false;
        duel.challenged.data().unit.is_in_battle = false;

        self.duel_clean_up(id);
    }

    /// `DuelResultСheck(id)`: `true` when a side is at 1 HP (and the running check task was
    /// cancelled), otherwise schedules the next check. Mirrors the C# quirk: a side already at
    /// <= 1 HP on the first check (no check task yet) throws and returns `false`.
    pub fn duel_result_check(&self, server: &GameServer, id: u32) -> bool {
        let Some(duel) = self.get(id) else {
            tracing::warn!("DuelResultСheck: Id={id} not found in duels[]");
            return false;
        };
        let (a_hp, b_hp) = duel.hps();
        if a_hp <= 1 || b_hp <= 1 {
            let task = duel.state().result_check_task.take();
            return match task {
                Some(t) => {
                    t.cancel();
                    true
                }
                None => {
                    tracing::warn!("DuelResultСheck: Id={id} has no result check task");
                    false
                }
            };
        }
        let task_duel = duel.clone();
        let timer = schedule(server, self.delays().check, move |srv| async move {
            srv.systems
                .social
                .duels
                .result_check_task_execute(&srv, &task_duel)
                .await;
        });
        duel.state().result_check_task = Some(timer);
        false
    }

    /// `DuelDistanceСheck(id)`: whether a side ran 75 m or more away from the flag (3D);
    /// otherwise schedules the next check.
    pub fn duel_distance_check(&self, server: &GameServer, id: u32) -> DuelDistance {
        let Some(duel) = self.get(id) else {
            tracing::warn!("DistanceСheck: Id={id} not found in duels[]");
            return DuelDistance::Error;
        };
        let (flag, fallback) = {
            let st = duel.state();
            (st.flag.clone(), st.fallback_center)
        };
        let center = match flag {
            Some(f) => {
                let obj = WorldObject::from(&f);
                match server.world.world_of(&obj) {
                    Some(w) => {
                        let p = w.world_pos_rot(&obj);
                        (p.x, p.y, p.z)
                    }
                    None => {
                        let t = obj.transform();
                        (t.x, t.y, t.z)
                    }
                }
            }
            None => fallback,
        };
        for (p, far) in [
            (&duel.challenger, DuelDistance::ChallengerFar),
            (&duel.challenged, DuelDistance::ChallengedFar),
        ] {
            let pos = util::world_position(server, p);
            if util::distance(center, pos, true) >= DISTANCE_FOR_SURRENDER {
                let task = duel.state().distance_check_task.take();
                if let Some(t) = task {
                    t.cancel();
                }
                return far;
            }
        }
        let task_duel = duel.clone();
        let timer = schedule(server, self.delays().check, move |srv| async move {
            srv.systems
                .social
                .duels
                .distance_check_task_execute(&srv, &task_duel)
                .await;
        });
        duel.state().distance_check_task = Some(timer);
        DuelDistance::Near
    }

    /// `DuelDistanceСheckTask.Execute`.
    pub async fn distance_check_task_execute(&self, server: &GameServer, duel: &DuelRef) {
        if duel.state().distance_check_task.is_none() {
            return;
        }
        let a = duel.challenger.character_id;
        let b = duel.challenged.character_id;
        let res = self.duel_distance_check(server, a);
        match res {
            DuelDistance::ChallengerFar => {
                self.duel_stop(server, b, duel_det_type::SURRENDER, a).await;
            }
            DuelDistance::ChallengedFar => {
                self.duel_stop(server, a, duel_det_type::SURRENDER, b).await;
            }
            DuelDistance::Error | DuelDistance::Near => {}
        }
    }

    /// `DuelResultСheckTask.Execute` (the draw branch is unreachable, as in C#).
    pub async fn result_check_task_execute(&self, server: &GameServer, duel: &DuelRef) {
        if duel.state().result_check_task.is_none() {
            return;
        }
        let a = duel.challenger.character_id;
        let b = duel.challenged.character_id;
        if !self.duel_result_check(server, a) {
            return;
        }
        let (a_hp, b_hp) = duel.hps();
        #[allow(clippy::nonminimal_bool, clippy::if_same_then_else)]
        if a_hp <= 1 {
            self.duel_stop(server, b, duel_det_type::WIN, a).await;
        } else if b_hp <= 1 {
            self.duel_stop(server, a, duel_det_type::WIN, b).await;
        } else if a_hp <= 1 && b_hp <= 1 {
            self.duel_stop(server, a, duel_det_type::DRAW, b).await;
        }
    }

    /// `DuelEndTimerTask.Execute`: the side with more HP wins, equal HP is a draw.
    pub async fn end_timer_task_execute(
        &self,
        server: &GameServer,
        duel: &DuelRef,
        challenger_id: u32,
    ) {
        let Some(task) = duel.state().end_timer_task.take() else {
            return;
        };
        task.cancel();
        let (a_hp, b_hp) = duel.hps();
        let b = duel.challenged.character_id;
        if a_hp < b_hp {
            self.duel_stop(server, b, duel_det_type::WIN, challenger_id)
                .await;
        } else if a_hp > b_hp {
            self.duel_stop(server, challenger_id, duel_det_type::WIN, b)
                .await;
        } else {
            self.duel_stop(server, challenger_id, duel_det_type::DRAW, b)
                .await;
        }
    }
}

/// `Transform.World` of a player.
fn world_pos_rot(
    server: &GameServer,
    p: &PlayerRef,
) -> crate::models::transform::PositionAndRotation {
    let t = p.data().unit.transform;
    if t.parent_obj_id != 0 {
        if let Some(w) = server.world.world(t.instance_id) {
            return w.world_pos_rot(&WorldObject::from(p));
        }
    }
    t.local()
}

/// WIN_DUEL achievement credit for the winner of a decided duel (a draw credits nothing).
fn report_duel_win(server: &GameServer, winner: &PlayerRef) {
    crate::systems::report_achievement(
        server,
        winner,
        AchievementInput::Increment {
            kind_id: char_record_kind::WIN_DUEL,
            amount: 1,
            matcher: RecordMatch::Any,
        },
    );
}

#[cfg(test)]
#[path = "duel_tests.rs"]
mod tests;
