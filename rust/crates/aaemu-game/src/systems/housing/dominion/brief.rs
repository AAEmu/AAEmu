//! Offline character briefs for the original siege roster. The online service path keeps
//! its live profile; this fallback only resolves IDs that the current dominion roster lists.

use sqlx::Row;

use crate::error::{GameError, Result};
use crate::packets::g2c_misc::SCCharBriefPacket;
use crate::server::GameServer;

use super::state::{DominionManager, SiegeStatus};

fn registered(manager: &DominionManager, character_id: u32, now_seconds: i64) -> bool {
    character_id != 0
        && manager.snapshots().iter().any(|state| {
            state.siege.as_ref().is_some_and(|siege| {
                siege.status != SiegeStatus::Finished
                    && now_seconds < siege.ends_at
                    && siege.participants.contains_key(&character_id)
            })
        })
}

/// Resolve actual stored identity for an offline, nondeleted siege participant. An ID/name
/// cache cannot supply its race. No object/authority lock is retained across the query.
pub async fn lookup(server: &GameServer, character_id: u32) -> Result<Option<SCCharBriefPacket>> {
    if !registered(
        &server.systems.housing.dominions,
        character_id,
        chrono::Utc::now().timestamp(),
    ) {
        return Ok(None);
    }
    let rows = sqlx::query(
        "SELECT name, CAST(race AS SIGNED) AS race FROM characters \
         WHERE id=? AND deleted=0 LIMIT 2",
    )
    .bind(character_id)
    .fetch_all(&server.db)
    .await?;
    if rows.len() > 1 {
        return Err(GameError::Protocol(format!(
            "ambiguous stored character ID {character_id}"
        )));
    }
    let Some(row) = rows.first() else {
        return Ok(None);
    };
    let name: String = row.try_get("name")?;
    let race: i64 = row.try_get("race")?;
    let race = u8::try_from(race).map_err(|_| {
        GameError::Protocol(format!("invalid stored race for character {character_id}"))
    })?;
    // A removal/expiry while SQL was in flight must not extend an old roster's lookup scope.
    if !registered(
        &server.systems.housing.dominions,
        character_id,
        chrono::Utc::now().timestamp(),
    ) {
        return Ok(None);
    }
    Ok(Some(SCCharBriefPacket {
        char_id: character_id,
        name,
        race,
    }))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::housing::dominion::state::{SiegeSide, TowerIdentity};

    fn roster(server: &GameServer, now: i64) {
        let manager = &server.systems.housing.dominions;
        let tower = TowerIdentity {
            house_id: 1,
            instance_id: 0,
            guard_tower_setting_id: 6,
        };
        manager
            .declare_unowned_with(33, tower, 10, now, || true)
            .unwrap();
        manager
            .schedule_siege(33, 20, now + 60, now + 1200, now)
            .unwrap();
        for id in 2..=10 {
            manager
                .register_participant(33, id, 20, SiegeSide::Attacker, now)
                .unwrap();
        }
        manager.unregister_participant(33, 5).unwrap();
    }

    #[tokio::test]
    async fn unregistered_ids_skip_db_and_registered_db_failure_never_invents_race() {
        let server = crate::test_support::test_server();
        let now = chrono::Utc::now().timestamp();
        roster(&server, now);
        server.names.add_character(2, "CachedNameWithoutRace", 1002);
        server.db.close().await;
        assert!(lookup(&server, 0).await.unwrap().is_none());
        assert!(lookup(&server, 5).await.unwrap().is_none());
        assert!(lookup(&server, 999).await.unwrap().is_none());
        assert!(lookup(&server, 2).await.is_err());
        let manager = &server.systems.housing.dominions;
        assert!(registered(manager, 2, now)); // warmup roster is published before siege start
        assert!(!registered(manager, 2, now + 1200)); // deadline is exclusive
        manager.cancel_siege(33, now + 1).unwrap();
        assert!(lookup(&server, 2).await.unwrap().is_none());
    }

    #[tokio::test]
    #[ignore = "requires private MySQL3317 via AAEMU_TEST_MYSQL; uses a connection-local temporary characters table"]
    async fn private_mysql_offline_siege_briefs_use_stored_race_and_reject_deleted_or_invalid_rows()
    {
        use aaemu_commons::{Marshal, PacketStream};
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
        // Shadow the real table; no live character or schema is changed. The composite
        // primary key reflects the base schema's possibility of an ambiguous global ID.
        sqlx::query("CREATE TEMPORARY TABLE characters (id int unsigned NOT NULL, account_id int unsigned NOT NULL, name varchar(128) NOT NULL, race int DEFAULT NULL, deleted int NOT NULL, PRIMARY KEY(id,account_id))")
            .execute(&pool).await.unwrap();
        sqlx::query("INSERT INTO characters VALUES (2,1002,'RealMixedCase',5,0),(4,1004,'Deleted',3,1),(5,1005,'NotRegistered',2,0),(6,1006,'DuplicateA',1,0),(6,2006,'DuplicateB',4,0),(7,1007,'NoRace',NULL,0),(8,1008,'NegativeRace',-1,0),(9,1009,'WideRace',256,0)")
            .execute(&pool).await.unwrap();
        let mut server = crate::test_support::test_server();
        server.db = pool.clone();
        let now = chrono::Utc::now().timestamp();
        roster(&server, now);
        server.names.add_character(2, "WrongCachedName", 1002);
        assert!(server.world.character_by_id(2).is_none());
        let packet = lookup(&server, 2).await.unwrap().unwrap();
        assert_eq!(
            (packet.char_id, packet.name.as_str(), packet.race),
            (2, "RealMixedCase", 5)
        );
        let mut body = PacketStream::new();
        packet.write(&mut body);
        let mut expected = PacketStream::new();
        expected.write_u32(2).write_str("RealMixedCase").write_u8(5);
        assert_eq!(body.into_bytes(), expected.into_bytes());
        for id in [3, 4, 5, 10] {
            assert!(lookup(&server, id).await.unwrap().is_none());
        }
        for id in [6, 7, 8, 9] {
            assert!(lookup(&server, id).await.is_err());
        }
        server
            .systems
            .housing
            .dominions
            .unregister_participant(33, 2)
            .unwrap();
        assert!(lookup(&server, 2).await.unwrap().is_none());
        sqlx::query("DROP TEMPORARY TABLE characters")
            .execute(&pool)
            .await
            .unwrap();
        pool.close().await;
    }
}
