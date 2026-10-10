//! `Models/Tasks/ShutdownTask` (the delayed server shutdown with its count-down notices,
//! started by the GM `Scripts` command and held in `SaveManager.ShutdownTask`) and
//! `SCNoticeMessagePacket`.

use std::sync::{Arc, Mutex, Weak};

use aaemu_commons::{Marshal, PacketStream};
use chrono::{NaiveDateTime, TimeDelta, Utc};

use super::commands::output::Color;
use super::tasks::{GameTask, TaskState};
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::systems::HookSlot;

/// `SCNoticeMessagePacket(type, color, visTime, message)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCNoticeMessagePacket {
    pub kind: u8,
    /// `ARGBColor.A.ToString("X2")`.
    pub alpha_hex: String,
    pub vis_time: i32,
    /// `RRGGBB` + message.
    pub text: String,
}
packet_def!(
    SCNoticeMessagePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCNoticeMessagePacket
);

impl SCNoticeMessagePacket {
    /// The C# constructor: opacity 0 becomes 0xFF, a non-positive visible time becomes
    /// `1000 + 50 ms per character`.
    pub fn new(kind: u8, color: Color, vis_time: i32, message: &str) -> Self {
        let a = if color.a == 0 { 0xFF } else { color.a };
        let vis_time = if vis_time <= 0 {
            1000 + message.encode_utf16().count() as i32 * 50
        } else {
            vis_time
        };
        Self {
            kind,
            alpha_hex: format!("{a:02X}"),
            vis_time,
            text: format!("{:02X}{:02X}{:02X}{message}", color.r, color.g, color.b),
        }
    }
}

impl Marshal for SCNoticeMessagePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.kind)
            .write_str(&self.alpha_hex)
            .write_i32(self.vis_time)
            .write_str(&self.text);
    }
}

/// `Color.Magenta`.
const MAGENTA: Color = Color::argb(255, 255, 0, 255);

/// `_triggerPoints` (seconds before the shutdown with a notice).
const TRIGGER_POINTS: [u32; 19] = [
    14400, 10800, 7200, 3600, 2700, 1800, 1200, 900, 600, 300, 240, 180, 120, 90, 60, 45, 30, 20,
    10,
];

/// `character.BuyBackItems.Wipe()` for every online character (economy).
pub type BuyBackWipe = dyn Fn(&GameServer) + Send + Sync;

/// Wipes the buy-back containers of the online characters before the final save (economy).
pub static BUYBACK_WIPE: std::sync::LazyLock<HookSlot<BuyBackWipe>> =
    std::sync::LazyLock::new(HookSlot::default);

/// What the process does when the shutdown time is reached (`Environment.Exit`); replaceable in
/// tests.
pub type ExitFn = Arc<dyn Fn(i32) + Send + Sync>;

struct ShutdownState {
    already_shutting_down: bool,
    shutdown_time: NaiveDateTime,
    next_trigger_time: NaiveDateTime,
}

/// `ShutdownTask`.
pub struct ShutdownTask {
    state: TaskState,
    server: Weak<GameServer>,
    inner: Mutex<ShutdownState>,
    exit_code: i32,
    exit: ExitFn,
}

fn now() -> NaiveDateTime {
    Utc::now().naive_utc()
}

/// `DateTime.MaxValue`.
fn max_value() -> NaiveDateTime {
    NaiveDateTime::MAX
}

/// `CalculateLargestNextTrigger()`.
fn largest_next_trigger(shutdown_time: NaiveDateTime, now: NaiveDateTime) -> NaiveDateTime {
    let remaining = (shutdown_time - now).num_milliseconds() as f64 / 1000.0;
    let remaining = remaining.ceil();
    for t in TRIGGER_POINTS {
        if remaining > f64::from(t) {
            return shutdown_time - TimeDelta::seconds(i64::from(t));
        }
    }
    max_value()
}

/// The notice text and popup time for `remaining_seconds`.
pub fn countdown_notice(remaining_seconds: i32) -> (String, i32) {
    let show_seconds = remaining_seconds <= 100;
    let show_minutes = remaining_seconds <= 3600 && !show_seconds;
    let show_hours = remaining_seconds > 3600 && !show_seconds;
    if show_hours {
        (
            format!(
                "The server is shutting down in {} hours!",
                remaining_seconds / 3600
            ),
            10000,
        )
    } else if show_minutes {
        (
            format!(
                "The server is shutting down in {} minutes!",
                remaining_seconds / 60
            ),
            6000,
        )
    } else if show_seconds {
        (
            format!("The server is shutting down in {remaining_seconds} seconds!"),
            3000,
        )
    } else {
        ("The server is shutting down soon!".into(), 3000)
    }
}

impl std::fmt::Debug for ShutdownTask {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let st = self.inner.lock().expect("shutdown poisoned");
        f.debug_struct("ShutdownTask")
            .field("shutdown_time", &st.shutdown_time)
            .field("exit_code", &self.exit_code)
            .finish()
    }
}

impl ShutdownTask {
    /// `new ShutdownTask(shutdownTime, exitCode)`.
    pub fn new(
        server: &Arc<GameServer>,
        shutdown_time: NaiveDateTime,
        exit_code: i32,
    ) -> Arc<Self> {
        Self::with_exit(
            server,
            shutdown_time,
            exit_code,
            Arc::new(|code| std::process::exit(code)),
        )
    }

    pub fn with_exit(
        server: &Arc<GameServer>,
        shutdown_time: NaiveDateTime,
        exit_code: i32,
        exit: ExitFn,
    ) -> Arc<Self> {
        Arc::new(Self {
            state: TaskState::new("ShutdownTask"),
            server: Arc::downgrade(server),
            inner: Mutex::new(ShutdownState {
                already_shutting_down: false,
                shutdown_time,
                next_trigger_time: largest_next_trigger(shutdown_time, now()),
            }),
            exit_code,
            exit,
        })
    }

    /// `ChangeShutdownTime(shutdownTime)`.
    pub fn change_shutdown_time(&self, shutdown_time: NaiveDateTime) {
        let remaining = {
            let mut st = self.inner.lock().expect("shutdown poisoned");
            st.shutdown_time = shutdown_time;
            st.next_trigger_time = largest_next_trigger(shutdown_time, now());
            ((shutdown_time - now()).num_milliseconds() as f64 / 60_000.0).ceil() as i32
        };
        if let Some(server) = self.server.upgrade() {
            server
                .world
                .broadcast_to_server(&SCNoticeMessagePacket::new(
                    3,
                    Color::RED,
                    10000,
                    &format!(
                        "Server shutdown has been rescheduled to {remaining} minutes from now!"
                    ),
                ));
        }
    }

    /// One `Execute()` (async: the final save).
    pub async fn run(&self, server: &GameServer) {
        let (due, remaining_seconds, notify) = {
            let mut st = self.inner.lock().expect("shutdown poisoned");
            if st.already_shutting_down {
                return;
            }
            let now = now();
            if st.shutdown_time <= now {
                st.already_shutting_down = true;
                (true, 0, false)
            } else {
                let remaining =
                    ((st.shutdown_time - now).num_milliseconds() as f64 / 1000.0).ceil() as i32;
                if now < st.next_trigger_time {
                    (false, remaining, false)
                } else {
                    st.next_trigger_time = largest_next_trigger(st.shutdown_time, now);
                    (false, remaining, true)
                }
            }
        };
        if due {
            if let Some(wipe) = BUYBACK_WIPE.get() {
                wipe(server);
            }
            if server.save.do_save(server).await {
                server
                    .world
                    .broadcast_to_server(&SCNoticeMessagePacket::new(
                        3,
                        MAGENTA,
                        15000,
                        "The server is shutting down right now!",
                    ));
                (self.exit)(self.exit_code);
            }
            return;
        }
        if notify {
            let (text, popup) = countdown_notice(remaining_seconds);
            server
                .world
                .broadcast_to_server(&SCNoticeMessagePacket::new(3, Color::RED, popup, &text));
        }
    }
}

impl GameTask for ShutdownTask {
    fn state(&self) -> &TaskState {
        &self.state
    }

    fn execute(&self) {}

    fn execute_async(self: Arc<Self>) -> futures::future::BoxFuture<'static, ()> {
        Box::pin(async move {
            if let Some(server) = self.server.upgrade() {
                self.run(&server).await;
            }
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn notice_packet() {
        let p = SCNoticeMessagePacket::new(3, Color::argb(0, 255, 0, 0), 0, "abc");
        assert_eq!(p.alpha_hex, "FF");
        assert_eq!(p.vis_time, 1150);
        assert_eq!(p.text, "FF0000abc");
        let mut e = PacketStream::new();
        e.write_u8(3)
            .write_str("FF")
            .write_i32(1150)
            .write_str("FF0000abc");
        assert_eq!(crate::net::packet::encode(&p)[6..].to_vec(), e.into_bytes());
    }

    #[test]
    fn countdown() {
        assert_eq!(
            countdown_notice(7300),
            ("The server is shutting down in 2 hours!".into(), 10000)
        );
        assert_eq!(
            countdown_notice(600),
            ("The server is shutting down in 10 minutes!".into(), 6000)
        );
        assert_eq!(
            countdown_notice(30),
            ("The server is shutting down in 30 seconds!".into(), 3000)
        );
        let base = NaiveDateTime::default();
        let shutdown = base + TimeDelta::seconds(700);
        assert_eq!(
            largest_next_trigger(shutdown, base),
            shutdown - TimeDelta::seconds(600)
        );
        assert_eq!(largest_next_trigger(shutdown, shutdown), max_value());
    }

    #[tokio::test]
    async fn notices_then_exit() {
        let server = Arc::new(crate::test_support::test_server());
        let exited = Arc::new(Mutex::new(None));
        let e = exited.clone();
        let task = ShutdownTask::with_exit(
            &server,
            now() - TimeDelta::seconds(1),
            -1,
            Arc::new(move |c| *e.lock().unwrap() = Some(c)),
        );
        task.run(&server).await;
        // The final save fails without MySQL: no exit (C# `if (DoSave())`).
        assert_eq!(*exited.lock().unwrap(), None);
    }
}
