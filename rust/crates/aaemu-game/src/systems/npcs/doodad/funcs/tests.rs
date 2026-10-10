//! Unit tests of the doodad funcs, world interactions and coffers on synthetic doodads.

use std::sync::{Arc, Mutex};

use aaemu_commons::{Marshal, PacketStream};

use super::*;
use crate::data::GameData;
use crate::models::container::{slot_type, ContainerKind, ItemContainer};
use crate::models::doodad::{BondDoodad, Doodad, DoodadCofferState};
use crate::models::item::{Item, ItemRow};
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::systems::npcs::doodad::coffer::{
    self, CSCofferInteractionPacket, SCCofferContentsUpdatePacket,
};
use crate::systems::npcs::doodad::interactions::{
    self, execute_interaction, world_interaction_type as wi,
};
use crate::systems::npcs::doodad::templates::*;
use crate::systems::npcs::hooks::{DoodadSkillCaster, DoodadSkillTarget};
use crate::test_support::*;
use crate::world::{DoodadRef, Entity, PlayerRef};

/// Records the hook calls; return values are configurable.
#[derive(Default)]
struct Rec {
    calls: Mutex<Vec<String>>,
    acquire_ok: bool,
    money_ok: bool,
    loot_pack: Option<bool>,
    item_count: i32,
    has_quest: bool,
    consumed: i32,
}

impl Rec {
    fn push(&self, s: String) {
        self.calls.lock().unwrap().push(s);
    }
    fn calls(&self) -> Vec<String> {
        self.calls.lock().unwrap().clone()
    }
}

impl EconomyBridge for Rec {
    fn acquire_item(
        &self,
        _s: &GameServer,
        _p: &PlayerRef,
        task: u8,
        t: u32,
        c: i32,
        g: i32,
    ) -> bool {
        self.push(format!("acquire {task} {t} {c} {g}"));
        self.acquire_ok
    }
    fn change_money(&self, _s: &GameServer, _p: &PlayerRef, amount: i64, task: u8) -> bool {
        self.push(format!("money {amount} {task}"));
        self.money_ok
    }
    fn give_doodad_loot_pack(
        &self,
        _s: &GameServer,
        _p: &PlayerRef,
        id: u32,
        skill: u32,
        task: u8,
    ) -> Option<bool> {
        self.push(format!("lootpack {id} {skill} {task}"));
        self.loot_pack
    }
    fn item_count(&self, _s: &GameServer, _p: &PlayerRef, t: u32) -> i32 {
        self.push(format!("count {t}"));
        self.item_count
    }
    fn bag_consume_item(&self, _s: &GameServer, _p: &PlayerRef, task: u8, t: u32, c: i32) -> i32 {
        self.push(format!("consume {task} {t} {c}"));
        self.consumed
    }
    fn move_item_to_system_container(
        &self,
        _s: &GameServer,
        _p: &PlayerRef,
        task: u8,
        id: u64,
    ) -> bool {
        self.push(format!("system {task} {id}"));
        true
    }
}

impl SkillsBridge for Rec {
    fn use_doodad_func_skill(
        &self,
        _s: &GameServer,
        caster: &WorldObject,
        caster_obj: DoodadSkillCaster,
        target: Option<&WorldObject>,
        target_obj: DoodadSkillTarget,
        skill_id: u32,
        scheduled: bool,
    ) {
        self.push(format!(
            "skill {} {caster_obj:?} {:?} {target_obj:?} {skill_id} {scheduled}",
            caster.obj_id(),
            target.map(WorldObject::obj_id)
        ));
    }
    fn doodad_func_skill_hit(&self, _s: &GameServer, p: &PlayerRef, d: &WorldObject, skill: u32) {
        self.push(format!("skillhit {} {} {skill}", p.obj_id, d.obj_id()));
    }
    fn add_buff(&self, _s: &GameServer, t: &WorldObject, buff: u32, c: &WorldObject) {
        self.push(format!("buff {} {buff} {}", t.obj_id(), c.obj_id()));
    }
    fn interrupt_skills(&self, _s: &GameServer, u: &WorldObject) {
        self.push(format!("interrupt {}", u.obj_id()));
    }
}

impl QuestsBridge for Rec {
    fn has_quest(&self, _s: &GameServer, _p: &PlayerRef, q: u32) -> bool {
        self.push(format!("hasquest {q}"));
        self.has_quest
    }
    fn do_report_events(&self, _s: &GameServer, p: &PlayerRef, q: u32, npc: u32, d: u32, sel: i32) {
        self.push(format!("report {} {q} {npc} {d} {sel}", p.obj_id));
    }
}

struct Fx {
    server: GameServer,
    client: TestClient,
    player: PlayerRef,
    doodad: DoodadRef,
    rec: Arc<Rec>,
}

impl Fx {
    fn ctx(&self) -> DoodadCtx<'_> {
        DoodadCtx::new(
            &self.server,
            self.server.world.world(0).unwrap(),
            self.doodad.clone(),
        )
    }
    fn caster(&self) -> WorldObject {
        WorldObject::from(&self.player)
    }
    fn run(&self, t: FuncTemplate, skill_id: u32, next_phase: i32) {
        let c = self.caster();
        use_func(&t, &self.ctx(), Some(&c), skill_id, next_phase);
    }
    fn to_next(&self) -> bool {
        self.doodad.data().phase.to_next_phase
    }
}

fn fixture_with(rec: Rec, data: GameData) -> Fx {
    let server = test_server_with(data);
    let rec = Arc::new(rec);
    server.systems.npcs.hooks.economy.install(rec.clone());
    server.systems.npcs.hooks.skills.install(rec.clone());
    server.systems.npcs.hooks.quests.install(rec.clone());
    let mut client = TestClient::new(&server, 1, 1001);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "Alpha", 100.0, 100.0),
        0x100,
    );
    let doodad = Entity::new(0x200, Doodad::new(1000));
    doodad.data().transform.set_xyz(102.0, 100.0, 100.0);
    let world = server.world.world(0).unwrap();
    world.spawn(&doodad, &server.view_ctx());
    client.drain();
    Fx {
        server,
        client,
        player,
        doodad,
        rec,
    }
}

fn fixture(rec: Rec) -> Fx {
    fixture_with(rec, GameData::empty())
}

fn body(p: &dyn Marshal) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write(&mut s);
    s.into_bytes()
}

fn error_body(e: i16) -> Vec<u8> {
    body(&SCErrorMsgPacket::new(e, 0, true))
}

#[tokio::test]
async fn to_next_phase_funcs_and_stubs() {
    let fx = fixture(Rec::default());
    for t in [
        FuncTemplate::Cutdown(DoodadFuncCutdown { id: 1 }),
        FuncTemplate::Cutdowning(DoodadFuncCutdowning { id: 1 }),
        FuncTemplate::Harvest(DoodadFuncHarvest { id: 1 }),
        FuncTemplate::OpenFarmInfo(DoodadFuncOpenFarmInfo { id: 1, farm_id: 0 }),
        FuncTemplate::PuzzleRoll(DoodadFuncPuzzleRoll::default()),
    ] {
        fx.doodad.data().phase.to_next_phase = false;
        fx.run(t, 0, 0);
        assert!(fx.to_next());
    }
    for t in [
        FuncTemplate::StoreUi(DoodadFuncStoreUi::default()),
        FuncTemplate::EvidenceItemLoot(DoodadFuncEvidenceItemLoot::default()),
        FuncTemplate::TreeByProductsCollect(DoodadFuncTreeByProductsCollect::default()),
        // phase funcs are not `DoodadFuncTemplate`s
        FuncTemplate::Timer(DoodadFuncTimer::default()),
    ] {
        fx.doodad.data().phase.to_next_phase = false;
        fx.run(t, 0, 0);
        assert!(!fx.to_next());
    }
}

#[tokio::test]
async fn loot_item_coins_mirror_double_add() {
    let fx = fixture(Rec {
        money_ok: true,
        ..Rec::default()
    });
    fx.player.data().money = 10;
    let t = FuncTemplate::LootItem(DoodadFuncLootItem {
        id: 1,
        item_id: ITEM_COINS,
        count_min: 7,
        count_max: 7,
        percent: 10000,
        ..Default::default()
    });
    fx.run(t, 0, 0);
    assert_eq!(fx.player.data().money, 17); // the hook adds the second 7
    assert_eq!(fx.rec.calls(), vec!["money 7 83"]);
    assert!(fx.to_next());
}

#[tokio::test]
async fn loot_item_failure_sends_error() {
    let mut fx = fixture(Rec::default());
    let t = FuncTemplate::LootItem(DoodadFuncLootItem {
        id: 1,
        item_id: 8000,
        count_min: 2,
        count_max: 2,
        percent: 10000,
        ..Default::default()
    });
    fx.doodad.data().phase.to_next_phase = true;
    fx.run(t, 0, 0);
    assert_eq!(fx.rec.calls(), vec!["acquire 25 8000 2 -1"]);
    assert!(!fx.to_next());
    assert_eq!(
        fx.client.drain(),
        vec![(
            sc::SCErrorMsgPacket,
            error_body(error_message::BAG_INVALID_ITEM)
        )]
    );
    // chance miss: percent -1 never passes, nothing happens
    let t = FuncTemplate::LootItem(DoodadFuncLootItem {
        percent: -1,
        ..Default::default()
    });
    fx.run(t, 0, 0);
    assert_eq!(fx.rec.calls().len(), 1);
}

#[tokio::test]
async fn loot_pack_results() {
    let mut fx = fixture(Rec {
        loot_pack: Some(false),
        ..Rec::default()
    });
    let t = FuncTemplate::LootPack(DoodadFuncLootPack {
        id: 1,
        loot_pack_id: 77,
    });
    fx.run(t.clone(), 5, 0);
    assert!(!fx.to_next());
    assert_eq!(
        fx.client.drain(),
        vec![(sc::SCErrorMsgPacket, error_body(error_message::BAG_FULL))]
    );
    let fx = fixture(Rec {
        loot_pack: Some(true),
        ..Rec::default()
    });
    fx.run(t, 5, 0);
    assert!(fx.to_next());
    assert_eq!(fx.rec.calls(), vec!["lootpack 77 5 17"]);
}

#[tokio::test]
async fn conditional_use_overrides_phase() {
    let t = FuncTemplate::ConditionalUse(DoodadFuncConditionalUse {
        id: 1,
        item_id: 50,
        item_trigger_phase: 11,
        quest_id: 60,
        quest_trigger_phase: 22,
        ..Default::default()
    });
    let fx = fixture(Rec {
        item_count: 1,
        ..Rec::default()
    });
    fx.run(t.clone(), 0, 0);
    assert_eq!(fx.doodad.data().phase.override_phase, 11);
    assert!(fx.to_next());

    let fx = fixture(Rec {
        has_quest: true,
        ..Rec::default()
    });
    fx.run(t.clone(), 0, 0);
    assert_eq!(fx.doodad.data().phase.override_phase, 22);
    assert_eq!(fx.rec.calls(), vec!["count 50", "hasquest 60"]);

    let fx = fixture(Rec::default());
    fx.run(t, 0, 0);
    assert_eq!(fx.doodad.data().phase.override_phase, 0);
    assert!(!fx.to_next());
}

#[tokio::test]
async fn fake_use_variants() {
    let fx = fixture(Rec::default());
    fx.doodad.data().parent_obj_id = 0x300;
    fx.run(
        FuncTemplate::FakeUse(DoodadFuncFakeUse {
            id: 1,
            skill_id: 900,
            fake_skill_id: 0,
            target_parent: true,
        }),
        0,
        0,
    );
    assert!(fx.to_next());
    assert_eq!(
        fx.rec.calls(),
        vec!["skill 256 Doodad(512) None Doodad(768) 900 false"]
    );

    let fake = FuncTemplate::FakeUse(DoodadFuncFakeUse {
        id: 1,
        skill_id: 0,
        fake_skill_id: 55,
        target_parent: false,
    });
    fx.doodad.data().phase.to_next_phase = false;
    fx.run(fake.clone(), 0, 5);
    assert!(!fx.to_next());
    fx.run(fake.clone(), 12, 0);
    assert!(!fx.to_next());
    fx.run(fake, 12, 5);
    assert!(fx.to_next());

    fx.doodad.data().phase.to_next_phase = false;
    fx.run(FuncTemplate::FakeUse(DoodadFuncFakeUse::default()), 0, 0);
    assert!(fx.to_next());
}

#[tokio::test]
async fn use_func_skill_and_next_phase() {
    let fx = fixture(Rec::default());
    // unknown skill template: return before ToNextPhase
    fx.run(
        FuncTemplate::Use(DoodadFuncUse {
            id: 1,
            skill_id: 4242,
        }),
        7,
        0,
    );
    assert!(!fx.to_next());
    assert!(fx.rec.calls().is_empty());
    // no skill of its own: ToNextPhase = skillId > 0
    fx.run(FuncTemplate::Use(DoodadFuncUse::default()), 7, 0);
    assert!(fx.to_next());
    fx.run(FuncTemplate::Use(DoodadFuncUse::default()), 0, 0);
    assert!(!fx.to_next());
}

#[tokio::test]
async fn use_func_house_fail_safe() {
    let fx = fixture(Rec::default());
    {
        let mut d = fx.doodad.data();
        d.owner_db_id = 99; // no such house
        d.owner_id = 5;
    }
    fx.run(FuncTemplate::Use(DoodadFuncUse::default()), 7, 0);
    let d = fx.doodad.data();
    assert_eq!((d.owner_db_id, d.owner_id), (0, 0));
    assert!(d.phase.to_next_phase);
}

#[tokio::test]
async fn skill_hit_and_buff() {
    let fx = fixture(Rec::default());
    let t = FuncTemplate::SkillHit(DoodadFuncSkillHit {
        id: 1,
        skill_id: 33,
    });
    fx.run(t.clone(), 33, 0);
    assert!(fx.to_next());
    fx.run(t, 34, 0);
    assert!(!fx.to_next());
    fx.run(
        FuncTemplate::Buff(DoodadFuncBuff {
            id: 1,
            buff_id: 9,
            ..Default::default()
        }),
        0,
        0,
    );
    // radius 10: the doodad (2 m away) is a BaseUnit target, the caster itself is not
    fx.run(
        FuncTemplate::Buff(DoodadFuncBuff {
            id: 1,
            buff_id: 8,
            radius: 10.0,
            ..Default::default()
        }),
        0,
        0,
    );
    assert_eq!(
        fx.rec.calls(),
        vec![
            "skillhit 256 512 33",
            "skillhit 256 512 33",
            "buff 256 9 256",
            "buff 512 8 256"
        ]
    );
}

#[tokio::test]
async fn remove_item_and_purchase() {
    let fx = fixture(Rec {
        consumed: 2,
        ..Rec::default()
    });
    fx.run(
        FuncTemplate::RemoveItem(DoodadFuncRemoveItem {
            id: 1,
            item_id: 70,
            count: 2,
        }),
        0,
        0,
    );
    assert!(fx.to_next());
    assert_eq!(fx.rec.calls(), vec!["consume 16 70 2"]);

    let mut fx = fixture(Rec::default());
    fx.run(
        FuncTemplate::Purchase(DoodadFuncPurchase {
            id: 1,
            item_id: 70,
            count: 1,
            ..Default::default()
        }),
        0,
        0,
    );
    // SpaceLeftForItem (default 0) < 1
    assert_eq!(
        fx.client.drain(),
        vec![(sc::SCErrorMsgPacket, error_body(error_message::BAG_FULL))]
    );
}

#[tokio::test]
async fn recover_item_without_item_info() {
    let fx = fixture(Rec::default());
    fx.run(
        FuncTemplate::RecoverItem(DoodadFuncRecoverItem { id: 1 }),
        0,
        0,
    );
    assert!(fx.to_next());

    let fx = fixture(Rec {
        acquire_ok: true,
        ..Rec::default()
    });
    fx.doodad.data().item_template_id = 321;
    fx.run(
        FuncTemplate::RecoverItem(DoodadFuncRecoverItem { id: 1 }),
        0,
        0,
    );
    assert_eq!(fx.rec.calls(), vec!["acquire 25 321 1 -1"]);
    let d = fx.doodad.data();
    assert_eq!(d.item_template_id, 0);
    assert!(d.phase.to_next_phase);
}

#[tokio::test]
async fn consume_changer_moves_pack() {
    let mut data = GameData::empty();
    data.systems
        .npcs
        .doodad
        .consume_changer_items
        .push(DoodadFuncConsumeChangerItem {
            id: 1,
            doodad_func_consume_changer_id: 5,
            item_id: 4000,
        });
    let fx = fixture_with(Rec::default(), data);
    let item = Item::from_row(
        ItemRow {
            type_name: "Item".into(),
            id: 777,
            template_id: 4000,
            slot: 26,
            count: 1,
            ..Default::default()
        },
        None,
        1,
    );
    let eq = fx.player.data().inventory.equipment().clone();
    eq.lock().unwrap().items.push(item);
    fx.run(
        FuncTemplate::ConsumeChanger(DoodadFuncConsumeChanger {
            id: 5,
            slot_id: 26,
            count: 1,
        }),
        0,
        0,
    );
    let d = fx.doodad.data();
    assert_eq!((d.item_id, d.item_template_id), (777, 4000));
    assert!(d.phase.to_next_phase);
    assert_eq!(fx.rec.calls(), vec!["system 16 777"]);
}

#[tokio::test]
async fn quest_accept_or_report() {
    let mut fx = fixture(Rec::default());
    let t = FuncTemplate::Quest(DoodadFuncQuest {
        id: 1,
        quest_kind_id: 0,
        quest_id: 1234,
    });
    fx.run(t.clone(), 0, 0);
    assert_eq!(
        fx.client.drain(),
        vec![(
            sc::SCDoodadQuestAcceptPacket,
            vec![0x00, 0x02, 0x00, 0xd2, 0x04, 0, 0]
        )]
    );
    let fx = fixture(Rec {
        has_quest: true,
        ..Rec::default()
    });
    fx.run(t, 0, 0);
    assert_eq!(
        fx.rec.calls(),
        vec!["hasquest 1234", "report 256 1234 0 512 0"]
    );
}

#[tokio::test]
async fn attachment_bonds_to_seat() {
    let mut fx = fixture(Rec::default());
    let t = FuncTemplate::Attachment(DoodadFuncAttachment {
        id: 1,
        attach_point_id: 2,
        space: 1,
        bond_kind_id: bond_kind::BOND_CHAIR_SINGLE,
    });
    fx.run(t.clone(), 0, 0);
    let bond = fx.player.data().bonding.expect("bonded");
    assert_eq!(
        bond,
        BondDoodad {
            owner_obj_id: 0x200,
            attach_point: 2,
            kind: 2,
            space: 1,
            spot: 0
        }
    );
    // SCBondDoodadPacket broadcast to self
    assert_eq!(
        fx.client.drain(),
        vec![(
            sc::SCBondDoodadPacket,
            vec![0x00, 0x01, 0x00, 2, 0x00, 0x02, 0x00, 2, 1, 0, 0, 0, 0, 0, 0, 0]
        )]
    );
    // the character is now a child of the doodad, 2 m away in local space
    let t2 = fx.player.data().unit.transform;
    assert_eq!(t2.parent_obj_id, 0x200);
    assert!((t2.x + 2.0).abs() < 1e-3 && t2.y.abs() < 1e-3);
    assert_eq!(fx.doodad.data().seat.seats[&0x200], vec![1]);
    // no free spot left
    fx.player.data().bonding = None;
    fx.run(t, 0, 0);
    assert!(fx.player.data().bonding.is_none());
}

#[tokio::test]
async fn binding_without_return_point() {
    let fx = fixture(Rec::default());
    fx.run(
        FuncTemplate::Binding(DoodadFuncBinding {
            id: 1,
            district_id: 5,
        }),
        0,
        0,
    );
    assert!(!fx.to_next());
}

#[tokio::test]
async fn exit_indun_sends_error() {
    let mut fx = fixture(Rec::default());
    fx.run(
        FuncTemplate::ExitIndun(DoodadFuncExitIndun::default()),
        0,
        0,
    );
    assert_eq!(
        fx.client.drain(),
        vec![(
            sc::SCErrorMsgPacket,
            error_body(error_message::INVALID_RETURN_POS_INSTANCE)
        )]
    );
}

// ---------------------------------------------------------------------------------------------
// World interactions
// ---------------------------------------------------------------------------------------------

#[tokio::test]
async fn interaction_classes() {
    assert_eq!(interactions::interaction_class(wi::USE), Some("Use"));
    assert_eq!(
        interactions::interaction_class(wi::SELL_BACKPACK),
        Some("SellBackpack")
    );
    assert_eq!(interactions::interaction_class(wi::SUMMON_MINE_SPOT), None);
    assert_eq!(interactions::interaction_class(95), None);
    let count = (0..=110)
        .filter(|&v| interactions::interaction_class(v).is_some())
        .count();
    assert_eq!(count, 41);
    let fx = fixture(Rec::default());
    assert!(!execute_interaction(
        &fx.server,
        wi::SHEARING,
        &fx.caster(),
        None,
        0,
        0,
        None
    ));
}

#[tokio::test]
async fn cutdown_broadcasts_cutdowning() {
    let mut fx = fixture(Rec::default());
    let target = WorldObject::from(&fx.doodad);
    assert!(execute_interaction(
        &fx.server,
        wi::CUTDOWN,
        &fx.caster(),
        Some(&target),
        10,
        0,
        None
    ));
    let frames = fx.client.drain();
    assert!(frames.contains(&(
        sc::SCVegetationCutdowningPacket,
        vec![0x00, 0x01, 0x00, 0x00, 0x02, 0x00]
    )));
}

#[tokio::test]
async fn recover_item_interaction_fails_without_func() {
    let mut fx = fixture(Rec::default());
    let target = WorldObject::from(&fx.doodad);
    assert!(execute_interaction(
        &fx.server,
        wi::RECOVER_ITEM,
        &fx.caster(),
        Some(&target),
        10,
        0,
        None
    ));
    assert_eq!(
        fx.client.drain(),
        vec![(
            sc::SCErrorMsgPacket,
            error_body(error_message::FAILED_TO_USE_ITEM)
        )]
    );
    assert_eq!(fx.rec.calls(), vec!["interrupt 256"]);
}

#[tokio::test]
async fn allow_removal_checks_coffer_and_children() {
    let fx = fixture(Rec::default());
    assert!(interactions::doodad_allow_removal(&fx.server, &fx.doodad));
    let container =
        ItemContainer::new(ContainerKind::Coffer, 1, slot_type::SYSTEM, 5000).into_ref();
    fx.server.items.add_container(container.clone());
    fx.doodad.data().coffer = Some(DoodadCofferState {
        capacity: 10,
        container_id: 5000,
        opened_by: None,
    });
    assert!(interactions::doodad_allow_removal(&fx.server, &fx.doodad));
    container.lock().unwrap().items.push(test_item(1, 0));
    assert!(!interactions::doodad_allow_removal(&fx.server, &fx.doodad));
}

// ---------------------------------------------------------------------------------------------
// Coffers
// ---------------------------------------------------------------------------------------------

fn test_item(id: u64, slot: i32) -> Item {
    Item::from_row(
        ItemRow {
            type_name: "Item".into(),
            id,
            template_id: 0x1234,
            slot,
            count: 3,
            ..Default::default()
        },
        None,
        1,
    )
}

#[tokio::test]
async fn coffer_contents_packet_bytes() {
    let fx = fixture(Rec::default());
    let container =
        ItemContainer::new(ContainerKind::Coffer, 1, slot_type::SYSTEM, 0x55).into_ref();
    container.lock().unwrap().items.push(test_item(9, 1));
    fx.server.items.add_container(container);
    let state = DoodadCofferState {
        capacity: 2,
        container_id: 0x55,
        opened_by: None,
    };
    let p = SCCofferContentsUpdatePacket::new(&fx.server, 0x200, &state, 0);
    let mut expected = vec![0x00, 0x02, 0x00]; // bc obj id
    expected.extend_from_slice(&0x55u64.to_le_bytes());
    expected.push(2); // slot count
    expected.extend_from_slice(&[0, 0, 0, 0, 0]); // slot 0, empty
    expected.push(1); // slot 1
    expected.extend(body(&test_item(9, 1)));
    assert_eq!(body(&p), expected);

    // 45 slots from 30: 15 slots; without a container the id is 0 and every slot is empty
    let state = DoodadCofferState {
        capacity: 45,
        container_id: 0,
        opened_by: None,
    };
    let p = SCCofferContentsUpdatePacket::new(&fx.server, 0x200, &state, 30);
    assert_eq!(p.container_id, 0);
    assert_eq!(p.slots.len(), 15);
    assert_eq!(p.slots[0], (30, None));
}

#[tokio::test]
async fn coffer_open_close_tracking() {
    let mut fx = fixture(Rec::default());
    // not a coffer
    assert!(!coffer::open_coffer_doodad(&fx.server, &fx.player, 0x200));
    fx.doodad.data().coffer = Some(DoodadCofferState {
        capacity: 45,
        container_id: 0,
        opened_by: None,
    });
    assert!(coffer::open_coffer_doodad(&fx.server, &fx.player, 0x200));
    assert_eq!(
        fx.client.drain_ops(),
        vec![
            sc::SCCofferContentsUpdatePacket,
            sc::SCCofferContentsUpdatePacket
        ]
    );
    // in use (by anyone, even the same character)
    assert!(!coffer::open_coffer_doodad(&fx.server, &fx.player, 0x200));
    fx.doodad.data().coffer.as_mut().unwrap().opened_by = Some(99);
    assert!(!coffer::close_coffer_doodad(&fx.server, &fx.player, 0x200));
    fx.doodad.data().coffer.as_mut().unwrap().opened_by = Some(1);
    coffer::close_coffers_opened_by(&fx.server, &fx.player);
    assert_eq!(fx.doodad.data().coffer.as_ref().unwrap().opened_by, None);
    assert!(coffer::close_coffer_doodad(&fx.server, &fx.player, 0x200));
}

#[tokio::test]
async fn coffer_permissions() {
    let fx = fixture(Rec::default());
    fx.doodad.data().coffer = Some(DoodadCofferState::default());
    fx.server.names.add_character(77, "Owner", 1001);
    {
        let mut d = fx.doodad.data();
        d.owner_id = 77;
        d.data = i32::from(crate::models::house::housing_permission::PRIVATE);
    }
    // same account (test character 1 has account 1001)
    assert!(coffer::allowed_to_interact(
        &fx.server, &fx.doodad, &fx.player
    ));
    fx.doodad.data().data = i32::from(crate::models::house::housing_permission::FAMILY);
    assert!(!coffer::allowed_to_interact(
        &fx.server, &fx.doodad, &fx.player
    ));
    fx.doodad.data().data = i32::from(crate::models::house::housing_permission::PUBLIC);
    assert!(coffer::allowed_to_interact(
        &fx.server, &fx.doodad, &fx.player
    ));
}

#[test]
fn cs_coffer_interaction_decode() {
    let mut s = PacketStream::from_bytes(vec![0x00, 0x02, 0x00, 0x01]);
    let p = CSCofferInteractionPacket::read(&mut s).unwrap();
    assert_eq!(
        p,
        CSCofferInteractionPacket {
            coffer_obj_id: 0x200,
            opening: true
        }
    );
}
