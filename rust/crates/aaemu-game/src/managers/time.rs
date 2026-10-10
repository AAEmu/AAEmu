//! `Core/Managers/TimeManager` + `TimeOfDayObserver`: the game clock (starts at 12:00, advances
//! `TickDelay * 10` game seconds every 10 real seconds) and the per-character time-of-day
//! subscriptions. An observer may carry a resolver for the per-character override of
//! `TimeOfDayObserver.OnNext` (`StormShipLogic.ResolveClientTimeOfDayHours`).

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use crate::net::connection::ConnectionHandle;
use crate::packets::world::{SCDetailedTimeOfDayPacket, SCTimeOfDayPacket};

const MAX_TIME: f32 = 86_400.0;
const SPEED: f32 = 0.001_666_6;
const TICK_DELAY: f32 = 3600.0 * SPEED;

/// `TimeOfDayObserver.OnNext` override: the hours to send instead of the game time (`None` = the
/// game time).
pub type TimeOfDayResolver = Arc<dyn Fn() -> Option<f32> + Send + Sync>;

/// A `TimeOfDayObserver`: the owner's connection and its time-of-day override.
#[derive(Clone)]
struct Observer {
    conn: ConnectionHandle,
    resolve: Option<TimeOfDayResolver>,
}

impl std::fmt::Debug for Observer {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Observer")
            .field("conn", &self.conn)
            .field("resolve", &self.resolve.is_some())
            .finish()
    }
}

#[derive(Debug)]
struct State {
    time: f32,
    /// Observers by subscription id (`IObserver<float>` of a character).
    observers: HashMap<u64, Observer>,
    next_id: u64,
    /// `!_work`.
    stopped: bool,
}

/// `TimeManager`.
#[derive(Debug, Clone)]
pub struct TimeManager {
    state: Arc<Mutex<State>>,
}

impl Default for TimeManager {
    fn default() -> Self {
        Self {
            state: Arc::new(Mutex::new(State {
                time: 12.0 * 3600.0,
                observers: HashMap::new(),
                next_id: 1,
                stopped: false,
            })),
        }
    }
}

impl TimeManager {
    /// `GetTime` (hours).
    pub fn get_time(&self) -> f32 {
        self.state.lock().expect("time poisoned").time / 3600.0
    }

    /// `Get()`: the game time in seconds.
    pub fn get(&self) -> f32 {
        self.state.lock().expect("time poisoned").time
    }

    /// `Set(hours)`.
    pub fn set(&self, hours: f32) {
        self.state.lock().expect("time poisoned").time = hours * 3600.0;
    }

    /// `Subscribe(connection, observer)`: sends `SCDetailedTimeOfDayPacket` and registers the
    /// observer; the returned id is the `IDisposable` to pass to [`TimeManager::unsubscribe`].
    pub fn subscribe(&self, conn: &ConnectionHandle) -> u64 {
        self.subscribe_with(conn, None)
    }

    /// [`TimeManager::subscribe`] with the observer's time-of-day override (`resolve`), asked on
    /// every push (`StormShipLogic.ResolveClientTimeOfDayHours(owner) ?? value`). The initial
    /// `SCDetailedTimeOfDayPacket` always carries the game time, as in the C#.
    pub fn subscribe_with(
        &self,
        conn: &ConnectionHandle,
        resolve: Option<TimeOfDayResolver>,
    ) -> u64 {
        conn.send(SCDetailedTimeOfDayPacket {
            time: self.get_time(),
        });
        let mut st = self.state.lock().expect("time poisoned");
        let id = st.next_id;
        st.next_id += 1;
        st.observers.insert(
            id,
            Observer {
                conn: conn.clone(),
                resolve,
            },
        );
        id
    }

    /// `Unsubscriber.Dispose`.
    pub fn unsubscribe(&self, id: u64) {
        self.state
            .lock()
            .expect("time poisoned")
            .observers
            .remove(&id);
    }

    /// One `Tick` + `Push`: advance the clock and notify every observer.
    pub fn tick(&self) {
        let (time, observers) = {
            let mut st = self.state.lock().expect("time poisoned");
            st.time += TICK_DELAY * 10.0;
            if st.time > MAX_TIME {
                st.time -= MAX_TIME;
            }
            (
                st.time / 3600.0,
                st.observers.values().cloned().collect::<Vec<_>>(),
            )
        };
        for o in observers {
            let tod = o.resolve.as_ref().and_then(|f| f()).unwrap_or(time);
            o.conn.send(SCTimeOfDayPacket { time: tod });
        }
    }

    /// `Start()`: the 10 s tick thread.
    pub fn start(&self) {
        let me = self.clone();
        me.state.lock().expect("time poisoned").stopped = false;
        tokio::spawn(async move {
            // C# loop: advance + push, then sleep 10 s (`while (_work)`).
            let mut iv = tokio::time::interval(Duration::from_secs(10));
            loop {
                iv.tick().await;
                if me.state.lock().expect("time poisoned").stopped {
                    break;
                }
                me.tick();
            }
        });
    }

    /// `Stop()`: ends the tick loop.
    pub fn stop(&self) {
        self.state.lock().expect("time poisoned").stopped = true;
    }
}

#[cfg(test)]
mod tests {
    use std::sync::Arc;

    use crate::data::GameData;
    use crate::opcodes::sc;
    use crate::systems::ai::physics::forces::STORM_BUFF_ID;
    use crate::systems::skills::data::BuffTemplate;
    use crate::test_support::*;
    use crate::world::WorldObject;

    fn pushed_times(client: &mut TestClient) -> Vec<f32> {
        client
            .drain()
            .into_iter()
            .filter(|(op, _)| *op == sc::SCTimeOfDayPacket)
            .map(|(_, b)| f32::from_le_bytes([b[0], b[1], b[2], b[3]]))
            .collect()
    }

    #[tokio::test]
    async fn resolver_overrides_the_pushed_time() {
        let server = test_server();
        let mut client = TestClient::new(&server, 1, 1);
        let handle = client.conn.handle();
        let plain = server.time.subscribe(&handle);
        let forced = server
            .time
            .subscribe_with(&handle, Some(Arc::new(|| Some(2.0))));
        let none = server.time.subscribe_with(&handle, Some(Arc::new(|| None)));
        // Subscribing sends the detailed (real) time to each observer.
        let detailed = client
            .drain()
            .into_iter()
            .filter(|(op, _)| *op == sc::SCDetailedTimeOfDayPacket)
            .count();
        assert_eq!(detailed, 3);
        server.time.tick();
        let now = server.time.get_time();
        let mut got = pushed_times(&mut client);
        got.sort_by(f32::total_cmp);
        let mut want = vec![now, now, 2.0];
        want.sort_by(f32::total_cmp);
        assert_eq!(got, want);
        for id in [plain, forced, none] {
            server.time.unsubscribe(id);
        }
        server.time.tick();
        assert!(pushed_times(&mut client).is_empty());
    }

    /// `TimeOfDayObserver.OnNext` with `StormShipLogic.ResolveClientTimeOfDayHours`: 2:00 only
    /// with the realistic sea weather model and the storm buff.
    #[tokio::test]
    async fn storm_buff_forces_night_with_realistic_sea_weather() {
        let mut data = GameData::empty();
        data.systems.skills.add_buff(BuffTemplate {
            id: STORM_BUFF_ID,
            kind: 2,
            duration: 0,
            ..BuffTemplate::default()
        });
        let server = Arc::new(test_server_with(data));
        server.set_self();
        crate::systems::skills::bind(&server);
        let mut client = TestClient::new(&server, 1, 1);
        let p = spawn_player(
            &server,
            &client,
            test_character(1, "Sailor", 10.0, 10.0),
            0x100,
        );
        let resolver = crate::world::session::storm_time_of_day_resolver(&server, &p);
        let sub = server.time.subscribe_with(&p.conn, Some(resolver));
        client.drain();

        // Official model: the game time.
        server.time.tick();
        assert_eq!(pushed_times(&mut client), vec![server.time.get_time()]);
        // Realistic, no storm buff: the game time.
        server.config.update(|c| {
            c.world.sea_weather_model = crate::config::SeaWeatherModelType::Realistic;
        });
        server.time.tick();
        assert_eq!(pushed_times(&mut client), vec![server.time.get_time()]);
        // Realistic + storm buff: 2:00.
        let obj = WorldObject::from(&p);
        crate::systems::skills::buffs::add_buff_by_id(&server, &obj, STORM_BUFF_ID, &obj, 0);
        assert!(p.data().unit.buffs.check_buff(STORM_BUFF_ID));
        client.drain();
        server.time.tick();
        assert_eq!(pushed_times(&mut client), vec![2.0]);
        // Back to official: the game time even with the buff.
        server.config.update(|c| {
            c.world.sea_weather_model = crate::config::SeaWeatherModelType::Official;
        });
        server.time.tick();
        assert_eq!(pushed_times(&mut client), vec![server.time.get_time()]);
        server.time.unsubscribe(sub);
    }
}
