//! `Models/Tasks/ShutdownTask` (the delayed server shutdown with its count-down notices,
//! started by the GM `Scripts` command and held in `SaveManager.ShutdownTask`) and
//! `SCNoticeMessagePacket`.

use std::sync::{Arc, Mutex, Weak};
use std::time::Duration;

use aaemu_commons::{Marshal, PacketStream};
use chrono::{NaiveDateTime, TimeDelta, Utc};

use super::commands::output::Color;
use super::tasks::{GameTask, TaskState};
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::g2c_misc::{kicked_reason, SCKickedPacket};
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

/// The notice broadcast at T-0, before the players are returned to the world list.
pub const FINAL_NOTICE: &str = "The server is shutting down for maintenance.";
/// The `SCKicked` text (reason `KickByMaintenance`) for what is still connected after the grace
/// period, and for entering the world during the shutdown.
pub const KICK_MESSAGE: &str = "Server maintenance";
/// How long the clients get to leave the game server for the world list. The client needs about
/// 2 s to switch to character select and load the lobby before it can act on `SCReconnectAuth`,
/// plus the login round trip.
pub const DISCONNECT_WAIT: Duration = Duration::from_secs(10);
/// Pause before the world list request is (re)sent to the connections that are still there: the
/// in-world leave only takes the client to character select, so the request follows once it has
/// had time to load the lobby.
pub const RECONNECT_INTERVAL: Duration = Duration::from_secs(2);
/// How long the kicked connections get to flush `SCKicked` and run their disconnect save.
pub const KICK_FLUSH_WAIT: Duration = Duration::from_secs(2);
/// Poll interval of the connection-count waits.
pub const WAIT_POLL: Duration = Duration::from_millis(100);
/// Tries of the final `DoSave` (it fails while another save runs).
pub const SAVE_ATTEMPTS: u32 = 3;
/// Pause between two final save tries.
pub const SAVE_RETRY_DELAY: Duration = Duration::from_secs(1);

/// The timings of the graceful part of the shutdown ([`graceful_return_players`]).
#[derive(Debug, Clone, Copy)]
pub struct GracefulTimings {
    pub disconnect_wait: Duration,
    pub reconnect_interval: Duration,
    pub kick_flush_wait: Duration,
    pub poll: Duration,
    pub save_attempts: u32,
    pub save_retry_delay: Duration,
}

impl Default for GracefulTimings {
    fn default() -> Self {
        Self {
            disconnect_wait: DISCONNECT_WAIT,
            reconnect_interval: RECONNECT_INTERVAL,
            kick_flush_wait: KICK_FLUSH_WAIT,
            poll: WAIT_POLL,
            save_attempts: SAVE_ATTEMPTS,
            save_retry_delay: SAVE_RETRY_DELAY,
        }
    }
}

/// What [`begin_graceful`] asked the connections to do.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ReturnSummary {
    /// Connections with a character in the world (they leave it first).
    pub in_world: usize,
    /// Authenticated connections sent to the world list (`GLPlayerReconnect`; for the ones that
    /// were in the world after the first [`RECONNECT_INTERVAL`]).
    pub returned: usize,
    /// Connections without an account (they are only closed by the kick step).
    pub unauthenticated: usize,
}

/// Phase 1: stop accepting world entries, broadcast the final notice and ask every connection to
/// leave the world and return to the world list (`ConnCommand::ReturnToWorldList`, run on each
/// connection's own task by `session::return_to_world_list`).
pub fn begin_graceful(server: &GameServer) -> ReturnSummary {
    server.set_shutting_down(true);
    server
        .world
        .broadcast_to_server(&SCNoticeMessagePacket::new(3, MAGENTA, 15000, FINAL_NOTICE));
    let handles = server.connections.all();
    let in_world = server.world.all_characters().len();
    let mut returned = 0;
    for handle in &handles {
        if handle.account_id() != 0 {
            returned += 1;
        }
        handle.return_to_world_list();
    }
    let summary = ReturnSummary {
        in_world,
        returned,
        unauthenticated: handles.len() - returned,
    };
    tracing::info!(
        "Shutdown: returning {} players to the world list ({} in the world, {} unauthenticated connections)",
        summary.returned,
        summary.in_world,
        summary.unauthenticated
    );
    summary
}

/// Phase 2: waits until no game connection remains or `timeout` passed; returns how many remain.
/// With `rerequest_every`, every connection still there gets `ConnCommand::ReturnToWorldList`
/// again at that interval (the world list request of the ones that just left the world, and a
/// retry for the others; also reaches connections that appeared during the shutdown).
pub async fn wait_for_disconnects(
    server: &GameServer,
    timeout: Duration,
    poll: Duration,
    rerequest_every: Option<Duration>,
) -> usize {
    let start = tokio::time::Instant::now();
    let deadline = start + timeout;
    let mut next_request = rerequest_every.map(|every| start + every);
    loop {
        let remaining = server.connections.len();
        let now = tokio::time::Instant::now();
        if remaining == 0 || now >= deadline {
            return remaining;
        }
        if let (Some(at), Some(every)) = (next_request, rerequest_every) {
            if now >= at {
                for handle in server.connections.all() {
                    handle.return_to_world_list();
                }
                next_request = Some(at + every);
            }
        }
        tokio::time::sleep(poll).await;
    }
}

/// Phase 3: `SCKicked(KickByMaintenance)` and close for every connection that is still there.
pub fn kick_remaining(server: &GameServer) -> usize {
    let handles = server.connections.all();
    for handle in &handles {
        handle.send(SCKickedPacket {
            reason: kicked_reason::KICK_BY_MAINTENANCE,
            msg: KICK_MESSAGE.into(),
        });
        handle.close();
    }
    handles.len()
}

/// Phases 1-3: everything up to the final save. Returns the number of connections that had to be
/// kicked.
pub async fn graceful_return_players(server: &GameServer, timings: &GracefulTimings) -> usize {
    let summary = begin_graceful(server);
    let remaining = wait_for_disconnects(
        server,
        timings.disconnect_wait,
        timings.poll,
        Some(timings.reconnect_interval),
    )
    .await;
    tracing::info!(
        "Shutdown: returned {} players to the world list, {remaining} connections remain",
        summary.returned
    );
    if remaining == 0 {
        return 0;
    }
    let kicked = kick_remaining(server);
    let left = wait_for_disconnects(server, timings.kick_flush_wait, timings.poll, None).await;
    tracing::info!("Shutdown: kicked {kicked} connections for maintenance, {left} still open");
    kicked
}

/// Stops the server's services in the order `main` shuts down on Ctrl+C: hosted services, the
/// final save, then the task / tick / time managers. The caller exits the process afterwards
/// (the network listeners are tokio tasks that die with it).
pub async fn stop_server(server: &GameServer) {
    server.systems.services.stop_hosted_services().await;
    tracing::info!("Stopping daemon...");
    // SaveManager.StopAsync: one final save.
    server.save.stop(server).await;
    server.systems.services.tasks.stop();
    server.systems.services.tick.stop();
    server.time.stop();
}

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
    timings: GracefulTimings,
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
        Self::with_timings(
            server,
            shutdown_time,
            exit_code,
            exit,
            GracefulTimings::default(),
        )
    }

    pub fn with_timings(
        server: &Arc<GameServer>,
        shutdown_time: NaiveDateTime,
        exit_code: i32,
        exit: ExitFn,
        timings: GracefulTimings,
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
            timings,
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

    /// T-0: return the players to the world list, then the final save, the normal stop and the
    /// exit. Deviation: the C# saved and exited without telling the clients (they showed a
    /// disconnect error). If the final save fails nothing exits (as in the C#, which then never
    /// retried) and the server accepts world entries again.
    async fn finish(&self, server: &GameServer) {
        graceful_return_players(server, &self.timings).await;
        if let Some(wipe) = BUYBACK_WIPE.get() {
            wipe(server);
        }
        let mut saved = false;
        for attempt in 1..=self.timings.save_attempts {
            if server.save.do_save(server).await {
                saved = true;
                break;
            }
            tracing::warn!(
                "Shutdown: final save failed (attempt {attempt}/{})",
                self.timings.save_attempts
            );
            if attempt < self.timings.save_attempts {
                tokio::time::sleep(self.timings.save_retry_delay).await;
            }
        }
        if !saved {
            tracing::error!("Shutdown aborted: the final save failed");
            server.set_shutting_down(false);
            return;
        }
        tracing::info!("Shutdown: final save done, stopping the server");
        stop_server(server).await;
        (self.exit)(self.exit_code);
    }

    /// One `Execute()` (async: the graceful part and the final save).
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
            self.finish(server).await;
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

    use crate::net::connection::{ConnCommand, GameState};
    use crate::opcodes::{pp, sc};
    use crate::test_support::{spawn_player, test_character, test_server, TestClient};
    use crate::world::session;

    /// Short waits for tests (the real ones are seconds).
    fn fast() -> GracefulTimings {
        GracefulTimings {
            disconnect_wait: Duration::from_millis(60),
            reconnect_interval: Duration::from_millis(20),
            kick_flush_wait: Duration::from_millis(30),
            poll: Duration::from_millis(5),
            save_attempts: 2,
            save_retry_delay: Duration::from_millis(5),
        }
    }

    fn reconnect_packet(account: u32, conn: u32) -> Vec<u8> {
        let mut v = vec![0x02, 0x00, 0x01];
        v.extend_from_slice(&account.to_le_bytes());
        v.extend_from_slice(&conn.to_le_bytes());
        v
    }

    fn kicked_body() -> Vec<u8> {
        let mut s = PacketStream::new();
        s.write_u8(kicked_reason::KICK_BY_MAINTENANCE)
            .write_str(KICK_MESSAGE);
        s.into_bytes()
    }

    /// A player in the world on its own connection (`id` is both connection and character id).
    fn in_world_client(server: &GameServer, id: u32) -> (TestClient, crate::world::PlayerRef) {
        let mut client = TestClient::new(server, id, 1000 + id);
        let p = spawn_player(
            server,
            &client,
            test_character(id, &format!("P{id}"), 100.0, 100.0),
            0x100 + id,
        );
        client.conn.active_char = Some(p.clone());
        client.conn.state = GameState::World;
        client.drain();
        (client, p)
    }

    /// Runs the queued `ConnCommand::ReturnToWorldList` the way the connection task does.
    async fn pump(server: &GameServer, client: &mut TestClient) -> Vec<bool> {
        let mut ran = Vec::new();
        for cmd in client.commands() {
            match cmd {
                ConnCommand::ReturnToWorldList => {
                    ran.push(session::return_to_world_list(server, &mut client.conn).await);
                }
                other => panic!("unexpected command {other:?}"),
            }
        }
        ran
    }

    #[tokio::test]
    async fn graceful_sequence_per_connection() {
        let server = Arc::new(test_server());
        let (login_tx, mut login_rx) = tokio::sync::mpsc::unbounded_channel();
        server.login.set(Some(login_tx));
        let (mut a, pa) = in_world_client(&server, 77);
        // A lobby client (authenticated, no character) and one that never authenticated.
        let mut lobby = TestClient::new(&server, 78, 1078);
        let mut anon = TestClient::new(&server, 79, 0);

        let summary = begin_graceful(&server);
        assert!(server.is_shutting_down());
        assert_eq!(
            summary,
            ReturnSummary {
                in_world: 1,
                returned: 2,
                unauthenticated: 1
            }
        );
        // The final notice reaches the player in the world (the lobby clients get none).
        assert_eq!(a.drain_ops(), vec![sc::SCNoticeMessagePacket]);
        assert!(lobby.drain().is_empty());

        // In-world client: leave at once. The world list request waits until the client had
        // time to load the lobby (the first re-request round).
        assert_eq!(pump(&server, &mut a).await, vec![true]);
        let frames = a.drain();
        let tail = &frames[frames.len() - 2..];
        assert_eq!(tail[0], (sc::SCLeaveWorldGrantedPacket, vec![1]));
        assert_eq!(tail[1], (0x8000 | pp::ChangeStatePacket, vec![0, 0, 0, 0]));
        assert_eq!(a.conn.state, GameState::Lobby);
        assert!(server.world.character_by_obj_id(pa.obj_id).is_none());
        assert!(a.conn.leave_task.is_none());
        assert!(login_rx.try_recv().is_err());

        // Nobody disconnected yet: the wait re-requests on every connection and times out with
        // all three still there.
        assert_eq!(
            wait_for_disconnects(
                &server,
                Duration::from_millis(40),
                Duration::from_millis(5),
                Some(Duration::from_millis(10))
            )
            .await,
            3
        );
        for client in [&mut a, &mut lobby, &mut anon] {
            assert!(client
                .commands()
                .iter()
                .any(|c| matches!(c, ConnCommand::ReturnToWorldList)));
        }
        // Re-request on the client that left: the world list request to the login server ...
        assert!(!session::return_to_world_list(&server, &mut a.conn).await);
        assert_eq!(login_rx.try_recv().unwrap(), reconnect_packet(1077, 77));
        // ... which answers LGPlayerReconnect -> SCReconnectAuth(connection id).
        assert!(crate::login::handle(&server, &[0x02, 0x00, 77, 0, 0, 0]).await);
        assert_eq!(
            a.drain(),
            vec![(sc::SCReconnectAuthPacket, 77u32.to_le_bytes().to_vec())]
        );

        // Lobby client: no leave, only the world list request.
        assert!(!session::return_to_world_list(&server, &mut lobby.conn).await);
        assert!(lobby.drain().is_empty());
        assert_eq!(login_rx.try_recv().unwrap(), reconnect_packet(1078, 78));

        // Unauthenticated connection: nothing to ask the login server.
        assert!(!session::return_to_world_list(&server, &mut anon.conn).await);
        assert!(login_rx.try_recv().is_err());

        // The clients close their game connections on their way to the world list.
        server.connections.remove(77);
        server.connections.remove(78);
        assert_eq!(
            wait_for_disconnects(
                &server,
                Duration::from_millis(20),
                Duration::from_millis(5),
                None
            )
            .await,
            1
        );
        // What remains is kicked for maintenance and closed.
        assert_eq!(kick_remaining(&server), 1);
        assert_eq!(anon.drain(), vec![(sc::SCKickedPacket, kicked_body())]);
        assert!(matches!(anon.commands().as_slice(), [ConnCommand::Close]));
    }

    #[tokio::test]
    async fn a_lobby_connection_appearing_during_the_shutdown_gets_the_world_list() {
        let server = Arc::new(test_server());
        let (login_tx, mut login_rx) = tokio::sync::mpsc::unbounded_channel();
        server.login.set(Some(login_tx));
        begin_graceful(&server);
        // A client that authenticated after T-0 (the lobby is not refused once connected).
        let mut late = TestClient::new(&server, 90, 1090);
        late.conn.state = GameState::Lobby;
        wait_for_disconnects(
            &server,
            Duration::from_millis(30),
            Duration::from_millis(5),
            Some(Duration::from_millis(10)),
        )
        .await;
        assert_eq!(pump_first(&server, &mut late).await, Some(false));
        assert_eq!(login_rx.try_recv().unwrap(), reconnect_packet(1090, 90));
        assert!(crate::login::handle(&server, &[0x02, 0x00, 90, 0, 0, 0]).await);
        assert_eq!(
            late.drain(),
            vec![(sc::SCReconnectAuthPacket, 90u32.to_le_bytes().to_vec())]
        );
    }

    /// Runs the first queued `ReturnToWorldList` only (the rest are re-requests).
    async fn pump_first(server: &GameServer, client: &mut TestClient) -> Option<bool> {
        let first = client.commands().into_iter().next()?;
        assert!(matches!(first, ConnCommand::ReturnToWorldList));
        Some(session::return_to_world_list(server, &mut client.conn).await)
    }

    #[tokio::test]
    async fn graceful_return_players_kicks_the_stragglers() {
        let server = test_server();
        let (login_tx, _login_rx) = tokio::sync::mpsc::unbounded_channel();
        server.login.set(Some(login_tx));
        let (mut a, _) = in_world_client(&server, 77);
        // The client does not leave on its own: after the grace period it is kicked.
        assert_eq!(graceful_return_players(&server, &fast()).await, 1);
        assert_eq!(
            a.drain_ops(),
            vec![sc::SCNoticeMessagePacket, sc::SCKickedPacket]
        );
        let cmds = a.commands();
        // The request at T-0, the re-requests while waiting, then the close.
        assert!(cmds.len() >= 3 && matches!(cmds.last(), Some(ConnCommand::Close)));
        assert!(cmds[..cmds.len() - 1]
            .iter()
            .all(|c| matches!(c, ConnCommand::ReturnToWorldList)));
        // A server nobody is connected to has nothing to kick.
        server.connections.remove(77);
        assert_eq!(graceful_return_players(&server, &fast()).await, 0);
    }

    #[tokio::test]
    async fn entering_the_world_is_refused_during_shutdown() {
        let server = test_server();
        server.set_shutting_down(true);

        // X2EnterWorld (the world-list click on this server).
        let mut c = TestClient::new(&server, 80, 0);
        crate::packets::c2g::enter_world_login(&server, &mut c.conn, 1080, 5)
            .await
            .unwrap();
        assert_eq!(c.drain(), vec![(sc::SCKickedPacket, kicked_body())]);
        assert!(c.conn.is_closing());
        assert_eq!(c.conn.account_id(), 0);

        // CSSelectCharacter in the lobby.
        let mut c = TestClient::new(&server, 81, 1081);
        session::select_character(&server, &mut c.conn, 1)
            .await
            .unwrap();
        assert_eq!(c.drain(), vec![(sc::SCKickedPacket, kicked_body())]);
        assert!(c.conn.is_closing());

        // CSSpawnCharacter with a character selected before the shutdown began.
        let (mut c, _) = in_world_client(&server, 82);
        session::spawn_character(&server, &mut c.conn, Default::default());
        assert_eq!(c.drain(), vec![(sc::SCKickedPacket, kicked_body())]);
        assert!(c.conn.is_closing());

        // Once the shutdown is over (aborted), entering works again.
        server.set_shutting_down(false);
        let mut c = TestClient::new(&server, 83, 0);
        crate::packets::c2g::enter_world_login(&server, &mut c.conn, 1083, 5)
            .await
            .unwrap();
        assert!(!c.drain_ops().contains(&sc::SCKickedPacket));
    }

    #[tokio::test]
    async fn a_due_task_returns_players_then_aborts_without_a_save() {
        let server = Arc::new(test_server());
        let (login_tx, mut login_rx) = tokio::sync::mpsc::unbounded_channel();
        server.login.set(Some(login_tx));
        let (mut a, _) = in_world_client(&server, 77);
        let exited = Arc::new(Mutex::new(None));
        let e = exited.clone();
        let task = ShutdownTask::with_timings(
            &server,
            now() - TimeDelta::seconds(1),
            -1,
            Arc::new(move |c| *e.lock().unwrap() = Some(c)),
            // The leave saves against a closed MySQL port first: allow for its connect timeout.
            GracefulTimings {
                disconnect_wait: Duration::from_secs(10),
                reconnect_interval: Duration::from_millis(500),
                ..fast()
            },
        );
        // Runs on its own task, like the TaskManager does; the test plays the connection task.
        let runner = {
            let (task, server) = (task.clone(), server.clone());
            tokio::spawn(async move { task.run(&server).await })
        };
        // The connection task's side: the leave when the first command arrives, the world list
        // request on the re-request round after it.
        let mut left = false;
        for _ in 0..2000 {
            let cmds = a.commands();
            assert!(cmds
                .iter()
                .all(|c| matches!(c, ConnCommand::ReturnToWorldList)));
            if !cmds.is_empty() {
                let was_in_world = session::return_to_world_list(&server, &mut a.conn).await;
                if left {
                    assert!(!was_in_world);
                    break;
                }
                assert!(was_in_world);
                left = true;
            }
            tokio::time::sleep(Duration::from_millis(5)).await;
        }
        assert!(left);
        server.connections.remove(77); // the client closed its game connection
        runner.await.unwrap();
        // The notice, the leave, then the world list request (and no kick: it disconnected).
        // (The reconnect auth is the login server's answer, not part of this test.)
        let ops = a.drain_ops();
        assert_eq!(ops.first(), Some(&sc::SCNoticeMessagePacket));
        assert!(ops.contains(&sc::SCLeaveWorldGrantedPacket));
        assert!(!ops.contains(&sc::SCKickedPacket));
        assert_eq!(login_rx.try_recv().unwrap(), reconnect_packet(1077, 77));
        // The final save fails without MySQL: no exit (C# `if (DoSave())`), world entry reopens.
        assert_eq!(*exited.lock().unwrap(), None);
        assert!(!server.is_shutting_down());
        // Executing the task again does nothing (`_alreadyShuttingDown`).
        task.run(&server).await;
        assert!(a.drain().is_empty());
    }
}
