//! Housing access and failed payment/recovery regressions on a synthetic world.

use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};

use super::data::{HousingDecoration, HousingTemplate, ItemHousingDecoration, Taxation};
use super::hooks::{slot_type, HouseItem, HouseMail, HousingEconomyHooks};
use super::{manager, ops};
use crate::data::GameData;
use crate::models::doodad::doodad_owner_type;
use crate::models::house::housing_permission;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::systems::npcs::doodad::funcs;
use crate::systems::npcs::doodad::interactions::{self, world_interaction_type as wi};
use crate::systems::npcs::doodad::runtime::DoodadCtx;
use crate::systems::npcs::doodad::templates::{
    DoodadFunc, DoodadFuncRecoverItem, DoodadTemplate, FuncTemplate,
};
use crate::systems::npcs::hooks::EconomyBridge;
use crate::test_support::*;
use crate::world::{DoodadRef, HouseRef, PlayerRef, WorldObject};

const HOUSE: u32 = 310;
const CHAIR: u32 = 510;
const CHAIR_ITEM: u32 = 810;
const DESIGN: u32 = 51;

struct Eco {
    item: Mutex<HouseItem>,
    debit_ok: AtomicBool,
    mail_ok: AtomicBool,
    debit_calls: AtomicUsize,
    recovery_calls: AtomicUsize,
    consumed_certificates: AtomicUsize,
    available_certificates: AtomicUsize,
    mails: Mutex<Vec<HouseMail>>,
}

impl Default for Eco {
    fn default() -> Self {
        Self {
            item: Mutex::new(HouseItem {
                item_id: 500,
                template_id: CHAIR_ITEM,
                owner_id: 1,
                count: 1,
                max_count: 1,
                slot_type: slot_type::INVENTORY,
                ..Default::default()
            }),
            debit_ok: AtomicBool::new(true),
            mail_ok: AtomicBool::new(true),
            debit_calls: AtomicUsize::new(0),
            recovery_calls: AtomicUsize::new(0),
            consumed_certificates: AtomicUsize::new(0),
            available_certificates: AtomicUsize::new(100),
            mails: Mutex::new(Vec::new()),
        }
    }
}

impl HousingEconomyHooks for Eco {
    fn inventory_item(
        &self,
        _: &crate::server::GameServer,
        player: &PlayerRef,
        id: u64,
    ) -> Option<HouseItem> {
        let item = *self.item.lock().unwrap();
        (item.item_id == id && item.owner_id == u64::from(player.character_id)).then_some(item)
    }
    fn item(&self, _: &crate::server::GameServer, id: u64) -> Option<HouseItem> {
        let item = *self.item.lock().unwrap();
        (item.item_id == id).then_some(item)
    }
    fn bag_consume_item(
        &self,
        _: &crate::server::GameServer,
        _: &PlayerRef,
        _: u8,
        template: u32,
        count: i32,
        _: Option<u64>,
    ) -> i32 {
        if template == manager::APPRAISAL_CERTIFICATE {
            self.consumed_certificates
                .fetch_add(count as usize, Ordering::SeqCst);
            return count;
        }
        self.debit_calls.fetch_add(1, Ordering::SeqCst);
        let mut item = self.item.lock().unwrap();
        if !self.debit_ok.load(Ordering::SeqCst) || item.count < count {
            return 0;
        }
        item.count -= count;
        count
    }
    fn move_item_to_system_container(
        &self,
        _: &crate::server::GameServer,
        _: &PlayerRef,
        _: u8,
        _: u64,
    ) -> bool {
        self.debit_calls.fetch_add(1, Ordering::SeqCst);
        if !self.debit_ok.load(Ordering::SeqCst) {
            return false;
        }
        self.item.lock().unwrap().slot_type = slot_type::SYSTEM;
        true
    }
    fn pay_house_sale_certificates(
        &self,
        _: &crate::server::GameServer,
        _: &PlayerRef,
        count: i32,
    ) -> bool {
        if count <= 0
            || self
                .available_certificates
                .try_update(Ordering::SeqCst, Ordering::SeqCst, |have| {
                    have.checked_sub(count as usize)
                })
                .is_err()
        {
            return false;
        }
        self.consumed_certificates
            .fetch_add(count as usize, Ordering::SeqCst);
        true
    }
    fn subtract_money(
        &self,
        _: &crate::server::GameServer,
        player: &PlayerRef,
        amount: i32,
        _: u8,
    ) -> bool {
        self.debit_calls.fetch_add(1, Ordering::SeqCst);
        let mut c = player.data();
        if amount < 0 || c.money < i64::from(amount) {
            return false;
        }
        c.money -= i64::from(amount);
        true
    }
    fn send_mail_with_new_items(
        &self,
        _: &crate::server::GameServer,
        _: &PlayerRef,
        mail: HouseMail,
        _: u32,
        _: i32,
    ) -> bool {
        if !self.mail_ok.load(Ordering::SeqCst) {
            return false;
        }
        self.mails.lock().unwrap().push(mail);
        true
    }
    fn send_mail(&self, _: &crate::server::GameServer, mail: HouseMail) -> bool {
        if !self.mail_ok.load(Ordering::SeqCst) {
            return false;
        }
        self.mails.lock().unwrap().push(mail);
        true
    }
    fn send_return_mails(
        &self,
        server: &crate::server::GameServer,
        mails: Vec<HouseMail>,
        payment: Option<(&PlayerRef, i32)>,
    ) -> bool {
        if !self.mail_ok.load(Ordering::SeqCst) {
            return false;
        }
        if let Some((player, price)) = payment {
            if !self.subtract_money(server, player, price, super::hooks::item_task::BUY_HOUSE) {
                return false;
            }
        }
        self.mails.lock().unwrap().extend(mails);
        true
    }
}

impl EconomyBridge for Eco {
    fn acquire_item(
        &self,
        _: &crate::server::GameServer,
        _: &PlayerRef,
        _: u8,
        _: u32,
        _: i32,
        _: i32,
    ) -> bool {
        self.recovery_calls.fetch_add(1, Ordering::SeqCst);
        self.debit_ok.load(Ordering::SeqCst)
    }
}

struct Fx {
    server: crate::server::GameServer,
    eco: Arc<Eco>,
    owner: PlayerRef,
    guest: PlayerRef,
    owner_client: TestClient,
    guest_client: TestClient,
    house: HouseRef,
}

fn fixture(always_public: bool) -> Fx {
    fixture_with_limits(always_public, 0, None)
}

fn fixture_with_limits(always_public: bool, overall_limit: u32, group_limit: Option<u32>) -> Fx {
    let mut data = GameData::empty();
    for (id, max_count) in [(CHAIR_ITEM, 1), (manager::APPRAISAL_CERTIFICATE, 1000)] {
        data.items
            .templates
            .insert(id, crate::data::items::ItemTemplate::plain(id, max_count));
    }
    data.systems.economy.craft.crafts.insert(
        88,
        crate::systems::economy::craft_data::Craft {
            id: 88,
            skill_id: 88,
            craft_products: vec![crate::systems::economy::craft_data::CraftProduct {
                item_id: CHAIR_ITEM,
                amount: 1,
                ..Default::default()
            }],
            ..Default::default()
        },
    );
    data.systems.economy.craft.skills.insert(
        88,
        crate::systems::economy::craft_data::CraftSkill {
            id: 88,
            ..Default::default()
        },
    );
    data.systems
        .skills
        .add_skill(crate::systems::skills::data::SkillTemplate {
            id: 88,
            max_range: 4,
            ..Default::default()
        });
    data.systems.housing.add_template(HousingTemplate {
        id: HOUSE,
        main_model_id: 700,
        hp: 100,
        garden_radius: 8.0,
        is_sellable: true,
        always_public,
        deco_limit: overall_limit,
        housing_deco_limit_id: 1,
        taxation: Some(Taxation {
            id: 1,
            tax: 0,
            show: true,
        }),
        ..Default::default()
    });
    data.systems
        .housing
        .add_decoration(HousingDecoration {
            id: DESIGN,
            doodad_id: CHAIR,
            deco_act_ability_group_id: 7,
            ..Default::default()
        })
        .unwrap();
    data.systems
        .housing
        .add_decoration(HousingDecoration {
            id: DESIGN + 1,
            doodad_id: CHAIR,
            deco_act_ability_group_id: 8,
            ..Default::default()
        })
        .unwrap();
    data.systems
        .housing
        .add_item_housing_decoration(ItemHousingDecoration {
            id: 2,
            design_id: DESIGN + 1,
            item_id: CHAIR_ITEM + 1,
            restore: true,
        });
    if let Some(limit) = group_limit {
        data.systems.housing.decoration_limits.insert((1, 7), limit);
    }
    data.systems
        .housing
        .add_item_housing_decoration(ItemHousingDecoration {
            id: 1,
            design_id: DESIGN,
            item_id: CHAIR_ITEM,
            restore: true,
        });
    for id in [CHAIR, manager::FOR_SALE_MARKER_DOODAD_ID] {
        data.systems.npcs.doodad.templates.insert(
            id,
            DoodadTemplate {
                id,
                ..Default::default()
            },
        );
    }
    data.systems.npcs.doodad.funcs_by_groups.insert(
        70,
        vec![DoodadFunc {
            group_id: 70,
            func_id: 1,
            func_type: "DoodadFuncRecoverItem".into(),
            ..Default::default()
        }],
    );
    data.systems
        .npcs
        .doodad
        .func_templates
        .entry("DoodadFuncRecoverItem")
        .or_default()
        .insert(
            1,
            FuncTemplate::RecoverItem(DoodadFuncRecoverItem { id: 1 }),
        );
    let server = test_server_with(data);
    let eco = Arc::new(Eco::default());
    server.systems.housing.hooks.install(eco.clone());
    server.systems.npcs.hooks.economy.install(eco.clone());
    server.names.add_character(1, "Owner", 1001);
    server.names.add_character(2, "Guest", 1002);
    let mut owner_client = TestClient::new(&server, 1, 1001);
    let mut guest_client = TestClient::new(&server, 2, 1002);
    let owner = spawn_player(
        &server,
        &owner_client,
        test_character(1, "Owner", 100.0, 100.0),
        server.ids.object.get_next_id().unwrap(),
    );
    let guest = spawn_player(
        &server,
        &guest_client,
        test_character(2, "Guest", 102.0, 100.0),
        server.ids.object.get_next_id().unwrap(),
    );
    guest.data().money = 10_000;
    let world = server.world.world(0).unwrap();
    let house = manager::create(&server, HOUSE, 101, &world, 0, 0).unwrap();
    {
        let mut h = house.data();
        h.id = 1;
        h.unit.owner_id = 1;
        h.account_id = 1001;
        h.unit.transform.set_xyz(105.0, 100.0, 100.0);
        h.unit.transform.zone_id = TEST_ZONE;
        h.protection_end_date = manager::now() + 7 * 86400;
    }
    manager::set_current_step(&server, &house, -1, false);
    assert!(server.systems.housing.manager.insert(&house));
    manager::spawn_house(&server, &house);
    owner_client.drain();
    guest_client.drain();
    Fx {
        server,
        eco,
        owner,
        guest,
        owner_client,
        guest_client,
        house,
    }
}

fn decoration(fx: &Fx) -> super::packets::CSDecorateHousePacket {
    super::packets::CSDecorateHousePacket {
        house_tl: fx.house.data().unit.tl_id,
        design_id: DESIGN,
        x: 1.0,
        y: 1.0,
        z: 0.0,
        quat: [0.0, 0.0, 0.0, 1.0],
        parent_obj_id: fx.house.obj_id,
        item_id: 500,
    }
}

fn furniture(fx: &Fx) -> Vec<DoodadRef> {
    fx.server
        .world
        .world(0)
        .unwrap()
        .doodads_by_house_db_id(1)
        .into_iter()
        .filter(|d| d.data().template_id == CHAIR)
        .collect()
}

fn recoverable(fx: &Fx) -> DoodadRef {
    let world = fx.server.world.world(0).unwrap();
    let d = crate::systems::npcs::doodad::manager::create(
        &fx.server,
        &world,
        0,
        CHAIR,
        Some(&WorldObject::from(&fx.house)),
        true,
    )
    .unwrap();
    {
        let mut data = d.data();
        data.owner_type = doodad_owner_type::HOUSING;
        data.owner_db_id = 1;
        data.item_template_id = CHAIR_ITEM;
        data.func_group_id = 70;
    }
    crate::systems::npcs::doodad::manager::spawn(&fx.server, &world, &d);
    d
}

fn recover(fx: &Fx, player: &PlayerRef, d: &DoodadRef) {
    interactions::execute_interaction(
        &fx.server,
        wi::RECOVER_ITEM,
        &WorldObject::from(player),
        Some(&WorldObject::from(d)),
        10,
        0,
        None,
    );
}

fn production_economy(fx: &Fx) {
    fx.server.systems.housing.hooks.install(Arc::new(
        crate::systems::economy::bridges::EconomyForHousing,
    ));
    fx.server
        .systems
        .npcs
        .hooks
        .economy
        .install(Arc::new(crate::systems::economy::bridges::EconomyForNpcs));
    for player in [&fx.owner, &fx.guest] {
        let inv = crate::systems::economy::inventory::Inv::of(player);
        for slot in [slot_type::INVENTORY, slot_type::SYSTEM] {
            fx.server.items.add_container(inv.get(slot).unwrap());
        }
    }
}

fn give(fx: &Fx, template_id: u32, count: i32) -> u64 {
    let id = u64::from(fx.server.ids.item.get_next_id().unwrap());
    let item = crate::models::item::Item::create(
        id,
        fx.server.game_data.items.template(template_id).unwrap(),
        count,
        0,
        1,
        &fx.server.game_data.items,
        0,
    );
    let bag = crate::systems::economy::inventory::Inv::of(&fx.owner).bag();
    bag.lock()
        .unwrap()
        .add_new_item(item, -1, &fx.server.game_data.items)
        .unwrap();
    id
}

#[derive(Default)]
struct BenchRecorder {
    casts: AtomicUsize,
}

impl crate::systems::economy::craft::CraftHooks for BenchRecorder {
    fn start_craft_cast(
        &self,
        _: &crate::server::GameServer,
        _: &PlayerRef,
        _: u32,
        _: u32,
        _: f32,
    ) {
        self.casts.fetch_add(1, Ordering::SeqCst);
    }
}

fn bench(fx: &Fx) -> (DoodadRef, Arc<BenchRecorder>) {
    let d = recoverable(fx);
    d.data().transform.set_xyz(101.0, 100.0, 100.0);
    fx.server
        .world
        .world(0)
        .unwrap()
        .add_visible_object(&d, &fx.server.view_ctx());
    let recorder = Arc::new(BenchRecorder::default());
    fx.server
        .systems
        .economy
        .craft
        .hooks
        .install(recorder.clone());
    (d, recorder)
}

async fn request_bench_craft(fx: &mut Fx, owner: bool, obj_id: u32) {
    let client = if owner {
        &mut fx.owner_client
    } else {
        &mut fx.guest_client
    };
    client.conn.active_char = Some(if owner {
        fx.owner.clone()
    } else {
        fx.guest.clone()
    });
    crate::systems::economy::craft::CSExecuteCraft {
        craft_id: 88,
        obj_id,
        count: 1,
    }
    .handle(&fx.server, &mut client.conn)
    .await
    .unwrap();
}

#[tokio::test]
async fn permission_broadcast_uses_effective_value_and_ignores_invalid_values() {
    let mut fx = fixture(true);
    let tl = fx.house.data().unit.tl_id;
    ops::change_house_permission(&fx.server, &fx.owner, tl, housing_permission::PRIVATE);
    assert_eq!(
        fx.guest_client.drain(),
        vec![(
            sc::SCHousePermissionChangedPacket,
            vec![tl as u8, (tl >> 8) as u8, housing_permission::PUBLIC]
        )]
    );
    fx.house.data().is_dirty = false;
    ops::change_house_permission(&fx.server, &fx.owner, tl, 255);
    assert_eq!(fx.house.data().permission, housing_permission::PUBLIC);
    assert!(!fx.house.data().is_dirty);
    assert!(fx.guest_client.drain().is_empty());
}

#[tokio::test]
async fn house_rename_reaches_neighbor_and_recovery_toggle_requires_owner() {
    let mut fx = fixture(false);
    let tl = fx.house.data().unit.tl_id;
    ops::change_house_name(
        &fx.server,
        &mut fx.owner_client.conn,
        &fx.owner,
        tl,
        "new cottage",
    );
    assert_eq!(fx.house.data().unit.name, "New cottage");
    assert_eq!(fx.guest_client.drain()[0].0, sc::SCUnitNameChangedPacket);
    ops::toggle_allow_recover(&fx.server, &fx.guest, tl);
    assert!(!fx.house.data().allow_recover);
    ops::toggle_allow_recover(&fx.server, &fx.owner, tl);
    assert!(fx.house.data().allow_recover);
}

#[tokio::test]
async fn remote_owner_receives_rename_once_while_nearby_viewers_also_update() {
    let mut fx = fixture(false);
    fx.owner
        .data()
        .unit
        .transform
        .set_xyz(10_000.0, 10_000.0, 100.0);
    fx.server
        .world
        .world(0)
        .unwrap()
        .add_visible_object(&fx.owner, &fx.server.view_ctx());
    fx.owner_client.drain();
    fx.guest_client.drain();
    let tl = fx.house.data().unit.tl_id;
    ops::change_house_name(
        &fx.server,
        &mut fx.owner_client.conn,
        &fx.owner,
        tl,
        "remote cottage",
    );
    assert_eq!(
        fx.owner_client.drain_ops(),
        vec![sc::SCUnitNameChangedPacket]
    );
    assert_eq!(
        fx.guest_client.drain_ops(),
        vec![sc::SCUnitNameChangedPacket]
    );
}

#[tokio::test]
async fn sale_rejects_unrepresentable_money_before_certificate_debit() {
    let fx = fixture(false);
    for price in [0, i32::MAX as u32 + 1, u32::MAX] {
        assert!(!ops::set_for_sale(
            &fx.server,
            &fx.house,
            price,
            0,
            Some(&fx.owner)
        ));
    }
    assert_eq!(fx.eco.consumed_certificates.load(Ordering::SeqCst), 0);
    fx.house.data().sell_price = u32::MAX; // protect against legacy persisted listings too
    let tl = fx.house.data().unit.tl_id;
    assert!(!ops::buy_house(&fx.server, tl, u32::MAX, &fx.guest));
    assert_eq!(fx.eco.debit_calls.load(Ordering::SeqCst), 0);
}

#[tokio::test]
async fn insufficient_appraisal_certificates_do_not_consume_partial_fee() {
    let fx = fixture(false);
    fx.eco.available_certificates.store(2, Ordering::SeqCst);
    assert!(!ops::set_for_sale(
        &fx.server,
        &fx.house,
        2_500_000,
        0,
        Some(&fx.owner)
    ));
    assert_eq!(fx.eco.available_certificates.load(Ordering::SeqCst), 2);
    assert_eq!(fx.eco.consumed_certificates.load(Ordering::SeqCst), 0);
    assert_eq!(fx.house.data().sell_price, 0);
}

#[tokio::test]
async fn production_listing_preserves_insufficient_certificates_and_consumes_complete_fee() {
    let fx = fixture(false);
    production_economy(&fx);
    give(&fx, manager::APPRAISAL_CERTIFICATE, 2);
    assert!(!ops::set_for_sale(
        &fx.server,
        &fx.house,
        2_500_000,
        0,
        Some(&fx.owner)
    ));
    let inv = crate::systems::economy::inventory::Inv::of(&fx.owner);
    assert_eq!(
        inv.items_count_in(slot_type::INVENTORY, manager::APPRAISAL_CERTIFICATE, -1),
        2
    );
    give(&fx, manager::APPRAISAL_CERTIFICATE, 1);
    assert!(ops::set_for_sale(
        &fx.server,
        &fx.house,
        2_500_000,
        0,
        Some(&fx.owner)
    ));
    assert_eq!(
        inv.items_count_in(slot_type::INVENTORY, manager::APPRAISAL_CERTIFICATE, -1),
        0
    );
}

#[tokio::test]
async fn production_unique_decoration_moves_to_system_then_returns_same_item() {
    let fx = fixture(false);
    production_economy(&fx);
    let id = give(&fx, CHAIR_ITEM, 1);
    let mut p = decoration(&fx);
    p.item_id = id;
    assert_eq!(ops::decorate_house(&fx.server, &fx.owner, &p), Ok(true));
    assert_eq!(
        fx.server.items.item_by_id(id).unwrap().slot_type,
        slot_type::SYSTEM
    );
    assert_eq!(ops::decorate_house(&fx.server, &fx.owner, &p), Ok(false));
    let d = furniture(&fx).pop().unwrap();
    d.data().func_group_id = 70;
    recover(&fx, &fx.owner, &d);
    assert!(d.data().phase.deleted);
    let item = fx.server.items.item_by_id(id).unwrap();
    assert_eq!(item.slot_type, slot_type::INVENTORY);
    assert_eq!(item.owner_id, 1);
    assert_eq!(item.count, 1);
}

#[tokio::test]
async fn production_purchase_pays_once_and_queues_both_receipts() {
    let fx = fixture(false);
    production_economy(&fx);
    assert!(ops::set_for_sale(&fx.server, &fx.house, 100, 0, None));
    let tl = fx.house.data().unit.tl_id;
    assert!(ops::buy_house(&fx.server, tl, 100, &fx.guest));
    assert!(!ops::buy_house(&fx.server, tl, 100, &fx.guest));
    assert_eq!(fx.guest.data().money, 9900);
    let state = fx.server.systems.economy.mail.state();
    let buyer = state
        .all
        .iter()
        .map(|(_, mail)| mail)
        .find(|mail| mail.header.sender_name == ".houseBought")
        .unwrap();
    assert_eq!(buyer.header.receiver_id, 2);
    let seller = state
        .all
        .iter()
        .map(|(_, mail)| mail)
        .find(|mail| mail.header.sender_name == ".houseSold")
        .unwrap();
    assert_eq!(seller.header.receiver_id, 1);
    assert_eq!(seller.body.copper_coins, 100);
}

#[tokio::test]
async fn repeated_listing_is_idempotent_and_updated_listing_keeps_four_signs() {
    let fx = fixture(false);
    assert!(ops::set_for_sale(
        &fx.server,
        &fx.house,
        100,
        0,
        Some(&fx.owner)
    ));
    assert!(ops::set_for_sale(
        &fx.server,
        &fx.house,
        100,
        0,
        Some(&fx.owner)
    ));
    assert_eq!(fx.eco.consumed_certificates.load(Ordering::SeqCst), 1);
    assert!(ops::set_for_sale(&fx.server, &fx.house, 200, 0, None));
    let count = fx
        .server
        .world
        .world(0)
        .unwrap()
        .doodads_by_house_db_id(1)
        .iter()
        .filter(|d| d.data().template_id == manager::FOR_SALE_MARKER_DOODAD_ID)
        .count();
    assert_eq!(count, 4);
}

#[tokio::test]
async fn failed_certificate_refund_preserves_sale() {
    let fx = fixture(false);
    assert!(ops::set_for_sale(&fx.server, &fx.house, 100, 0, None));
    fx.eco.mail_ok.store(false, Ordering::SeqCst);
    assert!(!ops::cancel_for_sale(&fx.server, &fx.house, true));
    assert_eq!(fx.house.data().sell_price, 100);
    fx.eco.mail_ok.store(true, Ordering::SeqCst);
    assert!(ops::cancel_for_sale(&fx.server, &fx.house, true));
    assert_eq!(fx.house.data().sell_price, 0);
}

#[tokio::test]
async fn concurrent_purchases_charge_only_once() {
    let fx = fixture(false);
    assert!(ops::set_for_sale(&fx.server, &fx.house, 100, 0, None));
    let tl = fx.house.data().unit.tl_id;
    let results = std::thread::scope(|scope| {
        let a = scope.spawn(|| ops::buy_house(&fx.server, tl, 100, &fx.guest));
        let b = scope.spawn(|| ops::buy_house(&fx.server, tl, 100, &fx.guest));
        [a.join().unwrap(), b.join().unwrap()]
    });
    assert_eq!(results.into_iter().filter(|v| *v).count(), 1);
    assert_eq!(fx.guest.data().money, 9900);
    assert_eq!(fx.eco.debit_calls.load(Ordering::SeqCst), 1);
    assert_eq!(fx.house.data().unit.owner_id, fx.guest.character_id);
}

#[tokio::test]
async fn failed_sale_mail_commit_does_not_charge_or_transfer_the_house() {
    let fx = fixture(false);
    assert!(ops::set_for_sale(&fx.server, &fx.house, 100, 0, None));
    fx.eco.mail_ok.store(false, Ordering::SeqCst);
    let tl = fx.house.data().unit.tl_id;
    assert!(!ops::buy_house(&fx.server, tl, 100, &fx.guest));
    assert_eq!(fx.guest.data().money, 10_000);
    assert_eq!(fx.house.data().unit.owner_id, 1);
    assert_eq!(fx.house.data().sell_price, 100);
    assert!(fx.eco.mails.lock().unwrap().is_empty());
}

#[tokio::test]
async fn abandoned_house_cannot_be_bought_without_a_seller() {
    let fx = fixture(false);
    assert!(ops::set_for_sale(&fx.server, &fx.house, 100, 0, None));
    fx.house.data().unit.owner_id = 0;
    let tl = fx.house.data().unit.tl_id;
    assert!(!ops::buy_house(&fx.server, tl, 100, &fx.guest));
    assert_eq!(fx.guest.data().money, 10_000);
    assert_eq!(fx.house.data().unit.owner_id, 0);
    assert!(fx.eco.mails.lock().unwrap().is_empty());
}

#[tokio::test]
async fn expired_house_rejects_listing_and_purchase_before_any_payment() {
    let fx = fixture(false);
    fx.house.data().protection_end_date = manager::now();
    assert!(!ops::set_for_sale(
        &fx.server,
        &fx.house,
        100,
        0,
        Some(&fx.owner)
    ));
    assert_eq!(fx.eco.available_certificates.load(Ordering::SeqCst), 100);
    assert_eq!(fx.eco.consumed_certificates.load(Ordering::SeqCst), 0);
    assert_eq!(fx.house.data().sell_price, 0);
    fx.house.data().protection_end_date = manager::now() + 60;
    assert!(ops::set_for_sale(&fx.server, &fx.house, 100, 0, None));
    fx.house.data().protection_end_date = manager::now();
    let tl = fx.house.data().unit.tl_id;
    assert!(!ops::buy_house(&fx.server, tl, 100, &fx.guest));
    assert_eq!(fx.guest.data().money, 10_000);
    assert_eq!(fx.eco.debit_calls.load(Ordering::SeqCst), 0);
    assert_eq!(fx.house.data().sell_price, 100);
    assert_eq!(fx.house.data().unit.owner_id, 1);
    assert!(fx.eco.mails.lock().unwrap().is_empty());
}

#[tokio::test]
async fn failed_decoration_debit_never_publishes_or_attaches_furniture() {
    let mut fx = fixture(false);
    for max_count in [1, 10] {
        fx.eco.item.lock().unwrap().max_count = max_count;
        fx.eco.debit_ok.store(false, Ordering::SeqCst);
        assert_eq!(
            ops::decorate_house(&fx.server, &fx.owner, &decoration(&fx)),
            Ok(false)
        );
        assert!(furniture(&fx).is_empty());
        assert!(fx.house.data().attached_doodads.is_empty());
        assert!(!fx
            .guest_client
            .drain()
            .iter()
            .any(|(op, _)| *op == sc::SCDoodadCreatedPacket));
    }
    assert_eq!(fx.eco.item.lock().unwrap().slot_type, slot_type::INVENTORY);
    assert_eq!(fx.eco.item.lock().unwrap().count, 1);
}

#[tokio::test]
async fn decoration_requires_bag_item_and_cannot_reuse_placed_unique_item() {
    let fx = fixture(false);
    for slot in [
        slot_type::SYSTEM,
        slot_type::BANK,
        slot_type::MAIL,
        slot_type::AUCTION,
        slot_type::TRADE,
    ] {
        fx.eco.item.lock().unwrap().slot_type = slot;
        assert_eq!(
            ops::decorate_house(&fx.server, &fx.owner, &decoration(&fx)),
            Ok(false)
        );
    }
    fx.eco.item.lock().unwrap().slot_type = slot_type::INVENTORY;
    assert_eq!(
        ops::decorate_house(&fx.server, &fx.owner, &decoration(&fx)),
        Ok(true)
    );
    assert_eq!(
        ops::decorate_house(&fx.server, &fx.owner, &decoration(&fx)),
        Ok(false)
    );
    assert_eq!(furniture(&fx).len(), 1);
    assert_eq!(fx.eco.debit_calls.load(Ordering::SeqCst), 1);
}

#[tokio::test]
async fn malformed_decoration_pose_and_wrong_parent_are_rejected_before_debit() {
    let fx = fixture(false);
    for field in 0..5 {
        let mut p = decoration(&fx);
        match field {
            0 => p.x = f32::NAN,
            1 => p.z = f32::INFINITY,
            2 => p.quat[0] = f32::NAN,
            3 => p.quat = [0.0; 4],
            _ => p.parent_obj_id = fx.guest.obj_id,
        }
        assert_eq!(ops::decorate_house(&fx.server, &fx.owner, &p), Ok(false));
    }
    assert_eq!(fx.eco.debit_calls.load(Ordering::SeqCst), 0);
    assert!(furniture(&fx).is_empty());
}

#[tokio::test]
async fn decoration_enforces_overall_and_crafting_group_caps() {
    for (overall, group) in [(1, None), (10, Some(1)), (10, Some(0))] {
        let fx = fixture_with_limits(false, overall, group);
        {
            let mut item = fx.eco.item.lock().unwrap();
            item.max_count = 10;
            item.count = 10;
        }
        let first_expected = group != Some(0);
        assert_eq!(
            ops::decorate_house(&fx.server, &fx.owner, &decoration(&fx)),
            Ok(first_expected)
        );
        assert_eq!(
            ops::decorate_house(&fx.server, &fx.owner, &decoration(&fx)),
            Ok(false)
        );
        assert_eq!(furniture(&fx).len(), usize::from(first_expected));
        assert_eq!(
            fx.eco.item.lock().unwrap().count,
            10 - i32::from(first_expected)
        );
    }
}

#[tokio::test]
async fn shared_furniture_model_uses_the_placed_items_crafting_group_for_limits() {
    let fx = fixture_with_limits(false, 10, Some(1));
    let existing = recoverable(&fx);
    existing.data().item_template_id = CHAIR_ITEM + 1; // same model, different crafting group
    {
        let mut item = fx.eco.item.lock().unwrap();
        item.max_count = 10;
        item.count = 2;
    }
    assert_eq!(
        ops::decorate_house(&fx.server, &fx.owner, &decoration(&fx)),
        Ok(true)
    );
    assert_eq!(
        ops::decorate_house(&fx.server, &fx.owner, &decoration(&fx)),
        Ok(false)
    );
    assert_eq!(furniture(&fx).len(), 2);
    assert_eq!(fx.eco.item.lock().unwrap().count, 1);
}

#[tokio::test]
async fn stackable_furniture_recovery_requires_permission_and_enabled_toggle() {
    let fx = fixture(false);
    let d = recoverable(&fx);
    d.data().phase.to_next_phase = true; // stale phase result must not delete on denial
    recover(&fx, &fx.guest, &d);
    assert!(!d.data().phase.deleted);
    assert_eq!(fx.eco.recovery_calls.load(Ordering::SeqCst), 0);
    fx.house.data().permission = housing_permission::PUBLIC;
    recover(&fx, &fx.guest, &d);
    assert!(!d.data().phase.deleted); // public access alone cannot take furniture
    fx.house.data().allow_recover = true;
    recover(&fx, &fx.guest, &d);
    assert!(d.data().phase.deleted);
    assert_eq!(fx.eco.recovery_calls.load(Ordering::SeqCst), 1);
}

#[tokio::test]
async fn full_bag_keeps_furniture_and_owner_can_recover_with_toggle_disabled() {
    let fx = fixture(false);
    let d = recoverable(&fx);
    fx.eco.debit_ok.store(false, Ordering::SeqCst);
    recover(&fx, &fx.owner, &d);
    assert!(!d.data().phase.deleted);
    assert_eq!(d.data().item_template_id, CHAIR_ITEM);
    fx.eco.debit_ok.store(true, Ordering::SeqCst);
    recover(&fx, &fx.owner, &d);
    assert!(d.data().phase.deleted);
    assert_eq!(d.data().item_template_id, 0);
}

#[tokio::test]
async fn concurrent_stackable_recovery_creates_only_one_item() {
    let fx = fixture(false);
    let d = recoverable(&fx);
    std::thread::scope(|scope| {
        let run = || {
            let ctx = DoodadCtx::new(&fx.server, fx.server.world.world(0).unwrap(), d.clone());
            funcs::items::recover_item(
                &DoodadFuncRecoverItem { id: 1 },
                &ctx,
                Some(&WorldObject::from(&fx.owner)),
                10,
                0,
            );
        };
        let a = scope.spawn(run);
        let b = scope.spawn(run);
        a.join().unwrap();
        b.join().unwrap();
    });
    assert_eq!(fx.eco.recovery_calls.load(Ordering::SeqCst), 1);
    assert_eq!(d.data().item_template_id, 0);
}

#[tokio::test]
async fn crafting_packets_respect_private_house_bench_permissions() {
    let mut fx = fixture(false);
    let (bench, recorder) = bench(&fx);
    request_bench_craft(&mut fx, false, bench.obj_id).await;
    assert_eq!(recorder.casts.load(Ordering::SeqCst), 0);
    assert!(!fx
        .server
        .systems
        .economy
        .craft
        .is_crafting(fx.guest.character_id));
    request_bench_craft(&mut fx, true, bench.obj_id).await;
    assert_eq!(recorder.casts.load(Ordering::SeqCst), 1);
    fx.house.data().permission = housing_permission::PUBLIC;
    request_bench_craft(&mut fx, false, bench.obj_id).await;
    assert_eq!(recorder.casts.load(Ordering::SeqCst), 2);
}

#[tokio::test]
async fn housing_bench_crafting_rejects_remote_and_other_instance_targets() {
    let mut fx = fixture(false);
    let (bench, recorder) = bench(&fx);
    fx.owner.data().unit.transform.set_xyz(200.0, 200.0, 100.0);
    request_bench_craft(&mut fx, true, bench.obj_id).await;
    assert_eq!(recorder.casts.load(Ordering::SeqCst), 0);
    fx.owner.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    bench.data().transform.instance_id = 1;
    request_bench_craft(&mut fx, true, bench.obj_id).await;
    assert_eq!(recorder.casts.load(Ordering::SeqCst), 0);
    // Recipes with no housing station retain their existing portable-craft path.
    request_bench_craft(&mut fx, true, 0).await;
    assert_eq!(recorder.casts.load(Ordering::SeqCst), 1);
}

#[tokio::test]
async fn house_bench_permissions_are_rechecked_before_products_are_created() {
    let mut fx = fixture(false);
    fx.house.data().permission = housing_permission::PUBLIC;
    let (bench, recorder) = bench(&fx);
    request_bench_craft(&mut fx, false, bench.obj_id).await;
    assert_eq!(recorder.casts.load(Ordering::SeqCst), 1);
    fx.house.data().permission = housing_permission::PRIVATE;
    crate::systems::economy::craft::end_craft(&fx.server, &fx.guest);
    let inv = crate::systems::economy::inventory::Inv::of(&fx.guest);
    assert_eq!(inv.items_count_in(slot_type::INVENTORY, CHAIR_ITEM, -1), 0);
    assert!(!fx
        .server
        .systems
        .economy
        .craft
        .is_crafting(fx.guest.character_id));
}
