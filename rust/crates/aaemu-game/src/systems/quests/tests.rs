//! Quest subsystem tests: byte-exact packets, state machine tests per act type (on
//! hand-built quest data), the packet / event flow through a test client, the real
//! compact.sqlite3 loader validation and the port of `AAEmu.IntegrationTests` `QuestTests`.

use std::sync::{Arc, Mutex};

use aaemu_commons::PacketStream;

use super::data::QuestData;
use super::engine::{Ctx, QuestEnv, UnitSnapshot};
use super::events::{self, QuestEvent};
use super::hooks::QuestHooks;
use super::model::{
    acceptor, detail, kind, status, ItemCreationDefinition, ObjectiveStatus, QuestSupplies,
};
use super::packets::*;
use super::spheres::SphereQuest;
use super::state::QuestHandle;
use super::templates::{ActKind, QuestComponentTemplate, QuestTemplate};
use crate::data::items::{ItemData, ItemTemplate};
use crate::data::GameData;
use crate::models::char_data::{ActiveQuest, CompletedQuest};
use crate::models::character::Character;
use crate::models::item::Item;
use crate::net::packet::{encode, ServerPacket};
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::{Entity, PlayerRef, WorldObject};

// ----- recording hooks -----

#[derive(Default)]
struct Rec {
    calls: Mutex<Vec<String>>,
    /// Template ids `is_auto_equip_trade_pack` answers true for.
    trade_packs: Vec<u32>,
    /// Failed mails reported by `send_quest_reward_mails`.
    failed_mails: usize,
    team: Mutex<Vec<PlayerRef>>,
}

impl Rec {
    fn push(&self, s: String) {
        self.calls.lock().unwrap().push(s);
    }
    fn take(&self) -> Vec<String> {
        std::mem::take(&mut *self.calls.lock().unwrap())
    }
}

impl QuestHooks for Rec {
    fn add_exp(&self, _: &GameServer, _: Option<&PlayerRef>, c: &mut Character, exp: i32, a: bool) {
        c.experience += exp;
        self.push(format!("exp {exp} {a}"));
    }
    fn change_money(&self, _: &GameServer, _: Option<&PlayerRef>, c: &mut Character, amount: i32) {
        c.money += i64::from(amount);
        self.push(format!("money {amount}"));
    }
    fn change_game_points(
        &self,
        _: &GameServer,
        _: Option<&PlayerRef>,
        _: &mut Character,
        kind: u8,
        amount: i32,
    ) {
        self.push(format!("points {kind} {amount}"));
    }
    fn change_labor(
        &self,
        _: &GameServer,
        _: Option<&PlayerRef>,
        _: &mut Character,
        amount: i16,
        a: i32,
    ) {
        self.push(format!("labor {amount} {a}"));
    }
    fn add_crime(&self, _: &GameServer, _: Option<&PlayerRef>, _: &mut Character, points: i16) {
        self.push(format!("crime {points}"));
    }
    fn acquire_default_item(
        &self,
        _: &GameServer,
        _: Option<&PlayerRef>,
        c: &mut Character,
        task: u8,
        template_id: u32,
        count: i32,
        grade: u8,
    ) -> bool {
        give_item(c, template_id, count);
        self.push(format!("item {task} {template_id} {count} {grade}"));
        true
    }
    fn try_equip_new_backpack(
        &self,
        _: &GameServer,
        _: Option<&PlayerRef>,
        _: &mut Character,
        task: u8,
        template_id: u32,
        count: i32,
        grade: u8,
    ) -> bool {
        self.push(format!("backpack {task} {template_id} {count} {grade}"));
        true
    }
    fn consume_item(
        &self,
        _: &GameServer,
        _: Option<&PlayerRef>,
        c: &mut Character,
        task: u8,
        template_id: u32,
        count: i32,
        _: Option<u64>,
    ) -> i32 {
        let removed = take_items(c, template_id, count);
        self.push(format!("consume {task} {template_id} {count}"));
        removed
    }
    fn send_quest_reward_mails(
        &self,
        _: &GameServer,
        _: Option<&PlayerRef>,
        _: &mut Character,
        quest: u32,
        items: &[ItemCreationDefinition],
        copper: i32,
    ) -> usize {
        self.push(format!("mail {quest} {} {copper}", items.len()));
        self.failed_mails
    }
    fn is_auto_equip_trade_pack(&self, _: &GameServer, template_id: u32) -> bool {
        self.trade_packs.contains(&template_id)
    }
    fn use_skill(&self, _: &GameServer, caster: &WorldObject, skill_id: u32, target: &WorldObject) {
        self.push(format!(
            "skill {} {skill_id} {}",
            caster.obj_id(),
            target.obj_id()
        ));
    }
    fn set_npc_faction(&self, _: &GameServer, npc: &crate::world::NpcRef, faction_id: u32) {
        self.push(format!("faction {} {faction_id}", npc.obj_id));
    }
    fn team_members(&self, _: &GameServer, _: &PlayerRef) -> Vec<PlayerRef> {
        self.team.lock().unwrap().clone()
    }
}

static NEXT_ITEM: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);

fn give_item(c: &mut Character, template_id: u32, count: i32) {
    let t = ItemTemplate::plain(template_id, 1000);
    let id = NEXT_ITEM.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    let mut item = Item::create(id, &t, count, 0, 1, &ItemData::default(), 0);
    let bag = c.inventory.bag().clone();
    let mut g = bag.lock().unwrap();
    item.slot = g.get_unused_slot(-1);
    g.items.push(item);
    g.update_free_slot_count();
}

fn take_items(c: &mut Character, template_id: u32, count: i32) -> i32 {
    let bag = c.inventory.bag().clone();
    let mut g = bag.lock().unwrap();
    let mut left = count;
    for i in g.items.iter_mut().filter(|i| i.template_id == template_id) {
        let n = left.min(i.count);
        i.count -= n;
        left -= n;
    }
    g.items.retain(|i| i.count > 0);
    g.update_free_slot_count();
    count - left
}

// ----- hand-built quest data -----

fn quest(id: u32) -> QuestTemplate {
    QuestTemplate {
        id,
        detail_id: detail::NORMAL,
        ..QuestTemplate::default()
    }
}

fn component(id: u32, quest_id: u32, kind_id: u8) -> QuestComponentTemplate {
    QuestComponentTemplate {
        id,
        quest_id,
        kind_id,
        npc_ai_id: 1,
        ..QuestComponentTemplate::default()
    }
}

/// Quest 100: Start (accept from NPC 500) -> Supply (item 7000 x1) -> Progress (hunt NPC 600
/// x2) -> Ready (report to NPC 500) -> Reward (exp 300, copper 50, item 9000).
fn main_quest(d: &mut QuestData) {
    d.add_template(QuestTemplate {
        level: 10,
        ..quest(100)
    });
    d.add_component(component(1001, 100, kind::START));
    d.add_act(1001, 1, 1, 0, ActKind::ConAcceptNpc { npc_id: 500 });
    d.add_component(component(1002, 100, kind::SUPPLY));
    d.add_act(
        1002,
        2,
        1,
        1,
        ActKind::SupplyItem {
            item_id: 7000,
            grade_id: 0,
            show_action_bar: true,
            cleanup: true,
            drop_when_destroy: true,
            destroy_when_drop: true,
        },
    );
    d.add_component(component(1003, 100, kind::PROGRESS));
    d.add_act(
        1003,
        3,
        1,
        2,
        ActKind::ObjMonsterHunt {
            npc_id: 600,
            use_alias: false,
            alias_id: 0,
            highlight_doodad_id: 0,
            highlight_doodad_phase: -1,
        },
    );
    d.add_component(component(1004, 100, kind::READY));
    d.add_act(
        1004,
        4,
        1,
        0,
        ActKind::ConReportNpc {
            npc_id: 500,
            use_alias: false,
            alias_id: 0,
        },
    );
    d.add_component(component(1005, 100, kind::REWARD));
    d.add_act(1005, 5, 1, 0, ActKind::SupplyExp { exp: 300 });
    d.add_act(1005, 6, 1, 0, ActKind::SupplyCopper { amount: 50 });
    d.add_act(
        1005,
        7,
        2,
        1,
        ActKind::SupplyItem {
            item_id: 9000,
            grade_id: 1,
            show_action_bar: false,
            cleanup: false,
            drop_when_destroy: false,
            destroy_when_drop: false,
        },
    );
    d.supplies.insert(
        10,
        QuestSupplies {
            id: 10,
            level: 10,
            exp: 980,
            copper: 152,
        },
    );
}

fn server_with(build: impl FnOnce(&mut QuestData)) -> (GameServer, Arc<Rec>) {
    let mut d = QuestData::default();
    build(&mut d);
    d.finish();
    let mut data = GameData::empty();
    data.systems.quests = d;
    let server = test_server_with(data);
    let rec = Arc::new(Rec::default());
    server.systems.quests.hooks.install(rec.clone());
    (server, rec)
}

/// Runs `f` on a data-level context (no player: packets land in `ctx.sent`, evaluations in
/// the local queue).
fn with_ctx<R>(
    server: &GameServer,
    c: &mut Character,
    env: QuestEnv,
    f: impl FnOnce(&mut Ctx<'_>) -> R,
) -> R {
    let mut st = std::mem::take(&mut c.quest_state);
    st.loaded = true;
    let r = {
        let mut ctx = Ctx {
            server,
            data: &server.game_data.systems.quests,
            hooks: server.systems.quests.hooks(),
            player: None,
            c,
            st: &mut st,
            env,
            now_ms: 1_000_000,
            deferred: Vec::new(),
            buffs_changed: false,
            sent: Vec::new(),
        };
        let r = f(&mut ctx);
        ctx.sync_rows();
        r
    };
    c.quest_state = st;
    r
}

/// `DoQueuedEvaluations` for the local queue.
fn drain(ctx: &mut Ctx<'_>) {
    while !ctx.st.local_queue.is_empty() {
        let h = ctx.st.local_queue.remove(0);
        if ctx.st.quest(h).is_none() {
            continue;
        }
        ctx.starting_evaluation(h);
        ctx.run_current_step(h);
    }
}

fn ops(ctx: &mut Ctx<'_>) -> Vec<u16> {
    std::mem::take(&mut ctx.sent)
        .into_iter()
        .map(|p| p.0)
        .collect()
}

fn active(ctx: &Ctx<'_>, quest_id: u32) -> QuestHandle {
    ctx.st.active_handle(quest_id).expect("quest active")
}

fn character() -> Character {
    test_character(1, "Tester", 100.0, 100.0)
}

// ----- byte-exact packets -----

fn row() -> ActiveQuest {
    ActiveQuest {
        id: 0x11,
        template_id: 100,
        status: status::PROGRESS,
        objectives: [1, 2, 0, 0, 0],
        step: kind::PROGRESS,
        acceptor_type: acceptor::NPC,
        component_id: 1003,
        acceptor_id: 500,
        time_ms: ActiveQuest::DATETIME_MIN_MS,
    }
}

/// `Quest.Write` spelled out.
fn quest_bytes(s: &mut PacketStream, q: &ActiveQuest, left_time: i32, now_ms: i64) {
    s.write_i64(i64::from(q.id))
        .write_u32(q.template_id)
        .write_u8(q.status);
    for o in q.objectives {
        s.write_i32(o);
    }
    s.write_bool(false)
        .write_bc(0)
        .write_u32(0)
        .write_bc(0)
        .write_bc(0)
        .write_i32(left_time)
        .write_u32(if left_time == -1 { 0 } else { q.component_id })
        .write_i64(0)
        .write_i64(now_ms / 1000)
        .write_u8(q.acceptor_type)
        .write_u32(q.acceptor_id);
}

fn body(p: &dyn ServerPacket) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write_body(&mut s);
    s.into_bytes()
}

#[test]
fn sc_quest_context_packets_bytes() {
    let now = 1_700_000_000_123;
    let p = SCQuestContextStartedPacket {
        quest: row(),
        component_id: 1001,
        now_ms: now,
    };
    let mut s = PacketStream::new();
    quest_bytes(&mut s, &row(), -1, now);
    s.write_u32(1001);
    assert_eq!(body(&p), s.into_bytes());
    assert_eq!(p.opcode(), sc::SCQuestContextStartedPacket);
    // framing: level 1
    let f = encode(&p);
    assert_eq!(f[1], 1);

    // Updated with a running timer: LeftTime + ComponentId
    let mut q = row();
    q.time_ms = now + 60_000;
    let p = SCQuestContextUpdatedPacket::new(q.clone(), 1003, now);
    let mut s = PacketStream::new();
    quest_bytes(&mut s, &q, 60_000, now);
    s.write_u32(1003)
        .write_i32(0)
        .write_i32(0)
        .write_i32(0)
        .write_i32(0);
    assert_eq!(body(&p), s.into_bytes());
    assert_eq!(p.opcode(), sc::SCQuestContextUpdatedPacket);

    let body8 = [1, 2, 3, 4, 5, 6, 7, 8];
    let p = SCQuestContextCompletedPacket {
        quest_id: 100,
        body: body8,
        component_id: 0,
    };
    let mut s = PacketStream::new();
    s.write_u32(100).write_raw(&body8).write_u32(0);
    assert_eq!(body(&p), s.into_bytes());
    assert_eq!(p.opcode(), sc::SCQuestContextCompletedPacket);

    let p = SCQuestContextResetPacket {
        quest_id: 130,
        body: body8,
        component_id: 2,
    };
    let mut s = PacketStream::new();
    s.write_u32(130).write_raw(&body8).write_u32(2);
    assert_eq!(body(&p), s.into_bytes());
    assert_eq!(p.opcode(), sc::SCQuestContextResetPacket);
}

#[test]
fn sc_small_quest_packets_bytes() {
    let p = SCQuestRewardedByMailPacket {
        quests: vec![100, 0x01020304],
    };
    assert_eq!(body(&p), vec![2, 100, 0, 0, 0, 4, 3, 2, 1]);
    assert_eq!(p.opcode(), sc::SCQuestRewardedByMailPacket);
    let p = SCDoodadQuestAcceptPacket {
        doodad_obj_id: 0x030201,
        quest_id: 7,
    };
    assert_eq!(body(&p), vec![1, 2, 3, 7, 0, 0, 0]);
    assert_eq!(p.opcode(), sc::SCDoodadQuestAcceptPacket);
    let p = SCTutorialSavedPacket {
        id: 5,
        body: [0x20, 0, 0, 0, 0, 0, 0, 0],
    };
    assert_eq!(body(&p), vec![5, 0, 0, 0, 0x20, 0, 0, 0, 0, 0, 0, 0]);
    assert_eq!(p.opcode(), sc::SCTutorialSavedPacket);
    let p = SCAppellationGainedPacket { appellation_id: 9 };
    assert_eq!(body(&p), vec![9, 0, 0, 0]);
    assert_eq!(p.opcode(), sc::SCAppellationGainedPacket);
}

#[test]
fn sc_quests_packet_writes_long_ids() {
    let now = 1_700_000_000_000;
    let p = crate::packets::world::SCQuestsPacket {
        quests: vec![row()],
        now_ms: now,
    };
    let mut s = PacketStream::new();
    s.write_i32(1);
    quest_bytes(&mut s, &row(), -1, now);
    assert_eq!(body(&p), s.into_bytes());
}

#[test]
fn cs_quest_packets_decode() {
    use crate::net::registry::ClientPacket;
    let mut s = PacketStream::new();
    s.write_u32(100).write_bc(0x200).write_bc(0).write_u32(0);
    let p = CSStartQuestContextPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
    assert_eq!(
        p,
        CSStartQuestContextPacket {
            quest_context_id: 100,
            npc_obj_id: 0x200,
            doodad_obj_id: 0,
            sphere_id: 0
        }
    );
    let mut s = PacketStream::new();
    s.write_u32(100)
        .write_bc(0x200)
        .write_bc(0x300)
        .write_i32(-1);
    let p =
        CSCompleteQuestContextPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
    assert_eq!(
        (p.npc_obj_id, p.doodad_obj_id, p.selected),
        (0x200, 0x300, -1)
    );
    let mut s = PacketStream::new();
    s.write_bc(0x200)
        .write_u32(100)
        .write_u32(1003)
        .write_u32(3);
    let p = CSQuestTalkMadePacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
    assert_eq!(
        (
            p.npc_obj_id,
            p.quest_context_id,
            p.quest_comp_id,
            p.quest_act_id
        ),
        (0x200, 100, 1003, 3)
    );
    let mut s = PacketStream::new();
    s.write_u32(5).write_bc(9).write_i32(2);
    let p =
        CSTryQuestCompleteAsLetItDonePacket::read(&mut PacketStream::from_bytes(s.into_bytes()))
            .unwrap();
    assert_eq!((p.id, p.obj_id, p.selected), (5, 9, 2));
    let p =
        CSDropQuestContextPacket::read(&mut PacketStream::from_bytes(vec![1, 0, 0, 0])).unwrap();
    assert_eq!(p.quest_id, 1);
    let p = CSSaveTutorialPacket::read(&mut PacketStream::from_bytes(vec![2, 0, 0, 0])).unwrap();
    assert_eq!(p.id, 2);
    let p = CSQuestStartWithPacket::read(&mut PacketStream::from_bytes(vec![
        1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0,
    ]))
    .unwrap();
    assert_eq!((p.unk_id, p.unk_id2, p.unk_id3), (1, 2, 3));
    assert!(CSStartedCinemaPacket::read(&mut PacketStream::new()).is_ok());
}

// ----- state machine -----

#[tokio::test]
async fn full_quest_lifecycle_on_data() {
    let (server, rec) = server_with(main_quest);
    let mut c = character();
    c.unit.current_target = 0x200;
    let env = QuestEnv {
        target: Some(UnitSnapshot {
            obj_id: 0x200,
            npc_template_id: Some(500),
            is_unit: true,
            base_unit_type: 1,
            ..UnitSnapshot::default()
        }),
        ..QuestEnv::default()
    };
    with_ctx(&server, &mut c, env, |ctx| {
        assert!(ctx.add_quest(100, false, acceptor::NPC, 500));
        // Still targeting NPC 500 would turn the quest in as soon as it is Ready
        // (QuestActConReportNpc.RunAct checks the current target)
        ctx.env.target = None;
        let h = active(ctx, 100);
        // AddQuest: SCQuestContextStarted (StartQuest), then RunCurrentStep: Start -> None ->
        // Supply (step changes request an evaluation that is queued after QuestInitialized)
        assert_eq!(
            ops(ctx),
            vec![
                sc::SCQuestContextStartedPacket,
                sc::SCQuestContextUpdatedPacket
            ]
        );
        assert_eq!(ctx.st.quest(h).unwrap().step, kind::SUPPLY);
        // Supply step: the item goes into the reward pool and is distributed
        drain(ctx);
        assert_eq!(rec.take(), vec!["item 38 7000 1 0"]);
        let q = ctx.st.quest(h).unwrap();
        assert_eq!((q.step, q.status), (kind::PROGRESS, status::PROGRESS));
        // Progress: two kills of NPC 600
        ctx.dispatch(&QuestEvent::MonsterHunt {
            npc_id: 600,
            count: 1,
        })
        .unwrap();
        ctx.dispatch(&QuestEvent::MonsterHunt {
            npc_id: 601,
            count: 1,
        })
        .unwrap();
        drain(ctx);
        assert_eq!(ctx.st.quest(h).unwrap().objectives[0], 1);
        assert_eq!(ctx.objective_status(h), ObjectiveStatus::NotReady);
        ctx.dispatch(&QuestEvent::MonsterHunt {
            npc_id: 600,
            count: 1,
        })
        .unwrap();
        drain(ctx);
        let q = ctx.st.quest(h).unwrap();
        assert_eq!((q.step, q.status), (kind::READY, status::READY));
        assert_eq!(q.objectives[0], 2);
        ops(ctx);
        // Report at NPC 500
        ctx.dispatch(&QuestEvent::ReportNpc {
            quest_id: 100,
            npc_id: 500,
            selected: 1,
        })
        .unwrap();
        drain(ctx);
        // Reward: acts (exp, copper -> pool, item -> pool), then base level supplies
        assert_eq!(
            rec.take(),
            vec![
                "exp 300 true",
                "item 38 9000 1 1",
                "exp 980 true",
                "money 202",
                // DropQuest after the reward: QuestCleanup (cleanup) and QuestDropped
                // (destroy_when_drop) both consume the supply item
                "consume 39 7000 1",
                "consume 39 7000 1"
            ]
        );
        assert!(!ctx.has_quest(100));
        assert!(ctx.has_quest_completed(100));
        let o = ops(ctx);
        assert!(o.contains(&sc::SCQuestContextCompletedPacket));
        assert_eq!(*o.last().unwrap(), sc::SCQuestContextCompletedPacket);
    });
    assert_eq!(c.quests.removed, vec![100]);
    assert!(c.quests.active.is_empty());
    assert_eq!(
        c.quests.completed,
        vec![CompletedQuest {
            id: 1,
            body: vec![0, 0, 0, 0, 0x10, 0, 0, 0]
        }]
    );
}

#[tokio::test]
async fn add_quest_rules() {
    let (server, rec) = server_with(|d| {
        main_quest(d);
        // Quest 101: no start component
        d.add_template(quest(101));
        d.add_component(component(1011, 101, kind::PROGRESS));
        // Quest 102: start component needs level 20
        d.add_template(quest(102));
        d.add_component(component(1021, 102, kind::START));
        d.add_act(1021, 21, 21, 0, ActKind::ConAutoComplete);
        d.unit_reqs
            .by_owner
            .entry("QuestComponent".into())
            .or_default()
            .insert(
                1021,
                vec![super::unit_reqs::UnitReq {
                    id: 1,
                    owner_id: 1021,
                    owner_type: "QuestComponent".into(),
                    kind: super::unit_reqs::req_kind::LEVEL,
                    value1: 20,
                    value2: 0,
                }],
            );
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        assert!(!ctx.add_quest(999, false, 0, 0), "unknown template");
        assert!(!ctx.add_quest(101, false, 0, 0), "no Start step");
        assert!(!ctx.has_quest(101));
        assert!(!ctx.add_quest(102, false, 0, 0), "unit reqs");
        ctx.c.unit.level = 20;
        assert!(ctx.add_quest(102, false, 0, 0));
        assert!(!ctx.add_quest(102, false, 0, 0), "duplicate");
        // Completed, not repeatable -> QuestDailyLimit
        ctx.set_completed_quest_flag(100, true);
        ops(ctx);
        assert!(!ctx.add_quest(100, false, acceptor::NPC, 500));
        let sent = std::mem::take(&mut ctx.sent);
        assert_eq!(sent.len(), 1);
        assert_eq!(sent[0].0, sc::SCErrorMsgPacket);
        assert_eq!(&sent[0].1[..4], &[0x6d, 0x02, 0x6d, 0x02]); // 621
                                                                // forcibly: re-adds; C# calls DropQuest(questId, true) (update, not forcibly), so the
                                                                // completed flag stays set
        assert!(ctx.add_quest(100, true, acceptor::NPC, 500));
        assert!(ctx.has_quest_completed(100));
    });
    rec.take();
}

#[tokio::test]
async fn drop_quest_runs_cleanup_and_drop_components() {
    let (server, rec) = server_with(|d| {
        main_quest(d);
        let mut drop = component(1006, 100, kind::DROP);
        drop.skill_id = 77;
        drop.skill_self = true;
        d.add_component(drop);
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        assert!(ctx.add_quest(100, false, acceptor::NPC, 500));
        drain(ctx);
        rec.take();
        ops(ctx);
        let id = ctx.st.quest(active(ctx, 100)).unwrap().id;
        ctx.drop_quest(100, true, false);
        assert!(!ctx.has_quest(100));
        // Cleanup (SupplyItem cleanup) then Drop (destroy_when_drop) consume the supply item
        assert_eq!(rec.take(), vec!["consume 39 7000 1", "consume 39 7000 1"]);
        // Drop(update): one update packet with component 0
        assert_eq!(ops(ctx), vec![sc::SCQuestContextUpdatedPacket]);
        // Drop component skill (UseSkill of the first Drop component)
        assert!(ctx
            .deferred
            .contains(&super::engine::Deferred::UseSkillSelf { skill_id: 77 }));
        assert!(id > 0);
    });
    assert_eq!(c.quests.removed, vec![100]);
}

#[tokio::test]
async fn item_gather_and_cleanup() {
    let (server, rec) = server_with(|d| {
        d.add_template(quest(200));
        d.add_component(component(2001, 200, kind::START));
        d.add_act(2001, 1, 1, 0, ActKind::ConAutoComplete);
        d.add_component(component(2002, 200, kind::PROGRESS));
        d.add_act(
            2002,
            2,
            1,
            3,
            ActKind::ObjItemGather {
                item_id: 8000,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
                use_alias: false,
                alias_id: 0,
                cleanup: true,
                drop_when_destroy: true,
                destroy_when_drop: false,
            },
        );
        d.add_component(component(2003, 200, kind::READY));
        d.add_act(2003, 3, 3, 0, ActKind::ConAutoComplete);
        d.add_component(component(2004, 200, kind::REWARD));
        d.add_act(2004, 4, 1, 0, ActKind::SupplyCopper { amount: 5 });
    });
    let mut c = character();
    give_item(&mut c, 8000, 1);
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        assert!(ctx.add_quest(200, false, 0, 0));
        let h = active(ctx, 200);
        drain(ctx);
        // InitializeAction counted the item already in the bag
        assert_eq!(ctx.st.quest(h).unwrap().objectives[0], 1);
        assert_eq!(ctx.st.quest(h).unwrap().step, kind::PROGRESS);
        give_item(ctx.c, 8000, 5);
        ctx.dispatch(&QuestEvent::ItemGather {
            item_id: 8000,
            count: 5,
        })
        .unwrap();
        // capped at MaxObjective (3)
        assert_eq!(ctx.st.quest(h).unwrap().objectives[0], 3);
        drain(ctx);
        // Ready (auto complete) -> Reward -> completed; cleanup consumes min(objective, max)
        assert!(!ctx.has_quest(200));
        assert!(ctx.has_quest_completed(200));
    });
    assert_eq!(rec.take(), vec!["money 5", "consume 39 8000 3"]);
}

#[tokio::test]
async fn talk_and_team_share() {
    let (server, rec) = server_with(|d| {
        d.add_template(quest(300));
        d.add_component(component(3001, 300, kind::START));
        d.add_act(3001, 1, 1, 0, ActKind::ConAutoComplete);
        d.add_component(component(3002, 300, kind::PROGRESS));
        d.add_act(
            3002,
            2,
            1,
            0,
            ActKind::ObjTalk {
                npc_id: 500,
                team_share: true,
                item_id: 0,
                use_alias: false,
                alias_id: 0,
            },
        );
        d.add_act(
            3002,
            3,
            1,
            0,
            ActKind::ObjTalkNpcGroup {
                npc_group_id: 9,
                use_alias: false,
                alias_id: 0,
            },
        );
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        ctx.add_quest(300, false, 0, 0);
        drain(ctx);
        let h = active(ctx, 300);
        // objective indices: talk 0, group talk 1
        ctx.dispatch(&QuestEvent::TalkMade {
            quest_id: 300,
            npc_id: 500,
            component_id: 3002,
            act_id: 2,
            source_player_id: 1,
        })
        .unwrap();
        assert_eq!(ctx.st.quest(h).unwrap().objectives[..2], [1, 0]);
        assert!(matches!(
            ctx.deferred.last(),
            Some(super::engine::Deferred::TeamEvent { .. })
        ));
        ctx.deferred.clear();
        // From a team mate: no further sharing
        ctx.dispatch(&QuestEvent::TalkMade {
            quest_id: 300,
            npc_id: 500,
            component_id: 3002,
            act_id: 2,
            source_player_id: 2,
        })
        .unwrap();
        assert!(ctx.deferred.is_empty());
        ctx.dispatch(&QuestEvent::TalkNpcGroupMade {
            quest_id: 300,
            npc_group_id: 9,
            npc_id: 501,
            component_id: 3002,
            act_id: 3,
        })
        .unwrap();
        assert_eq!(ctx.st.quest(h).unwrap().objectives[..2], [1, 1]);
        drain(ctx);
        // No Ready / Reward components: Progress -> Ready -> Reward (both missing) completes
        assert!(!ctx.has_quest(300));
        assert!(ctx.has_quest_completed(300));
    });
    rec.take();
}

/// Quest with one progress act of `k` (objective count `count`); returns the handle.
fn single_act_quest(d: &mut QuestData, qid: u32, count: i32, k: ActKind) {
    d.add_template(quest(qid));
    d.add_component(component(qid * 10 + 1, qid, kind::START));
    d.add_act(
        qid * 10 + 1,
        qid * 10 + 1,
        qid * 10 + 1,
        0,
        ActKind::ConAutoComplete,
    );
    d.add_component(component(qid * 10 + 2, qid, kind::PROGRESS));
    d.add_act(qid * 10 + 2, qid * 10 + 2, qid * 10 + 2, count, k);
    d.add_component(component(qid * 10 + 3, qid, kind::READY));
    d.add_act(
        qid * 10 + 3,
        qid * 10 + 3,
        qid * 10 + 3,
        0,
        ActKind::ConReportNpc {
            npc_id: 1,
            use_alias: false,
            alias_id: 0,
        },
    );
}

/// Starts `qid` and runs it to its Progress step.
fn start(ctx: &mut Ctx<'_>, qid: u32) -> QuestHandle {
    assert!(ctx.add_quest(qid, false, 0, 0), "quest {qid} added");
    drain(ctx);
    active(ctx, qid)
}

fn step_of(ctx: &Ctx<'_>, h: QuestHandle) -> u8 {
    ctx.st.quest(h).map_or(0, |q| q.step)
}

#[tokio::test]
async fn objective_acts_progress_on_their_events() {
    let (server, _rec) = server_with(|d| {
        single_act_quest(
            d,
            1,
            2,
            ActKind::ObjCraft {
                craft_id: 55,
                use_alias: false,
                alias_id: 0,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
            },
        );
        single_act_quest(
            d,
            2,
            1,
            ActKind::ObjItemUse {
                item_id: 66,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
                use_alias: false,
                alias_id: 0,
                drop_when_destroy: false,
            },
        );
        single_act_quest(
            d,
            3,
            1,
            ActKind::ObjExpressFire {
                express_key_id: 7,
                npc_group_id: 3,
                use_alias: false,
                alias_id: 0,
            },
        );
        single_act_quest(
            d,
            4,
            2,
            ActKind::ObjInteraction {
                wi_id: 1,
                doodad_id: 88,
                use_alias: false,
                team_share: false,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
                alias_id: 0,
                phase: 0,
            },
        );
        single_act_quest(
            d,
            5,
            2,
            ActKind::ObjMonsterGroupHunt {
                quest_monster_group_id: 3,
                use_alias: false,
                alias_id: 0,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
            },
        );
        single_act_quest(
            d,
            6,
            1,
            ActKind::ObjZoneMonsterHunt {
                zone_id: 12,
                use_alias: false,
                alias_id: 0,
            },
        );
        single_act_quest(
            d,
            7,
            0,
            ActKind::ObjLevel {
                level: 11,
                use_alias: false,
                alias_id: 0,
            },
        );
        single_act_quest(
            d,
            8,
            0,
            ActKind::ObjCinema {
                cinema_id: 44,
                use_alias: false,
                alias_id: 0,
            },
        );
        d.group_npcs.add(3, 600);
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        let craft = start(ctx, 1);
        ctx.dispatch(&QuestEvent::Craft { craft_id: 54 }).unwrap();
        ctx.dispatch(&QuestEvent::Craft { craft_id: 55 }).unwrap();
        ctx.dispatch(&QuestEvent::Craft { craft_id: 55 }).unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, craft), kind::READY);

        let use_ = start(ctx, 2);
        ctx.dispatch(&QuestEvent::ItemUse { item_id: 66 }).unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, use_), kind::READY);

        let express = start(ctx, 3);
        ctx.dispatch(&QuestEvent::ExpressFire {
            npc_id: 601,
            emotion_id: 7,
        })
        .unwrap();
        ctx.dispatch(&QuestEvent::ExpressFire {
            npc_id: 600,
            emotion_id: 8,
        })
        .unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, express), kind::PROGRESS);
        ctx.dispatch(&QuestEvent::ExpressFire {
            npc_id: 600,
            emotion_id: 7,
        })
        .unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, express), kind::READY);

        let inter = start(ctx, 4);
        ctx.dispatch(&QuestEvent::Interaction {
            doodad_id: 88,
            source_player_id: 1,
        })
        .unwrap();
        drain(ctx);
        assert_eq!(ctx.st.quest(inter).unwrap().objectives[0], 1);
        ctx.dispatch(&QuestEvent::Interaction {
            doodad_id: 88,
            source_player_id: 1,
        })
        .unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, inter), kind::READY);

        let group = start(ctx, 5);
        ctx.dispatch(&QuestEvent::MonsterGroupHunt {
            group_id: 3,
            count: 2,
        })
        .unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, group), kind::READY);

        // ZoneMonsterHunt: `count > Count` (strictly more kills than the count)
        let zone = start(ctx, 6);
        let victim = super::engine::KillUnitInfo {
            obj_id: 9,
            npc_template_id: Some(600),
            ..Default::default()
        };
        let killer = super::engine::KillUnitInfo {
            obj_id: 1,
            character_id: Some(1),
            ..Default::default()
        };
        let zk = QuestEvent::ZoneKill {
            zone_group_id: 12,
            killer,
            victim,
        };
        ctx.dispatch(&zk).unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, zone), kind::PROGRESS);
        ctx.dispatch(&zk).unwrap();
        drain(ctx);
        // C# bug mirrored: the objective is capped at MaxObjective (= Count) but RunAct wants
        // `count > Count`, so a non-score zone monster hunt never completes
        assert_eq!(ctx.st.quest(zone).unwrap().objectives[0], 1);
        assert_eq!(step_of(ctx, zone), kind::PROGRESS);

        let level = start(ctx, 7);
        ctx.c.unit.level = 11;
        ctx.dispatch(&QuestEvent::LevelUp).unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, level), kind::READY);

        let cinema = start(ctx, 8);
        ctx.dispatch(&QuestEvent::CinemaEnded { cinema_id: 0 })
            .unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, cinema), kind::PROGRESS);
        ctx.dispatch(&QuestEvent::CinemaStarted { cinema_id: 0 })
            .unwrap();
        assert_eq!(ctx.st.currently_playing_cinema_id, 44);
        ctx.dispatch(&QuestEvent::CinemaEnded { cinema_id: 44 })
            .unwrap();
        assert_eq!(ctx.st.currently_playing_cinema_id, 0);
        drain(ctx);
        assert_eq!(step_of(ctx, cinema), kind::READY);
    });
}

#[tokio::test]
async fn zone_kill_faction_and_level_rules() {
    let (server, _rec) = server_with(|d| {
        single_act_quest(
            d,
            9,
            2,
            ActKind::ObjZoneKill {
                count_player_kill: 0,
                count_npc: 2,
                zone_id: 0,
                team_share: false,
                use_alias: false,
                alias_id: 0,
                lvl_min: 0,
                lvl_max: 0,
                is_party: false,
                lvl_min_npc: 5,
                lvl_max_npc: 10,
                pc_faction_id: 0,
                pc_faction_exclusive: false,
                npc_faction_id: 115,
                npc_faction_exclusive: false,
            },
        );
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        let h = start(ctx, 9);
        let me = super::engine::KillUnitInfo {
            obj_id: 1,
            character_id: Some(1),
            ..Default::default()
        };
        let npc = |faction, level| super::engine::KillUnitInfo {
            obj_id: 50,
            npc_template_id: Some(600),
            faction_id: faction,
            level,
            ..Default::default()
        };
        let kill = |ctx: &mut Ctx<'_>, killer: super::engine::KillUnitInfo, v| {
            ctx.dispatch(&QuestEvent::ZoneKill {
                zone_group_id: 1,
                killer,
                victim: v,
            })
            .unwrap();
        };
        kill(ctx, me.clone(), npc(114, 7)); // wrong faction
        kill(ctx, me.clone(), npc(115, 11)); // level too high
        let other = super::engine::KillUnitInfo {
            obj_id: 2,
            character_id: Some(2),
            ..Default::default()
        };
        kill(ctx, other, npc(115, 7)); // not party: other killer ignored
        assert_eq!(ctx.st.quest(h).unwrap().objectives[0], 0);
        kill(ctx, me.clone(), npc(115, 7));
        kill(ctx, me, npc(115, 5));
        drain(ctx);
        assert_eq!(step_of(ctx, h), kind::READY);
    });
}

#[tokio::test]
async fn aggro_act_only_reacts_to_kill_target() {
    let (server, _rec) = server_with(|d| {
        single_act_quest(
            d,
            10,
            3,
            ActKind::ObjAggro {
                range: 0,
                rank1: 10,
                rank1_ratio: 100,
                rank1_item: true,
                rank2: 50,
                rank2_ratio: 50,
                rank2_item: false,
                rank3: 100,
                rank3_ratio: 10,
                rank3_item: false,
                use_alias: false,
                alias_id: 0,
            },
        );
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        assert!(ctx.add_quest(10, false, acceptor::NPC, 600));
        drain(ctx);
        let h = active(ctx, 10);
        let npc = super::engine::KillUnitInfo {
            obj_id: 50,
            npc_template_id: Some(600),
            aggro_percent: 40.0,
            ..Default::default()
        };
        // DoDie's OnKill carries Killer / Victim only: ignored (mirrored C# behaviour)
        ctx.dispatch(&QuestEvent::Kill {
            target: None,
            killer: None,
            victim: Some(npc.clone()),
        })
        .unwrap();
        assert_eq!(ctx.st.quest(h).unwrap().objectives[0], 0);
        ctx.dispatch(&QuestEvent::Kill {
            target: Some(npc),
            killer: None,
            victim: None,
        })
        .unwrap();
        let q = ctx.st.quest(h).unwrap();
        assert_eq!(q.objectives[0], 2);
        assert_eq!(q.reward_ratio, 0.5);
        assert!(!q.allow_item_rewards);
    });
}

#[tokio::test]
async fn check_sphere_throws_obj_sphere_progresses() {
    let (server, _rec) = server_with(|d| {
        single_act_quest(
            d,
            11,
            0,
            ActKind::ObjSphere {
                sphere_id: 0,
                npc_id: 0,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
                use_alias: false,
                alias_id: 0,
            },
        );
        single_act_quest(d, 12, 0, ActKind::CheckSphere { sphere_id: 0 });
    });
    let mut c = character();
    let sphere = |quest_id, component_id| SphereQuest {
        zone_id: 1,
        world_id: "main_world".into(),
        quest_id,
        component_id,
        radius: 5.0,
        xyz: (0.0, 0.0, 0.0),
    };
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        let h = start(ctx, 11);
        ctx.dispatch(&QuestEvent::EnterSphere {
            sphere: sphere(11, 999),
        })
        .unwrap();
        assert_eq!(ctx.st.quest(h).unwrap().objectives[0], 0);
        ctx.dispatch(&QuestEvent::EnterSphere {
            sphere: sphere(11, 112),
        })
        .unwrap();
        assert_eq!(ctx.st.quest(h).unwrap().objectives[0], 1);
        ctx.dispatch(&QuestEvent::ExitSphere {
            sphere: sphere(11, 112),
        })
        .unwrap();
        assert_eq!(ctx.st.quest(h).unwrap().objectives[0], 0);
        // QuestActCheckSphere is not an objective: SetObjective(Objectives[255]) throws and
        // the invocation list stops there.
        let _h12 = start(ctx, 12);
        let r = ctx.dispatch(&QuestEvent::EnterSphere {
            sphere: sphere(12, 0),
        });
        assert!(r.is_err());
    });
}

#[tokio::test]
async fn check_timer_registers_and_fails() {
    let (server, _rec) = server_with(|d| {
        single_act_quest(
            d,
            13,
            1,
            ActKind::ObjItemUse {
                item_id: 2,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
                use_alias: false,
                alias_id: 0,
                drop_when_destroy: false,
            },
        );
        d.add_act(
            132,
            135,
            135,
            0,
            ActKind::CheckTimer {
                limit_time: 60_000,
                force_change_component: false,
                next_component: 0,
                play_skill: false,
                skill_id: 0,
                check_buff: false,
                buff_id: 0,
                sustain_buff: false,
                timer_npc_id: 0,
                is_skill_player: false,
            },
        );
        single_act_quest(
            d,
            14,
            1,
            ActKind::ObjItemUse {
                item_id: 1,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
                use_alias: false,
                alias_id: 0,
                drop_when_destroy: false,
            },
        );
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        let h = start(ctx, 13);
        assert!(server.systems.quests.has_quest_timer(1, 13));
        assert_eq!(ctx.st.quest(h).unwrap().time_ms, 1_000_000 + 60_000);
        // The quest's row carries the time: LeftTime 60000 at "now"
        assert_eq!(ctx.st.quest(h).unwrap().row().left_time(1_000_000), 60_000);
        ctx.dispatch(&QuestEvent::TimerExpired { quest_id: 13 })
            .unwrap();
        let q = ctx.st.quest(h).unwrap();
        assert_eq!(q.step, kind::FAIL);
        // Step change to Fail removed the timer
        assert!(!server.systems.quests.has_quest_timer(1, 13));
        drain(ctx);
        // No Fail component: RunCurrentStep finds no step, GoToNextStep never sets Failed
        assert_eq!(ctx.st.quest(h).unwrap().status, status::PROGRESS);
    });
    // Any quest's end-state step change removes the timer (C# does not check args.QuestId)
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        start(ctx, 13);
        assert!(server.systems.quests.has_quest_timer(1, 13));
        let other = start(ctx, 14);
        ctx.dispatch(&QuestEvent::ItemUse { item_id: 1 }).unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, other), kind::READY);
        assert!(!server.systems.quests.has_quest_timer(1, 13));
    });
}

#[tokio::test]
async fn let_it_done_and_score() {
    let (server, rec) = server_with(|d| {
        d.add_template(QuestTemplate {
            let_it_done: true,
            level: 0,
            ..quest(20)
        });
        d.add_component(component(201, 20, kind::START));
        d.add_act(201, 1, 1, 0, ActKind::ConAutoComplete);
        d.add_component(component(202, 20, kind::PROGRESS));
        d.add_act(
            202,
            2,
            2,
            4,
            ActKind::ObjCraft {
                craft_id: 1,
                use_alias: false,
                alias_id: 0,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
            },
        );
        d.add_component(component(203, 20, kind::REWARD));
        d.add_act(203, 3, 3, 0, ActKind::SupplyExp { exp: 10 });
        // Score quest
        d.add_template(QuestTemplate {
            score: 10,
            ..quest(21)
        });
        d.add_component(component(211, 21, kind::START));
        d.add_act(211, 11, 11, 0, ActKind::ConAutoComplete);
        d.add_component(component(212, 21, kind::PROGRESS));
        d.add_act(
            212,
            12,
            12,
            3,
            ActKind::ObjCraft {
                craft_id: 2,
                use_alias: false,
                alias_id: 0,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
            },
        );
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        let h = start(ctx, 20);
        // MaxObjective with LetItDone: ceil(4 * 1.5) = 6
        assert_eq!(ctx.objective_status(h), ObjectiveStatus::NotReady);
        ctx.try_complete_quest_as_let_it_done(20, 0);
        assert_eq!(step_of(ctx, h), kind::PROGRESS);
        for _ in 0..2 {
            ctx.dispatch(&QuestEvent::Craft { craft_id: 1 }).unwrap();
        }
        drain(ctx);
        // 2 >= 4/2: CanEarlyComplete, LetItDone progress never goes forward by itself
        assert_eq!(ctx.objective_status(h), ObjectiveStatus::CanEarlyComplete);
        assert_eq!(step_of(ctx, h), kind::PROGRESS);
        assert_eq!(ctx.st.quest(h).unwrap().status, status::PROGRESS);
        for _ in 0..10 {
            ctx.dispatch(&QuestEvent::Craft { craft_id: 1 }).unwrap();
        }
        assert_eq!(ctx.st.quest(h).unwrap().objectives[0], 6);
        assert_eq!(ctx.objective_status(h), ObjectiveStatus::Overachieved);
        ctx.try_complete_quest_as_let_it_done(20, 0);
        drain(ctx);
        assert!(!ctx.has_quest(20));

        // Score quest: score = objectives * count; MaxObjective = score / count + 1 = 4
        let h = start(ctx, 21);
        for _ in 0..3 {
            ctx.dispatch(&QuestEvent::Craft { craft_id: 2 }).unwrap();
        }
        drain(ctx);
        assert_eq!(step_of(ctx, h), kind::PROGRESS);
        assert!((ctx.objective_percent(h) - 0.9).abs() < 1e-6);
        ctx.dispatch(&QuestEvent::Craft { craft_id: 2 }).unwrap();
        drain(ctx);
        // 12 >= 10: completed (no Ready / Reward step)
        assert!(!ctx.has_quest(21));
    });
    assert_eq!(rec.take(), vec!["exp 10 true"]);
}

#[tokio::test]
async fn report_doodad_and_complete_quest_acts() {
    let (server, _rec) = server_with(|d| {
        d.add_template(quest(30));
        d.add_component(component(301, 30, kind::START));
        d.add_act(301, 1, 1, 0, ActKind::ConAcceptDoodad { doodad_id: 70 });
        d.add_component(component(302, 30, kind::PROGRESS));
        // (no count column in the DB: with Count 0 the report would be accepted at once)
        d.add_act(
            302,
            2,
            2,
            1,
            ActKind::ObjCompleteQuest {
                quest_id: 31,
                accept_with: false,
                use_alias: false,
                alias_id: 0,
            },
        );
        d.add_component(component(303, 30, kind::READY));
        d.add_act(
            303,
            3,
            3,
            0,
            ActKind::ConReportDoodad {
                doodad_id: 70,
                use_alias: false,
                alias_id: 0,
            },
        );
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        assert!(ctx.add_quest(30, false, acceptor::DOODAD, 70));
        drain(ctx);
        let h = active(ctx, 30);
        assert_eq!(step_of(ctx, h), kind::PROGRESS);
        // ReportDoodad before the objective is met is ignored
        ctx.dispatch(&QuestEvent::ReportDoodad {
            quest_id: 30,
            doodad_id: 70,
            selected: 0,
        })
        .unwrap();
        drain(ctx);
        assert_eq!(step_of(ctx, h), kind::PROGRESS);
        ctx.set_completed_quest_flag(31, true);
        ctx.request_evaluation(h);
        drain(ctx);
        assert_eq!(step_of(ctx, h), kind::READY);
        ctx.dispatch(&QuestEvent::ReportDoodad {
            quest_id: 30,
            doodad_id: 70,
            selected: 0,
        })
        .unwrap();
        drain(ctx);
        assert!(!ctx.has_quest(30));
        assert!(ctx.has_quest_completed(30));
        // ConReportDoodad.FinalizeQuest re-subscribes (`+=`): the handler stays registered
        // on the finalized quest, which is collected at the end of the operation.
    });
}

#[tokio::test]
async fn selective_rewards_and_mail_when_bag_full() {
    let (server, rec) = server_with(|d| {
        d.add_template(quest(40));
        d.add_component(component(401, 40, kind::START));
        d.add_act(401, 1, 1, 0, ActKind::ConAutoComplete);
        d.add_component(component(402, 40, kind::REWARD));
        d.add_act(
            402,
            2,
            1,
            1,
            ActKind::SupplySelectiveItem {
                item_id: 11,
                grade_id: 0,
            },
        );
        d.add_act(
            402,
            3,
            2,
            2,
            ActKind::SupplySelectiveItem {
                item_id: 12,
                grade_id: 0,
            },
        );
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        assert!(ctx.add_quest(40, false, 0, 0));
        let h = active(ctx, 40);
        // Start -> None -> Supply -> Progress -> Ready -> Reward: Reward is the first valid
        assert_eq!(step_of(ctx, h), kind::REWARD);
        ctx.st.quest_mut(h).unwrap().selected_reward_index = 2;
        drain(ctx);
        assert!(!ctx.has_quest(40));
    });
    assert_eq!(rec.take(), vec!["item 38 12 2 0"]);
    // Bag full: mail + SCQuestRewardedByMailPacket
    let mut c = character();
    c.inventory = crate::models::character::Inventory::standalone(1, 0, 0);
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        assert!(ctx.add_quest(40, false, 0, 0));
        let h = active(ctx, 40);
        ctx.st.quest_mut(h).unwrap().selected_reward_index = 1;
        ops(ctx);
        drain(ctx);
        let o = ops(ctx);
        assert!(o.contains(&sc::SCQuestRewardedByMailPacket));
    });
    assert_eq!(rec.take(), vec!["mail 40 1 0"]);
}

#[tokio::test]
async fn daily_reset_and_item_destroy() {
    let (server, _rec) = server_with(|d| {
        d.add_template(QuestTemplate {
            detail_id: detail::DAILY,
            ..quest(130)
        });
        d.add_template(quest(131));
        main_quest(d);
    });
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        ctx.set_completed_quest_flag(130, true);
        ctx.set_completed_quest_flag(131, true);
        ctx.reset_daily_quests(true);
        assert!(!ctx.has_quest_completed(130));
        assert!(ctx.has_quest_completed(131));
        let sent = std::mem::take(&mut ctx.sent);
        assert_eq!(sent.len(), 1);
        let mut s = PacketStream::new();
        s.write_u32(130)
            .write_raw(&[0x08, 0, 0, 0, 0, 0, 0, 0])
            .write_u32(2); // 131 still set
        assert_eq!(sent[0], (sc::SCQuestContextResetPacket, s.into_bytes()));
        // Destroying the supply item (DropWhenDestroy) of quest 100 drops it
        assert!(ctx.add_quest(100, false, acceptor::NPC, 500));
        drain(ctx);
        ctx.on_quest_item_manually_destroyed(7000, 100);
        assert!(!ctx.has_quest(100));
        // Login reset: leave time yesterday
        ctx.set_completed_quest_flag(130, true);
        ctx.c.leave_time = ctx.now_ms / 1000 - 86_400;
        ctx.check_daily_reset_at_login();
        assert!(!ctx.has_quest_completed(130));
    });
}

#[tokio::test]
async fn load_from_rows_restores_quests() {
    let (server, _rec) = server_with(main_quest);
    let mut c = character();
    let mut row = row();
    row.time_ms = 0;
    row.objectives = [2, 0, 0, 0, 0];
    c.quests.active = vec![
        row.clone(),
        ActiveQuest {
            template_id: 4242, // unknown template: skipped (and not saved again)
            ..row.clone()
        },
    ];
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        ctx.load_from_rows();
        let h = active(ctx, 100);
        let q = ctx.st.quest(h).unwrap();
        assert_eq!(
            (q.step, q.status, q.objectives),
            (kind::PROGRESS, status::PROGRESS, [2, 0, 0, 0, 0])
        );
        assert_eq!((q.component_id, q.acceptor_id), (1003, 500));
        // QuestInitialized + RequestEvaluation queued one evaluation
        assert_eq!(ctx.st.local_queue, vec![h]);
        drain(ctx);
        // objective 2 >= 2: Ready
        assert_eq!(step_of(ctx, h), kind::READY);
    });
    assert_eq!(c.quests.active.len(), 1);
    assert_eq!(c.quests.active[0].step, kind::READY);
}

// ----- flow test through the packet handlers and the event API -----

fn decode(frames: Vec<(u16, Vec<u8>)>) -> Vec<u16> {
    frames.into_iter().map(|f| f.0).collect()
}

#[tokio::test]
async fn npc_quest_flow_through_packets_and_events() {
    let (server, rec) = server_with(main_quest);
    let mut client = TestClient::new(&server, 1, 1001);
    let player = spawn_player(&server, &client, character(), 0x100);
    client.conn.active_char = Some(player.clone());
    let world = server.world.world(0).unwrap();
    let mut giver = crate::models::npc::Npc::new(500);
    giver.unit.transform.set_xyz(105.0, 105.0, 100.0);
    let giver = Entity::new(0x200, giver);
    world.spawn(&giver, &server.view_ctx());
    let mut wolf = crate::models::npc::Npc::new(600);
    wolf.unit.transform.set_xyz(110.0, 110.0, 100.0);
    let wolf = Entity::new(0x201, wolf);
    world.spawn(&wolf, &server.view_ctx());
    client.drain();

    // Accept the quest at the NPC (CSStartQuestContextPacket with the NPC's obj id)
    let start = CSStartQuestContextPacket {
        quest_context_id: 100,
        npc_obj_id: 0x200,
        doodad_obj_id: 0,
        sphere_id: 0,
    };
    use crate::net::registry::ClientPacket;
    start.handle(&server, &mut client.conn).await.unwrap();
    assert_eq!(player.data().unit.current_target, 0x200);
    // Look away: while NPC 500 is targeted the quest is turned in as soon as it is Ready
    player.data().unit.current_target = 0;
    let frames = client.drain();
    assert_eq!(
        decode(frames.clone()),
        vec![
            sc::SCQuestContextStartedPacket,
            sc::SCQuestContextUpdatedPacket
        ]
    );
    // SCQuestContextStartedPacket: quest id 1 (QuestIdManager), template 100, status Invalid,
    // acceptor NPC 500, component 1001
    let b = &frames[0].1;
    assert_eq!(&b[..8], &1i64.to_le_bytes());
    assert_eq!(&b[8..12], &100u32.to_le_bytes());
    assert_eq!(b[12], status::INVALID);
    assert_eq!(b[b.len() - 9], acceptor::NPC);
    assert_eq!(&b[b.len() - 8..b.len() - 4], &500u32.to_le_bytes());
    assert_eq!(&b[b.len() - 4..], &1001u32.to_le_bytes());

    // QuestManagerRunQueueTask
    server.systems.quests.run_queued_evaluations(&server);
    assert_eq!(rec.take(), vec!["item 38 7000 1 0"]);
    assert_eq!(player.data().quests.active[0].step, kind::PROGRESS);
    client.drain();

    // Kill objective through the event API
    events::on_npc_killed(&server, &player, &wolf);
    server.systems.quests.run_queued_evaluations(&server);
    assert_eq!(player.data().quests.active[0].objectives[0], 1);
    events::on_npc_killed(&server, &player, &wolf);
    server.systems.quests.run_queued_evaluations(&server);
    {
        let c = player.data();
        assert_eq!(c.quests.active[0].objectives[0], 2);
        assert_eq!(c.quests.active[0].step, kind::READY);
        assert_eq!(c.quests.active[0].status, status::READY);
    }
    let ops = client.drain_ops();
    assert!(ops.iter().all(|o| *o == sc::SCQuestContextUpdatedPacket));

    // Turn in at the NPC (CSCompleteQuestContextPacket)
    let complete = CSCompleteQuestContextPacket {
        quest_context_id: 100,
        npc_obj_id: 0x200,
        doodad_obj_id: 0,
        selected: 0,
    };
    complete.handle(&server, &mut client.conn).await.unwrap();
    server.systems.quests.run_queued_evaluations(&server);
    // Rewards hooks invoked
    assert_eq!(
        rec.take(),
        vec![
            "exp 300 true",
            "item 38 9000 1 1",
            "exp 980 true",
            "money 202",
            "consume 39 7000 1",
            "consume 39 7000 1"
        ]
    );
    let frames = client.drain();
    let last = frames.last().unwrap();
    assert_eq!(last.0, sc::SCQuestContextCompletedPacket);
    let mut s = PacketStream::new();
    s.write_u32(100)
        .write_raw(&[0, 0, 0, 0, 0x10, 0, 0, 0])
        .write_u32(0);
    assert_eq!(last.1, s.into_bytes());
    let c = player.data();
    assert!(c.quests.active.is_empty());
    assert_eq!(c.quests.removed, vec![100]);
    assert!(super::engine::has_quest_completed(&c.quests.completed, 100));
    assert_eq!(c.experience, 1280);
    assert_eq!(c.money, 202);
    assert_eq!(server.systems.quests.queued_count(), 0);
}

#[tokio::test]
async fn talk_quest_flow_and_nested_item_events() {
    let (server, rec) = server_with(|d| {
        d.add_template(quest(300));
        d.add_component(component(3001, 300, kind::START));
        d.add_act(3001, 1, 1, 0, ActKind::ConAutoComplete);
        d.add_component(component(3002, 300, kind::PROGRESS));
        d.add_act(
            3002,
            2,
            1,
            0,
            ActKind::ObjTalk {
                npc_id: 500,
                team_share: false,
                item_id: 0,
                use_alias: false,
                alias_id: 0,
            },
        );
        d.add_component(component(3003, 300, kind::REWARD));
        d.add_act(
            3003,
            3,
            1,
            1,
            ActKind::SupplyItem {
                item_id: 8000,
                grade_id: 0,
                show_action_bar: false,
                cleanup: false,
                drop_when_destroy: false,
                destroy_when_drop: false,
            },
        );
    });
    let mut client = TestClient::new(&server, 1, 1001);
    let player = spawn_player(&server, &client, character(), 0x100);
    client.conn.active_char = Some(player.clone());
    let world = server.world.world(0).unwrap();
    let mut n = crate::models::npc::Npc::new(500);
    n.unit.transform.set_xyz(105.0, 105.0, 100.0);
    world.spawn(Entity::new(0x200, n), &server.view_ctx());
    client.drain();

    assert_eq!(events::add_quest(&server, &player, 300, false), Some(true));
    server.systems.quests.run_queued_evaluations(&server);
    let talk = CSQuestTalkMadePacket {
        npc_obj_id: 0x200,
        quest_context_id: 300,
        quest_comp_id: 3002,
        quest_act_id: 2,
    };
    use crate::net::registry::ClientPacket;
    talk.handle(&server, &mut client.conn).await.unwrap();
    server.systems.quests.run_queued_evaluations(&server);
    assert!(player.data().quests.active.is_empty());
    assert_eq!(rec.take(), vec!["item 38 8000 1 0"]);
    // A hook raising a quest event for the same character is queued (no deadlock)
    struct Nested;
    impl QuestHooks for Nested {
        fn acquire_default_item(
            &self,
            server: &GameServer,
            player: Option<&PlayerRef>,
            _: &mut Character,
            _: u8,
            t: u32,
            n: i32,
            _: u8,
        ) -> bool {
            events::on_items_acquired(server, player.unwrap(), t, n);
            true
        }
    }
    server.systems.quests.hooks.install(Arc::new(Nested));
    assert_eq!(events::add_quest(&server, &player, 300, false), Some(false)); // completed
    let r = events::with_quests(&server, &player, None, |ctx| {
        ctx.hooks.clone().acquire_default_item(
            ctx.server,
            ctx.player.clone().as_ref(),
            ctx.c,
            0,
            1,
            1,
            0,
        )
    });
    assert_eq!(r, Some(true));
}

#[tokio::test]
async fn select_hook_loads_rows_and_sends() {
    let (server, _rec) = server_with(main_quest);
    let client = TestClient::new(&server, 1, 1001);
    let mut c = character();
    let mut r = row();
    r.time_ms = 0;
    c.quests.active = vec![r];
    let player = spawn_player(&server, &client, c, 0x100);
    super::on_character_selected(&server, &player);
    assert!(player.data().quest_state.loaded);
    assert!(events::has_quest(&player, 100));
    assert_eq!(server.systems.quests.queued_count(), 1);
    super::on_leave_world(&server, 1);
}

// ----- real compact.sqlite3 -----

fn real_db() -> Option<rusqlite::Connection> {
    let path = std::env::var("AAEMU_COMPACT_DB").ok()?;
    Some(
        rusqlite::Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .expect("open compact db"),
    )
}

fn real_data(conn: &rusqlite::Connection) -> QuestData {
    let files = crate::data::client_files::ClientFileManager::new();
    QuestData::load_from(conn, &files).expect("quest tables load without errors")
}

#[tokio::test]
#[ignore = "needs AAEMU_COMPACT_DB"]
async fn real_compact_db_quest_tables() {
    let Some(conn) = real_db() else {
        return;
    };
    let d = real_data(&conn);
    let count =
        |sql: &str| -> usize { conn.query_row(sql, [], |r| r.get::<_, i64>(0)).unwrap() as usize };
    assert_eq!(
        d.templates.len(),
        count("SELECT COUNT(*) FROM quest_contexts WHERE category_id <> 45")
    );
    assert_eq!(
        d.components.len(),
        count("SELECT COUNT(*) FROM quest_components c JOIN quest_contexts q ON q.id = c.quest_context_id WHERE q.category_id <> 45")
    );
    assert_eq!(
        d.base_acts.len(),
        count("SELECT COUNT(*) FROM quest_acts a JOIN quest_components c ON c.id = a.quest_component_id JOIN quest_contexts q ON q.id = c.quest_context_id WHERE q.category_id <> 45")
    );
    assert_eq!(
        d.supplies.len(),
        count("SELECT COUNT(*) FROM quest_supplies")
    );
    assert_eq!(
        d.group_items.rows(),
        count("SELECT COUNT(*) FROM quest_item_group_items")
    );
    assert_eq!(
        d.group_npcs.rows(),
        count("SELECT COUNT(*) FROM quest_monster_npcs")
    );
    assert_eq!(d.unit_reqs.rows, count("SELECT COUNT(*) FROM unit_reqs"));
    assert_eq!(d.spheres.len(), count("SELECT COUNT(*) FROM spheres"));
    assert_eq!(
        d.sphere_quests.len(),
        count("SELECT COUNT(*) FROM sphere_quests")
    );
    // Values from the 1.2 compact.sqlite3 this port was validated against
    assert_eq!(d.templates.len(), 4775);
    assert_eq!(d.components.len(), 17720);
    assert_eq!(d.base_acts.len(), 19047);
    assert_eq!(d.acts.len(), 19047);
    let n = |c: &str| d.acts_by_type.get(c).map_or(0, Vec::len);
    for (class, expected) in [
        ("QuestActCheckCompleteComponent", 10),
        ("QuestActCheckGuard", 4),
        ("QuestActCheckSphere", 1),
        ("QuestActCheckTimer", 44),
        ("QuestActConAcceptComponent", 316),
        ("QuestActConAcceptDoodad", 203),
        ("QuestActConAcceptItem", 340),
        ("QuestActConAcceptItemGain", 23),
        ("QuestActConAcceptLevelUp", 3),
        ("QuestActConAcceptNpc", 3072),
        ("QuestActConAcceptNpcKill", 1055),
        ("QuestActConAcceptSphere", 421),
        ("QuestActConAutoComplete", 1086),
        ("QuestActConReportDoodad", 15),
        ("QuestActConReportJournal", 467),
        ("QuestActConReportNpc", 2828),
        ("QuestActEtcItemObtain", 66),
        ("QuestActObjAbilityLevel", 11),
        ("QuestActObjAggro", 30),
        ("QuestActObjCinema", 12),
        ("QuestActObjCompleteQuest", 53),
        ("QuestActObjCraft", 173),
        ("QuestActObjExpressFire", 48),
        ("QuestActObjInteraction", 289),
        ("QuestActObjItemGather", 1492),
        ("QuestActObjItemGroupGather", 4),
        ("QuestActObjItemGroupUse", 1),
        ("QuestActObjItemUse", 243),
        ("QuestActObjLevel", 1),
        ("QuestActObjMateLevel", 6),
        ("QuestActObjMonsterGroupHunt", 528),
        ("QuestActObjMonsterHunt", 589),
        ("QuestActObjSphere", 125),
        ("QuestActObjTalk", 131),
        ("QuestActObjTalkNpcGroup", 2),
        ("QuestActObjZoneKill", 110),
        ("QuestActSupplyAppellation", 218),
        ("QuestActSupplyCopper", 1474),
        ("QuestActSupplyCrimePoint", 7),
        ("QuestActSupplyExp", 1100),
        ("QuestActSupplyHonorPoint", 46),
        ("QuestActSupplyItem", 1928),
        ("QuestActSupplyJuryPoint", 4),
        ("QuestActSupplyLivingPoint", 12),
        ("QuestActSupplyLp", 5),
        ("QuestActSupplyRemoveItem", 13),
        ("QuestActSupplySelectiveItem", 438),
    ] {
        assert_eq!(n(class), expected, "{class}");
    }
    assert_eq!(
        (d.supplies.len(), d.group_items.rows(), d.group_npcs.rows()),
        (55, 158, 2771)
    );
    assert_eq!(
        (d.unit_reqs.rows, d.spheres.len(), d.sphere_quests.len()),
        (13354, 1908, 1079)
    );
    // Every component's acts point back to it; objective indices fit `Objectives[5]`
    let mut over = 0;
    for (cid, c) in &d.components {
        for &a in &c.acts {
            assert_eq!(d.acts[a].component_id, *cid);
            assert_eq!(d.acts[a].quest_id, c.quest_id);
            if d.acts[a].objective_index != 0xFF && d.acts[a].objective_index >= 5 {
                over += 1;
            }
        }
    }
    eprintln!(
        "quests {} components {} acts {} (objective index >= 5: {over}) supplies {} item groups {} npc groups {} unit_reqs {} spheres {} sphere_quests {}",
        d.templates.len(),
        d.components.len(),
        d.acts.len(),
        d.supplies.len(),
        d.group_items.len(),
        d.group_npcs.len(),
        d.unit_reqs.rows,
        d.spheres.len(),
        d.sphere_quests.len()
    );
    // Every quest can be instantiated and started on a fresh character
    let mut gd = GameData::empty();
    gd.systems.quests = d;
    let server = test_server_with(gd);
    let ids: Vec<u32> = server.game_data.systems.quests.template_order.clone();
    let mut started = 0;
    for qid in ids {
        let mut c = character();
        with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
            let h = ctx.new_quest(qid, 1);
            if ctx.start_quest(h) {
                started += 1;
            }
        });
    }
    eprintln!("quests with a Start step: {started}");
}

// ----- AAEmu.IntegrationTests/Models/Game/Quests/QuestTests.cs -----

/// `GetQuestIdsWithComponentKindContainingActDetailType(params QuestCondition[])`.
fn quest_ids_with(
    conn: &rusqlite::Connection,
    conds: &[(u8, &str, Option<bool>, Option<bool>)],
) -> Vec<u32> {
    if conds.is_empty() {
        return Vec::new();
    }
    let mut from = String::new();
    let mut wh = String::new();
    for (i, (k, t, has_skill, has_buff)) in conds.iter().enumerate() {
        let n = i + 1;
        from.push_str(&format!(
            " inner join quest_components qc{n} on qc{n}.quest_context_id = qc.quest_context_id inner join quest_acts qa{n} on qc{n}.id = qa{n}.quest_component_id"
        ));
        wh.push_str(&format!(
            " and qc{n}.component_kind_id = {k} and qa{n}.act_detail_type = '{t}'"
        ));
        if let Some(s) = has_skill {
            wh.push_str(&format!(
                " and qc{n}.skill_id {} 0",
                if *s { ">" } else { "=" }
            ));
        }
        if let Some(b) = has_buff {
            wh.push_str(&format!(
                " and qc{n}.buff_id {} 0",
                if *b { ">" } else { "=" }
            ));
        }
    }
    let sql = format!(
        "select DISTINCT qc.quest_context_id from quest_components qc {from} where qc.id == qc1.id {wh} order by qc.quest_context_id"
    );
    let mut st = conn.prepare(&sql).unwrap();
    st.query_map([], |r| r.get::<_, i64>(0))
        .unwrap()
        .map(|v| v.unwrap() as u32)
        .collect()
}

/// `RemoveSphereAndNotImplementedQuests`. Also drops the ids whose `quest_contexts` row does
/// not exist (orphan components, e.g. 5765): the C# builds `new Quest(null, ..)` for them and
/// `StartQuest` returns false, so the C# tests would fail on them as well.
fn remove_sphere_and_not_implemented(conn: &rusqlite::Connection, ids: Vec<u32>) -> Vec<u32> {
    let mut ex = quest_ids_with(conn, &[(kind::PROGRESS, "QuestActObjSphere", None, None)]);
    ex.extend(quest_ids_with(
        conn,
        &[(kind::START, "QuestActConAcceptComponent", None, None)],
    ));
    let exists = |id: u32| {
        conn.query_row(
            "SELECT COUNT(*) FROM quest_contexts WHERE id = ? AND category_id <> 45",
            [id],
            |r| r.get::<_, i64>(0),
        )
        .unwrap()
            > 0
    };
    ids.into_iter()
        .filter(|i| !ex.contains(i) && exists(*i))
        .collect()
}

/// `SetupQuest`: a fresh character + the quest object (constructor only).
fn setup_quest<R>(
    server: &GameServer,
    qid: u32,
    f: impl FnOnce(&mut Ctx<'_>, QuestHandle) -> R,
) -> R {
    let mut c = character();
    c.id = 1 + qid; // random character id in C#
    with_ctx(server, &mut c, QuestEnv::default(), |ctx| {
        let h = ctx.new_quest(qid, 0);
        f(ctx, h)
    })
}

fn legacy_enabled() -> bool {
    std::env::var("AAEMU_QUEST_LEGACY_TESTS").is_ok()
}

fn real_server() -> Option<(GameServer, rusqlite::Connection)> {
    let conn = real_db()?;
    let mut gd = GameData::empty();
    gd.systems.quests = real_data(&conn);
    Some((test_server_with(gd), conn))
}

/// `Start_WhenQuestStart_AllActsAreQuestActConAcceptNpc_And_TargetNpcIsNotValid_ShouldNotStartQuest`.
/// Skipped in C# too: `StartQuest` only checks for a Start step, so it returns true here.
#[tokio::test]
#[ignore = "skipped in C# (quest system rewrite): StartQuest no longer checks the acceptor"]
async fn start_all_acts_con_accept_npc_target_not_valid_should_not_start() {
    if !legacy_enabled() {
        return;
    }
    let Some((server, conn)) = real_server() else {
        return;
    };
    let mut st = conn
        .prepare("select qc.quest_context_id, qa.act_detail_type, qaNot.act_detail_type from quest_contexts qcx inner join quest_components qc on qcx.id = qc.quest_context_id inner join quest_acts qa on qc.id = qa.quest_component_id and qa.act_detail_type = 'QuestActConAcceptNpc' left join quest_acts qaNot on qc.id = qaNot.quest_component_id and qaNot.act_detail_type <> 'QuestActConAcceptNpc' where qc.component_kind_id = 2 and qaNot.act_detail_type is null group by qc.quest_context_id, qa.act_detail_type order by quest_context_id")
        .unwrap();
    let ids: Vec<u32> = st
        .query_map([], |r| r.get::<_, i64>(0))
        .unwrap()
        .map(|v| v.unwrap() as u32)
        .collect();
    for qid in ids.into_iter().take(500) {
        setup_quest(&server, qid, |ctx, h| {
            assert!(!ctx.start_quest(h), "quest {qid}");
            assert!(ctx
                .sent
                .iter()
                .all(|p| p.0 != sc::SCQuestContextStartedPacket));
            assert!(!ctx
                .deferred
                .iter()
                .any(|d| matches!(d, super::engine::Deferred::UseSkillSelf { .. })));
        });
    }
}

/// `Start_DoodadQuestsSupplyingItem_WhenBagIsNotFull_ShouldAddToCharacterBag`. Skipped in C#:
/// `StartQuest` does not run the Supply step, so nothing is added.
#[tokio::test]
#[ignore = "skipped in C# (quest system rewrite): StartQuest does not supply items"]
async fn start_doodad_quests_supplying_item_bag_not_full_should_add_to_bag() {
    if !legacy_enabled() {
        return;
    }
    let Some((server, conn)) = real_server() else {
        return;
    };
    let ids = quest_ids_with(
        &conn,
        &[
            (kind::START, "QuestActConAcceptDoodad", None, None),
            (kind::SUPPLY, "QuestActSupplyItem", None, None),
        ],
    );
    for qid in remove_sphere_and_not_implemented(&conn, ids) {
        setup_quest(&server, qid, |ctx, h| {
            let r = ctx.start_quest(h);
            let data = ctx.data;
            let t = data.template(qid).unwrap();
            if let Some(c) = t.first_component(&data.components, kind::SUPPLY) {
                if let Some(&a) = c
                    .acts
                    .iter()
                    .find(|&&a| matches!(data.acts[a].kind, ActKind::SupplyItem { .. }))
                {
                    let ActKind::SupplyItem { item_id, .. } = data.acts[a].kind else {
                        unreachable!()
                    };
                    assert_eq!(
                        data.acts[a].count,
                        super::engine::items_count(ctx.c, item_id),
                        "quest {qid}"
                    );
                }
            }
            assert!(r);
        });
    }
}

/// `Start_DoodadQuestsSupplyingItem_WhenBagAndBackIsFull_ShouldAddToCharacterBag`. Skipped in
/// C#: `StartQuest` returns true whenever there is a Start step.
#[tokio::test]
#[ignore = "skipped in C# (quest system rewrite): StartQuest does not check the bag"]
async fn start_doodad_quests_supplying_item_bag_and_back_full_should_not_start() {
    if !legacy_enabled() {
        return;
    }
    let Some((server, conn)) = real_server() else {
        return;
    };
    let ids = quest_ids_with(
        &conn,
        &[
            (kind::START, "QuestActConAcceptDoodad", None, None),
            (kind::SUPPLY, "QuestActSupplyItem", None, None),
        ],
    );
    for qid in remove_sphere_and_not_implemented(&conn, ids) {
        let mut c = character();
        c.inventory = crate::models::character::Inventory::standalone(1, 0, 0);
        with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
            let h = ctx.new_quest(qid, 0);
            assert!(!ctx.start_quest(h), "quest {qid}");
            assert!(ctx.sent.iter().any(|p| p.0 == sc::SCErrorMsgPacket));
        });
    }
}

/// `Start_ActCheckTimer_ShouldStartSchedulerAndSendCharacterMessage` (enabled: passes).
#[tokio::test]
#[ignore = "needs AAEMU_COMPACT_DB"]
async fn start_act_check_timer_should_start_scheduler() {
    let Some((server, conn)) = real_server() else {
        return;
    };
    let ids = quest_ids_with(&conn, &[(kind::START, "QuestActCheckTimer", None, None)]);
    let ids = remove_sphere_and_not_implemented(&conn, ids);
    assert!(!ids.is_empty());
    for qid in ids {
        let char_id = 1 + qid;
        setup_quest(&server, qid, |ctx, h| {
            let data = ctx.data;
            if let Some(a) = data
                .template(qid)
                .and_then(|t| t.first_component(&data.components, kind::START))
                .and_then(|c| {
                    c.acts
                        .iter()
                        .copied()
                        .find(|&a| matches!(data.acts[a].kind, ActKind::ConAcceptNpc { .. }))
                })
            {
                let ActKind::ConAcceptNpc { npc_id } = data.acts[a].kind else {
                    unreachable!()
                };
                ctx.env.target = Some(UnitSnapshot {
                    npc_template_id: Some(npc_id),
                    is_unit: true,
                    ..UnitSnapshot::default()
                });
            }
            let r = ctx.start_quest(h);
            let has_timer = data
                .template(qid)
                .and_then(|t| t.first_component(&data.components, kind::START))
                .is_some_and(|c| {
                    c.acts
                        .iter()
                        .any(|&a| matches!(data.acts[a].kind, ActKind::CheckTimer { .. }))
                });
            if has_timer {
                assert!(
                    server.systems.quests.has_quest_timer(char_id, qid),
                    "quest {qid}"
                );
            }
            assert!(r, "quest {qid}");
        });
    }
}

/// `Start_ActConAcceptNpcWhenTargetingCorrectNpc_ShouldStartQuestSuccessfully` (enabled).
#[tokio::test]
#[ignore = "needs AAEMU_COMPACT_DB"]
async fn start_act_con_accept_npc_targeting_correct_npc_should_start() {
    let Some((server, conn)) = real_server() else {
        return;
    };
    let ids = quest_ids_with(&conn, &[(kind::START, "QuestActConAcceptNpc", None, None)]);
    let ids = remove_sphere_and_not_implemented(&conn, ids);
    assert!(ids.len() > 1000);
    for qid in ids {
        let char_id = 1 + qid;
        setup_quest(&server, qid, |ctx, h| {
            let data = ctx.data;
            let start = data
                .template(qid)
                .and_then(|t| t.first_component(&data.components, kind::START));
            if let Some(a) = start.and_then(|c| {
                c.acts
                    .iter()
                    .copied()
                    .find(|&a| matches!(data.acts[a].kind, ActKind::ConAcceptNpc { .. }))
            }) {
                let ActKind::ConAcceptNpc { npc_id } = data.acts[a].kind else {
                    unreachable!()
                };
                ctx.env.target = Some(UnitSnapshot {
                    npc_template_id: Some(npc_id),
                    is_unit: true,
                    ..UnitSnapshot::default()
                });
            }
            let r = ctx.start_quest(h);
            if start.is_some_and(|c| {
                c.acts
                    .iter()
                    .any(|&a| matches!(data.acts[a].kind, ActKind::CheckTimer { .. }))
            }) {
                assert!(
                    server.systems.quests.has_quest_timer(char_id, qid),
                    "quest {qid}"
                );
            }
            assert!(r, "quest {qid}");
        });
    }
}

/// `UseSkillAndBuff_MockedWorldManager_WhenQuestUseSkill_ShouldUseOnSelfOrTargetNpc`. Skipped
/// in C#: `StartQuest` neither sets the acceptor nor runs the Start components.
#[tokio::test]
#[ignore = "skipped in C# (quest system rewrite): StartQuest does not run the components"]
async fn use_skill_and_buff_mocked_world_should_use_on_self_or_npc() {
    if !legacy_enabled() {
        return;
    }
    let Some((server, conn)) = real_server() else {
        return;
    };
    let ids = quest_ids_with(
        &conn,
        &[(kind::START, "QuestActConAcceptNpc", Some(true), None)],
    );
    for qid in remove_sphere_and_not_implemented(&conn, ids) {
        setup_quest(&server, qid, |ctx, h| {
            assert!(ctx.start_quest(h));
            let q = ctx.st.quest(h).unwrap();
            assert_eq!(q.acceptor_type, acceptor::NPC, "quest {qid}");
            assert!(!ctx.deferred.is_empty(), "quest {qid}: skill used");
        });
    }
}

/// `UseSkillAndBuff_WhenQuestUseSkill_ShouldUseOnSelfOrTargetNpc` (skipped in C#, as above).
#[tokio::test]
#[ignore = "skipped in C# (quest system rewrite): StartQuest does not run the components"]
async fn use_skill_and_buff_should_use_on_self_or_npc() {
    if !legacy_enabled() {
        return;
    }
    let Some((server, conn)) = real_server() else {
        return;
    };
    let ids = quest_ids_with(
        &conn,
        &[(kind::START, "QuestActConAcceptNpc", Some(true), None)],
    );
    for qid in remove_sphere_and_not_implemented(&conn, ids) {
        setup_quest(&server, qid, |ctx, h| {
            assert!(ctx.start_quest(h));
            assert_eq!(
                ctx.st.quest(h).unwrap().acceptor_type,
                acceptor::NPC,
                "quest {qid}"
            );
        });
    }
}

/// `UseSkillAndBuff_WhenQuestUseBuff_ShouldUseOnSelfOrTargetNpc` (skipped in C#: `StartQuest`
/// does not run `UseBuff`).
#[tokio::test]
#[ignore = "skipped in C# (quest system rewrite): StartQuest does not run the components"]
async fn use_skill_and_buff_when_quest_use_buff() {
    if !legacy_enabled() {
        return;
    }
    let Some((server, conn)) = real_server() else {
        return;
    };
    let ids = quest_ids_with(
        &conn,
        &[(kind::START, "QuestActConAcceptNpc", None, Some(true))],
    );
    for qid in remove_sphere_and_not_implemented(&conn, ids) {
        setup_quest(&server, qid, |ctx, h| {
            assert!(ctx.start_quest(h));
            assert!(ctx.buffs_changed, "quest {qid}: buff added");
        });
    }
}
