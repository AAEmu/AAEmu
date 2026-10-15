//! `Core/Managers/UnitManagers/CharacterManager` remainder: `GetEffectiveAccessLevel`,
//! `PlayerRoll` (+ `CSRollDicePacket` / `SCDiceValuePacket`), `StartOnlineTracking`
//! (`CharacterOnlineTrackingTask`: online time + account divine clock) and
//! `AddOfflineCrimePoints`.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, Weak};
use std::time::Duration;

use aaemu_commons::{Marshal, PacketStream};
use chrono::{NaiveDateTime, Utc};
use futures::future::BoxFuture;
use rand::Rng;

use super::tasks::{GameTask, TaskState};
use crate::db::AccountDetails;
use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::world::{broadcast, PlayerRef, WorldObject};

/// `CharacterManager.GetEffectiveAccessLevel(character)`: the higher of the character's and the
/// account's access level.
pub async fn effective_access_level(server: &GameServer, player: &PlayerRef) -> i32 {
    let (access, account) = {
        let c = player.data();
        (c.access_level, c.account_id)
    };
    let details = super::accounts::get_account_details(server, account).await;
    effective_access_level_of(access, &details)
}

/// The rule of `GetEffectiveAccessLevel`: `Math.Max(character.AccessLevel,
/// accountDetails.AccessLevel)`.
pub fn effective_access_level_of(character_access_level: i32, details: &AccountDetails) -> i32 {
    character_access_level.max(details.access_level)
}

/// `SCDiceValuePacket(name, max, value)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCDiceValuePacket {
    pub name: String,
    pub max: i32,
    pub value: i32,
}
packet_def!(
    SCDiceValuePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDiceValuePacket
);
impl Marshal for SCDiceValuePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_str(&self.name)
            .write_i32(self.max)
            .write_i32(self.value);
    }
}

/// `PlayerRoll(player, max)`: a roll in `1..=max` broadcast to the player and its neighbours.
pub fn player_roll(server: &GameServer, player: &PlayerRef, max: i32) -> Option<i32> {
    let name = player.data().unit.name.clone();
    if max < 1 {
        tracing::warn!("{name} attempted to roll a die with an invalid max of {max}");
        return None;
    }
    let roll = rand::thread_rng().gen_range(0..max) + 1;
    let obj = WorldObject::from(player);
    if let Some(world) = server.world.world_of(&obj) {
        broadcast(
            &world,
            &obj,
            &SCDiceValuePacket {
                name,
                max,
                value: roll,
            },
            true,
        );
    } else {
        player.send(SCDiceValuePacket {
            name,
            max,
            value: roll,
        });
    }
    Some(roll)
}

/// `CSRollDicePacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRollDicePacket {
    pub max: i32,
}

impl ClientPacket for CSRollDicePacket {
    const NAME: &'static str = "CSRollDicePacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self { max: s.read_i32()? })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            // `Connection.ActiveChar` is null in the lobby: the C# throws (logged, ignored).
            match conn.active_char.clone() {
                Some(p) => {
                    player_roll(server, &p, self.max);
                }
                None => tracing::error!("CSRollDicePacket without an active character"),
            }
            Ok(())
        })
    }
}

/// `CharacterOnlineTrackingTask.CheckPrecision`.
pub const ONLINE_TRACKING_PRECISION: Duration = Duration::from_secs(5);

/// `CharacterOnlineTrackingTask`.
pub struct CharacterOnlineTrackingTask {
    state: TaskState,
    server: Weak<GameServer>,
    inner: Mutex<TrackingState>,
}

struct TrackingState {
    last_check: NaiveDateTime,
    busy: bool,
    /// Sub-second part of `OnlineTime` per character (the Rust character keeps seconds).
    fractions: HashMap<u32, f64>,
}

impl CharacterOnlineTrackingTask {
    pub fn new(server: &Arc<GameServer>) -> Arc<Self> {
        Arc::new(Self {
            state: TaskState::new("CharacterOnlineTrackingTask"),
            server: Arc::downgrade(server),
            inner: Mutex::new(TrackingState {
                last_check: Utc::now().naive_utc(),
                busy: false,
                fractions: HashMap::new(),
            }),
        })
    }

    /// One `Execute()`: adds the elapsed time to every online character and its account's
    /// divine clock.
    pub async fn run(&self, server: &GameServer) {
        let delta = {
            let mut st = self.inner.lock().expect("tracking poisoned");
            if st.busy {
                return;
            }
            st.busy = true;
            let now = Utc::now().naive_utc();
            let d = (now - st.last_check).num_milliseconds() as f64 / 1000.0;
            st.last_check = now;
            d
        };
        for p in server.world.all_characters() {
            let (id, account) = (p.character_id, p.data().account_id);
            let (delta_seconds, hours) = {
                let mut st = self.inner.lock().expect("tracking poisoned");
                let frac = st.fractions.entry(id).or_insert(0.0);
                let mut c = p.data();
                let last = f64::from(c.online_time) + *frac;
                let new = last + delta;
                let whole = new.floor();
                let before = c.online_time;
                c.online_time = whole as u32;
                *frac = new - whole;
                (
                    (whole - last.floor()) as u32,
                    // Some(hours online) when this tick crossed an hour boundary.
                    (c.online_time / 3600 > before / 3600).then_some(c.online_time / 3600),
                )
            };
            if let Some(hours) = hours {
                // PLAY_TIME (hours online, absolute).
                crate::systems::report_achievement(
                    server,
                    &p,
                    AchievementInput::Absolute {
                        kind_id: char_record_kind::PLAY_TIME,
                        value: i64::from(hours),
                        matcher: RecordMatch::Any,
                    },
                );
            }
            let (time, taken) = super::accounts::get_divine_clock(server, account).await;
            super::accounts::update_divine_clock(
                server,
                account,
                time.wrapping_add(delta_seconds),
                taken,
            )
            .await;
        }
        self.inner.lock().expect("tracking poisoned").busy = false;
    }
}

impl GameTask for CharacterOnlineTrackingTask {
    fn state(&self) -> &TaskState {
        &self.state
    }

    fn execute(&self) {
        // Always queued (repeat interval), so the async path runs; nothing to do inline.
    }

    fn execute_async(self: Arc<Self>) -> BoxFuture<'static, ()> {
        Box::pin(async move {
            if let Some(server) = self.server.upgrade() {
                self.run(&server).await;
            }
        })
    }
}

/// `StartOnlineTracking()`: the tracking task every 5 s (first run at once).
pub fn start_online_tracking(server: &Arc<GameServer>) {
    server.systems.services.tasks.schedule(
        CharacterOnlineTrackingTask::new(server),
        Some(Duration::ZERO),
        Some(ONLINE_TRACKING_PRECISION),
        -1,
    );
}

/// `AddOfflineCrimePoints(playerId, crimePointsToAdd)`.
pub async fn add_offline_crime_points(server: &GameServer, player_id: u32, points: i16) {
    let res = sqlx::query(
        "UPDATE characters SET `crime_point` = `crime_point` + ? , `crime_record` = `crime_record` + ? WHERE `id` = ?",
    )
    .bind(points)
    .bind(points)
    .bind(player_id)
    .execute(&server.db)
    .await;
    if !matches!(res, Ok(ref r) if r.rows_affected() == 1) {
        tracing::warn!(
            "Failed to update offline crime points for player {player_id}! (Add {points})"
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_support::{spawn_player, test_character, test_server, TestClient};

    #[test]
    fn dice_packet_is_byte_exact() {
        let mut e = PacketStream::new();
        e.write_str("Bob").write_i32(100).write_i32(42);
        let bytes = crate::net::packet::encode(&SCDiceValuePacket {
            name: "Bob".into(),
            max: 100,
            value: 42,
        });
        assert_eq!(bytes[6..].to_vec(), e.into_bytes());
    }

    #[tokio::test]
    async fn rolls_are_broadcast_in_range() {
        let server = test_server();
        let mut a = TestClient::new(&server, 1, 1);
        let mut b = TestClient::new(&server, 2, 2);
        let pa = spawn_player(&server, &a, test_character(1, "Al", 10.0, 10.0), 0x100);
        let _pb = spawn_player(&server, &b, test_character(2, "Bo", 12.0, 10.0), 0x101);
        a.drain();
        b.drain();
        assert_eq!(player_roll(&server, &pa, 0), None);
        for _ in 0..50 {
            let r = player_roll(&server, &pa, 6).unwrap();
            assert!((1..=6).contains(&r));
        }
        assert_eq!(a.drain_ops().len(), 50);
        assert_eq!(b.drain_ops(), vec![sc::SCDiceValuePacket; 50]);
        let mut s = PacketStream::from_bytes(vec![6, 0, 0, 0]);
        assert_eq!(CSRollDicePacket::read(&mut s).unwrap().max, 6);
    }
}
