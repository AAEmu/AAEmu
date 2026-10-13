//! Housing flow tests on a synthetic world (no MySQL): build + build steps through the
//! `CraftEffect` branch, visibility of a house and its bound doodads, taxes and the demolition
//! timer, sale / purchase, permissions, decoration, lobby packets, save rows, death.

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex};

use aaemu_commons::PacketStream;

use super::data::{
    HousingBindingDoodad, HousingBuildStep, HousingDecoration, HousingItemHousings,
    HousingTemplate, ItemHousingDecoration, Taxation, WorldSpawnPosition,
};
use super::hooks::{HouseItem, HouseMail, HousingEconomyHooks, MailAttachment, TaxMailUpdate};
use super::manager::{self, error_message as err};
use super::ops;
use super::packets::*;
use crate::data::features::Feature;
use crate::data::items::ItemTemplate;
use crate::data::world_data::Zone;
use crate::data::GameData;
use crate::models::house::housing_permission;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::npcs::doodad::templates::DoodadTemplate;
use crate::test_support::*;
use crate::world::{HouseRef, PlayerRef, WorldObject};

const DESIGN: u32 = 300;
const FARM: u32 = 301;
const DESIGN_ITEM: u32 = 9000;
const BOUND_DOODAD: u32 = 4001;
const CHAIR_DOODAD: u32 = 5000;
const CHAIR_ITEM: u32 = 8000;

/// Records every hook call; return values are configurable.
#[derive(Default)]
struct Eco {
    calls: Mutex<Vec<String>>,
    mails: Mutex<Vec<HouseMail>>,
    items: Mutex<Vec<HouseItem>>,
    item_counts: Mutex<BTreeMap<u32, i32>>,
    money_ok: bool,
    house_mails: Mutex<Vec<i64>>,
    updates: Mutex<Vec<(i64, TaxMailUpdate)>>,
}

impl Eco {
    fn push(&self, s: String) {
        self.calls.lock().unwrap().push(s);
    }
    fn calls(&self) -> Vec<String> {
        std::mem::take(&mut *self.calls.lock().unwrap())
    }
    fn mails(&self) -> Vec<HouseMail> {
        std::mem::take(&mut *self.mails.lock().unwrap())
    }
    fn find(&self, id: u64) -> Option<HouseItem> {
        self.items
            .lock()
            .unwrap()
            .iter()
            .find(|i| i.item_id == id)
            .copied()
    }
}

impl HousingEconomyHooks for Eco {
    fn pay_house_construction(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        design: HouseItem,
        total: i32,
        tax_item: bool,
    ) -> Result<(), super::hooks::HouseConstructionError> {
        use super::hooks::{construction_certificates, item_task, HouseConstructionError};
        let certificates = construction_certificates(total);
        let bound = self.inventory_item_count(server, player, manager::BOUND_TAX_CERTIFICATE);
        let unbound = self.inventory_item_count(server, player, manager::TAX_CERTIFICATE);
        if self.inventory_item_count(server, player, design.template_id) < 1 {
            return Err(HouseConstructionError::InvalidDesign);
        }
        if tax_item {
            if bound + unbound < certificates {
                return Err(HouseConstructionError::InsufficientPayment);
            }
            let from_bound = bound.min(certificates);
            if from_bound > 0 {
                self.bag_consume_item(
                    server,
                    player,
                    item_task::HOUSE_CREATION,
                    manager::BOUND_TAX_CERTIFICATE,
                    from_bound,
                    None,
                );
            }
            let remainder = certificates - from_bound;
            if remainder > 0 {
                self.bag_consume_item(
                    server,
                    player,
                    item_task::HOUSE_CREATION,
                    manager::TAX_CERTIFICATE,
                    remainder,
                    None,
                );
            }
        } else if !self.subtract_money(server, player, total, item_task::HOUSE_CREATION) {
            return Err(HouseConstructionError::InsufficientPayment);
        }
        self.bag_consume_item(
            server,
            player,
            item_task::HOUSE_BUILDING,
            design.template_id,
            1,
            Some(design.item_id),
        );
        Ok(())
    }

    fn inventory_item(&self, _s: &GameServer, _p: &PlayerRef, id: u64) -> Option<HouseItem> {
        self.find(id)
    }
    fn item(&self, _s: &GameServer, id: u64) -> Option<HouseItem> {
        self.find(id)
    }
    fn inventory_item_count(&self, _s: &GameServer, _p: &PlayerRef, t: u32) -> i32 {
        self.item_counts
            .lock()
            .unwrap()
            .get(&t)
            .copied()
            .unwrap_or(0)
    }
    fn bag_consume_item(
        &self,
        _s: &GameServer,
        _p: &PlayerRef,
        task: u8,
        t: u32,
        count: i32,
        preferred: Option<u64>,
    ) -> i32 {
        self.push(format!("consume {task} {t} {count} {preferred:?}"));
        let mut counts = self.item_counts.lock().unwrap();
        let have = counts.entry(t).or_insert(0);
        let n = count.min(*have);
        *have -= n;
        n
    }
    fn pay_house_sale_certificates(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        count: i32,
    ) -> bool {
        let have = self.inventory_item_count(server, player, manager::APPRAISAL_CERTIFICATE);
        have >= count
            && self.bag_consume_item(
                server,
                player,
                super::hooks::item_task::BUY_HOUSE,
                manager::APPRAISAL_CERTIFICATE,
                count,
                None,
            ) == count
    }
    fn subtract_money(&self, _s: &GameServer, _p: &PlayerRef, amount: i32, task: u8) -> bool {
        self.push(format!("money -{amount} {task}"));
        self.money_ok
    }
    fn move_item_to_system_container(
        &self,
        _s: &GameServer,
        _p: &PlayerRef,
        task: u8,
        id: u64,
    ) -> bool {
        self.push(format!("system {task} {id}"));
        if let Some(item) = self
            .items
            .lock()
            .unwrap()
            .iter_mut()
            .find(|item| item.item_id == id)
        {
            item.slot_type = super::hooks::slot_type::SYSTEM;
        }
        true
    }
    fn destroy_item(&self, _s: &GameServer, id: u64) {
        self.push(format!("destroy {id}"));
    }
    fn send_mail_with_new_items(
        &self,
        _s: &GameServer,
        _o: &PlayerRef,
        mail: HouseMail,
        t: u32,
        count: i32,
    ) -> bool {
        self.push(format!("mail+items {t} {count}"));
        self.mails.lock().unwrap().push(mail);
        true
    }
    fn send_mail(&self, _s: &GameServer, mail: HouseMail) -> bool {
        self.mails.lock().unwrap().push(mail);
        true
    }
    fn send_return_mails(
        &self,
        server: &GameServer,
        mails: Vec<HouseMail>,
        payment: Option<(&PlayerRef, i32)>,
    ) -> bool {
        if let Some((buyer, amount)) = payment {
            if !self.subtract_money(server, buyer, amount, super::hooks::item_task::BUY_HOUSE) {
                return false;
            }
        }
        self.mails.lock().unwrap().extend(mails);
        true
    }
    fn delete_house_mails(&self, _s: &GameServer, id: u32) {
        self.push(format!("delete mails {id}"));
    }
    fn house_mail_ids(&self, _s: &GameServer, _id: u32) -> Vec<i64> {
        self.house_mails.lock().unwrap().clone()
    }
    fn update_tax_mail(&self, _s: &GameServer, id: i64, u: &TaxMailUpdate) {
        self.updates.lock().unwrap().push((id, u.clone()));
    }
}

fn item_template(id: u32, max_count: i32, fixed_grade: i32) -> ItemTemplate {
    ItemTemplate {
        fixed_grade,
        ..ItemTemplate::plain(id, max_count)
    }
}

fn step(step: i16, model_id: u32, skill_id: u32, num_actions: i32) -> HousingBuildStep {
    HousingBuildStep {
        id: u32::from(step as u16) + 1,
        housing_id: DESIGN,
        step,
        model_id,
        skill_id,
        num_actions,
    }
}

/// Game data: two housing templates, the doodads they use, the design / chair items, zone
/// 100 in zone group 25 and the house buffs.
fn housing_data(tax_item: bool) -> GameData {
    let mut data = GameData::empty();
    data.features.set(Feature::TaxItem, tax_item);
    let h = &mut data.systems.housing;
    h.taxations.insert(
        2,
        Taxation {
            id: 2,
            tax: 100_000,
            show: true,
        },
    );
    let mut build_steps = BTreeMap::new();
    build_steps.insert(0, step(0, 710, 11000, 2));
    build_steps.insert(1, step(1, 711, 11001, 1));
    h.add_template(HousingTemplate {
        id: DESIGN,
        name: "Cottage Design".into(),
        main_model_id: 700,
        hp: 1600,
        garden_radius: 8.0,
        taxation: Some(Taxation {
            id: 2,
            tax: 100_000,
            show: true,
        }),
        taxation_id: 2,
        is_sellable: true,
        heavy_tax: true,
        build_steps,
        binding_doodads: vec![HousingBindingDoodad {
            attach_point_id: 9,
            doodad_id: BOUND_DOODAD,
            position: WorldSpawnPosition {
                x: 1.0,
                y: 2.0,
                z: 3.0,
                yaw: 90.0,
                ..Default::default()
            },
        }],
        ..Default::default()
    });
    h.add_template(HousingTemplate {
        id: FARM,
        name: "Farm".into(),
        main_model_id: 800,
        hp: 900,
        garden_radius: 4.0,
        taxation: Some(Taxation {
            id: 2,
            tax: 100_000,
            show: true,
        }),
        taxation_id: 2,
        always_public: true,
        ..Default::default()
    });
    h.add_item_housing(HousingItemHousings {
        id: 1,
        item_id: DESIGN_ITEM,
        design_id: DESIGN,
    });
    h.add_decoration(HousingDecoration {
        id: 50,
        name: "Chair".into(),
        doodad_id: CHAIR_DOODAD,
        actability_group_id: 3,
        actability_up: 7,
        ..Default::default()
    })
    .unwrap();
    h.add_item_housing_decoration(ItemHousingDecoration {
        id: 1,
        item_id: CHAIR_ITEM,
        design_id: 50,
        restore: true,
    });
    for id in [
        BOUND_DOODAD,
        CHAIR_DOODAD,
        manager::FOR_SALE_MARKER_DOODAD_ID,
    ] {
        data.systems.npcs.doodad.templates.insert(
            id,
            DoodadTemplate {
                id,
                ..Default::default()
            },
        );
    }
    for t in [
        item_template(DESIGN_ITEM, 1, 2),
        item_template(CHAIR_ITEM, 10, -1),
        item_template(manager::BOUND_TAX_CERTIFICATE, 1000, -1),
    ] {
        data.items.templates.insert(t.id, t);
    }
    data.zones.zones.insert(
        TEST_ZONE,
        Zone {
            id: 1,
            name: "zone".into(),
            zone_key: TEST_ZONE,
            group_id: 25,
            ..Default::default()
        },
    );
    for id in [manager::BUFF_UNTOUCHABLE, manager::BUFF_REMOVAL_DEBUFF] {
        data.systems
            .skills
            .add_buff(crate::systems::skills::data::BuffTemplate {
                id,
                ..Default::default()
            });
    }
    data
}

struct Fx {
    server: GameServer,
    eco: Arc<Eco>,
    a: TestClient,
    b: TestClient,
    pa: PlayerRef,
    pb: PlayerRef,
}

fn fixture(tax_item: bool) -> Fx {
    let server = test_server_with(housing_data(tax_item));
    let eco = Arc::new(Eco {
        money_ok: true,
        ..Default::default()
    });
    server.systems.housing.hooks.install(eco.clone());
    let a = TestClient::new(&server, 1, 1001);
    let b = TestClient::new(&server, 2, 1002);
    let mut ca = test_character(1, "Alpha", 100.0, 100.0);
    ca.money = 1_000_000;
    let mut cb = test_character(2, "Beta", 110.0, 100.0);
    cb.money = 1_000_000;
    server.names.add_character(1, "Alpha", 1001);
    server.names.add_character(2, "Beta", 1002);
    // Object ids from the ObjectIdManager (houses / doodads draw from it too).
    let (oa, ob) = (
        server.ids.object.get_next_id().unwrap(),
        server.ids.object.get_next_id().unwrap(),
    );
    let pa = spawn_player(&server, &a, ca, oa);
    let pb = spawn_player(&server, &b, cb, ob);
    let mut fx = Fx {
        server,
        eco,
        a,
        b,
        pa,
        pb,
    };
    fx.a.drain();
    fx.b.drain();
    fx
}

/// A house of `owner` at (x, y) registered and spawned (step -1 = finished, with its bound
/// doodads).
fn place_house(fx: &Fx, template: u32, owner: &PlayerRef, x: f32, y: f32, step: i32) -> HouseRef {
    let server = &fx.server;
    let world = server.world.world(0).unwrap();
    let (cid, acc) = {
        let c = owner.data();
        (c.id, c.account_id)
    };
    let house = manager::create(server, template, 101, &world, 0, 0).unwrap();
    {
        let mut h = house.data();
        h.id = server.ids.housing.get_next_id().unwrap();
        h.unit.transform.set_xyz(x, y, 100.0);
        h.unit.transform.zone_id = TEST_ZONE;
    }
    manager::set_current_step(server, &house, step, false);
    {
        let mut h = house.data();
        h.unit.owner_id = cid;
        h.co_owner_id = cid;
        h.account_id = acc;
        h.place_date = manager::now() - 100;
        h.protection_end_date = manager::now() + 7 * 24 * 3600 + 3600;
    }
    assert!(server.systems.housing.manager.insert(&house));
    manager::spawn_house(server, &house);
    house
}

fn ops_of(frames: &[(u16, Vec<u8>)]) -> Vec<u16> {
    frames.iter().map(|f| f.0).collect()
}

fn body_of(frames: &[(u16, Vec<u8>)], op: u16) -> Vec<u8> {
    frames
        .iter()
        .find(|f| f.0 == op)
        .unwrap_or_else(|| panic!("no packet {op:#x} in {:x?}", ops_of(frames)))
        .1
        .clone()
}

fn create_packet(z_rot: f32) -> CSCreateHousePacket {
    CSCreateHousePacket {
        design_id: DESIGN,
        x: 120.0,
        y: 100.0,
        z: 100.0,
        z_rot,
        item_id: 77,
        money_amount: 0,
        ht: 0,
        auto_use_aa_point: false,
    }
}

#[tokio::test]
async fn build_and_build_steps_flow() {
    let mut fx = fixture(false);
    fx.eco.items.lock().unwrap().push(HouseItem {
        item_id: 77,
        template_id: DESIGN_ITEM,
        owner_id: 1,
        slot_type: super::hooks::slot_type::INVENTORY,
        count: 1,
        max_count: 1,
        ..Default::default()
    });
    fx.eco.item_counts.lock().unwrap().insert(DESIGN_ITEM, 1);

    // An item of somebody else: BagInvalidItem.
    let mut bad = create_packet(0.0);
    bad.item_id = 5;
    ops::build(&fx.server, &fx.pa, 1001, &bad);
    assert_eq!(
        fx.a.drain(),
        vec![(sc::SCErrorMsgPacket, body_error(err::BAG_INVALID_ITEM))]
    );

    ops::build(&fx.server, &fx.pa, 1001, &create_packet(1.0));
    // The first placement pays one week plus the two-week deposit.
    assert_eq!(
        fx.eco.calls(),
        vec![
            "money -300000 31".to_string(),
            format!("consume 33 {DESIGN_ITEM} 1 Some(77)")
        ]
    );
    let houses = fx.server.systems.housing.manager.by_account(1001);
    assert_eq!(houses.len(), 1);
    let house = houses[0].clone();
    let (tl, rot_z) = {
        let h = house.data();
        assert_eq!(h.unit.name, "Cottage"); // item name fallback, " Design" stripped
        assert_eq!(h.current_step, 0);
        assert_eq!(h.unit.model_id, 710);
        assert_eq!(h.all_action, 3);
        assert_eq!(h.permission, housing_permission::PRIVATE);
        assert!(h.allow_recover);
        assert_eq!((h.unit.owner_id, h.co_owner_id, h.account_id), (1, 1, 1001));
        assert!(h.unit.buffs.check_buff(manager::BUFF_UNTOUCHABLE));
        (h.unit.tl_id, h.unit.transform.rot_z)
    };
    // zRot is quantized to the 1.2 sbyte rotation: round(1 / 2pi * 127) = 20.
    assert!((rot_z - 20.0 / 127.0 * std::f32::consts::TAU).abs() < 1e-6);

    let fa = fx.a.drain();
    assert_eq!(
        ops_of(&fa),
        vec![
            sc::SCMyHousePacket,
            sc::SCUnitStatePacket,
            sc::SCHouseStatePacket
        ]
    );
    assert_eq!(
        body_of(&fa, sc::SCMyHousePacket),
        ops::house_body(&fx.server, &house)
    );
    assert_eq!(
        body_of(&fa, sc::SCHouseStatePacket),
        ops::house_body(&fx.server, &house)
    );
    // The neighbour sees the house appear (no SCMyHouse).
    assert_eq!(
        fx.b.drain_ops(),
        vec![sc::SCUnitStatePacket, sc::SCHouseStatePacket]
    );

    // Build actions through the CraftEffect branch.
    assert!(ops::craft_building(&fx.server, &fx.pa, &house, 11000));
    assert_eq!(house.data().num_action, 1);
    let progress = |model, all, cur| SCHouseBuildProgressPacket {
        tl,
        model_id: model,
        all_step: all,
        cur_step: cur,
    };
    let enc = |p: &SCHouseBuildProgressPacket| {
        let mut s = PacketStream::new();
        aaemu_commons::Marshal::write(p, &mut s);
        s.into_bytes()
    };
    assert_eq!(
        fx.b.drain(),
        vec![(sc::SCHouseBuildProgressPacket, enc(&progress(710, 3, 1)))]
    );
    fx.a.drain(); // broadcast to the builder too
    assert!(ops::craft_building(&fx.server, &fx.pa, &house, 11000));
    {
        let h = house.data();
        assert_eq!((h.current_step, h.num_action, h.base_action), (1, 0, 2));
        assert_eq!(h.unit.model_id, 711);
    }
    assert_eq!(
        fx.a.drain(),
        vec![(sc::SCHouseBuildProgressPacket, enc(&progress(711, 3, 2)))]
    );
    fx.b.drain();
    // Wrong construction pack: the skill is cancelled, nothing changes.
    assert!(!ops::craft_building(&fx.server, &fx.pa, &house, 999));
    assert!(fx.pa.data().skill_char.skill_cancelled);
    assert_eq!(house.data().current_step, 1);
    assert!(fx.b.drain().is_empty());
    fx.pa.data().skill_char.skill_cancelled = false;

    // Last action: finished, the bound doodad appears.
    assert!(ops::craft_building(&fx.server, &fx.pa, &house, 11001));
    {
        let h = house.data();
        assert_eq!(h.current_step, -1);
        assert_eq!(h.unit.model_id, 700);
        assert_eq!(h.attached_doodads.len(), 1);
    }
    let fb = fx.b.drain();
    assert_eq!(
        fb[0],
        (sc::SCHouseBuildProgressPacket, enc(&progress(700, 3, 3)))
    );
    assert!(ops_of(&fb).contains(&sc::SCDoodadCreatedPacket));
    let doodad = fx.server.systems.housing.manager.attached(house.obj_id)[0].clone();
    {
        let d = doodad.data();
        assert_eq!(d.template_id, BOUND_DOODAD);
        assert_eq!(d.attach_point, 9);
        assert_eq!(d.owner_db_id, house.data().id);
        assert_eq!(d.transform.parent_obj_id, house.obj_id);
        assert!((d.transform.rot_z - std::f32::consts::FRAC_PI_2).abs() < 1e-6);
    }
    // A finished house: the C# BuildSteps[-1] lookup throws (no progress, no quest event).
    assert!(!ops::craft_building(&fx.server, &fx.pa, &house, 11001));

    // Second house of the account: the heavy-tax formula applies (base + deposit).
    fx.eco.items.lock().unwrap()[0].item_id = 78;
    fx.eco.item_counts.lock().unwrap().insert(DESIGN_ITEM, 1);
    let mut p2 = create_packet(0.0);
    p2.item_id = 78;
    p2.x = 300.0;
    ops::build(&fx.server, &fx.pa, 1001, &p2);
    assert_eq!(fx.eco.calls()[0], "money -300000 31");
}

fn body_error(e: i16) -> Vec<u8> {
    let mut s = PacketStream::new();
    s.write_i16(e).write_i16(e).write_u32(0).write_bool(true);
    s.into_bytes()
}

#[tokio::test]
async fn tax_certificates_pay_for_a_new_house() {
    let fx = fixture(true);
    let _first = place_house(&fx, DESIGN, &fx.pa, 300.0, 300.0, -1);
    fx.eco.items.lock().unwrap().push(HouseItem {
        item_id: 77,
        template_id: DESIGN_ITEM,
        owner_id: 1,
        slot_type: super::hooks::slot_type::INVENTORY,
        count: 1,
        max_count: 1,
        ..Default::default()
    });
    {
        let mut c = fx.eco.item_counts.lock().unwrap();
        c.insert(DESIGN_ITEM, 1);
        c.insert(manager::TAX_CERTIFICATE, 10);
        c.insert(manager::BOUND_TAX_CERTIFICATE, 25);
    }
    fx.eco.calls();
    ops::build(&fx.server, &fx.pa, 1001, &create_packet(0.0));
    // 2 heavy houses -> multiplier 1: 100000 + 200000 deposit = 30 certificates, bound first.
    assert_eq!(
        fx.eco.calls(),
        vec![
            format!("consume 31 {} 25 None", manager::BOUND_TAX_CERTIFICATE),
            format!("consume 31 {} 5 None", manager::TAX_CERTIFICATE),
            format!("consume 33 {DESIGN_ITEM} 1 Some(77)"),
        ]
    );
    assert_eq!(fx.server.systems.housing.manager.count(), 2);
}

#[tokio::test]
async fn tax_info_and_construct_tax_packets() {
    let mut fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 300.0, 300.0, -1);
    fx.a.drain();
    ops::construct_house_tax(&fx.server, &fx.pa, DESIGN, 0.0, 0.0, 0.0);
    let expected = SCConstructHouseTaxPacket {
        design_id: DESIGN,
        heavy_tax_house_count: 2,
        normal_tax_house_count: 0,
        is_heavy_tax_house: true,
        base_tax_money_amount: 100_000,
        deposit_tax_money_amount: 200_000,
        total_tax_money_amount: 300_000,
    };
    let mut s = PacketStream::new();
    aaemu_commons::Marshal::write(&expected, &mut s);
    assert_eq!(
        fx.a.drain(),
        vec![(sc::SCConstructHouseTaxPacket, s.into_bytes())]
    );

    let (tl, end) = {
        let h = house.data();
        (h.unit.tl_id, h.protection_end_date)
    };
    ops::house_tax_info(&fx.server, &fx.pa, tl);
    let expected = SCHouseTaxInfoPacket {
        tl,
        dominion_tax_rate: 0,
        money_amount: 200_000,
        money_amount2: 100_000,
        due: end,
        is_already_paid: true,
        weeks_without_pay: -1,
        is_heavy_tax_house: true,
    };
    let mut s = PacketStream::new();
    aaemu_commons::Marshal::write(&expected, &mut s);
    assert_eq!(
        fx.a.drain(),
        vec![(sc::SCHouseTaxInfoPacket, s.into_bytes())]
    );

    // Heavy tax multiplier with 4 heavy houses: 4 * 0.5 = 2x.
    for i in 0..3 {
        place_house(&fx, DESIGN, &fx.pa, 400.0 + 50.0 * i as f32, 300.0, -1);
    }
    let t = fx
        .server
        .game_data
        .systems
        .housing
        .template(DESIGN)
        .unwrap();
    let (_, info) = manager::calculate_building_tax_info(&fx.server, 1001, t, false).unwrap();
    assert_eq!(
        (info.heavy_house_count, info.total_tax_to_pay),
        (4, 200_000)
    );
    let (_, info) = manager::calculate_building_tax_info(&fx.server, 1001, t, true).unwrap();
    assert_eq!(info.total_tax_to_pay, 250_000 + 200_000);
}

#[tokio::test]
async fn tax_mail_and_demolition_timer() {
    let mut fx = fixture(false);
    // Tax due (protection ends in less than 7 days): a tax bill is mailed.
    let due = place_house(&fx, DESIGN, &fx.pa, 300.0, 300.0, -1);
    due.data().protection_end_date = manager::now() + 3 * 24 * 3600;
    due.data().unit.name = "Villa".into();
    fx.eco.mails();
    manager::check_housing_taxes(&fx.server);
    let mails = fx.eco.mails();
    assert_eq!(mails.len(), 1);
    let m = &mails[0];
    assert_eq!(m.mail_type, super::hooks::mail_type::BILLING);
    assert_eq!(m.sender_name, ".houseTax");
    assert_eq!(m.receiver_name.as_deref(), Some("Alpha"));
    assert_eq!(m.title, "title(25)");
    assert_eq!(m.billing_amount, 100_000);
    assert_eq!(m.status, super::hooks::MAIL_STATUS_UNPAID);
    let id = due.data().id;
    assert_eq!(m.extra, (25i64 << 48) + i64::from(id));
    let h = due.data().clone();
    assert_eq!(
        m.text,
        format!(
            "body('Villa', '{}', '{}', '{}', '100000', '0', '1', '1', '100000', 'true', '0')",
            h.place_date,
            h.protection_end_date,
            h.protection_end_date // overdue: the deadline is the protection end, late fee 1
        )
    );
    // Existing bills are updated instead.
    fx.eco.house_mails.lock().unwrap().push(42);
    manager::check_housing_taxes(&fx.server);
    assert!(fx.eco.mails().is_empty());
    let updates = fx.eco.updates.lock().unwrap().clone();
    assert_eq!(updates.len(), 1);
    assert_eq!(updates[0].0, 42);
    assert!(updates[0].1.complete());
    fx.eco.house_mails.lock().unwrap().clear();

    // Expired protection: the timer demolishes the house.
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    let (hid, tl) = {
        let h = house.data();
        (h.id, h.unit.tl_id)
    };
    house.data().protection_end_date = manager::now() - 10;
    fx.a.drain();
    fx.b.drain();
    fx.eco.calls();
    manager::check_housing_taxes(&fx.server);
    {
        let h = house.data();
        assert_eq!((h.unit.owner_id, h.account_id, h.co_owner_id), (0, 0, 0));
        assert_eq!(h.permission, housing_permission::PUBLIC);
        assert_eq!(h.unit.faction_id, 0); // Monstrosity is not in the (empty) faction table
        assert!(h.unit.buffs.check_buff(manager::BUFF_REMOVAL_DEBUFF));
        assert!(!h.unit.buffs.check_buff(manager::BUFF_UNTOUCHABLE));
    }
    assert!(fx.eco.calls().contains(&format!("delete mails {hid}")));
    let mails = fx.eco.mails();
    let demolish: Vec<_> = mails
        .iter()
        .filter(|m| m.mail_type == super::hooks::mail_type::DEMOLISH)
        .collect();
    assert_eq!(demolish.len(), 1);
    let m = demolish[0];
    assert_eq!(m.sender_name, ".houseDemolish");
    assert_eq!(m.extra, i64::from(hid));
    // Failed tax: returned 22 h later, no deposit.
    assert_eq!(m.recv_date_ms - m.send_date_ms, 22 * 3600 * 1000);
    assert_eq!(m.copper_coins, 0);
    assert_eq!(
        m.attachments,
        vec![MailAttachment::NewItem {
            template_id: DESIGN_ITEM,
            count: 1,
            grade: 2,
            owner_id: 1
        }]
    );
    let fa = fx.a.drain();
    let ops_a = ops_of(&fa);
    assert!(ops_a.contains(&sc::SCHouseDemolishedPacket));
    assert!(ops_a.contains(&sc::SCMyHouseRemovedPacket));
    assert!(ops_a.contains(&sc::SCUnitFactionChangedPacket));
    assert_eq!(
        body_of(&fa, sc::SCMyHouseRemovedPacket),
        tl.to_le_bytes().to_vec()
    );
    assert!(ops_of(&fx.b.drain()).contains(&sc::SCHouseDemolishedPacket));
    assert!(fx
        .server
        .systems
        .housing
        .manager
        .removed_ids()
        .contains(&hid));
    // Still a (killable) house until it dies.
    assert!(fx.server.systems.housing.manager.by_tl(tl).is_some());
    super::on_unit_died(&fx.server, &WorldObject::from(&house));
    assert!(fx.server.systems.housing.manager.by_tl(tl).is_none());
    let world = fx.server.world.world(0).unwrap();
    assert!(world.house(house.obj_id).is_none());
    // The bound doodad went with it.
    assert!(world
        .all_doodads()
        .iter()
        .all(|d| d.data().template_id != BOUND_DOODAD || d.data().owner_db_id != hid));
}

#[tokio::test]
async fn manual_demolish_returns_deposit_and_restorable_furniture() {
    let mut fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    // A restorable chair from a single (non-stackable) item, kept in the system container,
    // and one from a stack (consumed, only the template id is kept).
    fx.eco.items.lock().unwrap().extend([
        HouseItem {
            item_id: 501,
            template_id: CHAIR_ITEM,
            owner_id: 1,
            count: 1,
            max_count: 1,
            slot_type: super::hooks::slot_type::INVENTORY,
            ..Default::default()
        },
        HouseItem {
            item_id: 502,
            template_id: CHAIR_ITEM,
            owner_id: 1,
            count: 2,
            max_count: 10,
            slot_type: super::hooks::slot_type::INVENTORY,
            ..Default::default()
        },
    ]);
    fx.eco.item_counts.lock().unwrap().insert(CHAIR_ITEM, 3);
    for item in [501u64, 502] {
        let p = CSDecorateHousePacket {
            house_tl: house.data().unit.tl_id,
            design_id: 50,
            x: 1.0,
            y: 1.0,
            z: 0.0,
            quat: [0.0, 0.0, 0.0, 1.0],
            parent_obj_id: house.obj_id,
            item_id: item,
        };
        assert_eq!(ops::decorate_house(&fx.server, &fx.pa, &p), Ok(true));
    }
    let calls = fx.eco.calls();
    assert_eq!(
        calls,
        vec![
            "system 14 501".to_string(),
            "consume 14 8000 1 Some(502)".to_string()
        ]
    );
    assert_eq!(
        manager::act_ability_bonus_from_house(&fx.server, 3, &house),
        7 // same furniture type counts once
    );
    let world = fx.server.world.world(0).unwrap();
    let hid = house.data().id;
    let chairs: Vec<_> = world
        .doodads_by_house_db_id(hid)
        .into_iter()
        .filter(|d| d.data().template_id == CHAIR_DOODAD)
        .collect();
    assert_eq!(chairs.len(), 2);
    {
        let d = chairs
            .iter()
            .find(|d| d.data().item_id == 0)
            .unwrap()
            .data()
            .clone();
        assert_eq!(d.item_template_id, CHAIR_ITEM);
        assert_eq!(d.owner_id, 1);
        assert_eq!(d.parent_obj_id, house.obj_id);
        assert!(d.is_persistent);
    }
    // Demolished by a non-owner: refused.
    fx.b.drain();
    ops::demolish(&fx.server, Some(&fx.pb), &house, false, false);
    assert_eq!(
        fx.b.drain(),
        vec![(sc::SCErrorMsgPacket, body_error(err::INVALID_HOUSE_INFO))]
    );
    fx.eco.mails();
    ops::demolish(&fx.server, Some(&fx.pa), &house, false, false);
    let mails = fx.eco.mails();
    assert_eq!(mails.len(), 1);
    let m = &mails[0];
    assert_eq!(m.copper_coins, 200_000); // deposit (tax * 2) back
    assert_eq!(m.recv_date_ms, m.send_date_ms);
    // The design first, then the furniture (doodad order is the world's).
    assert_eq!(
        m.attachments[0],
        MailAttachment::NewItem {
            template_id: DESIGN_ITEM,
            count: 1,
            grade: 2,
            owner_id: 1
        }
    );
    let mut rest = m.attachments[1..].to_vec();
    rest.sort_by_key(|a| matches!(a, MailAttachment::NewItem { .. }));
    assert_eq!(
        rest,
        vec![
            // The single chair item kept in the system container goes back as is ...
            MailAttachment::Existing {
                item_id: 501,
                owner_id: 1,
                add_count: 0
            },
            // ... the stacked chair is recreated (grade 0: no fixed grade).
            MailAttachment::NewItem {
                template_id: CHAIR_ITEM,
                count: 1,
                grade: 0,
                owner_id: 1
            },
        ]
    );
    // Furniture doodads are gone.
    assert!(world
        .doodads_by_house_db_id(hid)
        .iter()
        .all(|d| d.data().template_id != CHAIR_DOODAD));
}

#[tokio::test]
async fn sale_and_purchase() {
    let mut fx = fixture(false);
    fx.pb.data().money = 3_000_000;
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    let tl = house.data().unit.tl_id;
    house.data().unit.name = "Villa".into();
    fx.a.drain();
    fx.b.drain();
    // Not enough appraisal certificates.
    assert!(!ops::set_for_sale_by_tl(
        &fx.server,
        tl,
        2_500_000,
        0,
        Some(&fx.pa)
    ));
    assert_eq!(
        fx.a.drain(),
        vec![(
            sc::SCErrorMsgPacket,
            body_error(err::HOUSE_CANNOT_SELL_AS_NOT_ENOUGH_SEAL)
        )]
    );
    fx.eco
        .item_counts
        .lock()
        .unwrap()
        .insert(manager::APPRAISAL_CERTIFICATE, 3);
    // Unknown designated buyer.
    assert!(!ops::set_for_sale_by_tl(
        &fx.server,
        tl,
        2_500_000,
        77,
        Some(&fx.pa)
    ));
    assert!(ops::set_for_sale_by_tl(
        &fx.server,
        tl,
        2_500_000,
        0,
        Some(&fx.pa)
    ));
    assert!(fx
        .eco
        .calls()
        .contains(&"consume 92 28085 3 None".to_string()));
    let fb = fx.b.drain();
    let expected = SCHouseSetForSalePacket {
        tl,
        money_amount: 2_500_000,
        sell_to_player_id: 0,
        sell_to_name: String::new(),
        house_name: "Villa".into(),
    };
    let mut s = PacketStream::new();
    aaemu_commons::Marshal::write(&expected, &mut s);
    assert_eq!(fb[0], (sc::SCHouseSetForSalePacket, s.into_bytes()));
    let world = fx.server.world.world(0).unwrap();
    let hid = house.data().id;
    let signs = |w: &crate::world::WorldInstance| {
        w.doodads_by_house_db_id(hid)
            .into_iter()
            .filter(|d| d.data().template_id == manager::FOR_SALE_MARKER_DOODAD_ID)
            .count()
    };
    assert_eq!(signs(&world), 4);
    // Owner cannot buy; price mismatch; not enough money.
    assert!(!ops::buy_house(&fx.server, tl, 2_500_000, &fx.pa));
    assert_eq!(
        fx.a.drain().last().unwrap(),
        &(
            sc::SCErrorMsgPacket,
            body_error(err::HOUSE_CANNOT_BUY_AS_OWNER)
        )
    );
    assert!(!ops::buy_house(&fx.server, tl, 1, &fx.pb));
    assert_eq!(
        fx.b.drain().last().unwrap(),
        &(
            sc::SCErrorMsgPacket,
            body_error(err::HOUSE_CANNOT_BUY_AS_SALE_INFO_CHANGED)
        )
    );
    fx.eco.calls();
    fx.eco.mails();
    assert!(ops::buy_house(&fx.server, tl, 2_500_000, &fx.pb));
    assert_eq!(fx.eco.calls()[0], "money -2500000 92");
    let mails = fx.eco.mails();
    assert_eq!(mails.len(), 2);
    assert_eq!(mails[0].sender_name, ".houseBought");
    assert_eq!(mails[0].title, "title(25,'Villa')");
    assert_eq!(mails[0].text, "body('Alpha', 'Villa', 2500000)");
    assert_eq!(mails[1].sender_name, ".houseSold");
    assert_eq!(mails[1].receiver_id, 1);
    assert_eq!(mails[1].copper_coins, 2_500_000);
    assert_eq!(mails[1].title, "title('Beta','Villa')");
    {
        let h = house.data();
        assert_eq!((h.unit.owner_id, h.account_id, h.sell_price), (2, 1002, 0));
        assert_eq!(h.permission, housing_permission::PRIVATE);
    }
    assert_eq!(signs(&world), 0);
    let fb = fx.b.drain();
    assert!(ops_of(&fb).contains(&sc::SCHouseSoldPacket));
    assert_eq!(
        body_of(&fb, sc::SCMyHousePacket),
        ops::house_body(&fx.server, &house)
    );
    assert!(ops_of(&fx.a.drain()).contains(&sc::SCMyHouseRemovedPacket));

    // Cancelling a sale mails the certificates back to the online owner.
    assert!(ops::set_for_sale_by_tl(&fx.server, tl, 100, 0, None)); // GM: no certificates
    fx.eco.calls();
    assert!(ops::cancel_for_sale_by_tl(&fx.server, tl, true));
    assert_eq!(fx.eco.calls(), vec!["mail+items 28085 1".to_string()]);
    let m = &fx.eco.mails()[0];
    assert_eq!(m.sender_name, ".houseSellCancel");
    assert_eq!(m.text, "body('Villa', 28085, 1)");
    assert!(ops_of(&fx.b.drain()).contains(&sc::SCHouseResetForSalePacket));
    assert_eq!(signs(&world), 0);
}

#[tokio::test]
async fn permissions_name_and_recover() {
    let mut fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    let tl = house.data().unit.tl_id;
    fx.a.drain();
    fx.b.drain();
    // Non-owner: ignored.
    ops::change_house_permission(&fx.server, &fx.pb, tl, housing_permission::GUILD);
    assert!(fx.b.drain().is_empty());
    ops::change_house_permission(&fx.server, &fx.pa, tl, housing_permission::PUBLIC);
    assert_eq!(
        fx.b.drain(),
        vec![(
            sc::SCHousePermissionChangedPacket,
            vec![tl as u8, (tl >> 8) as u8, 2]
        )]
    );
    fx.a.drain(); // the owner is around too
    ops::change_house_name(&fx.server, &mut fx.a.conn, &fx.pa, tl, "villa");
    assert_eq!(house.data().unit.name, "Villa");
    let mut s = PacketStream::new();
    s.write_bc(house.obj_id).write_str("Villa");
    assert_eq!(
        fx.a.drain(),
        vec![(sc::SCUnitNameChangedPacket, s.into_bytes())]
    );
    assert_eq!(fx.b.drain().len(), 1); // nearby viewers see the renamed house too
                                       // `place_house` does not set AllowRecover (Build does): false -> true.
    ops::toggle_allow_recover(&fx.server, &fx.pa, tl);
    assert!(house.data().allow_recover);
    assert_eq!(
        fx.b.drain(),
        vec![(
            sc::SCHousingRecoverTogglePacket,
            vec![tl as u8, (tl >> 8) as u8, 1]
        )]
    );
    // Always-public templates stay public.
    let farm = place_house(&fx, FARM, &fx.pa, 300.0, 300.0, -1);
    let ftl = farm.data().unit.tl_id;
    ops::change_house_permission(&fx.server, &fx.pa, ftl, housing_permission::PRIVATE);
    assert_eq!(farm.data().permission, housing_permission::PUBLIC);
}

#[tokio::test]
async fn placement_and_interaction_permissions() {
    let fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    // Garden square [112, 128) x [92, 108).
    let at = |x, y| super::house_at_location(&fx.server, x, y).map(|h| h.obj_id);
    assert_eq!(at(112.0, 92.0), Some(house.obj_id));
    assert_eq!(at(127.9, 107.9), Some(house.obj_id));
    assert_eq!(at(128.0, 100.0), None);
    assert_eq!(at(120.0, 108.0), None);
    assert_eq!(at(111.9, 100.0), None);

    let allowed = |p: &PlayerRef| super::allowed_to_interact(&fx.server, &house, p);
    assert!(allowed(&fx.pa)); // owner
    assert!(!allowed(&fx.pb)); // private
                               // Same account as the owner.
    fx.pb.data().account_id = 1001;
    assert!(allowed(&fx.pb));
    fx.pb.data().account_id = 1002;
    house.data().permission = housing_permission::FAMILY;
    // A missing family must not grant access to a family-only house.
    assert!(!allowed(&fx.pb));
    assert!(allowed(&fx.pa)); // the owner may still use the house without a family
    fx.pb.data().family = 9;
    assert!(!allowed(&fx.pb)); // family 9 does not exist / owner not a member
    house.data().permission = housing_permission::GUILD;
    assert!(!allowed(&fx.pb)); // neither character is in a guild
    assert!(allowed(&fx.pa));
    house.data().permission = housing_permission::PUBLIC;
    assert!(allowed(&fx.pb));
    // Unfinished houses are always open.
    house.data().permission = housing_permission::PRIVATE;
    house.data().current_step = 0;
    assert!(allowed(&fx.pb));
    // Doodads placed on the land go through the npcs bridge defaults.
    assert_eq!(
        crate::systems::npcs::hooks::house_by_db_id(&fx.server, house.data().id).map(|h| h.obj_id),
        Some(house.obj_id)
    );
}

#[tokio::test]
async fn lobby_and_select_packets() {
    let mut fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    let _other = place_house(&fx, DESIGN, &fx.pb, 300.0, 300.0, -1);
    fx.a.drain();
    super::send_login_houses(&fx.server, &mut fx.a.conn);
    let mut expected = 1u32.to_le_bytes().to_vec();
    expected.extend(ops::house_body(&fx.server, &house));
    assert_eq!(
        fx.a.drain(),
        vec![(sc::SCLoginCharInfoHousePacket, expected)]
    );
    super::send_my_houses(&fx.server, &mut fx.a.conn, 1);
    assert_eq!(
        fx.a.drain(),
        vec![(sc::SCMyHousePacket, ops::house_body(&fx.server, &house))]
    );
    super::send_my_houses(&fx.server, &mut fx.a.conn, 2);
    assert!(fx.a.drain().is_empty());

    // House.Write layout through the shared model (owner name resolved, finished house: 0/0).
    let b = ops::house_body(&fx.server, &house);
    let h = house.data().clone();
    let mut s = PacketStream::new();
    s.write_u16(h.unit.tl_id)
        .write_u32(h.id)
        .write_bc(house.obj_id)
        .write_u32(DESIGN)
        .write_pisc(&[700, 0])
        .write_u32(1)
        .write_u32(1)
        .write_str("Alpha")
        .write_u32(1001)
        .write_u8(housing_permission::PRIVATE)
        .write_i32(0)
        .write_i32(0)
        .write_i32(100_000)
        .write_i64(crate::models::transform::convert_long(120.0))
        .write_i64(crate::models::transform::convert_long(100.0))
        .write_f32(100.0)
        .write_str(&h.unit.name)
        .write_bool(false)
        .write_u32(0)
        .write_u32(0)
        .write_str("");
    assert_eq!(b, s.into_bytes());
}

#[tokio::test]
async fn faction_sync_and_character_deletion() {
    let mut fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    fx.b.drain();
    super::on_character_loaded(&fx.server, 1, 161);
    let fb = fx.b.drain();
    let mut s = PacketStream::new();
    s.write_bc(house.obj_id)
        .write_str(&house.data().unit.name)
        .write_u32(0)
        .write_u32(161)
        .write_bool(false);
    assert_eq!(fb, vec![(sc::SCUnitFactionChangedPacket, s.into_bytes())]);

    super::delete_character_assets(&fx.server, 1);
    {
        let h = house.data();
        assert_eq!(h.permission, housing_permission::PUBLIC);
        assert!(h.protection_end_date < manager::now() - 20 * 24 * 3600);
    }
    // The next tax tick demolishes it.
    manager::check_housing_taxes(&fx.server);
    assert_eq!(house.data().unit.owner_id, 0);
}

#[tokio::test]
async fn save_rows_and_weekly_tax() {
    let fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    let row = manager::save_row(&house).unwrap();
    {
        let h = house.data();
        assert_eq!(row.id, h.id);
        assert_eq!((row.owner, row.co_owner, row.account_id), (1, 1, 1001));
        assert_eq!(row.template_id, DESIGN);
        assert_eq!((row.x, row.y, row.z), (120.0, 100.0, 100.0));
        assert_eq!(row.current_step, -1);
        assert_eq!(row.faction_id, 0); // faction 101 is not in the empty faction table
        assert_eq!(row.protected_until, h.protection_end_date);
    }
    house.data().is_dirty = false;
    assert!(manager::save_row(&house).is_none());
    let (before, id) = {
        let h = house.data();
        (h.protection_end_date, h.id)
    };
    assert!(super::pay_weekly_tax(&fx.server, id));
    assert_eq!(house.data().protection_end_date, before + 7 * 24 * 3600);
    assert!(manager::save_row(&house).is_some());
    house.data().unit.owner_id = 0;
    assert!(manager::save_row(&house).is_none()); // demolished: never written
}

#[tokio::test]
async fn load_rows_and_persistent_bound_doodads() {
    let fx = fixture(false);
    let world = fx.server.world.world(0).unwrap();
    let row = manager::HouseRow {
        id: 55,
        account_id: 1001,
        owner: 1,
        co_owner: 1,
        template_id: DESIGN,
        name: "Loaded".into(),
        x: 200.0,
        y: 200.0,
        z: 50.0,
        current_step: -1,
        permission: housing_permission::GUILD,
        place_date: 1_000_000,
        protected_until: 1_000_000,
        faction_id: 101,
        allow_recover: true,
        ..Default::default()
    };
    let house = manager::load_house_row(&fx.server, &world, &row).unwrap();
    {
        let h = house.data();
        // Same place / protection date: two weeks for free.
        assert_eq!(h.protection_end_date, 1_000_000 + 14 * 24 * 3600);
        assert!(h.is_dirty); // persist the corrected dates for the next restart
        assert_eq!(h.unit.transform.zone_id, TEST_ZONE);
        assert_eq!(h.permission, housing_permission::GUILD);
        // Non-persistent mode: the bound doodads are created by the CurrentStep setter.
        assert_eq!(h.attached_doodads.len(), 1);
    }
    // Persistent mode: bound doodads come from the DB and are registered when loaded.
    let d = crate::systems::npcs::doodad::manager::create(
        &fx.server,
        &world,
        0,
        BOUND_DOODAD,
        None,
        true,
    )
    .unwrap();
    d.data().attach_point = 9;
    assert!(super::attach_loaded_doodad(&fx.server, &world, 55, &d));
    assert_eq!(d.data().transform.parent_obj_id, house.obj_id);
    // Without UsePersistentHouseDoodads it is only parented.
    assert_eq!(house.data().attached_doodads.len(), 1);
    assert!(!super::attach_loaded_doodad(&fx.server, &world, 56, &d));

    // ReconcileBoundDoodads: a duplicate binding is removed.
    fx.server
        .systems
        .housing
        .manager
        .add_attached(&house, d.clone());
    assert_eq!(manager::reconcile_bound_doodads(&fx.server), (0, 1));
    assert_eq!(house.data().attached_doodads.len(), 1);
}

#[tokio::test]
async fn house_radius_counts_in_skill_distance() {
    let fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    assert_eq!(
        super::house_radius(&fx.server, &WorldObject::from(&house)),
        Some(8.0)
    );
    assert_eq!(
        super::house_radius(&fx.server, &WorldObject::from(&fx.pa)),
        None
    );
}

#[tokio::test]
async fn house_sale_packets_require_owner_and_keep_internal_gm_operations() {
    use crate::net::registry::ClientPacket;

    let mut fx = fixture(false);
    fx.a.conn.active_char = Some(fx.pa.clone());
    fx.b.conn.active_char = Some(fx.pb.clone());
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    let tl = house.data().unit.tl_id;
    fx.eco
        .item_counts
        .lock()
        .unwrap()
        .insert(manager::APPRAISAL_CERTIFICATE, 1);
    assert!(!ops::set_for_sale_by_tl(
        &fx.server,
        tl,
        100,
        0,
        Some(&fx.pb)
    ));
    CSSellHousePacket {
        tl,
        money_amount: 100,
        sell_to: String::new(),
    }
    .handle(&fx.server, &mut fx.b.conn)
    .await
    .unwrap();
    assert_eq!(house.data().sell_price, 0);
    assert!(fx.eco.calls().is_empty());
    assert!(ops::set_for_sale_by_tl(&fx.server, tl, 100, 0, None));
    CSSellHousePacket {
        tl,
        money_amount: 0,
        sell_to: String::new(),
    }
    .handle(&fx.server, &mut fx.b.conn)
    .await
    .unwrap();
    CSSellHouseCancelPacket { tl }
        .handle(&fx.server, &mut fx.b.conn)
        .await
        .unwrap();
    assert_eq!(house.data().sell_price, 100);
    assert!(fx.eco.calls().is_empty());
    assert!(fx.eco.mails().is_empty());
    CSSellHouseCancelPacket { tl }
        .handle(&fx.server, &mut fx.a.conn)
        .await
        .unwrap();
    assert_eq!(house.data().sell_price, 0);
    assert_eq!(fx.eco.calls(), vec!["mail+items 28085 1"]);
}

#[tokio::test]
async fn mismatched_house_design_rejected_before_any_payment_or_consumption() {
    let fx = fixture(false);
    fx.eco.items.lock().unwrap().push(HouseItem {
        item_id: 77,
        template_id: DESIGN_ITEM,
        owner_id: 1,
        slot_type: super::hooks::slot_type::INVENTORY,
        count: 1,
        max_count: 1,
        ..Default::default()
    });
    fx.eco.item_counts.lock().unwrap().insert(DESIGN_ITEM, 1);
    let mut packet = create_packet(0.0);
    packet.design_id = FARM; // the owned cottage design does not build this farm
    ops::build(&fx.server, &fx.pa, 1001, &packet);
    assert!(fx.eco.calls().is_empty());
    assert!(fx
        .server
        .systems
        .housing
        .manager
        .by_account(1001)
        .is_empty());
    assert_eq!(fx.eco.item_counts.lock().unwrap()[&DESIGN_ITEM], 1);
}

#[tokio::test]
async fn decoration_rejects_private_house_and_mismatched_owned_item() {
    let fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 120.0, 100.0, -1);
    fx.eco.items.lock().unwrap().push(HouseItem {
        item_id: 501,
        template_id: CHAIR_ITEM,
        owner_id: 2,
        count: 1,
        max_count: 1,
        ..Default::default()
    });
    let packet = CSDecorateHousePacket {
        house_tl: house.data().unit.tl_id,
        design_id: 50,
        x: 1.0,
        y: 1.0,
        z: 0.0,
        quat: [0.0, 0.0, 0.0, 1.0],
        parent_obj_id: house.obj_id,
        item_id: 501,
    };
    assert_eq!(ops::decorate_house(&fx.server, &fx.pb, &packet), Ok(false));
    house.data().permission = housing_permission::PUBLIC;
    fx.eco.items.lock().unwrap()[0].template_id = DESIGN_ITEM;
    assert_eq!(ops::decorate_house(&fx.server, &fx.pb, &packet), Ok(false));
    assert!(fx.eco.calls().is_empty());
    let world = fx.server.world.world(0).unwrap();
    let house_id = house.data().id;
    assert!(!world
        .doodads_by_house_db_id(house_id)
        .iter()
        .any(|d| d.data().template_id == CHAIR_DOODAD));
}

#[test]
fn decoration_design_accepts_every_bound_item_template() {
    let mut data = super::Data::default();
    for (id, item_id) in [(1, 8000), (2, 8001)] {
        data.add_item_housing_decoration(ItemHousingDecoration {
            id,
            item_id,
            design_id: 50,
            restore: true,
        });
    }
    assert!(data.decoration_accepts_item(50, 8000));
    assert!(data.decoration_accepts_item(50, 8001));
    assert!(!data.decoration_accepts_item(50, 8002));
    assert!(!data.decoration_accepts_item(51, 8000));
}

#[tokio::test]
async fn tax_plaque_distinguishes_paid_due_and_expired_houses() {
    let mut fx = fixture(false);
    let house = place_house(&fx, DESIGN, &fx.pa, 300., 300., -1);
    let tl = house.data().unit.tl_id;
    fx.a.drain();
    let time = manager::now();
    for (end, paid, weeks) in [
        (time + 14 * 86400, true, -1i32),
        (time + 3 * 86400, false, 0),
        (time - 86400, false, 1),
    ] {
        house.data().protection_end_date = end;
        ops::house_tax_info(&fx.server, &fx.pa, tl);
        let body = body_of(&fx.a.drain(), sc::SCHouseTaxInfoPacket);
        assert_eq!(body[22], u8::from(paid));
        assert_eq!(i32::from_le_bytes(body[23..27].try_into().unwrap()), weeks);
    }
}
