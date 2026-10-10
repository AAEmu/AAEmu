//! `Core/Managers/TimeManager` + `TimeOfDayObserver`: the game clock (starts at 12:00, advances
//! `TickDelay * 10` game seconds every 10 real seconds) and the per-character time-of-day
//! subscriptions. The storm-ship time override (`StormShipLogic`) is a later phase.

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use crate::net::connection::ConnectionHandle;
use crate::packets::world::{SCDetailedTimeOfDayPacket, SCTimeOfDayPacket};

const MAX_TIME: f32 = 86_400.0;
const SPEED: f32 = 0.001_666_6;
const TICK_DELAY: f32 = 3600.0 * SPEED;

#[derive(Debug)]
struct State {
    time: f32,
    /// Observers by subscription id (`IObserver<float>` of a character).
    observers: HashMap<u64, ConnectionHandle>,
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
        conn.send(SCDetailedTimeOfDayPacket {
            time: self.get_time(),
        });
        let mut st = self.state.lock().expect("time poisoned");
        let id = st.next_id;
        st.next_id += 1;
        st.observers.insert(id, conn.clone());
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
            o.send(SCTimeOfDayPacket { time });
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
