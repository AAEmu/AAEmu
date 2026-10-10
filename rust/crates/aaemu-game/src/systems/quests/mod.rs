//! Phase-3 subsystem: quests.
//!
//! C# scope: `QuestManager` (+ `QuestManagerEvents`), `Models/Game/Quests/**` (templates, all
//! act types, the quest state machine), `CharacterQuests`, `SphereQuestManager` /
//! `SphereQuest`, the quest parts of `UnitRequirementsGameData` / `SphereGameData`, the quest
//! tasks (`QuestTimeoutTask`, `QuestDailyResetTask`, `QuestManagerRunQueueTask`) and the quest
//! packets.
//!
//! * [`data`]: the tables (`server.game_data.systems.quests`).
//! * [`state`]: the live quest objects on `Character::quest_state`.
//! * [`engine`] / [`acts`]: the state machine against a locked character ([`engine::Ctx`]).
//! * [`events`]: the public event API other subsystems call (and the operation runner).
//! * [`hooks`]: what the quest code needs from other subsystems ([`hooks::QuestHooks`]).
//! * [`spheres`]: quest spheres; [`packets`]: C->S / S->C packets.

pub mod acts;
pub mod bridges;
pub mod data;
pub mod engine;
pub mod events;
pub mod hooks;
pub mod model;
pub mod packets;
pub mod spheres;
pub mod state;
pub mod templates;
pub mod unit_reqs;

#[cfg(test)]
mod manager_tests;
#[cfg(test)]
mod quest_tests;
#[cfg(test)]
mod tests;

use std::collections::{HashMap, VecDeque};
use std::sync::{Arc, Mutex, OnceLock, Weak};

use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::server::GameServer;
use crate::systems::{DataLoadCtx, HookSlot, SaveStage};
use crate::world::{Player, PlayerRef};

pub use data::QuestData as Data;
use hooks::{DefaultQuestHooks, QuestHooks};
use state::QuestHandle;

impl Data {
    /// `QuestManager.Load` and the quest parts of the game data loaders.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Self::load_from(ctx.conn, ctx.files)
    }
}

/// One scheduled `QuestTimeoutTask`.
#[derive(Debug)]
struct TimerEntry {
    quest_id: u32,
    abort: Option<tokio::task::AbortHandle>,
}

/// Managers of this subsystem (`server.systems.quests`).
#[derive(Default)]
pub struct System {
    server: OnceLock<Weak<GameServer>>,
    /// `QuestManager.EvaluationQueue` (player, quest).
    queue: Mutex<VecDeque<(Weak<Player>, u32, QuestHandle)>>,
    /// Serializes `DoQueuedEvaluations`.
    draining: Mutex<()>,
    /// `QuestManager.QuestTimeoutTask` (character id -> quest timers, dictionary order).
    timers: Mutex<HashMap<u32, Vec<TimerEntry>>>,
    /// `WorldInstance.SphereQuestManager`.
    pub spheres: spheres::SphereQuestManager,
    /// The quest hooks (`bridges::GameQuestHooks`, installed at start).
    pub hooks: HookSlot<dyn QuestHooks>,
    /// The economy subsystem's `QuestHooks` (items, money, mails, trade packs) that
    /// `bridges::GameQuestHooks` forwards to.
    pub economy_hooks: HookSlot<dyn QuestHooks>,
}

impl std::fmt::Debug for System {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("quests::System")
            .field("queue", &self.queue.lock().map(|q| q.len()).unwrap_or(0))
            .field("hooks", &self.hooks)
            .finish()
    }
}

impl System {
    /// Startup: quest sphere manager (world coordinates need the world templates), the sphere
    /// tick (500 ms) and the daily reset task.
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        install_hooks(server);
        let s = &server.systems.quests;
        let _ = s.server.set(Arc::downgrade(server));
        let data = &server.game_data.systems.quests;
        let worlds = &server.game_data.worlds;
        s.spheres
            .load(data, &data.sign_spheres, "main_world", |zone| {
                worlds
                    .get_world_template_by_zone_key(zone)
                    .and_then(|w| w.xml_world_zones().get(&zone))
                    .map_or((0, 0), |z| (z.origin_x, z.origin_y))
            });
        // SphereQuestManager.Initialize: TickManager subscription (500 ms)
        let weak = Arc::downgrade(server);
        tokio::spawn(async move {
            let mut iv = tokio::time::interval(std::time::Duration::from_millis(500));
            loop {
                iv.tick().await;
                let Some(server) = weak.upgrade() else {
                    break;
                };
                crate::systems::run_blocking(&server, |s| s.systems.quests.spheres.tick(s)).await;
            }
        });
        // QuestDailyResetTask ("0 0 0 */1 * *": every day at midnight, UTC here)
        let weak = Arc::downgrade(server);
        tokio::spawn(async move {
            loop {
                let now = chrono::Utc::now().timestamp_millis();
                let next = (now.div_euclid(86_400_000) + 1) * 86_400_000;
                tokio::time::sleep(std::time::Duration::from_millis((next - now) as u64)).await;
                let Some(server) = weak.upgrade() else {
                    break;
                };
                daily_reset(&server);
            }
        });
        tracing::info!("Loaded {} quests", data.templates.len());
        Ok(())
    }

    /// Quest rows are written with the characters (`Character.Save` -> `Quests.Save`).
    pub async fn save(
        &self,
        _server: &GameServer,
        _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        _stage: SaveStage,
    ) -> crate::Result<usize> {
        Ok(0)
    }

    /// The installed hooks (or the no-op defaults).
    pub fn hooks(&self) -> Arc<dyn QuestHooks> {
        self.hooks
            .get()
            .unwrap_or_else(|| Arc::new(DefaultQuestHooks) as Arc<dyn QuestHooks>)
    }

    // ----- evaluation queue (QuestManager.EnqueueEvaluation / DoQueuedEvaluations) -----

    /// `EnqueueEvaluation(quest)`: schedules `QuestManagerRunQueueTask` (1 ms) when the
    /// queue was empty.
    pub fn enqueue_evaluation(&self, player: &PlayerRef, h: QuestHandle) {
        let need_new_task = {
            let mut q = self.queue.lock().expect("queue poisoned");
            let need = q.is_empty();
            let id = player.character_id;
            if !q.iter().any(|(_, c, x)| *c == id && *x == h) {
                q.push_back((Arc::downgrade(player), id, h));
            }
            need
        };
        if need_new_task {
            if let (Some(weak), Ok(rt)) = (
                self.server.get().cloned(),
                tokio::runtime::Handle::try_current(),
            ) {
                rt.spawn(async move {
                    tokio::time::sleep(std::time::Duration::from_millis(1)).await;
                    if let Some(server) = weak.upgrade() {
                        server.systems.quests.run_queued_evaluations(&server);
                    }
                });
            }
        }
    }

    /// The quests of one character waiting in the evaluation queue.
    pub fn queued_handles(&self, character_id: u32) -> Vec<QuestHandle> {
        self.queue
            .lock()
            .expect("queue poisoned")
            .iter()
            .filter(|(_, c, _)| *c == character_id)
            .map(|(_, _, h)| *h)
            .collect()
    }

    /// Number of queued evaluations.
    pub fn queued_count(&self) -> usize {
        self.queue.lock().expect("queue poisoned").len()
    }

    /// `DoQueuedEvaluations`: runs `RunCurrentStep` for every queued quest (evaluations
    /// requested meanwhile are processed in the same loop).
    pub fn run_queued_evaluations(&self, server: &GameServer) {
        let _serial = self.draining.lock().expect("draining poisoned");
        loop {
            let Some((weak, _, h)) = self.queue.lock().expect("queue poisoned").pop_front() else {
                break;
            };
            let Some(player) = weak.upgrade() else {
                continue;
            };
            events::with_quests(server, &player, None, move |ctx| {
                if ctx.st.quest(h).is_none() {
                    return;
                }
                ctx.starting_evaluation(h);
                tracing::info!(
                    "DoQueuedEvaluations, {} ({}), Quest {}",
                    ctx.c.unit.name,
                    ctx.c.id,
                    ctx.template_id_of(h)
                );
                ctx.run_current_step(h);
            });
        }
    }

    // ----- quest timers -----

    /// `AddQuestTimer` (registry part): false when the quest already has a timer.
    pub fn add_quest_timer(&self, character_id: u32, quest_id: u32, limit_time_ms: i32) -> bool {
        let mut timers = self.timers.lock().expect("timers poisoned");
        let list = timers.entry(character_id).or_default();
        if list.iter().any(|t| t.quest_id == quest_id) {
            return false;
        }
        let abort = match (
            self.server.get().cloned(),
            tokio::runtime::Handle::try_current(),
        ) {
            (Some(weak), Ok(rt)) => {
                let delay = u64::try_from(limit_time_ms).unwrap_or(0);
                Some(
                    rt.spawn(async move {
                        tokio::time::sleep(std::time::Duration::from_millis(delay)).await;
                        if let Some(server) = weak.upgrade() {
                            if let Some(p) = server.world.character_by_id(character_id) {
                                events::on_timer_expired(&server, &p, quest_id);
                            }
                        }
                    })
                    .abort_handle(),
                )
            }
            _ => None,
        };
        list.push(TimerEntry { quest_id, abort });
        true
    }

    /// `RemoveQuestTimer(ownerId, questId)` (0 = all): cancels and removes; returns the count.
    pub fn remove_quest_timer(&self, character_id: u32, quest_id: u32) -> usize {
        let mut timers = self.timers.lock().expect("timers poisoned");
        let Some(list) = timers.get_mut(&character_id) else {
            return 0;
        };
        let mut n = 0;
        list.retain(|t| {
            if quest_id == 0 || t.quest_id == quest_id {
                if let Some(a) = &t.abort {
                    a.abort();
                }
                n += 1;
                false
            } else {
                true
            }
        });
        n
    }

    /// `QuestTimeoutTask[owner].Remove(questId)` in `AddQuest`: drops the entry without
    /// cancelling the scheduled task (mirrored C# behaviour).
    pub fn forget_quest_timer(&self, character_id: u32, quest_id: u32) {
        let mut timers = self.timers.lock().expect("timers poisoned");
        if let Some(list) = timers.get_mut(&character_id) {
            list.retain(|t| t.quest_id != quest_id);
        }
    }

    /// `QuestTimeoutTask[characterId][questId] != null`.
    pub fn has_quest_timer(&self, character_id: u32, quest_id: u32) -> bool {
        self.timers
            .lock()
            .expect("timers poisoned")
            .get(&character_id)
            .is_some_and(|l| l.iter().any(|t| t.quest_id == quest_id))
    }
}

/// `QuestDailyResetTask.Execute`.
pub fn daily_reset(server: &GameServer) {
    let hooks = server.systems.quests.hooks();
    for p in server.world.all_characters() {
        events::with_quests(server, &p, None, |ctx| ctx.reset_daily_quests(true));
        let account = p.data().account_id;
        hooks.daily_account_login(server, account);
    }
}

/// `CSSelectCharacterPacket` -> `Character.Load()`: `Quests.Load` (live quests from the
/// loaded rows) + `Quests.CheckDailyResetAtLogin()`. Call after the `Player` exists and before
/// `Quests.Send` (`SCQuestsPacket` uses the re-derived rows).
pub fn on_character_selected(server: &GameServer, player: &PlayerRef) {
    events::with_quests(server, player, None, |ctx| {
        if !ctx.st.loaded {
            ctx.load_from_rows();
        }
        ctx.check_daily_reset_at_login();
    });
}

/// `EnterWorldManager.LeaveWorldTask`: `questManager.RemoveQuestTimer(activeChar.Id, 0)`.
pub fn on_leave_world(server: &GameServer, character_id: u32) {
    server.systems.quests.remove_quest_timer(character_id, 0);
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    use packets::*;
    reg.register::<CSStartQuestContextPacket>(LEVEL_PLAIN, cs::CSStartQuestContextPacket);
    reg.register::<CSCompleteQuestContextPacket>(LEVEL_PLAIN, cs::CSCompleteQuestContextPacket);
    reg.register::<CSDropQuestContextPacket>(LEVEL_PLAIN, cs::CSDropQuestContextPacket);
    reg.register::<CSQuestTalkMadePacket>(LEVEL_PLAIN, cs::CSQuestTalkMadePacket);
    reg.register::<CSQuestStartWithPacket>(LEVEL_PLAIN, cs::CSQuestStartWithPacket);
    reg.register::<CSTryQuestCompleteAsLetItDonePacket>(
        LEVEL_PLAIN,
        cs::CSTryQuestCompleteAsLetItDonePacket,
    );
    reg.register::<CSRestartMainQuestPacket>(LEVEL_PLAIN, cs::CSRestartMainQuestPacket);
    reg.register::<CSSaveTutorialPacket>(LEVEL_PLAIN, cs::CSSaveTutorialPacket);
    reg.register::<CSStartedCinemaPacket>(LEVEL_PLAIN, cs::CSStartedCinemaPacket);
    reg.register::<CSCompletedCinemaPacket>(LEVEL_PLAIN, cs::CSCompletedCinemaPacket);
}

/// Installs the quest subsystem's hooks (its implementations of the other subsystems' hooks
/// and its own `QuestHooks`). No I/O; also used by the flow tests.
pub fn install_hooks(server: &GameServer) {
    bridges::install(server);
}
