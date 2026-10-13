//! A real SQL interleaving of the same prepared financial snapshots as world saves.

use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
use sqlx::MySqlPool;
use std::str::FromStr;

use crate::managers::save::{
    capture_financial_snapshot, commit_financial_snapshot, write_financial_snapshot,
};
use crate::systems::economy::mail;
use crate::systems::housing::manager;

use super::tax_tests::{certificates, fixture, give, place};

type FinanceState = (i64, i32, Option<i32>, i32, i64, i64, i64, i64);

async fn assert_finance(
    pool: &MySqlPool,
    house_id: u32,
    certificate_id: u64,
    bill_id: i64,
    expected: FinanceState,
) {
    let houses = crate::systems::housing::db::load_rows(pool).await.unwrap();
    let protection = houses
        .into_iter()
        .find(|h| h.id == house_id)
        .unwrap()
        .protected_until;
    let count: i32 = sqlx::query_scalar("SELECT `count` FROM items WHERE id=?")
        .bind(certificate_id)
        .fetch_one(pool)
        .await
        .unwrap();
    let bill_status: Option<i32> =
        sqlx::query_scalar("SELECT status FROM mails WHERE id=? AND type<>?")
            .bind(bill_id)
            .bind(i32::from(mail::mail_type::BALANCE_RECEIPT))
            .fetch_optional(pool)
            .await
            .unwrap();
    let treasury = super::db::load_rows(pool).await.unwrap()[0]
        .treasury
        .cur_house_tax_money;
    let (receipts, receipt_money): (i64, i64) = sqlx::query_as(
        "SELECT COUNT(*),CAST(COALESCE(SUM(money_amount_1),0) AS SIGNED) FROM mails WHERE type=?",
    )
    .bind(i32::from(mail::mail_type::BALANCE_RECEIPT))
    .fetch_one(pool)
    .await
    .unwrap();
    let (money, bank_money): (i64, i64) = sqlx::query_as(
        "SELECT CAST(money AS SIGNED),CAST(money2 AS SIGNED) FROM characters WHERE id=(SELECT owner FROM housings WHERE id=?)",
    )
    .bind(house_id)
    .fetch_one(pool)
    .await
    .unwrap();
    assert_eq!(
        (
            protection,
            count,
            bill_status,
            treasury,
            receipts,
            receipt_money,
            money,
            bank_money
        ),
        expected
    );
}

#[tokio::test]
async fn departing_character_remains_in_financial_snapshot_after_world_removal() {
    let f = fixture();
    f.payer.data().money = 99_000;
    f.payer.data().money2 = 17_000;
    f.server.save.remember_departing(&f.payer);
    let before = capture_financial_snapshot(&f.server);
    assert_eq!(
        before
            .characters
            .iter()
            .filter(|(p, _)| p.character_id == f.payer.character_id)
            .count(),
        1
    );
    assert!(f.server.world.try_remove_character(f.payer.obj_id));
    let snapshot = capture_financial_snapshot(&f.server);
    let (player, character) = snapshot
        .characters
        .iter()
        .find(|(p, _)| p.character_id == f.payer.character_id)
        .unwrap();
    assert!(std::sync::Arc::ptr_eq(player, &f.payer));
    assert_eq!((character.money, character.money2), (99_000, 17_000));
    f.payer.data().money = 100_000;
    f.payer.data().money2 = 18_000;
    assert_eq!((character.money, character.money2), (99_000, 17_000));
}

#[tokio::test]
#[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; connection-local financial tables only"]
async fn private_mysql_property_payment_and_collection_cannot_straddle_financial_snapshot() {
    let Ok(url) = std::env::var("AAEMU_TEST_MYSQL") else {
        return;
    };
    let options = MySqlConnectOptions::from_str(&url).unwrap();
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
    let tables = [
        "housings",
        "dominions",
        "dominion_siege_participants",
        "mails",
        "items",
        "item_containers",
        "characters",
        "options",
        "abilities",
        "actabilities",
        "appellations",
        "character_active_buffs",
        "portal_book_coords",
        "portal_visited_district",
        "friends",
        "blocked",
        "skills",
        "quests",
        "completed_quests",
    ];
    for table in tables {
        let (_, ddl): (String, String) = sqlx::query_as(&format!("SHOW CREATE TABLE `{table}`"))
            .fetch_one(&pool)
            .await
            .unwrap();
        assert!(ddl.starts_with("CREATE TABLE"));
        sqlx::query(&ddl.replacen("CREATE TABLE", "CREATE TEMPORARY TABLE", 1))
            .execute(&pool)
            .await
            .unwrap();
    }
    if sqlx::query("SHOW COLUMNS FROM housings LIKE 'hp'")
        .fetch_optional(&pool)
        .await
        .unwrap()
        .is_none()
    {
        sqlx::query("ALTER TABLE housings ADD COLUMN hp INT NULL DEFAULT NULL")
            .execute(&pool)
            .await
            .unwrap();
    }
    let mut f = fixture();
    f.server.db = pool.clone();
    f.payer.data().money = 42_000;
    f.payer.data().money2 = 7_000;
    let certificate_id = give(&f, manager::TAX_CERTIFICATE, 100, 5);
    let house = place(&f, 300, 9000, 600.0, 0);
    assert_eq!(certificates(&f), 67);
    {
        let mut h = house.data();
        h.protection_end_date = manager::now() + 6 * 86400;
        h.is_dirty = true;
    }
    manager::update_tax_info(&f.server, &house);
    let house_id = house.data().id;
    let protection = house.data().protection_end_date;
    let bill_id = f.server.systems.economy.mail.get_my_house_mails(house_id)[0];
    let old = (
        protection,
        67,
        Some(i32::from(mail::mail_status::UNPAID)),
        25000,
        0,
        0,
        42_000,
        7_000,
    );
    let snapshot = capture_financial_snapshot(&f.server);
    let mut tx = pool.begin().await.unwrap();
    let saved = write_financial_snapshot(&f.server, &mut tx, snapshot)
        .await
        .unwrap();
    tx.commit().await.unwrap();
    commit_financial_snapshot(&f.server, saved);
    assert_finance(&pool, house_id, certificate_id, bill_id, old).await;

    // Capture OLD dirty rows exactly once, then run the real certificate debit,
    // house extension, bill deletion, ledger credit and collection receipt path.
    house.data().is_dirty = true;
    let container = f.server.items.holding_container(certificate_id).unwrap();
    {
        let mut c = container.lock().unwrap();
        c.items
            .iter_mut()
            .find(|i| i.id == certificate_id)
            .unwrap()
            .dirty = true;
    }
    f.server
        .systems
        .economy
        .mail
        .with_mail_mut(bill_id, |m| m.is_dirty = true)
        .unwrap();
    f.payer.data().money = 42_000;
    let snapshot = capture_financial_snapshot(&f.server);
    assert_eq!(
        snapshot
            .characters
            .iter()
            .find(|(p, _)| p.character_id == f.payer.character_id)
            .unwrap()
            .1
            .money,
        42_000
    );
    assert!(mail::pay_housing_tax(&f.server, &f.payer, bill_id, false));
    assert_eq!(certificates(&f), 54);
    {
        let _operation = f.server.systems.housing.operation_lock.lock().unwrap();
        super::tax::collect_locked(&f.server, &f.leader, 25).unwrap();
    }
    f.payer.data().money = 99_000;
    f.payer.data().money2 = 17_000;
    assert_eq!(
        snapshot
            .characters
            .iter()
            .find(|(p, _)| p.character_id == f.payer.character_id)
            .unwrap()
            .1
            .money,
        42_000
    );
    let receipt = mail::get_current_mail_list_for(&f.server, f.leader.character_id)
        .into_iter()
        .filter_map(|id| f.server.systems.economy.mail.get_mail_by_id(id))
        .find(|m| m.mail_type == mail::mail_type::BALANCE_RECEIPT)
        .unwrap();
    assert_eq!(receipt.body.copper_coins, 50000);
    // A departing actor can leave the world before an older in-flight snapshot
    // commits. Its older wallet receipt must not drop the newer pending actor.
    f.server.save.remember_departing(&f.payer);
    assert!(f.server.world.try_remove_character(f.payer.obj_id));
    let mut tx = pool.begin().await.unwrap();
    let saved = write_financial_snapshot(&f.server, &mut tx, snapshot)
        .await
        .unwrap();
    tx.commit().await.unwrap();
    commit_financial_snapshot(&f.server, saved);
    assert_finance(&pool, house_id, certificate_id, bill_id, old).await;
    assert!(house.data().is_dirty);
    assert!(f.server.items.item_by_id(certificate_id).unwrap().dirty);
    if receipt.id != bill_id {
        assert!(f
            .server
            .systems
            .economy
            .mail
            .state()
            .deleted
            .contains(&bill_id));
    } else {
        let replacement = f
            .server
            .systems
            .economy
            .mail
            .get_mail_by_id(bill_id)
            .unwrap();
        assert_eq!(replacement.mail_type, mail::mail_type::BALANCE_RECEIPT);
        assert_eq!(replacement.header.receiver_id, f.leader.character_id);
        assert!(replacement.is_dirty);
    }
    assert!(
        f.server
            .systems
            .economy
            .mail
            .get_mail_by_id(receipt.id)
            .unwrap()
            .is_dirty
    );
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .prepare_save()
            .changed_count(),
        1
    );

    // SQL rollback cannot acknowledge the newer payment or collection. The retry
    // must write the entire new financial state, with no duplicated receipt.
    let snapshot = capture_financial_snapshot(&f.server);
    assert_eq!(
        snapshot
            .characters
            .iter()
            .find(|(p, _)| p.character_id == f.payer.character_id)
            .unwrap()
            .1
            .money,
        99_000
    );
    let mut tx = pool.begin().await.unwrap();
    let _saved = write_financial_snapshot(&f.server, &mut tx, snapshot)
        .await
        .unwrap();
    tx.rollback().await.unwrap();
    assert_finance(&pool, house_id, certificate_id, bill_id, old).await;
    assert!(house.data().is_dirty);
    assert!(f.server.items.item_by_id(certificate_id).unwrap().dirty);
    if receipt.id != bill_id {
        assert!(f
            .server
            .systems
            .economy
            .mail
            .state()
            .deleted
            .contains(&bill_id));
    } else {
        assert!(
            f.server
                .systems
                .economy
                .mail
                .get_mail_by_id(receipt.id)
                .unwrap()
                .is_dirty
        );
    }
    let snapshot = capture_financial_snapshot(&f.server);
    let departure = &snapshot
        .characters
        .iter()
        .find(|(p, _)| p.character_id == f.payer.character_id)
        .unwrap()
        .1;
    assert_eq!((departure.money, departure.money2), (99_000, 17_000));
    let mut tx = pool.begin().await.unwrap();
    let saved = write_financial_snapshot(&f.server, &mut tx, snapshot)
        .await
        .unwrap();
    tx.commit().await.unwrap();
    commit_financial_snapshot(&f.server, saved);
    assert_finance(
        &pool,
        house_id,
        certificate_id,
        bill_id,
        (
            protection + 7 * 86400,
            54,
            None,
            0,
            1,
            50000,
            99_000,
            17_000,
        ),
    )
    .await;
    assert!(!house.data().is_dirty);
    assert!(!f.server.items.item_by_id(certificate_id).unwrap().dirty);
    assert!(f.server.systems.economy.mail.state().deleted.is_empty());
    assert!(
        !f.server
            .systems
            .economy
            .mail
            .get_mail_by_id(receipt.id)
            .unwrap()
            .is_dirty
    );
    assert_eq!(
        f.server
            .systems
            .housing
            .dominions
            .prepare_save()
            .changed_count(),
        0
    );
    assert!(!capture_financial_snapshot(&f.server)
        .characters
        .iter()
        .any(|(p, _)| p.character_id == f.payer.character_id));
    for table in tables {
        sqlx::query(&format!("DROP TEMPORARY TABLE `{table}`"))
            .execute(&pool)
            .await
            .unwrap();
    }
    pool.close().await;
}
