//! Scripted bot scenarios with assertions on the server responses.

use std::collections::BTreeMap;
use std::time::{Duration, Instant};

use aaemu_game::opcodes::{pp, sc};
use anyhow::{bail, ensure, Context};
use serde::{Deserialize, Serialize};

use crate::game::{GameClient, LEVEL_PROXY};
use crate::login::LoginClient;
use crate::record::Recorder;
use crate::stream::StreamClient;

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
}

impl std::str::FromStr for Kind {
    type Err = anyhow::Error;
    fn from_str(s: &str) -> anyhow::Result<Self> {
        Ok(match s {
            "login" => Self::Login,
            "lobby" => Self::Lobby,
            "full" => Self::Full,
            "load" => Self::Load,
            _ => bail!("unknown scenario '{s}' (login, lobby, full, load)"),
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
    /// Wait for `SCLeaveWorldGrantedPacket` (10 s logout timer) before disconnecting.
    #[serde(default = "default_true")]
    pub wait_leave_granted: bool,
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
fn default_true() -> bool {
    true
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

    if kind == Kind::Full {
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
                game.wait_for("skill response", |p| {
                    p.is_sc(sc::SCSkillStartedPacket) || p.is_sc(sc::SCErrorMsgPacket)
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

    // Back to where we spawned: the position saved on logout (and seen by the next session)
    // then does not depend on where the NPCs wandered.
    game.move_to(p0, 9000).await?;
    let s = r.begin("game.leave");
    game.leave_world(0).await?;
    game.wait_sc(sc::SCPrepareLeaveWorldPacket).await?;
    if cfg.wait_leave_granted {
        game.wait_sc(sc::SCLeaveWorldGrantedPacket).await?;
    }
    game.drain(Duration::from_millis(300), Duration::from_secs(2))
        .await
        .ok();
    r.end(s, "left");
    drop(stream);
    Ok(())
}
