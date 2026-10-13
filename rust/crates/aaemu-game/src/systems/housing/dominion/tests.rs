use super::state::*;

const ZONE: u32 = 33;
const DEFENDER: u32 = 10;
const ATTACKER: u32 = 20;

fn tower() -> TowerIdentity {
    TowerIdentity {
        house_id: 61,
        instance_id: 0,
        guard_tower_setting_id: 6,
    }
}

fn owned() -> DominionManager {
    let manager = DominionManager::default();
    manager
        .declare_unowned_with(ZONE, tower(), DEFENDER, 1000, || true)
        .unwrap();
    manager
}

fn siege() -> DominionManager {
    let manager = owned();
    manager
        .schedule_siege(ZONE, ATTACKER, 2000, 3000, 1500)
        .unwrap();
    manager
        .register_participant(ZONE, 11, ATTACKER, SiegeSide::Attacker, 1500)
        .unwrap();
    manager
        .register_participant(ZONE, 12, DEFENDER, SiegeSide::Defender, 1500)
        .unwrap();
    manager
}

fn destroyed_siege() -> DominionManager {
    let manager = siege();
    manager.mark_tower_destroyed(ZONE, tower(), 2000).unwrap();
    manager
}

#[test]
fn claim_consumption_is_atomic_and_never_fabricates_treasury_or_schedule() {
    let manager = DominionManager::default();
    assert_eq!(
        manager.declare_unowned_with(ZONE, tower(), DEFENDER, 1000, || false),
        Err(DominionError::Resources)
    );
    assert!(manager.state(ZONE).is_none());
    let claimed = manager
        .declare_unowned_with(ZONE, tower(), DEFENDER, 1000, || true)
        .unwrap();
    assert_eq!(claimed.owner_expedition_id, DEFENDER);
    assert_eq!(claimed.ownership_generation, 1);
    assert_eq!(claimed.reign_start_at, 1000);
    assert_eq!(claimed.treasury, Treasury::default());
    assert_eq!(
        (
            claimed.tax_rate,
            claimed.national_tax_rate,
            claimed.last_paid_at
        ),
        (0, 0, 0)
    );
    assert!(claimed.siege.is_none());
    assert_eq!(
        manager.declare_unowned_with(ZONE, tower(), ATTACKER, 1001, || panic!(
            "must not consume a losing claim"
        )),
        Err(DominionError::AlreadyOwned)
    );
    assert!(manager.tick(1_791_900_000).unwrap().is_empty());
    assert_eq!(manager.state(ZONE).unwrap(), claimed);
}

#[test]
fn concurrent_claims_consume_only_the_winning_expediton_pack() {
    use std::sync::{
        atomic::{AtomicUsize, Ordering},
        Arc,
    };
    let manager = Arc::new(DominionManager::default());
    let consumes = Arc::new(AtomicUsize::new(0));
    let threads: Vec<_> = [DEFENDER, ATTACKER]
        .into_iter()
        .map(|expedition| {
            let manager = manager.clone();
            let consumes = consumes.clone();
            std::thread::spawn(move || {
                manager.declare_unowned_with(ZONE, tower(), expedition, 1000, || {
                    consumes.fetch_add(1, Ordering::SeqCst);
                    true
                })
            })
        })
        .collect();
    let results: Vec<_> = threads.into_iter().map(|t| t.join().unwrap()).collect();
    assert_eq!(results.iter().filter(|r| r.is_ok()).count(), 1);
    assert_eq!(consumes.load(Ordering::SeqCst), 1);
}

#[test]
fn siege_requires_explicit_schedule_membership_tower_instance_and_destruction() {
    let manager = siege();
    assert!(!manager.registered_attacker(ZONE, tower(), 11, ATTACKER, 1999));
    assert!(manager.registered_attacker(ZONE, tower(), 11, ATTACKER, 2000));
    assert!(!manager.registered_attacker(ZONE, tower(), 12, DEFENDER, 2000));
    assert!(!manager.registered_attacker(
        ZONE,
        TowerIdentity {
            instance_id: 99,
            ..tower()
        },
        11,
        ATTACKER,
        2000
    ));
    assert_eq!(
        manager.begin_engraving(ZONE, tower(), 11, ATTACKER, 2_000_000),
        Err(DominionError::TowerNotDestroyed)
    );
    assert_eq!(
        manager.mark_tower_destroyed(ZONE, tower(), 1999),
        Err(DominionError::SiegeNotActive)
    );
    manager.mark_tower_destroyed(ZONE, tower(), 2000).unwrap();
    assert!(!manager.allow_engraving(ZONE, tower(), 13, ATTACKER, 2_000_000));
    assert!(!manager.allow_engraving(ZONE, tower(), 11, 999, 2_000_000));
    assert!(!manager.allow_engraving(ZONE, tower(), 12, DEFENDER, 2_000_000));
    assert!(manager.allow_engraving(ZONE, tower(), 11, ATTACKER, 2_000_000));
    assert!(!manager.allow_engraving(ZONE, tower(), 11, ATTACKER, 3_000_000));
}

#[test]
fn native_participant_capacity_allows_one_hundred_per_side_and_existing_registration() {
    let manager = owned();
    manager
        .schedule_siege(ZONE, ATTACKER, 2000, 3000, 1500)
        .unwrap();
    for (base, expedition, side) in [
        (1000, ATTACKER, SiegeSide::Attacker),
        (2000, DEFENDER, SiegeSide::Defender),
    ] {
        for offset in 0..MAX_PARTICIPANTS_PER_SIDE as u32 {
            manager
                .register_participant(ZONE, base + offset, expedition, side, 1500)
                .unwrap();
        }
        assert_eq!(
            manager.register_participant(ZONE, base + 100, expedition, side, 1500),
            Err(DominionError::ParticipantLimit)
        );
    }
    let before = manager.state(ZONE).unwrap();
    assert_eq!(before.siege.as_ref().unwrap().participants.len(), 200);
    before.validate().unwrap();
    manager.acknowledge_committed(&manager.prepare_save());
    manager
        .register_participant(ZONE, 1000, ATTACKER, SiegeSide::Attacker, 1500)
        .unwrap();
    assert_eq!(manager.prepare_save().changed_count(), 0); // idempotent at capacity
    assert_eq!(
        manager.register_participant(ZONE, 1000, DEFENDER, SiegeSide::Defender, 1500),
        Err(DominionError::ParticipantLimit)
    );
    assert_eq!(manager.state(ZONE).unwrap(), before); // a rejected side change keeps its old registration
    manager.unregister_participant(ZONE, 2000).unwrap();
    manager
        .register_participant(ZONE, 1000, DEFENDER, SiegeSide::Defender, 1500)
        .unwrap();
    let participants = manager.state(ZONE).unwrap().siege.unwrap().participants;
    assert_eq!(
        participants
            .values()
            .filter(|p| p.side == SiegeSide::Attacker)
            .count(),
        99
    );
    assert_eq!(
        participants
            .values()
            .filter(|p| p.side == SiegeSide::Defender)
            .count(),
        100
    );
}

#[test]
fn restore_rejects_oversized_native_participant_side_without_replacing_authority() {
    let manager = owned();
    manager
        .schedule_siege(ZONE, ATTACKER, 2000, 3000, 1500)
        .unwrap();
    for id in 1..=MAX_PARTICIPANTS_PER_SIDE as u32 {
        manager
            .register_participant(ZONE, id, ATTACKER, SiegeSide::Attacker, 1500)
            .unwrap();
    }
    let before = manager.snapshots();
    let mut oversized = before[0].clone();
    oversized.siege.as_mut().unwrap().participants.insert(
        101,
        SiegeParticipant {
            character_id: 101,
            expedition_id: ATTACKER,
            side: SiegeSide::Attacker,
        },
    );
    assert_eq!(oversized.validate(), Err(DominionError::ParticipantLimit));
    assert_eq!(
        manager.restore(vec![oversized]),
        Err(DominionError::ParticipantLimit)
    );
    assert_eq!(manager.snapshots(), before);
}

#[test]
fn capture_requires_full_five_minutes_and_invalidates_competing_and_replayed_handles() {
    let manager = destroyed_siege();
    manager
        .register_participant(ZONE, 13, ATTACKER, SiegeSide::Attacker, 2000)
        .unwrap();
    let first = manager
        .begin_engraving(ZONE, tower(), 11, ATTACKER, 2_000_123)
        .unwrap();
    let other = manager
        .begin_engraving(ZONE, tower(), 13, ATTACKER, 2_000_123)
        .unwrap();
    assert_eq!(first.ready_at_ms, 2_300_123);
    assert_eq!(
        manager.complete_engraving(&first, ATTACKER, 2_300_122),
        Err(DominionError::NotReady)
    );
    assert_eq!(
        manager.complete_engraving(&first, DEFENDER, 2_300_123),
        Err(DominionError::NotRegistered)
    );
    assert_eq!(manager.state(ZONE).unwrap().owner_expedition_id, DEFENDER);
    let captured = manager
        .complete_engraving(&first, ATTACKER, 2_300_123)
        .unwrap();
    assert_eq!(captured.owner_expedition_id, ATTACKER);
    assert_eq!(
        (
            captured.reign_start_at,
            captured.last_siege_end_at,
            captured.ownership_generation
        ),
        (2300, 2300, 2)
    );
    assert!(!captured.tower_destroyed);
    assert_eq!(captured.tower_hp, None);
    assert_eq!(
        captured.siege.as_ref().unwrap().outcome,
        SiegeOutcome::AttackerCaptured
    );
    captured.validate().unwrap();
    assert_eq!(
        manager.complete_engraving(&other, ATTACKER, 2_300_124),
        Err(DominionError::StaleHandle)
    );
    assert_eq!(
        manager.complete_engraving(&first, ATTACKER, 2_300_124),
        Err(DominionError::StaleHandle)
    );
}

#[test]
fn abort_repair_removal_and_restart_never_resume_an_old_channel() {
    let manager = destroyed_siege();
    let first = manager
        .begin_engraving(ZONE, tower(), 11, ATTACKER, 2_000_000)
        .unwrap();
    assert!(manager.abort_engraving(&first));
    let second = manager
        .begin_engraving(ZONE, tower(), 11, ATTACKER, 2_000_000)
        .unwrap();
    assert_ne!(first, second);
    assert_eq!(
        manager.complete_engraving(&first, ATTACKER, 2_300_000),
        Err(DominionError::StaleHandle)
    );
    let restarted = DominionManager::default();
    restarted.restore(manager.snapshots()).unwrap();
    let fresh = restarted
        .begin_engraving(ZONE, tower(), 11, ATTACKER, 2_000_000)
        .unwrap();
    assert_eq!(
        restarted.complete_engraving(&second, ATTACKER, 2_300_000),
        Err(DominionError::StaleHandle)
    );
    assert_eq!(restarted.state(ZONE).unwrap().owner_expedition_id, DEFENDER);
    restarted.reset_tower_destroyed(ZONE, tower()).unwrap();
    assert_eq!(
        restarted.complete_engraving(&fresh, ATTACKER, 2_300_000),
        Err(DominionError::StaleHandle)
    );
    manager.unregister_participant(ZONE, 11).unwrap();
    assert_eq!(
        manager.complete_engraving(&second, ATTACKER, 2_300_000),
        Err(DominionError::StaleHandle)
    );
    manager.restore(manager.snapshots()).unwrap();
    manager.mark_tower_destroyed(ZONE, tower(), 2000).unwrap();
    manager
        .register_participant(ZONE, 11, ATTACKER, SiegeSide::Attacker, 2000)
        .unwrap();
    let third = manager
        .begin_engraving(ZONE, tower(), 11, ATTACKER, 2_000_000)
        .unwrap();
    manager.remove_tower(ZONE, tower()).unwrap();
    assert_eq!(
        manager.complete_engraving(&third, ATTACKER, 2_300_000),
        Err(DominionError::StaleHandle)
    );
}

#[test]
fn expiry_and_cancel_preserve_defender_and_reset_damage_without_reward() {
    let manager = destroyed_siege();
    let handle = manager
        .begin_engraving(ZONE, tower(), 11, ATTACKER, 2_000_000)
        .unwrap();
    manager
        .register_participant(ZONE, 13, ATTACKER, SiegeSide::Attacker, 2000)
        .unwrap();
    assert_eq!(
        manager.begin_engraving(ZONE, tower(), 13, ATTACKER, 2_700_000),
        Err(DominionError::TooLate)
    );
    assert_eq!(
        manager.complete_engraving(&handle, ATTACKER, 3_000_000),
        Err(DominionError::SiegeNotActive)
    );
    let expired = manager.tick(3000).unwrap().pop().unwrap();
    assert_eq!(
        (expired.owner_expedition_id, expired.reign_start_at),
        (DEFENDER, 1000)
    );
    assert_eq!(
        expired.siege.as_ref().unwrap().outcome,
        SiegeOutcome::DefenderHeld
    );
    assert_eq!((expired.tower_destroyed, expired.tower_hp), (false, None));
    assert_eq!(expired.treasury, Treasury::default());
    manager
        .schedule_siege(ZONE, ATTACKER, 4000, 5000, 3500)
        .unwrap();
    let cancelled = manager.cancel_siege(ZONE, 3600).unwrap();
    assert_eq!(cancelled.owner_expedition_id, DEFENDER);
    assert_eq!(cancelled.siege.as_ref().unwrap().siege_id, 2);
    assert_eq!(cancelled.siege.unwrap().outcome, SiegeOutcome::Cancelled);
}

#[test]
fn partial_damage_persists_and_destroyed_flag_cannot_be_restored_with_live_hp() {
    let manager = siege();
    let damaged = manager
        .update_tower_hp_at(ZONE, tower(), 1234, 2000)
        .unwrap();
    let restarted = DominionManager::default();
    restarted.restore(vec![damaged.clone()]).unwrap();
    assert_eq!(restarted.state(ZONE).unwrap().tower_hp, Some(1234));
    assert!(!restarted.allow_engraving(ZONE, tower(), 11, ATTACKER, 2_000_000));
    let mut invalid = damaged;
    invalid.tower_destroyed = true;
    assert_eq!(
        restarted.restore(vec![invalid]),
        Err(DominionError::InvalidState)
    );
    assert_eq!(restarted.state(ZONE).unwrap().tower_hp, Some(1234));
    assert_eq!(
        manager.update_tower_hp_at(ZONE, tower(), -1, 2000),
        Err(DominionError::InvalidState)
    );
    assert_eq!(
        manager.update_tower_hp_at(ZONE, tower(), 1234, 3000),
        Err(DominionError::SiegeNotActive)
    );
    let destroyed = manager.mark_tower_destroyed(ZONE, tower(), 2000).unwrap();
    assert_eq!(destroyed.tower_hp, Some(0));
}

#[test]
fn tax_rate_matches_original_zero_to_fifty_and_treasury_is_checked_and_owner_only() {
    let manager = owned();
    for rate in [0, 50] {
        manager.set_tax_rate(ZONE, DEFENDER, rate, 1001).unwrap();
    }
    for rate in [-1, 51] {
        assert_eq!(
            manager.set_tax_rate(ZONE, DEFENDER, rate, 1001),
            Err(DominionError::InvalidTreasury)
        );
    }
    assert_eq!(
        manager.set_tax_rate(ZONE, ATTACKER, 25, 1001),
        Err(DominionError::WrongOwner)
    );
    manager
        .credit_treasury(
            ZONE,
            Treasury {
                cur_house_tax_money: i32::MAX,
                ..Treasury::default()
            },
        )
        .unwrap();
    assert_eq!(
        manager.credit_treasury(
            ZONE,
            Treasury {
                cur_house_tax_money: 1,
                ..Treasury::default()
            }
        ),
        Err(DominionError::InvalidTreasury)
    );
    assert_eq!(
        manager.collect_treasury_with(ZONE, ATTACKER, 1100, |_| panic!(
            "must not pay a foreign expedition"
        )),
        Err(DominionError::WrongOwner)
    );
    assert_eq!(
        manager.collect_treasury_with(ZONE, DEFENDER, 1100, |_| false),
        Err(DominionError::Resources)
    );
    assert_eq!(
        manager.state(ZONE).unwrap().treasury.cur_house_tax_money,
        i32::MAX
    );
    let paid = manager
        .collect_treasury_with(ZONE, DEFENDER, 1100, |money| {
            money.cur_house_tax_money == i32::MAX
        })
        .unwrap();
    assert_eq!(paid.treasury, Treasury::default());
    assert_eq!(paid.last_paid_at, 1100);
}

#[test]
fn tax_rate_cooldown_preserves_noops_and_rejected_changes_and_allows_exact_hour() {
    let manager = owned();
    let first = manager.set_tax_rate(ZONE, DEFENDER, 25, 1001).unwrap();
    let saved = manager.prepare_save();
    manager.acknowledge_committed(&saved);
    assert_eq!(manager.prepare_save().changed_count(), 0);

    // Repeating a rate neither restarts the cooldown nor dirties already saved state.
    assert_eq!(
        manager.set_tax_rate(ZONE, DEFENDER, 25, 1002),
        Ok(first.clone())
    );
    assert_eq!(manager.prepare_save().changed_count(), 0);
    for now in [1000, 1001, 1001 + TAX_RATE_CHANGE_COOLDOWN_SECONDS - 1] {
        assert_eq!(
            manager.set_tax_rate(ZONE, DEFENDER, 50, now),
            Err(DominionError::TaxRateChangeLater)
        );
        assert_eq!(manager.get(ZONE), Some(first.clone()));
        assert_eq!(manager.prepare_save().changed_count(), 0);
    }

    let boundary = 1001 + TAX_RATE_CHANGE_COOLDOWN_SECONDS;
    let next = manager.set_tax_rate(ZONE, DEFENDER, 50, boundary).unwrap();
    assert_eq!(next.tax_rate, 50);
    assert_eq!(next.last_tax_rate_changed_at, boundary);
    assert_eq!(manager.prepare_save().changed_count(), 1);
}

#[test]
fn restored_tax_rate_cooldown_fails_closed_for_future_timestamps_without_overflow() {
    let original = owned();
    let mut state = original.get(ZONE).unwrap();
    state.tax_rate = 25;
    state.last_tax_rate_changed_at = i64::MAX;
    let manager = DominionManager::default();
    manager.restore(vec![state.clone()]).unwrap();
    for now in [0, 1001, i64::MAX] {
        assert_eq!(
            manager.set_tax_rate(ZONE, DEFENDER, 50, now),
            Err(DominionError::TaxRateChangeLater)
        );
        assert_eq!(manager.get(ZONE), Some(state.clone()));
    }
    assert_eq!(manager.set_tax_rate(ZONE, DEFENDER, 25, 1001), Ok(state));
    assert_eq!(manager.prepare_save().changed_count(), 0);

    let manager = owned();
    manager.set_tax_rate(ZONE, DEFENDER, 25, 1001).unwrap();
    assert!(manager.set_tax_rate(ZONE, DEFENDER, 50, i64::MAX).is_ok());
}

#[test]
fn acknowledgments_preserve_changes_and_deletion_recreation_after_snapshot() {
    let manager = owned();
    let first = manager.prepare_save();
    manager.set_tax_rate(ZONE, DEFENDER, 25, 1001).unwrap();
    manager.acknowledge_committed(&first);
    assert_eq!(manager.prepare_save().changed_count(), 1);
    let latest = manager.prepare_save();
    manager.acknowledge_committed(&latest);
    assert_eq!(manager.prepare_save().changed_count(), 0);
    manager.remove_tower(ZONE, tower()).unwrap();
    let removal = manager.prepare_save();
    manager
        .declare_unowned_with(ZONE, tower(), ATTACKER, 1100, || true)
        .unwrap();
    manager.acknowledge_committed(&removal);
    assert_eq!(manager.prepare_save().changed_count(), 1);
    manager.remove_tower(ZONE, tower()).unwrap();
    let old_removal = manager.prepare_save();
    manager.register_tower(ZONE, tower()).unwrap();
    manager.remove_tower(ZONE, tower()).unwrap();
    manager.acknowledge_committed(&old_removal);
    assert_eq!(manager.prepare_save().changed_count(), 1);
}

#[test]
fn reload_invalidates_old_save_receipts_and_malformed_restore_is_atomic() {
    let manager = siege();
    let receipt = manager.prepare_save();
    manager.restore(manager.snapshots()).unwrap();
    manager.set_tax_rate(ZONE, DEFENDER, 25, 1001).unwrap();
    manager.acknowledge_committed(&receipt);
    assert_eq!(manager.prepare_save().changed_count(), 1);
    let before = manager.snapshots();
    let mut bad = before[0].clone();
    bad.siege
        .as_mut()
        .unwrap()
        .participants
        .get_mut(&11)
        .unwrap()
        .expedition_id = 999;
    assert_eq!(manager.restore(vec![bad]), Err(DominionError::InvalidSiege));
    assert_eq!(manager.snapshots(), before);
    let other = owned();
    other.acknowledge_committed(&receipt);
    assert_eq!(other.prepare_save().changed_count(), 1);
}

#[tokio::test]
async fn startup_rejects_missing_replaced_towers_instances_settings_and_expeditons() {
    use crate::data::{
        world_data::{SystemFaction, Zone},
        GameData,
    };
    use crate::systems::housing::{
        data::{GuardTowerSetting, HousingTemplate},
        manager,
    };
    use crate::systems::social::expedition::Expedition;
    let mut data = GameData::empty();
    data.zones.zones.insert(
        crate::test_support::TEST_ZONE,
        Zone {
            id: 1,
            zone_key: crate::test_support::TEST_ZONE,
            group_id: ZONE,
            ..Zone::default()
        },
    );
    data.systems.housing.add_template(HousingTemplate {
        id: 300,
        guard_tower_setting_id: 6,
        main_model_id: 700,
        ..HousingTemplate::default()
    });
    data.systems.housing.castles.guard_tower_settings.insert(
        6,
        GuardTowerSetting {
            id: 6,
            ..GuardTowerSetting::default()
        },
    );
    let server = crate::test_support::test_server_with(data);
    server.expeditions.add(Expedition {
        faction: SystemFaction {
            id: DEFENDER,
            ..SystemFaction::default()
        },
        members: Vec::new(),
        policies: Vec::new(),
        removed_members: Vec::new(),
        is_disbanded: false,
    });
    let state = owned().state(ZONE).unwrap();
    assert!(super::db::validate_loaded_identity(&server, std::slice::from_ref(&state)).is_err());
    let world = server.world.world(0).unwrap();
    let house = manager::create(&server, 300, 0, &world, 0, 0).unwrap();
    {
        let mut h = house.data();
        h.id = tower().house_id;
        h.unit.transform.zone_id = crate::test_support::TEST_ZONE;
        h.unit.transform.set_xyz(200.0, 200.0, 100.0);
    }
    assert!(server.systems.housing.manager.insert(&house));
    manager::spawn_house(&server, &house);
    super::db::validate_loaded_identity(&server, std::slice::from_ref(&state)).unwrap();
    for bad_tower in [
        TowerIdentity {
            house_id: 99,
            ..tower()
        },
        TowerIdentity {
            instance_id: 99,
            ..tower()
        },
        TowerIdentity {
            guard_tower_setting_id: 99,
            ..tower()
        },
    ] {
        let mut bad = state.clone();
        bad.tower = bad_tower;
        assert!(super::db::validate_loaded_identity(&server, &[bad]).is_err());
    }
    server
        .expeditions
        .with_mut(DEFENDER, |e| e.is_disbanded = true)
        .unwrap();
    assert!(super::db::validate_loaded_identity(&server, &[state]).is_err());
}

#[tokio::test]
#[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; connection-local dominion and participant temporary tables"]
async fn private_mysql_dominion_rollback_failure_retry_and_restart_preserve_authority() {
    use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
    use std::str::FromStr;
    let Ok(url) = std::env::var("AAEMU_TEST_MYSQL") else {
        return;
    };
    let options = MySqlConnectOptions::from_str(&url).unwrap();
    assert_eq!(
        options.get_port(),
        3317,
        "only private MySQL3317 is allowed"
    );
    let pool = MySqlPoolOptions::new()
        .max_connections(1)
        .connect_with(options)
        .await
        .unwrap();
    for table in ["dominions", "dominion_siege_participants"] {
        let (_, ddl): (String, String) = sqlx::query_as(&format!("SHOW CREATE TABLE {table}"))
            .fetch_one(&pool)
            .await
            .unwrap();
        sqlx::query(&ddl.replacen("CREATE TABLE", "CREATE TEMPORARY TABLE", 1))
            .execute(&pool)
            .await
            .unwrap();
    }
    let manager = destroyed_siege();
    let old_channel = manager
        .begin_engraving(ZONE, tower(), 11, ATTACKER, 2_000_000)
        .unwrap();
    let mut tx = pool.begin().await.unwrap();
    assert_eq!(manager.save(&mut tx).await.unwrap(), 1);
    tx.rollback().await.unwrap();
    assert!(super::db::load_rows(&pool).await.unwrap().is_empty());
    assert_eq!(manager.prepare_save().changed_count(), 1);
    let mut tx = pool.begin().await.unwrap();
    manager.save(&mut tx).await.unwrap();
    manager.set_tax_rate(ZONE, DEFENDER, 25, 2001).unwrap();
    tx.commit().await.unwrap();
    manager.commit_save();
    assert_eq!(manager.prepare_save().changed_count(), 1);
    let rows = super::db::load_rows(&pool).await.unwrap();
    assert_eq!(rows[0].tax_rate, 0);
    assert_eq!(rows[0].siege.as_ref().unwrap().participants.len(), 2);
    assert_eq!(rows[0].tower_hp, Some(0));
    let restarted = DominionManager::default();
    restarted.restore(rows).unwrap();
    assert_eq!(
        restarted.complete_engraving(&old_channel, ATTACKER, 2_300_000),
        Err(DominionError::StaleHandle)
    );
    assert_eq!(restarted.state(ZONE).unwrap().owner_expedition_id, DEFENDER);
    manager
        .register_participant(ZONE, 13, ATTACKER, SiegeSide::Attacker, 2001)
        .unwrap();
    // A late participant-write failure must roll back the preceding authority-row write.
    sqlx::query("ALTER TABLE dominion_siege_participants CHANGE COLUMN side blocked_side tinyint unsigned NOT NULL").execute(&pool).await.unwrap();
    let mut tx = pool.begin().await.unwrap();
    assert!(manager.save(&mut tx).await.is_err());
    tx.rollback().await.unwrap();
    manager.commit_save(); // no successful batch from this failed save can be acknowledged
    assert_eq!(manager.prepare_save().changed_count(), 1);
    sqlx::query("ALTER TABLE dominion_siege_participants CHANGE COLUMN blocked_side side tinyint unsigned NOT NULL").execute(&pool).await.unwrap();
    let rows = super::db::load_rows(&pool).await.unwrap();
    assert_eq!(rows[0].tax_rate, 0);
    assert_eq!(rows[0].siege.as_ref().unwrap().participants.len(), 2);
    let mut tx = pool.begin().await.unwrap();
    manager.save(&mut tx).await.unwrap();
    tx.commit().await.unwrap();
    manager.commit_save();
    assert_eq!(manager.prepare_save().changed_count(), 0);
    assert_eq!(
        super::db::load_rows(&pool).await.unwrap(),
        manager.snapshots()
    );
    manager.remove_tower(ZONE, tower()).unwrap();
    let mut tx = pool.begin().await.unwrap();
    manager.save(&mut tx).await.unwrap();
    tx.commit().await.unwrap();
    manager.commit_save();
    assert_eq!(manager.prepare_save().changed_count(), 0);
    assert!(super::db::load_rows(&pool).await.unwrap().is_empty());
    for table in ["dominion_siege_participants", "dominions"] {
        sqlx::query(&format!("DROP TEMPORARY TABLE {table}"))
            .execute(&pool)
            .await
            .unwrap();
    }
    pool.close().await;
}
