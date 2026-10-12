//! Cash shop: byte-exact packets, menu / page lists and the buy task with an in-memory account
//! store and a recording mail hook (no MySQL).

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use aaemu_commons::{Marshal, PacketStream};
use chrono::NaiveDate;
use futures::future::BoxFuture;

use crate::db::AccountDetails;
use crate::models::item_task::{task_type, ItemTask};
use crate::net::packet::encode;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::economy::cash_shop::*;
use crate::systems::economy::error_message;
use crate::systems::economy::tests::{run, task_body, world, World, SINGLE, STACK};

fn body_of<P: Marshal>(p: &P) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write(&mut s);
    s.into_bytes()
}

fn check(f: Vec<u8>, op: u16, body: &[u8]) {
    assert_eq!(u16::from_le_bytes([f[4], f[5]]), op);
    assert_eq!(&f[6..], body);
}

#[derive(Default)]
struct MemDb {
    accounts: Mutex<HashMap<u32, (i32, i32)>>,
    sales: Mutex<Vec<AuditIcsSale>>,
    stock: Mutex<Vec<(u32, i32)>>,
    fail_debits: Mutex<bool>,
    fail_stock_updates: Mutex<bool>,
}

impl CashShopDb for MemDb {
    fn account_details<'a>(
        &'a self,
        _server: &'a GameServer,
        account_id: u32,
    ) -> BoxFuture<'a, AccountDetails> {
        let (credits, loyalty) = self
            .accounts
            .lock()
            .unwrap()
            .get(&account_id)
            .copied()
            .unwrap_or_default();
        Box::pin(async move {
            AccountDetails {
                account_id: account_id as i32,
                credits,
                loyalty,
                ..AccountDetails::default()
            }
        })
    }

    fn add_credits<'a>(
        &'a self,
        _server: &'a GameServer,
        account_id: u32,
        amount: i32,
    ) -> BoxFuture<'a, bool> {
        if amount < 0 && *self.fail_debits.lock().unwrap() {
            return Box::pin(async { false });
        }
        self.accounts
            .lock()
            .unwrap()
            .entry(account_id)
            .or_default()
            .0 += amount;
        Box::pin(async { true })
    }

    fn add_loyalty<'a>(
        &'a self,
        _server: &'a GameServer,
        account_id: u32,
        amount: i32,
    ) -> BoxFuture<'a, bool> {
        if amount < 0 && *self.fail_debits.lock().unwrap() {
            return Box::pin(async { false });
        }
        self.accounts
            .lock()
            .unwrap()
            .entry(account_id)
            .or_default()
            .1 += amount;
        Box::pin(async { true })
    }

    fn sales_for_shop_item<'a>(
        &'a self,
        _server: &'a GameServer,
        account_id: u32,
        character_id: u32,
        shop_item_id: u32,
    ) -> BoxFuture<'a, Vec<AuditIcsSale>> {
        let v = self
            .sales
            .lock()
            .unwrap()
            .iter()
            .filter(|s| {
                s.shop_item_id == shop_item_id
                    && if character_id > 0 {
                        s.buyer_char == character_id
                    } else {
                        s.buyer_account == account_id
                    }
            })
            .cloned()
            .collect();
        Box::pin(async move { v })
    }

    fn log_sale<'a>(&'a self, _server: &'a GameServer, sale: AuditIcsSale) -> BoxFuture<'a, bool> {
        self.sales.lock().unwrap().push(sale);
        Box::pin(async { true })
    }

    fn update_remaining_stock<'a>(
        &'a self,
        _server: &'a GameServer,
        shop_item_id: u32,
        remaining: i32,
    ) -> BoxFuture<'a, bool> {
        if *self.fail_stock_updates.lock().unwrap() {
            return Box::pin(async { false });
        }
        self.stock.lock().unwrap().push((shop_item_id, remaining));
        Box::pin(async { true })
    }
}

#[derive(Default)]
struct MailLog(Mutex<Vec<CommercialMailRequest>>);

impl CashShopMailHooks for MailLog {
    fn send_commercial_mail(&self, _server: &GameServer, mail: CommercialMailRequest) -> bool {
        self.0.lock().unwrap().push(mail);
        true
    }
}

fn sku(sku: u32, shop_id: u32, item_id: u32, currency: u8, price: u32) -> IcsSku {
    IcsSku {
        sku,
        shop_id,
        item_id,
        item_count: 1,
        currency,
        price,
        ..IcsSku::default()
    }
}

fn shop_item(shop_id: u32, name: &str) -> IcsItem {
    IcsItem {
        shop_id,
        display_item_id: STACK,
        name: name.into(),
        remaining: -1,
        ..IcsItem::default()
    }
}

/// Shop: 10 entries on tab 2-1 (shop ids 100..110, one SKU each, STACK for 10 credits),
/// shop 200 on tab 1-1 with two SKUs (credits / coins) and a stock of 3, shop 300 limited to
/// 2 per account (loyalty).
fn shop_state() -> CashShopState {
    let mut st = CashShopState {
        enabled: true,
        ..CashShopState::default()
    };
    for i in 0..10u32 {
        let id = 100 + i;
        st.skus
            .push(sku(1000 + i, id, STACK, currency::CREDITS, 10));
        st.shop_items.insert(id, shop_item(id, ""));
        st.menu_items.push(IcsMenu {
            id: i64::from(i),
            main_tab: 2,
            sub_tab: 1,
            tab_pos: i as i32,
            shop_id: id,
        });
    }
    let mut a = sku(2000, 200, STACK, currency::CREDITS, 50);
    a.discount_price = 40;
    a.bonus_item_id = SINGLE;
    a.bonus_item_count = 1;
    st.skus.push(a);
    let mut b = sku(2001, 200, SINGLE, currency::COINS, 30);
    b.item_count = 2;
    st.skus.push(b);
    st.shop_items.insert(
        200,
        IcsItem {
            remaining: 3,
            ..shop_item(200, "Featured")
        },
    );
    st.menu_items.push(IcsMenu {
        id: 50,
        main_tab: 1,
        sub_tab: 1,
        tab_pos: 0,
        shop_id: 200,
    });
    st.skus.push(sku(3000, 300, STACK, currency::LOYALTY, 5));
    st.shop_items.insert(
        300,
        IcsItem {
            limited_type: limit_type::ACCOUNT,
            limited_stock_max: 2,
            ..shop_item(300, "Limited")
        },
    );
    let names = CashShopData {
        item_names: [(STACK, "Stack".to_string())].into_iter().collect(),
    };
    st.attach_skus(&names);
    st
}

struct Shop {
    w: World,
    db: Arc<MemDb>,
    mail: Arc<MailLog>,
}

fn shop() -> Shop {
    let w = world();
    let cs = &w.server.systems.economy.cash_shop;
    cs.set_state(shop_state());
    let db = Arc::new(MemDb::default());
    let mail = Arc::new(MailLog::default());
    cs.db.install(db.clone());
    cs.mail.install(mail.clone());
    Shop { w, db, mail }
}

fn result_body(success: bool, name: &str, aa: i32) -> Vec<u8> {
    body_of(&SCICSBuyResultPacket {
        success,
        buy_mode: 1,
        receiver_name: name.into(),
        charge_aa_point: aa,
    })
}

fn error_body(err: i16) -> Vec<u8> {
    let mut s = PacketStream::new();
    s.write_i16(err)
        .write_i16(err)
        .write_u32(0)
        .write_bool(true);
    s.into_bytes()
}

#[test]
fn packets_layout() {
    // Menu list: enabled + 6 x (main tab, 7 sub tab flags)
    let menu = vec![
        IcsMenu {
            main_tab: 1,
            sub_tab: 1,
            ..IcsMenu::default()
        },
        IcsMenu {
            main_tab: 2,
            sub_tab: 3,
            ..IcsMenu::default()
        },
    ];
    let p = SCICSMenuListPacket::new(true, &menu).unwrap();
    let mut want = vec![1u8];
    for m in 1..=6u8 {
        want.push(m);
        for s in 1..=7u8 {
            want.push(if (m, s) == (1, 1) || (m, s) == (2, 3) {
                s
            } else {
                0
            });
        }
    }
    check(encode(&p), sc::SCICSMenuListPacket, &want);
    assert!(SCICSMenuListPacket::new(
        true,
        &[IcsMenu {
            main_tab: 7,
            sub_tab: 1,
            ..IcsMenu::default()
        }]
    )
    .is_none());

    // Good list with a first SKU
    let date = NaiveDate::from_ymd_opt(2024, 1, 2)
        .unwrap()
        .and_hms_opt(3, 4, 5)
        .unwrap();
    let mut item = IcsItem {
        shop_id: 7,
        display_item_id: 8,
        name: "Ab".into(),
        limited_type: 1,
        limited_stock_max: 9,
        level_min: 10,
        level_max: 11,
        buy_restrict_type: 2,
        buy_restrict_id: 12,
        is_sale: true,
        is_hidden: false,
        sale_start: Some(date),
        sale_end: None,
        remaining: -1,
        shop_buttons: 3,
        skus: Vec::new(),
    };
    let mut s0 = sku(13, 7, 14, currency::COINS, 15);
    s0.bonus_item_id = 16;
    s0.bonus_item_count = 17;
    item.skus.push(s0.clone());
    let p = SCICSGoodListPacket {
        page_end: true,
        total_page: 2,
        main_tab: 1,
        sub_tab: 4,
        item: item.clone(),
    };
    let mut w = PacketStream::new();
    w.write_bool(true)
        .write_u16(2)
        .write_u32(7)
        .write_str("Ab")
        .write_u8(1)
        .write_u8(4)
        .write_u8(10)
        .write_u8(11)
        .write_u32(8)
        .write_bool(true)
        .write_bool(false)
        .write_u8(1)
        .write_u16(9)
        .write_u8(2)
        .write_u32(12)
        .write_i64(date.and_utc().timestamp())
        .write_i64(0)
        .write_u8(3)
        .write_u32(15)
        .write_i32(-1)
        .write_u32(16)
        .write_u32(17)
        .write_u8(3);
    check(encode(&p), sc::SCICSGoodListPacket, w.as_bytes());
    // ... and without one
    item.skus.clear();
    let p = SCICSGoodListPacket {
        page_end: false,
        total_page: 1,
        main_tab: 1,
        sub_tab: 1,
        item,
    };
    let b = body_of(&p);
    assert_eq!(&b[b.len() - 18..], &[0u8; 18][..]);

    // Good detail
    let mut d = s0;
    d.select_type = 1;
    d.is_default = true;
    d.event_type = 4;
    d.event_end_date = Some(date);
    d.discount_price = 5;
    let mut w = PacketStream::new();
    w.write_bool(false)
        .write_u32(7)
        .write_u32(13)
        .write_u32(14)
        .write_u32(1)
        .write_u8(1)
        .write_bool(true)
        .write_u8(4)
        .write_i64(date.and_utc().timestamp())
        .write_u8(3)
        .write_u32(15)
        .write_u32(5)
        .write_u32(16)
        .write_u32(17);
    check(
        encode(&SCICSGoodDetailPacket {
            page_end: false,
            sku: d,
        }),
        sc::SCICSGoodDetailPacket,
        w.as_bytes(),
    );

    check(encode(&SCICSCheckTimePacket), sc::SCICSCheckTimePacket, &[]);
    check(
        encode(&SCICSBuyResultPacket {
            success: true,
            buy_mode: 1,
            receiver_name: "Bob".into(),
            charge_aa_point: 2,
        }),
        sc::SCICSBuyResultPacket,
        &[1, 1, 3, 0, b'B', b'o', b'b', 2, 0, 0, 0],
    );
    check(
        encode(&SCICSSyncGoodPacket {
            cash_shop_id: 1,
            remain_count: 2,
        }),
        sc::SCICSSyncGoodPacket,
        &[1, 0, 0, 0, 2, 0, 0, 0],
    );
    check(
        encode(&SCICSCashPointPacket { point: 300 }),
        sc::SCICSCashPointPacket,
        &[0x2c, 1, 0, 0],
    );
    check(
        encode(&SCICSExchangeRatioPacket { exchange_ratio: 4 }),
        sc::SCICSExchangeRatioPacket,
        &[4, 0, 0, 0],
    );
    check(
        encode(&SCPremiumPointChangedPacket {
            obj_id: 0x010203,
            point: 5,
        }),
        sc::SCPremiumPointChangedPacket,
        &[3, 2, 1, 5, 0, 0, 0],
    );
    check(
        encode(&SCBmPointPacket { bm_point: 6 }),
        sc::SCBmPointPacket,
        &[6, 0, 0, 0, 0, 0, 0, 0],
    );
    check(
        encode(&SCAccountWarnedPacket {
            source: 2,
            msg: "Hi".into(),
        }),
        sc::SCAccountWarnedPacket,
        &[2, 2, 0, b'H', b'i'],
    );
    // Premium list with the fixed C# entry
    let det = premium_detail();
    let mut w = PacketStream::new();
    w.write_bool(true)
        .write_u8(1)
        .write_i32(8000001)
        .write_str("Премиум-подпиcка (30 дней)")
        .write_u16(1)
        .write_u8(0)
        .write_u8(0)
        .write_i32(720)
        .write_u8(0)
        .write_i32(300)
        .write_u32(0)
        .write_i32(0)
        .write_i32(0);
    check(
        encode(&SCPremiumServiceListPacket {
            is_end: true,
            size: 1,
            detail: det,
            exchange_ratio: 0,
        }),
        sc::SCPremiumServiceListPacket,
        w.as_bytes(),
    );
}

#[test]
fn buy_good_decodes() {
    let mut s = PacketStream::new();
    s.write_u8(2)
        .write_u32(100)
        .write_u8(2)
        .write_u8(1)
        .write_u8(0)
        .write_u32(200)
        .write_u8(1)
        .write_u8(1)
        .write_u8(1)
        .write_str("Bob");
    let p = CSICSBuyGoodPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
    assert_eq!(p.receiver_name, "Bob");
    assert_eq!(
        p.buys,
        vec![
            IcsBuyEntry {
                cash_shop_id: 100,
                main_tab: 2,
                sub_tab: 1,
                detail_index: 0
            },
            IcsBuyEntry {
                cash_shop_id: 200,
                main_tab: 1,
                sub_tab: 1,
                detail_index: 1
            }
        ]
    );
}

#[test]
fn attach_skus_names_and_order() {
    let st = shop_state();
    // Names come from the first SKU's item when blank
    assert_eq!(st.shop_items[&100].name, "Stack");
    assert_eq!(st.shop_items[&200].name, "Featured");
    let skus: Vec<u32> = st.shop_items[&200].skus.iter().map(|s| s.sku).collect();
    assert_eq!(skus, vec![2000, 2001]);
}

#[tokio::test]
async fn menu_and_pages() {
    let mut s = shop();
    let w = &mut s.w;
    run::<CSICSMenuListPacket>(w, 0, &[]);
    let f = w.a.drain();
    assert_eq!(f.len(), 1);
    let st = shop_state();
    assert_eq!(
        f[0].1,
        body_of(&SCICSMenuListPacket::new(true, &st.menu_items).unwrap())
    );

    // Page 1 of tab 2-1: 8 list packets then 8 details, the last of each flagged
    let mut b = PacketStream::new();
    b.write_u8(2).write_u8(1).write_u16(1);
    run::<CSICSGoodsListPacket>(w, 0, b.as_bytes());
    let f = w.a.drain();
    assert_eq!(f.len(), 16);
    assert!(f[..8].iter().all(|x| x.0 == sc::SCICSGoodListPacket));
    assert!(f[8..].iter().all(|x| x.0 == sc::SCICSGoodDetailPacket));
    assert_eq!(
        f[0].1,
        body_of(&SCICSGoodListPacket {
            page_end: false,
            total_page: 2,
            main_tab: 2,
            sub_tab: 1,
            item: st.shop_items[&100].clone(),
        })
    );
    assert_eq!(f[7].1[0], 1);
    assert_eq!(f[8].1[0], 0);
    assert_eq!(f[15].1[0], 1);
    // Page 2: the remaining 2; page 0 behaves like page 1
    let mut b = PacketStream::new();
    b.write_u8(2).write_u8(1).write_u16(2);
    run::<CSICSGoodsListPacket>(w, 0, b.as_bytes());
    assert_eq!(w.a.drain().len(), 4);
    let mut b = PacketStream::new();
    b.write_u8(2).write_u8(1).write_u16(0);
    run::<CSICSGoodsListPacket>(w, 0, b.as_bytes());
    assert_eq!(w.a.drain().len(), 16);
    // Featured tab: 1 item with 2 SKUs
    let mut b = PacketStream::new();
    b.write_u8(1).write_u8(1).write_u16(1);
    run::<CSICSGoodsListPacket>(w, 0, b.as_bytes());
    let f = w.a.drain();
    assert_eq!(
        f.iter().map(|x| x.0).collect::<Vec<_>>(),
        vec![
            sc::SCICSGoodListPacket,
            sc::SCICSGoodDetailPacket,
            sc::SCICSGoodDetailPacket
        ]
    );
    assert_eq!((f[1].1[0], f[2].1[0]), (0, 1));

    // Disabling the shop tells every character
    disable_shop(&w.server);
    assert_eq!(w.a.drain_ops(), vec![sc::SCICSCheckTimePacket]);
    assert_eq!(w.b.drain_ops(), vec![sc::SCICSCheckTimePacket]);
    run::<CSICSMenuListPacket>(w, 0, &[]);
    assert_eq!(w.a.drain()[0].1[0], 0);
}

fn cart(st: &CashShopState, picks: &[(u32, usize)]) -> Vec<IcsSku> {
    picks
        .iter()
        .map(|(id, i)| st.shop_items[id].skus[*i].clone())
        .collect()
}

#[tokio::test]
async fn checkout_rejects_unpaid_aa_points_and_invalid_cart_values_before_mutation() {
    let base = cart(&shop_state(), &[(200, 0)])[0].clone();
    let mut aa = base.clone();
    aa.currency = currency::AA_POINTS;
    let mut price = base.clone();
    price.price = u32::MAX;
    price.discount_price = 0;
    let mut total = base.clone();
    total.price = i32::MAX as u32;
    total.discount_price = 0;
    let mut zero = base.clone();
    zero.item_count = 0;
    let mut count = base.clone();
    count.item_count = u32::MAX;
    let mut bonus = base.clone();
    bonus.bonus_item_count = u32::MAX;
    let mut missing = base.clone();
    missing.item_id = 999_999;
    let mut missing_bonus = base;
    missing_bonus.bonus_item_id = 999_999;
    for (cart, error) in [
        (vec![aa], error_message::INGAME_SHOP_BUY_FAIL_AA_POINT),
        (vec![price], error_message::INGAME_SHOP_BUY_FAIL),
        (
            vec![total.clone(), total],
            error_message::INGAME_SHOP_BUY_FAIL,
        ),
        (vec![zero], error_message::INGAME_SHOP_BUY_FAIL),
        (vec![count], error_message::INGAME_SHOP_BUY_FAIL),
        (vec![bonus], error_message::INGAME_SHOP_BUY_FAIL),
        (vec![missing], error_message::INGAME_SHOP_BUY_FAIL),
        (vec![missing_bonus], error_message::INGAME_SHOP_BUY_FAIL),
    ] {
        let mut s = shop();
        s.db.accounts.lock().unwrap().insert(1001, (100, 100));
        let buyer = s.w.pa.clone();
        buyer.data().money2 = 123_456; // Bank gold must not be charged as AA points.
        execute_buy(&s.w.server, 1, &buyer, &buyer, &cart).await;
        assert_eq!(s.db.accounts.lock().unwrap()[&1001], (100, 100));
        assert_eq!(buyer.data().money2, 123_456);
        assert_eq!(
            s.w.server.systems.economy.cash_shop.state().shop_items[&200].remaining,
            3
        );
        assert!(s.db.stock.lock().unwrap().is_empty());
        assert!(s.db.sales.lock().unwrap().is_empty());
        assert!(s.mail.0.lock().unwrap().is_empty());
        assert_eq!(
            s.w.a.drain(),
            vec![
                (sc::SCErrorMsgPacket, error_body(error)),
                (sc::SCICSBuyResultPacket, result_body(false, "Alice", 0)),
            ]
        );
    }
}

#[tokio::test]
async fn checkout_charges_displayed_discount_for_coins_and_loyalty() {
    for (currency, balance) in [(currency::COINS, 10), (currency::LOYALTY, 10)] {
        let mut s = shop();
        let buyer = s.w.pa.clone();
        buyer.data().money = balance;
        s.db.accounts
            .lock()
            .unwrap()
            .insert(1001, (0, balance as i32));
        let mut sku = cart(&shop_state(), &[(200, 0)])[0].clone();
        sku.currency = currency;
        sku.discount_price = 10;
        execute_buy(&s.w.server, 1, &buyer, &buyer, &[sku]).await;
        if currency == currency::COINS {
            assert_eq!(buyer.data().money, 0);
        } else {
            assert_eq!(s.db.accounts.lock().unwrap()[&1001].1, 0);
        }
        assert_eq!(s.db.sales.lock().unwrap()[0].sale_cost, 10);
        assert_eq!(s.mail.0.lock().unwrap().len(), 1);
        assert_eq!(
            s.w.a.drain().last().unwrap().1,
            result_body(true, "Alice", 0)
        );
    }
}

#[tokio::test]
async fn checkout_failed_payment_or_stock_update_never_fulfills_or_records_sale() {
    for stock_failure in [false, true] {
        let mut s = shop();
        let buyer = s.w.pa.clone();
        s.db.accounts.lock().unwrap().insert(1001, (100, 100));
        *s.db.fail_debits.lock().unwrap() = !stock_failure;
        *s.db.fail_stock_updates.lock().unwrap() = stock_failure;
        execute_buy(
            &s.w.server,
            1,
            &buyer,
            &buyer,
            &cart(&shop_state(), &[(200, 0)]),
        )
        .await;
        assert_eq!(s.db.accounts.lock().unwrap()[&1001], (100, 100));
        assert_eq!(
            s.w.server.systems.economy.cash_shop.state().shop_items[&200].remaining,
            3
        );
        assert!(s.db.sales.lock().unwrap().is_empty());
        assert!(s.mail.0.lock().unwrap().is_empty());
        assert_eq!(
            s.w.a.drain(),
            vec![
                (
                    sc::SCErrorMsgPacket,
                    error_body(error_message::INGAME_SHOP_BUY_FAIL)
                ),
                (sc::SCICSBuyResultPacket, result_body(false, "Alice", 0)),
            ]
        );
    }
}

#[tokio::test]
async fn checkout_checks_total_item_quantity_for_stock_and_purchase_limits() {
    for limited in [false, true] {
        let mut s = shop();
        let buyer = s.w.pa.clone();
        s.db.accounts.lock().unwrap().insert(1001, (100, 100));
        let mut entry = cart(&shop_state(), &[(200, 0)])[0].clone();
        entry.item_count = 2;
        if limited {
            let mut st = s.w.server.systems.economy.cash_shop.state();
            let item = st.shop_items.get_mut(&200).unwrap();
            item.remaining = -1;
            item.limited_type = limit_type::ACCOUNT;
            item.limited_stock_max = 3;
        }
        execute_buy(&s.w.server, 1, &buyer, &buyer, &[entry.clone(), entry]).await;
        assert_eq!(s.db.accounts.lock().unwrap()[&1001], (100, 100));
        assert!(s.db.sales.lock().unwrap().is_empty());
        assert!(s.mail.0.lock().unwrap().is_empty());
        assert_eq!(
            s.w.a.drain(),
            vec![
                (
                    sc::SCErrorMsgPacket,
                    error_body(error_message::INGAME_SHOP_SOLD_OUT)
                ),
                (sc::SCICSBuyResultPacket, result_body(false, "Alice", 0)),
            ]
        );
    }
}

#[tokio::test]
#[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; uses connection-local temporary accounts"]
async fn private_mysql_cash_shop_debits_are_guarded_and_do_not_overdraw() {
    use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
    use std::str::FromStr;
    let Ok(url) = std::env::var("AAEMU_TEST_MYSQL") else {
        return;
    };
    let options = MySqlConnectOptions::from_str(&url).expect("valid private MySQL URL");
    assert_eq!(
        options.get_port(),
        3317,
        "Only private MySQL3317 is allowed"
    );
    let pool = MySqlPoolOptions::new()
        .max_connections(1)
        .connect_with(options)
        .await
        .unwrap();
    sqlx::query("CREATE TEMPORARY TABLE accounts (account_id INT PRIMARY KEY, credits INT NOT NULL DEFAULT 0, loyalty INT NOT NULL DEFAULT 0)").execute(&pool).await.unwrap();
    sqlx::query("INSERT INTO accounts VALUES (1,100,30)")
        .execute(&pool)
        .await
        .unwrap();
    let (a, b) = tokio::join!(
        add_credits_sql(&pool, 1, -80),
        add_credits_sql(&pool, 1, -80)
    );
    assert_ne!(a, b);
    assert!(!add_credits_sql(&pool, 2, -1).await);
    assert!(!add_loyalty_sql(&pool, 1, -31).await);
    assert!(add_loyalty_sql(&pool, 1, -30).await);
    let values: (i32, i32) =
        sqlx::query_as("SELECT credits,loyalty FROM accounts WHERE account_id=1")
            .fetch_one(&pool)
            .await
            .unwrap();
    assert_eq!(values, (20, 0));
    sqlx::query("DROP TEMPORARY TABLE accounts")
        .execute(&pool)
        .await
        .unwrap();
    pool.close().await;
}

#[tokio::test]
async fn buy_with_credits_as_gift() {
    let mut s = shop();
    s.db.accounts.lock().unwrap().insert(1001, (100, 7));
    let st = shop_state();
    let (pa, pb) = (s.w.pa.clone(), s.w.pb.clone());
    // Featured SKU 2000: discounted 40 credits + bonus item, stock 3
    execute_buy(&s.w.server, 1, &pa, &pb, &cart(&st, &[(200, 0)])).await;
    assert_eq!(s.db.accounts.lock().unwrap()[&1001], (60, 7));
    assert_eq!(*s.db.stock.lock().unwrap(), vec![(200, 2)]);
    assert_eq!(
        s.w.server.systems.economy.cash_shop.state().shop_items[&200].remaining,
        2
    );
    let mails = s.mail.0.lock().unwrap().clone();
    assert_eq!(mails.len(), 1);
    let m = &mails[0];
    assert_eq!(
        (
            m.receiver_id,
            m.receiver_name.as_str(),
            m.sender_name.as_str()
        ),
        (2, "Bob", "Alice")
    );
    assert!(m.is_gift && !m.is_refund);
    assert_eq!(m.purchased_item_title, "Featured");
    assert_eq!(
        m.items
            .iter()
            .map(|i| (i.template_id, i.count))
            .collect::<Vec<_>>(),
        vec![(STACK, 1), (SINGLE, 1)]
    );
    let sales = s.db.sales.lock().unwrap().clone();
    assert_eq!(sales.len(), 1);
    assert_eq!(
        (
            sales[0].buyer_account,
            sales[0].buyer_char,
            sales[0].target_account,
            sales[0].target_char,
            sales[0].shop_item_id,
            sales[0].sku,
            sales[0].sale_cost,
            sales[0].sale_currency
        ),
        (1001, 1, 1002, 2, 200, 2000, 40, currency::CREDITS)
    );
    assert_eq!(pa.data().bm_point, 7);
    let f = s.w.a.drain();
    assert_eq!(
        f,
        vec![
            (sc::SCICSCashPointPacket, 60i32.to_le_bytes().to_vec()),
            (sc::SCBmPointPacket, 7i64.to_le_bytes().to_vec()),
            (sc::SCICSBuyResultPacket, result_body(true, "Bob", 0)),
        ]
    );
    assert!(s.w.b.drain().is_empty());

    // Not enough credits
    execute_buy(&s.w.server, 1, &pa, &pa, &cart(&st, &[(200, 0), (200, 0)])).await;
    assert_eq!(
        s.w.a.drain(),
        vec![
            (
                sc::SCErrorMsgPacket,
                error_body(error_message::INGAME_SHOP_NOT_ENOUGH_AA_CASH)
            ),
            (sc::SCICSBuyResultPacket, result_body(false, "Alice", 0)),
        ]
    );
}

#[tokio::test]
async fn buy_with_coins_stock_and_level() {
    let mut s = shop();
    let st = shop_state();
    let pa = s.w.pa.clone();
    pa.data().money = 100;
    // SKU 2001: 2 x SINGLE for 30 coins
    execute_buy(&s.w.server, 1, &pa, &pa, &cart(&st, &[(200, 1)])).await;
    assert_eq!(pa.data().money, 70);
    let f = s.w.a.drain();
    assert_eq!(f[0].0, sc::SCItemTaskSuccessPacket);
    assert_eq!(
        f[0].1,
        task_body(task_type::STORE_BUY, vec![ItemTask::MoneyChange(-30)])
    );
    assert_eq!(f.last().unwrap().1, result_body(true, "Alice", 0));
    let m = s.mail.0.lock().unwrap()[0].clone();
    assert!(!m.is_gift);
    assert_eq!(m.items[0].count, 2);
    // Stock is 3 - 2 = 1: a cart with two entries of shop 200 is sold out
    pa.data().money = 1000;
    execute_buy(&s.w.server, 1, &pa, &pa, &cart(&st, &[(200, 1), (200, 1)])).await;
    assert_eq!(
        s.w.a.drain()[0].1,
        error_body(error_message::INGAME_SHOP_SOLD_OUT)
    );
    // One entry passes validation but needs 2 of the remaining 1: sold out, nothing sold
    execute_buy(&s.w.server, 1, &pa, &pa, &cart(&st, &[(200, 1)])).await;
    assert_eq!(
        s.w.a.drain(),
        vec![
            (
                sc::SCErrorMsgPacket,
                error_body(error_message::INGAME_SHOP_SOLD_OUT)
            ),
            (sc::SCICSBuyResultPacket, result_body(false, "Alice", 0)),
        ]
    );
    assert_eq!(pa.data().money, 1000);
    // Level restriction
    s.w.server
        .systems
        .economy
        .cash_shop
        .state()
        .shop_items
        .get_mut(&100)
        .unwrap()
        .level_min = 50;
    s.db.accounts.lock().unwrap().insert(1001, (100, 0));
    execute_buy(&s.w.server, 1, &pa, &pa, &cart(&st, &[(100, 0)])).await;
    assert_eq!(
        s.w.a.drain()[0].1,
        error_body(error_message::INGAME_SHOP_BUY_LOW_LEVEL)
    );
    // Not enough coins
    pa.data().money = 1;
    execute_buy(&s.w.server, 1, &pa, &pa, &cart(&st, &[(200, 1)])).await;
    assert_eq!(
        s.w.a.drain()[0].1,
        error_body(error_message::NOT_ENOUGH_COIN)
    );
}

#[tokio::test]
async fn limited_per_account_with_loyalty() {
    let mut s = shop();
    let st = shop_state();
    let pa = s.w.pa.clone();
    s.db.accounts.lock().unwrap().insert(1001, (0, 100));
    for _ in 0..2 {
        execute_buy(&s.w.server, 1, &pa, &pa, &cart(&st, &[(300, 0)])).await;
        assert_eq!(
            s.w.a.drain().last().unwrap().1,
            result_body(true, "Alice", 0)
        );
    }
    assert_eq!(s.db.accounts.lock().unwrap()[&1001], (0, 90));
    assert_eq!(pa.data().bm_point, 90);
    // Third one goes over the limit of 2
    execute_buy(&s.w.server, 1, &pa, &pa, &cart(&st, &[(300, 0)])).await;
    assert_eq!(
        s.w.a.drain(),
        vec![
            (
                sc::SCErrorMsgPacket,
                error_body(error_message::INGAME_SHOP_SOLD_OUT)
            ),
            (sc::SCICSBuyResultPacket, result_body(false, "Alice", 0)),
        ]
    );
    assert_eq!(s.mail.0.lock().unwrap().len(), 2);
}

#[tokio::test]
async fn buy_packet_handler() {
    let mut s = shop();
    s.db.accounts.lock().unwrap().insert(1001, (100, 0));
    // Unknown receiver
    let mut b = PacketStream::new();
    b.write_u8(1)
        .write_u32(100)
        .write_u8(2)
        .write_u8(1)
        .write_u8(0)
        .write_str("Nobody");
    run::<CSICSBuyGoodPacket>(&mut s.w, 0, b.as_bytes());
    assert_eq!(
        s.w.a.drain(),
        vec![
            (
                sc::SCErrorMsgPacket,
                error_body(error_message::INGAME_SHOP_FIND_CHARACTER_NAME_FAIL)
            ),
            (sc::SCICSBuyResultPacket, result_body(false, "Nobody", 0)),
        ]
    );
    // Only invalid entries: empty cart
    let mut b = PacketStream::new();
    b.write_u8(2)
        .write_u32(999)
        .write_u8(2)
        .write_u8(1)
        .write_u8(0)
        .write_u32(100)
        .write_u8(2)
        .write_u8(1)
        .write_u8(5)
        .write_str("");
    run::<CSICSBuyGoodPacket>(&mut s.w, 0, b.as_bytes());
    assert_eq!(
        s.w.a.drain(),
        vec![
            (
                sc::SCErrorMsgPacket,
                error_body(error_message::BUY_CART_EMPTY)
            ),
            (sc::SCICSBuyResultPacket, result_body(false, "", 0)),
        ]
    );
    // A real gift to bob (case-insensitive name), delivered after the 1 s task delay
    let mut b = PacketStream::new();
    b.write_u8(1)
        .write_u32(100)
        .write_u8(2)
        .write_u8(1)
        .write_u8(0)
        .write_str("bob");
    run::<CSICSBuyGoodPacket>(&mut s.w, 0, b.as_bytes());
    assert!(s.w.a.drain().is_empty());
    tokio::time::sleep(std::time::Duration::from_millis(1300)).await;
    assert_eq!(s.w.a.drain().last().unwrap().1, result_body(true, "Bob", 0));
    assert_eq!(s.db.accounts.lock().unwrap()[&1001].0, 90);
    assert_eq!(s.mail.0.lock().unwrap()[0].receiver_id, 2);

    // Money request
    run::<CSICSMoneyRequestPacket>(&mut s.w, 0, &[]);
    assert_eq!(
        s.w.a.drain(),
        vec![(sc::SCICSCashPointPacket, 90i32.to_le_bytes().to_vec())]
    );
    // Premium service list / message
    run::<CSPremiumServiceListPacket>(&mut s.w, 0, &[]);
    assert_eq!(
        s.w.a.drain(),
        vec![(
            sc::SCPremiumServiceListPacket,
            body_of(&SCPremiumServiceListPacket {
                is_end: true,
                size: 1,
                detail: premium_detail(),
                exchange_ratio: 0
            })
        )]
    );
    run::<CSPremiumServiceMsgPacket>(&mut s.w, 0, &3i32.to_le_bytes());
    assert_eq!(
        s.w.a.drain(),
        vec![(
            sc::SCAccountWarnedPacket,
            body_of(&SCAccountWarnedPacket {
                source: 2,
                msg: "Premium ...".into()
            })
        )]
    );
    run::<CSPremiumServiceBuyPacket>(&mut s.w, 0, &3i32.to_le_bytes());
    assert!(s.w.a.drain().is_empty());
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_compact_db_cash_shop_item_names() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let app = std::path::Path::new(&path)
        .parent()
        .unwrap()
        .parent()
        .unwrap();
    let files = crate::data::client_files::ClientFileManager::from_sources(&[], app);
    let d = CashShopData::load(&crate::systems::DataLoadCtx {
        conn: &conn,
        app_path: app,
        files: &files,
        language: "en_us",
    })
    .unwrap();
    let named = d.item_names.values().filter(|n| !n.is_empty()).count();
    eprintln!(
        "cash shop item names: {} rows, {named} non-empty, 8000001 = {:?}",
        d.item_names.len(),
        d.item_name(8000001)
    );
    assert!(named > 1000);
}
