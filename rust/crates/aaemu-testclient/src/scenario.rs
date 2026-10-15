//! Scripted bot scenarios with assertions on the server responses.

use std::collections::BTreeMap;
use std::time::{Duration, Instant};

use aaemu_commons::PacketStream;
use aaemu_game::opcodes::{pp, sc};
use aaemu_game::systems::quests::model::status;
use aaemu_game::systems::skills::types::{SkillCastTarget, SkillCaster, SkillObject};
use anyhow::{bail, ensure, Context};
use serde::{Deserialize, Serialize};

use crate::game::{GameClient, Packet, UnitInfo, LEVEL_PROXY};
use crate::login::LoginClient;
use crate::record::Recorder;
use crate::stream::StreamClient;

mod questitem;

/// Which scenario to run.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Kind {
    /// Login server only: auth, world list, world cookie.
    Login,
    /// Login + game lobby: enter world, FinishState burst, character list, stream join,
    /// character creation, then quit.
    Lobby,
    /// Everything: lobby, select + spawn, move, chat, skill on an NPC, inventory move, quest
    /// accept, leave world.
    Full,
    /// The load-test loop: login, (create), select, move, chat, leave.
    Load,
    /// `full` plus a fight with a hostile NPC (random outcome: diff with `parity-combat.json`).
    Combat,
    /// NPC residency: accept the configured quest at its resident giver, leave beyond the warm
    /// horizon for longer than the warm-retention window, then return and require the same
    /// giver instance and an active quest.
    #[serde(rename = "npc-return")]
    NpcReturn,
    /// Quest-item use: GM setup (quest, orbs, teleport), then the quest item's use skill on
    /// nearby NPCs until the quest objective is counted (see `scenario/questitem.rs`).
    #[serde(rename = "questitem")]
    QuestItem,
}

impl std::str::FromStr for Kind {
    type Err = anyhow::Error;
    fn from_str(s: &str) -> anyhow::Result<Self> {
        Ok(match s {
            "login" => Self::Login,
            "lobby" => Self::Lobby,
            "full" => Self::Full,
            "load" => Self::Load,
            "combat" => Self::Combat,
            "npc-return" => Self::NpcReturn,
            "questitem" => Self::QuestItem,
            _ => bail!(
                "unknown scenario '{s}' (login, lobby, full, combat, npc-return, questitem, load)"
            ),
        })
    }
}

/// Bot settings.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct BotConfig {
    /// Login server `host:port`.
    pub login: String,
    pub user: String,
    pub password: String,
    /// Character name used when the account has none.
    pub character: String,
    #[serde(default = "default_gs")]
    pub game_server_id: u8,
    /// Skill cast on the nearest NPC (2 = melee attack).
    #[serde(default = "default_skill")]
    pub skill_id: u32,
    /// Quest accepted from an NPC (`CSStartQuestContextPacket`).
    #[serde(default = "default_quest")]
    pub quest_id: u32,
    /// Template of the NPC that gives `quest_id` (must be near the start position).
    #[serde(default = "default_quest_npc")]
    pub quest_npc_template: u32,
    /// Hostile NPC attacked in the combat step (3492: Solzreed fox near the Nuian spawn); 0
    /// skips the step.
    #[serde(default = "default_combat_npc")]
    pub combat_npc_template: u32,
    /// Wait for `SCLeaveWorldGrantedPacket` (10 s logout timer) before disconnecting.
    #[serde(default = "default_true")]
    pub wait_leave_granted: bool,
    /// `npc-return`: distance (m) walked away from the spawn before returning. Must clear the
    /// NPC warm/trigger horizon (default 2048 m).
    #[serde(default = "default_npc_return_distance")]
    pub npc_return_distance: f32,
    /// `npc-return`: seconds to stay away; must exceed `World.NpcWarmRetentionSeconds`
    /// (server default 60 s).
    #[serde(default = "default_npc_return_away_secs")]
    pub npc_return_away_secs: u64,
    /// `questitem`: quest started by GM command (2394 "Searching for Answers").
    #[serde(default = "default_quest_item_quest")]
    pub quest_item_quest: u32,
    /// `questitem`: the quest's giver NPC template (511), recorded as the acceptor so the
    /// `QuestActConAcceptNpc` start condition passes and the quest reaches its Progress step.
    #[serde(default = "default_quest_item_giver")]
    pub quest_item_giver: u32,
    /// `questitem`: quest item template (17594 "Detection Orb").
    #[serde(default = "default_quest_item_template")]
    pub quest_item_template: u32,
    /// `questitem`: the item's use skill (16881).
    #[serde(default = "default_quest_item_skill")]
    pub quest_item_skill: u32,
    /// `questitem`: NPC templates the item is used on (7824 / 7823 Field of Honor
    /// participants at Soulreath).
    #[serde(default = "default_quest_item_npcs")]
    pub quest_item_npcs: Vec<u32>,
    /// `questitem`: successful uses required (objective count of the quest act).
    #[serde(default = "default_quest_item_uses")]
    pub quest_item_uses: u32,
    /// `questitem`: GM teleport target next to those NPCs (`/move x y z`).
    #[serde(default = "default_quest_item_pos")]
    pub quest_item_pos: [f32; 3],
    /// `questitem`: casts tried (rejected ones included) before the step fails.
    #[serde(default = "default_quest_item_max_attempts")]
    pub quest_item_max_attempts: u32,
}

fn default_gs() -> u8 {
    1
}
fn default_skill() -> u32 {
    2
}
fn default_quest() -> u32 {
    330 // Nuian starter quest given by NPC 3597, next to the Nuian spawn point
}
fn default_quest_npc() -> u32 {
    3597
}
fn default_combat_npc() -> u32 {
    3492
}
fn default_true() -> bool {
    true
}
fn default_npc_return_distance() -> f32 {
    2048.0
}
fn default_npc_return_away_secs() -> u64 {
    70
}
fn default_quest_item_quest() -> u32 {
    2394
}
fn default_quest_item_giver() -> u32 {
    511
}
fn default_quest_item_template() -> u32 {
    17594
}
fn default_quest_item_skill() -> u32 {
    16881
}
fn default_quest_item_npcs() -> Vec<u32> {
    vec![7824, 7823]
}
fn default_quest_item_uses() -> u32 {
    3
}
fn default_quest_item_pos() -> [f32; 3] {
    [11041.0, 15622.0, 246.0]
}
fn default_quest_item_max_attempts() -> u32 {
    12
}

/// Result of one scenario step.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct StepResult {
    pub name: String,
    pub ok: bool,
    pub ms: u64,
    pub note: String,
}

/// Result of a scenario run.
#[derive(Debug, Default, Serialize, Deserialize)]
pub struct Outcome {
    pub steps: Vec<StepResult>,
    /// Values that differ between runs by construction (ids, ports, ...), for the diff
    /// normaliser.
    pub vars: BTreeMap<String, u64>,
    pub error: Option<String>,
}

impl Outcome {
    pub fn ok(&self) -> bool {
        self.error.is_none() && self.steps.iter().all(|s| s.ok)
    }
    pub fn step_ms(&self, name: &str) -> Option<u64> {
        self.steps.iter().find(|s| s.name == name).map(|s| s.ms)
    }
}

struct Runner<'a> {
    rec: &'a Recorder,
    out: Outcome,
}

impl Runner<'_> {
    fn begin(&self, name: &str) -> (String, Instant) {
        self.rec.step(name);
        (name.to_string(), Instant::now())
    }

    fn end(&mut self, (name, t): (String, Instant), note: impl Into<String>) {
        self.out.steps.push(StepResult {
            name,
            ok: true,
            ms: t.elapsed().as_millis() as u64,
            note: note.into(),
        });
    }

    fn var(&mut self, name: &str, v: impl Into<u64>) {
        self.out.vars.insert(name.to_string(), v.into());
    }
}

/// Runs `kind` for one bot. Failures end the run; the error is in [`Outcome::error`].
pub async fn run(kind: Kind, cfg: &BotConfig, rec: &Recorder) -> Outcome {
    let mut r = Runner {
        rec,
        out: Outcome::default(),
    };
    if let Err(e) = run_inner(kind, cfg, &mut r).await {
        let step = rec.current_step();
        r.out.steps.push(StepResult {
            name: step.clone(),
            ok: false,
            ms: 0,
            note: format!("{e:#}"),
        });
        r.out.error = Some(format!("[{step}] {e:#}"));
    }
    r.out
}

const QUIET: Duration = Duration::from_millis(1500);

async fn run_inner(kind: Kind, cfg: &BotConfig, r: &mut Runner<'_>) -> anyhow::Result<()> {
    // ---- login server ----
    let s = r.begin("login.auth");
    let mut login = LoginClient::connect(&cfg.login, r.rec.clone()).await?;
    let account_id = login.auth_trion(&cfg.user, &cfg.password).await?;
    ensure!(account_id != 0, "account id 0");
    r.var("account_id", account_id);
    r.end(s, format!("account {account_id}"));

    let s = r.begin("login.worlds");
    let worlds = login.list_worlds().await?;
    let world = worlds
        .worlds
        .iter()
        .find(|w| w.id == cfg.game_server_id)
        .with_context(|| format!("world {} not in ACWorldList", cfg.game_server_id))?;
    ensure!(world.active, "world {} is not active", world.name);
    r.end(
        s,
        format!(
            "{} world(s), {} character(s)",
            worlds.worlds.len(),
            worlds.characters.len()
        ),
    );

    let s = r.begin("login.enter");
    let cookie = login.enter_world(cfg.game_server_id).await?;
    r.var("cookie", cookie.cookie);
    r.var("game_port", cookie.port);
    r.end(
        s,
        format!("{}:{} cookie {}", cookie.ip, cookie.port, cookie.cookie),
    );
    if kind == Kind::Login {
        return Ok(());
    }

    // ---- game server: lobby ----
    let s = r.begin("game.enter");
    let addr = format!("{}:{}", cookie.ip, cookie.port);
    let mut game = GameClient::connect(&addr, r.rec.clone()).await?;
    game.enter_world(account_id, cookie.cookie).await?;
    game.wait_sc(sc::X2EnterWorldResponsePacket).await?;
    game.wait_for("ChangeStatePacket(0)", |p| {
        p.is(LEVEL_PROXY, pp::ChangeStatePacket)
    })
    .await?;
    ensure!(
        game.state.last_change_state == Some(0),
        "ChangeState {:?}",
        game.state.last_change_state
    );
    r.var("token", game.state.token);
    r.var("stream_port", game.state.stream_port);
    r.end(s, format!("token {}", game.state.token));

    let s = r.begin("game.finish_state");
    for state in 0..=7 {
        game.finish_state(state).await?;
        if state < 7 {
            game.wait_for("ChangeStatePacket", |p| {
                p.is(LEVEL_PROXY, pp::ChangeStatePacket)
            })
            .await?;
            ensure!(
                game.state.last_change_state == Some(state + 1),
                "FinishState({state}) answered with ChangeState {:?}",
                game.state.last_change_state
            );
        } else {
            game.wait_sc(sc::SCUpdatePremiumPointPacket).await?;
        }
    }
    r.end(s, "states 0..7");

    let s = r.begin("stream.join");
    let stream_addr = format!("{}:{}", cookie.ip, game.state.stream_port);
    let mut stream = StreamClient::connect(&stream_addr, r.rec.clone()).await?;
    let resp = stream.join(account_id, game.state.token).await?;
    ensure!(resp == 0, "TCJoinResponse {resp}");
    r.end(s, "joined");

    let s = r.begin("game.list");
    game.list_characters().await?;
    game.wait_for("last SCCharacterListPacket", |p| {
        p.is_sc(sc::SCCharacterListPacket) && p.body.first() == Some(&1)
    })
    .await?;
    game.drain(Duration::from_millis(300), Duration::from_secs(2))
        .await?;
    r.end(s, format!("{} character(s)", game.state.characters.len()));

    let s = r.begin("game.create");
    let created = if game.state.characters.is_empty() {
        game.create_character(&cfg.character).await?;
        let p = game
            .wait_for("character creation result", |p| {
                p.is_sc(sc::SCCreateCharacterResponsePacket)
                    || p.is_sc(sc::SCCharacterCreationFailedPacket)
            })
            .await?;
        if p.is_sc(sc::SCCharacterCreationFailedPacket) {
            bail!("SCCharacterCreationFailed reason {:?}", p.body.first());
        }
        true
    } else {
        false
    };
    let ch = game.state.characters[0].clone();
    r.var("char_id", ch.id);
    r.end(
        s,
        if created {
            format!("created {} ({})", ch.name, ch.id)
        } else {
            format!("existing {} ({})", ch.name, ch.id)
        },
    );

    if kind == Kind::Lobby {
        let s = r.begin("game.quit");
        game.leave_world(0).await?;
        game.drain(Duration::from_millis(500), Duration::from_secs(2))
            .await?;
        r.end(s, "");
        return Ok(());
    }

    // ---- world ----
    let s = r.begin("game.select");
    game.select_character(ch.id).await?;
    game.wait_sc(sc::SCCharacterStatePacket).await?;
    game.drain(QUIET, Duration::from_secs(10)).await?;
    r.end(s, format!("{} bag items seen", game.state.bag_items.len()));

    let s = r.begin("game.spawn");
    game.spawn_character().await?;
    game.wait_for("own SCUnitStatePacket", |p| p.is_sc(sc::SCUnitStatePacket))
        .await?;
    game.notify_in_game().await?;
    game.notify_in_game_completed().await?;
    game.drain(QUIET, Duration::from_secs(15)).await?;
    ensure!(game.state.self_obj != 0, "own unit state not received");
    r.var("self_obj", game.state.self_obj);
    let npcs = game
        .state
        .units
        .values()
        .filter(|u| u.unit_type == 1)
        .count();
    r.end(
        s,
        format!(
            "obj {} at {:?}, {npcs} npcs visible",
            game.state.self_obj, game.state.self_pos
        ),
    );

    if kind == Kind::NpcReturn {
        npc_return(&mut game, cfg, r).await?;
        drop(stream);
        return Ok(());
    }

    if kind == Kind::QuestItem {
        questitem::run(&mut game, cfg, r).await?;
        drop(stream);
        return Ok(());
    }

    let s = r.begin("game.move");
    let p0 = game.state.self_pos;
    for i in 1..=3u32 {
        let pos = (p0.0 + i as f32 * 0.5, p0.1 + i as f32 * 0.5, p0.2);
        game.move_to(pos, i * 100).await?;
    }
    game.drain(Duration::from_millis(500), Duration::from_secs(3))
        .await?;
    ensure!(!game.state.disconnected, "disconnected after moving");
    r.end(s, format!("moved to {:?}", game.state.self_pos));

    let s = r.begin("game.chat");
    game.chat(0, "hello from aaemu-testclient").await?;
    game.wait_sc(sc::SCChatMessagePacket).await?;
    r.end(s, "say echoed");

    if matches!(kind, Kind::Full | Kind::Combat) {
        let s = r.begin("game.skill");
        // NPCs of newly active regions appear within a few seconds.
        if game.stable_unit(1, 40.0).is_none() {
            let _ = game
                .wait_for("an NPC in range", |p| p.is_sc(sc::SCUnitStatePacket))
                .await;
            game.drain(Duration::from_millis(500), Duration::from_secs(3))
                .await?;
        }
        let npc = game.stable_unit(1, 40.0);
        let note = match npc {
            Some(npc) => {
                r.var("npc_obj", npc.obj_id);
                // Walk up to the target so range checks pass.
                game.move_to((npc.pos.0 + 1.0, npc.pos.1, npc.pos.2), 2000)
                    .await?;
                game.drain(Duration::from_millis(300), Duration::from_secs(1))
                    .await?;
                game.start_skill(cfg.skill_id, npc.obj_id).await?;
                let caster = game.state.self_obj;
                game.wait_for("skill response", |p| {
                    own_skill_response(p, cfg.skill_id, caster) || p.is_sc(sc::SCErrorMsgPacket)
                })
                .await?;
                game.drain(QUIET, Duration::from_secs(5)).await?;
                format!("skill {} on npc {} ({})", cfg.skill_id, npc.obj_id, npc.id)
            }
            None => "no npc in range".into(),
        };
        r.end(s, note);

        let s = r.begin("game.inventory");
        let note = match game.state.bag_items.first().copied() {
            Some((item, slot, tpl)) => {
                r.var("item_id", item);
                game.swap_items(item, slot, 0, 49).await?;
                game.wait_for("item task result", |p| {
                    p.is_sc(sc::SCItemTaskSuccessPacket) || p.is_sc(sc::SCErrorMsgPacket)
                })
                .await?;
                game.drain(Duration::from_millis(500), Duration::from_secs(3))
                    .await?;
                format!("moved item {item} (template {tpl}) from slot {slot} to 49")
            }
            None => "no bag item".into(),
        };
        r.end(s, note);

        let s = r.begin("game.quest");
        let giver = game
            .state
            .units
            .values()
            .filter(|u| u.unit_type == 1 && u.id == cfg.quest_npc_template)
            .min_by(|a, b| a.obj_id.cmp(&b.obj_id))
            .cloned();
        let note = match giver {
            Some(npc) => {
                game.move_to((npc.pos.0 + 1.0, npc.pos.1, npc.pos.2), 3000)
                    .await?;
                game.start_quest(cfg.quest_id, npc.obj_id).await?;
                // No answer at all when the quest cannot be started (e.g. already taken).
                let got = game
                    .drain(Duration::from_millis(800), Duration::from_secs(4))
                    .await?;
                let answer = got
                    .iter()
                    .find(|p| {
                        p.is_sc(sc::SCQuestContextStartedPacket) || p.is_sc(sc::SCErrorMsgPacket)
                    })
                    .map_or("no answer", |p| p.name());
                format!("quest {} from npc {} -> {answer}", cfg.quest_id, npc.id)
            }
            None => {
                game.start_quest(cfg.quest_id, 0).await?;
                game.drain(Duration::from_millis(800), Duration::from_secs(4))
                    .await?;
                format!("quest {} (no giver in sight)", cfg.quest_id)
            }
        };
        r.end(s, note);
    }

    if kind == Kind::Combat && cfg.combat_npc_template != 0 {
        let s = r.begin("game.combat");
        let note = combat(&mut game, cfg).await?;
        r.end(s, note);
    }

    // Back to where we spawned: the position saved on logout (and seen by the next session)
    // then does not depend on where the NPCs wandered.
    game.move_to(p0, 19000).await?;
    let s = r.begin("game.leave");
    game.leave_world(0).await?;
    let prep = game.wait_sc(sc::SCPrepareLeaveWorldPacket).await?;
    let logout_ms = prep.stream().read_i32().unwrap_or(0);
    // In combat the logout timer is 5 minutes: just drop the connection then.
    if cfg.wait_leave_granted && logout_ms <= 10_000 {
        game.wait_sc(sc::SCLeaveWorldGrantedPacket).await?;
    }
    game.drain(Duration::from_millis(300), Duration::from_secs(2))
        .await
        .ok();
    r.end(s, "left");
    drop(stream);
    Ok(())
}

/// `npc-return`: accept the configured quest at its resident NPC giver, leave the giver beyond
/// the warm/trigger horizon for longer than the warm-retention window, then walk back to the
/// spawn and require the same live giver instance to be announced again while the quest is
/// still active.
///
/// The residency check (same object id + template, fresh `SCUnitStatePacket`) stays on one
/// connection. The quest-state readback re-selects the character on that same connection
/// (logout to character select, then select again): the `SCQuestsPacket` snapshot then proves
/// the quest survived the away/return cycle.
async fn npc_return(
    game: &mut GameClient,
    cfg: &BotConfig,
    r: &mut Runner<'_>,
) -> anyhow::Result<()> {
    let p0 = game.state.self_pos;
    let self_obj = game.state.self_obj;
    let char_id = game.state.selected_char;

    // ---- accept the quest at the configured resident giver ----
    let s = r.begin("game.npc-return.accept");
    let giver = wait_for_giver(game, cfg.quest_npc_template, Duration::from_secs(10)).await?;
    let giver_obj = giver.obj_id;
    game.move_to((giver.pos.0 + 1.0, giver.pos.1, giver.pos.2), 3000)
        .await?;
    game.drain(Duration::from_millis(300), Duration::from_secs(2))
        .await?;
    game.start_quest(cfg.quest_id, giver_obj).await?;
    // No answer at all when the quest cannot be started (e.g. already taken).
    let answers = game
        .drain(Duration::from_millis(800), Duration::from_secs(4))
        .await?;
    if let Some(err) = answers.iter().find(|p| p.is_sc(sc::SCErrorMsgPacket)) {
        let code = err.stream().read_i16().unwrap_or(0);
        bail!(
            "start_quest({}) on giver {giver_obj} was rejected (SCErrorMsg {code})",
            cfg.quest_id
        );
    }
    let confirmed = answers.iter().any(|p| {
        (p.is_sc(sc::SCQuestContextStartedPacket) || p.is_sc(sc::SCQuestContextUpdatedPacket))
            && quest_template_id(p).ok() == Some(cfg.quest_id)
    });
    ensure!(
        confirmed,
        "start_quest({}) on giver {giver_obj} got no SCQuestContextStarted/Updated carrying it: \
         this character probably already has quest {}; run npc-return with a fresh disposable \
         character",
        cfg.quest_id,
        cfg.quest_id
    );
    r.var("giver_obj", giver_obj);
    r.var("giver_template", giver.id);
    r.end(
        s,
        format!(
            "giver {giver_obj} (template {}); quest {} started (SCQuestContextStarted/Updated carried it)",
            giver.id, cfg.quest_id
        ),
    );

    // ---- leave beyond the warm/trigger horizon and outlast the warm retention ----
    let s = r.begin("game.npc-return.away");
    ensure!(
        cfg.npc_return_distance > 0.0,
        "npc_return_distance must be positive"
    );
    let far = (p0.0 + cfg.npc_return_distance, p0.1, p0.2);
    game.move_to(far, 60_000).await?;
    // The server moves our unit and echoes the movement to us; the echoed transform is the
    // evidence that the leave actually happened server-side.
    game.wait_for("own SCOneUnitMovementPacket (away)", |p| {
        p.is_sc(sc::SCOneUnitMovementPacket) && packet_obj_id(p).ok() == Some(self_obj)
    })
    .await?;
    game.drain(Duration::from_millis(500), Duration::from_secs(3))
        .await?;
    let at = game.state.units.get(&self_obj).map(|u| u.pos);
    ensure!(
        at.is_some_and(|u| dist3(u, far) <= 5.0),
        "server did not apply the away move to {far:?}: own unit state {at:?}"
    );
    ensure!(
        !game.state.units.contains_key(&giver_obj),
        "giver {giver_obj} is still visible {} m away (warm horizon not left)",
        cfg.npc_return_distance
    );
    let pumped = pump(game, cfg.npc_return_away_secs).await?;
    ensure!(!game.state.disconnected, "connection lost while away");
    r.var("npc_return_distance", cfg.npc_return_distance as u64);
    r.end(
        s,
        format!(
            "{:.0} m away at {far:?}, {} s away, {} packets pumped",
            cfg.npc_return_distance,
            cfg.npc_return_away_secs,
            pumped.len()
        ),
    );

    // ---- come back: the resident giver must be announced again, same live instance ----
    let s = r.begin("game.npc-return.return");
    game.move_to(p0, 60_000).await?;
    // The giver's SCUnitStatePacket can arrive before our own movement echo, so packet order
    // cannot be relied on (a waiting scan would consume the announcement first). `observe`
    // updates the unit map for every received packet regardless of order: the echo moves our
    // own entry back to the spawn, and the announcement re-inserts the giver (which was
    // asserted gone while away). Wait for both, then validate the entries below.
    let return_deadline = Instant::now() + Duration::from_secs(20);
    loop {
        let at = game.state.units.get(&self_obj).map(|u| u.pos);
        let giver_back = game.state.units.contains_key(&giver_obj);
        if at.is_some_and(|u| dist3(u, p0) <= 5.0) && giver_back {
            break;
        }
        if Instant::now() >= return_deadline {
            bail!(
                "no return within 20 s: own transform {at:?} (expected near {p0:?}), \
                 giver {giver_obj} visible: {giver_back}"
            );
        }
        game.drain(Duration::from_millis(500), Duration::from_secs(1))
            .await?;
    }
    let unit = game
        .state
        .units
        .get(&giver_obj)
        .context("giver missing from unit state after its SCUnitStatePacket")?;
    ensure!(
        unit.unit_type == 1 && unit.id == cfg.quest_npc_template,
        "returned unit {giver_obj} has type {} / template {}, expected NPC template {}",
        unit.unit_type,
        unit.id,
        cfg.quest_npc_template
    );
    let pos = unit.pos;
    r.end(
        s,
        format!(
            "same giver {giver_obj} (template {}) re-announced at {pos:?}",
            unit.id
        ),
    );

    // ---- the quest is still active on a fresh character snapshot ----
    let s = r.begin("game.npc-return.quest");
    game.leave_world(1).await?;
    game.wait_sc(sc::SCPrepareLeaveWorldPacket).await?;
    game.wait_sc(sc::SCLeaveWorldGrantedPacket).await?;
    game.drain(Duration::from_millis(300), Duration::from_secs(2))
        .await?;
    game.list_characters().await?;
    game.wait_for("SCCharacterListPacket", |p| {
        p.is_sc(sc::SCCharacterListPacket) && p.body.first() == Some(&1)
    })
    .await?;
    game.select_character(char_id).await?;
    // Collect the whole select snapshot until a quiet gap after SCCharacterStatePacket: waiting
    // for that packet alone could swallow an SCQuestsPacket that arrived just before it.
    let mut snapshot = Vec::new();
    let snapshot_deadline = Instant::now() + Duration::from_secs(30);
    loop {
        let batch = game.drain(QUIET, Duration::from_secs(10)).await?;
        let quiet = batch.is_empty();
        snapshot.extend(batch);
        let state_seen = snapshot.iter().any(|p| p.is_sc(sc::SCCharacterStatePacket));
        if state_seen && quiet {
            break;
        }
        if Instant::now() >= snapshot_deadline {
            bail!(
                "no complete character snapshot after re-selecting character {char_id} \
                 (SCCharacterStatePacket seen: {state_seen})"
            );
        }
    }
    let row = snapshot
        .iter()
        .filter(|p| p.is_sc(sc::SCQuestsPacket))
        .filter_map(|p| quest_rows(&p.body).ok())
        .flatten()
        .find(|(template, _)| *template == cfg.quest_id);
    let (template, st) = row.with_context(|| {
        format!(
            "quest {} is not in SCQuestsPacket after the away/return cycle",
            cfg.quest_id
        )
    })?;
    ensure!(
        matches!(st, status::PROGRESS | status::READY),
        "quest {template} is no longer active after the away/return cycle (status {st})"
    );
    r.end(s, format!("quest {template} still active (status {st})"));

    // The re-select snapshot leaves the connection in the character-select lobby, where the
    // server ignores `CSLeaveWorldPacket` (`EnterWorldManager.Leave` only acts in
    // `GameState.World`). Complete the same spawn -> notify handshake as the initial spawn
    // before the final leave.
    let s = r.begin("game.npc-return.respawn");
    game.spawn_character().await?;
    game.wait_for("own SCUnitStatePacket (re-select)", |p| {
        p.is_sc(sc::SCUnitStatePacket)
    })
    .await?;
    game.notify_in_game().await?;
    game.notify_in_game_completed().await?;
    game.drain(QUIET, Duration::from_secs(15)).await?;
    ensure!(
        game.state.self_obj == self_obj,
        "re-select spawn announced obj {} instead of the original {self_obj}",
        game.state.self_obj
    );
    r.end(
        s,
        format!("obj {self_obj} spawned again at {:?}", game.state.self_pos),
    );

    let s = r.begin("game.leave");
    game.leave_world(0).await?;
    let prep = game.wait_sc(sc::SCPrepareLeaveWorldPacket).await?;
    let logout_ms = prep.stream().read_i32().unwrap_or(0);
    if cfg.wait_leave_granted && logout_ms <= 10_000 {
        game.wait_sc(sc::SCLeaveWorldGrantedPacket).await?;
    }
    game.drain(Duration::from_millis(300), Duration::from_secs(2))
        .await
        .ok();
    r.end(s, "left");
    Ok(())
}

/// The configured quest giver, once it is visible at the spawn point.
async fn wait_for_giver(
    game: &mut GameClient,
    template_id: u32,
    timeout: Duration,
) -> anyhow::Result<UnitInfo> {
    let deadline = Instant::now() + timeout;
    loop {
        let giver = game
            .state
            .units
            .values()
            .filter(|u| u.unit_type == 1 && u.id == template_id)
            .min_by(|a, b| a.obj_id.cmp(&b.obj_id))
            .cloned();
        if let Some(giver) = giver {
            return Ok(giver);
        }
        if Instant::now() >= deadline {
            bail!("quest giver NPC {template_id} is not visible at the spawn point ({timeout:?})");
        }
        game.drain(Duration::from_millis(500), Duration::from_secs(1))
            .await?;
    }
}

/// Reads packets for `secs`, collecting whatever arrives. `GameClient::recv_within` keeps the
/// connection alive (keep-alive pings) while waiting.
async fn pump(game: &mut GameClient, secs: u64) -> anyhow::Result<Vec<Packet>> {
    let deadline = Instant::now() + Duration::from_secs(secs);
    let mut seen = Vec::new();
    while Instant::now() < deadline {
        let left = deadline.saturating_duration_since(Instant::now());
        if let Some(p) = game
            .recv_within(left.min(Duration::from_millis(500)))
            .await?
        {
            seen.push(p);
        }
    }
    Ok(seen)
}

fn dist3(a: (f32, f32, f32), b: (f32, f32, f32)) -> f32 {
    let (dx, dy, dz) = (a.0 - b.0, a.1 - b.1, a.2 - b.2);
    (dx * dx + dy * dy + dz * dz).sqrt()
}

/// First `Bc` of a unit packet body: the object id of `SCUnitStatePacket` /
/// `SCOneUnitMovementPacket`.
fn packet_obj_id(p: &Packet) -> anyhow::Result<u32> {
    Ok(p.stream().read_bc()?)
}

/// Quest template id of an `SCQuestContextStartedPacket` / `SCQuestContextUpdatedPacket` body
/// (the second field after the quest instance id written by `Quest.Write`).
fn quest_template_id(p: &Packet) -> anyhow::Result<u32> {
    let mut s = p.stream();
    s.read_i64()?;
    Ok(s.read_u32()?)
}

/// One `ActiveQuest` row as written by `Quest.Write` (`systems/quests/packets.rs`), reduced to
/// the fields the readback asserts: `(template_id, status)`.
fn read_quest_row(s: &mut PacketStream) -> anyhow::Result<(u32, u8)> {
    s.read_i64()?; // quest instance id (long)
    let template = s.read_u32()?;
    let status = s.read_u8()?;
    for _ in 0..5 {
        s.read_i32()?; // objectives
    }
    s.read_bool()?; // isCheckSet
    s.read_bc()?; // obj id
    s.read_u32()?; // type id
    s.read_bc()?; // obj id
    s.read_bc()?; // obj id
    s.read_i32()?; // left time
    s.read_u32()?; // component id (0 without a time limit)
    s.read_i64()?; // doodad id
    s.read_i64()?; // accept time (seconds)
    s.read_u8()?; // acceptor type
    s.read_u32()?; // acceptor id
    Ok((template, status))
}

/// `SCQuestsPacket` body (`[i32 count][ActiveQuest...]`) as `(template_id, status)` rows. The
/// count is untrusted: negatives are rejected and the rows are parsed incrementally (no
/// capacity reservation from the wire value).
fn quest_rows(body: &[u8]) -> anyhow::Result<Vec<(u32, u8)>> {
    let mut s = PacketStream::from_bytes(body.to_vec());
    let count = s.read_i32()?;
    ensure!(count >= 0, "negative SCQuestsPacket quest count {count}");
    let mut rows = Vec::new();
    for _ in 0..count {
        rows.push(read_quest_row(&mut s)?);
    }
    Ok(rows)
}

/// Walks up to the closest `combat_npc_template` NPC (following its movement) and hits it with
/// `skill_id` up to three times.
async fn combat(game: &mut GameClient, cfg: &BotConfig) -> anyhow::Result<String> {
    let pick = |game: &GameClient| {
        let me = game.state.self_pos;
        game.state
            .units
            .values()
            .filter(|u| u.unit_type == 1 && u.id == cfg.combat_npc_template)
            .min_by(|a, b| {
                let d =
                    |u: &crate::game::UnitInfo| (u.pos.0 - me.0).powi(2) + (u.pos.1 - me.1).powi(2);
                d(a).total_cmp(&d(b))
            })
            .cloned()
    };
    let target = pick(game)
        .with_context(|| format!("no combat NPC {} in sight", cfg.combat_npc_template))?;
    game.change_target(target.obj_id).await?;
    let mut started = 0;
    let mut results = Vec::new();
    for attempt in 0..3u32 {
        let pos = game
            .state
            .units
            .get(&target.obj_id)
            .map_or(target.pos, |u| u.pos);
        game.move_to_facing((pos.0 + 0.8, pos.1, pos.2), pos, 10000 + attempt * 1000)
            .await?;
        game.drain(Duration::from_millis(300), Duration::from_secs(1))
            .await?;
        game.start_skill(cfg.skill_id, target.obj_id).await?;
        // NPC attacks also broadcast these opcodes. Wait for our own cast.
        let caster = game.state.self_obj;
        let p = game
            .wait_for("skill response", |p| {
                own_skill_response(p, cfg.skill_id, caster) || p.is_sc(sc::SCErrorMsgPacket)
            })
            .await?;
        let result = skill_response_result(&p)?;
        ensure!(
            result == 0,
            "combat skill {} rejected with SkillResult {result}",
            cfg.skill_id
        );
        results.push(result);
        started += 1;
        game.drain(Duration::from_millis(800), Duration::from_secs(3))
            .await?;
    }
    Ok(format!(
        "{started} player casts of skill {} on npc {} ({}), SkillResults {results:?}",
        cfg.skill_id, target.obj_id, target.id
    ))
}

fn own_skill_response(packet: &Packet, skill_id: u32, caster: u32) -> bool {
    if !packet.is_sc(sc::SCSkillStartedPacket) && !packet.is_sc(sc::SCSkillFiredPacket) {
        return false;
    }
    let mut stream = packet.stream();
    stream.read_u32().ok() == Some(skill_id)
        && stream.read_u16().is_ok()
        && stream.read_u8().is_ok()
        && stream.read_bc().ok() == Some(caster)
}

fn skill_response_result(packet: &Packet) -> anyhow::Result<u8> {
    if packet.is_sc(sc::SCSkillFiredPacket) {
        return Ok(0);
    }
    ensure!(
        packet.is_sc(sc::SCSkillStartedPacket),
        "server reported an error instead of a skill response"
    );
    let mut stream = packet.stream();
    stream.read_u32()?;
    stream.read_u16()?;
    let caster_kind = stream.read_u8()?;
    SkillCaster::read(caster_kind, &mut stream)?;
    let target_kind = stream.read_u8()?;
    SkillCastTarget::read(target_kind, &mut stream)?;
    let object_kind = stream.read_u8()?;
    SkillObject::read(object_kind, &mut stream)?;
    stream.read_u16()?;
    stream.read_u16()?;
    stream.read_u8()?;
    let flags = stream.read_u8()?;
    Ok(if flags & 1 != 0 { stream.read_u8()? } else { 0 })
}

#[cfg(test)]
mod skill_response_tests {
    use super::*;
    use crate::game::LEVEL_GAME;

    #[test]
    fn npc_casts_and_other_skills_cannot_satisfy_a_player_response() {
        let packet = Packet {
            level: LEVEL_GAME,
            opcode: sc::SCSkillFiredPacket,
            body: vec![2, 0, 0, 0, 10, 0, 0, 1, 0, 0],
        };
        assert!(own_skill_response(&packet, 2, 1));
        assert!(!own_skill_response(&packet, 2, 2));
        assert!(!own_skill_response(&packet, 3, 1));
    }

    #[test]
    fn skill_result_precedes_the_optional_uint_in_the_1_2_packet() {
        let packet = Packet {
            level: LEVEL_GAME,
            opcode: sc::SCSkillStartedPacket,
            body: vec![
                2, 0, 0, 0, 10, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 5, 15, 0x34, 0x12, 0,
                0,
            ],
        };
        assert_eq!(skill_response_result(&packet).unwrap(), 15);
        let truncated = Packet {
            body: vec![2, 0, 0],
            ..packet
        };
        assert!(skill_response_result(&truncated).is_err());
    }
}

#[cfg(test)]
mod npc_return_tests {
    use super::*;
    use aaemu_commons::Marshal;
    use aaemu_game::models::char_data::ActiveQuest;
    use aaemu_game::systems::quests::model::{acceptor, kind};
    use aaemu_game::systems::quests::packets::{
        SCQuestContextStartedPacket, SCQuestContextUpdatedPacket,
    };

    use crate::game::LEVEL_GAME;

    fn quest(template_id: u32, quest_status: u8) -> ActiveQuest {
        ActiveQuest {
            id: 7,
            template_id,
            status: quest_status,
            objectives: [1, 0, 0, 0, 0],
            step: kind::PROGRESS,
            acceptor_type: acceptor::NPC,
            component_id: 1001,
            acceptor_id: 3597,
            time_ms: ActiveQuest::DATETIME_MIN_MS,
        }
    }

    #[test]
    fn sc_quests_packet_rows_decode_the_active_quest_layout() {
        let mut body = PacketStream::new();
        body.write_i32(1);
        quest(330, status::PROGRESS).write(&mut body, 0);
        assert_eq!(
            quest_rows(body.as_bytes()).unwrap(),
            vec![(330, status::PROGRESS)]
        );
    }

    #[test]
    fn sc_quests_packet_rows_decode_multiple_rows_in_order() {
        let mut body = PacketStream::new();
        body.write_i32(2);
        quest(330, status::PROGRESS).write(&mut body, 0);
        quest(331, status::READY).write(&mut body, 0);
        assert_eq!(
            quest_rows(body.as_bytes()).unwrap(),
            vec![(330, status::PROGRESS), (331, status::READY)]
        );
    }

    #[test]
    fn sc_quests_packet_rejects_negative_and_malformed_counts() {
        let mut negative = PacketStream::new();
        negative.write_i32(-1);
        assert!(quest_rows(negative.as_bytes()).is_err());

        assert!(quest_rows(&[]).is_err());

        let mut body = PacketStream::new();
        body.write_i32(2);
        quest(330, status::PROGRESS).write(&mut body, 0);
        quest(331, status::READY).write(&mut body, 0);
        let mut bytes = body.into_bytes();
        bytes.truncate(bytes.len() - 8);
        assert!(quest_rows(&bytes).is_err());
    }

    #[test]
    fn read_quest_row_parses_incrementally_and_errors_on_truncation() {
        let mut body = PacketStream::new();
        quest(330, status::PROGRESS).write(&mut body, 0);
        quest(331, status::READY).write(&mut body, 0);
        let mut bytes = body.into_bytes();
        bytes.truncate(bytes.len() - 8);

        let mut s = PacketStream::from_bytes(bytes);
        assert_eq!(read_quest_row(&mut s).unwrap(), (330, status::PROGRESS));
        assert!(read_quest_row(&mut s).is_err());
    }

    #[test]
    fn quest_started_and_updated_bodies_identify_the_quest_template() {
        let mut body = PacketStream::new();
        SCQuestContextStartedPacket {
            quest: quest(330, status::INVALID),
            component_id: 1001,
            now_ms: 0,
        }
        .write(&mut body);
        let started = Packet {
            level: LEVEL_GAME,
            opcode: sc::SCQuestContextStartedPacket,
            body: body.into_bytes(),
        };
        assert_eq!(quest_template_id(&started).unwrap(), 330);

        let mut body = PacketStream::new();
        SCQuestContextUpdatedPacket::new(quest(330, status::PROGRESS), 0, 0).write(&mut body);
        let updated = Packet {
            level: LEVEL_GAME,
            opcode: sc::SCQuestContextUpdatedPacket,
            body: body.into_bytes(),
        };
        assert_eq!(quest_template_id(&updated).unwrap(), 330);
    }

    #[test]
    fn movement_echo_object_id_is_the_leading_bc() {
        let mut body = PacketStream::new();
        body.write_bc(0x1234).write_u8(1);
        let p = Packet {
            level: LEVEL_GAME,
            opcode: sc::SCOneUnitMovementPacket,
            body: body.into_bytes(),
        };
        assert_eq!(packet_obj_id(&p).unwrap(), 0x1234);
    }
}
