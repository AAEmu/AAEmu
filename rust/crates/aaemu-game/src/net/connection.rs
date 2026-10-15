//! Per-connection state (`GameConnection`) and the connection table (`GameConnectionTable`).
//!
//! Each client connection is driven by one tokio task that owns its [`GameConnection`] (so its
//! state needs no locking) plus a writer task fed through its [`Outbox`]. Other tasks (e.g. the
//! login-server link) reach a connection through its [`ConnectionHandle`].

use std::net::SocketAddr;
use std::sync::atomic::{AtomicI64, AtomicU32, Ordering};
use std::sync::Arc;
use std::time::Instant;

use bytes::Bytes;
use dashmap::DashMap;
use tokio::sync::mpsc;

use crate::models::character::Character;
use crate::models::item::UnixTime;
use crate::net::outbox::{outbox, Outbox, Push, MAX_QUEUED_BYTES};
use crate::net::packet::{encode, ServerPacket, INVALID_OPCODE};

/// `GameState`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum GameState {
    #[default]
    Lobby,
    World,
}

/// `PaymentMethodType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(i32)]
pub enum PaymentMethodType {
    Premium = 1,
    Demo = 3,
    None = 5,
}

/// `AccountPayment` with its C# defaults (TODO in C#: load real payment info).
#[derive(Debug, Clone)]
pub struct AccountPayment {
    pub method: PaymentMethodType,
    pub location: i32,
    /// `DateTime.MinValue` -> 0.
    pub start_time: UnixTime,
    /// `new DateTime(2030, 1, 1)`.
    pub end_time: UnixTime,
}

impl AccountPayment {
    /// `PremiumState`: premium method and `now` inside `[StartTime, EndTime]` (unix seconds).
    pub fn premium_state(&self, now: UnixTime) -> bool {
        self.method == PaymentMethodType::Premium && now >= self.start_time && now <= self.end_time
    }
}

impl Default for AccountPayment {
    fn default() -> Self {
        Self {
            method: PaymentMethodType::Premium,
            location: 1,
            start_time: 0,
            end_time: 1_893_456_000,
        }
    }
}

/// Commands other tasks can send to a connection task.
#[derive(Debug)]
pub enum ConnCommand {
    Send(Box<dyn ServerPacket>),
    Close,
    /// The logout timer of `EnterWorldManager.Leave` expired: run `LeaveWorldTask` with this
    /// `LeaveWorldTargetType` on the connection task.
    LeaveWorld(u8),
    /// `Connection.ActiveChar = null; Shutdown()` (GM `disconnectme`): the connection closes
    /// without saving / removing its character.
    DetachAndClose,
    /// Graceful server shutdown: leave the world at once (no logout timer) if in it, then ask
    /// the login server for the world list (`session::return_to_world_list`).
    ReturnToWorldList,
}

/// Cloneable handle stored in the [`ConnectionTable`].
#[derive(Debug, Clone)]
pub struct ConnectionHandle {
    pub id: u32,
    pub ip: SocketAddr,
    account_id: Arc<AtomicU32>,
    /// `LastPing` as unix milliseconds (`i64::MIN` = `DateTime.MinValue`, never pinged).
    last_ping_ms: Arc<AtomicI64>,
    tx: mpsc::UnboundedSender<ConnCommand>,
    writer: Outbox,
}

impl ConnectionHandle {
    /// A handle whose packets go nowhere (C# `Character.Connection == null`): used for
    /// characters that exist before anyone plays them (character creation).
    pub fn detached(id: u32, ip: SocketAddr, account_id: u32) -> Self {
        let (tx, _) = mpsc::unbounded_channel();
        let (writer, _) = outbox();
        Self {
            id,
            ip,
            account_id: Arc::new(AtomicU32::new(account_id)),
            last_ping_ms: Arc::new(AtomicI64::new(i64::MIN)),
            tx,
            writer,
        }
    }

    pub fn account_id(&self) -> u32 {
        self.account_id.load(Ordering::Relaxed)
    }

    /// `LastPing` (`None` = `DateTime.MinValue`: no `PingPacket` received yet).
    pub fn last_ping(&self) -> Option<chrono::NaiveDateTime> {
        let ms = self.last_ping_ms.load(Ordering::Relaxed);
        (ms != i64::MIN)
            .then(|| chrono::DateTime::from_timestamp_millis(ms).map(|d| d.naive_utc()))
            .flatten()
    }

    /// `GameConnection.SendPacket` from any task: the packet is encoded right away and queued
    /// on the connection's writer, so packets sent through handles and through the owning
    /// [`GameConnection`] keep the global call order (C# writes to the socket synchronously).
    pub fn send(&self, packet: impl ServerPacket + 'static) {
        self.send_dyn(&packet);
    }

    /// [`ConnectionHandle::send`] for a borrowed / type-erased packet.
    pub fn send_dyn(&self, packet: &dyn ServerPacket) {
        if let Some(bytes) = encode_for_send(packet) {
            self.send_frame(bytes);
        }
    }

    /// Queues an already encoded frame (see [`encode_for_send`] / [`Fanout`]). A client whose
    /// queue is over [`MAX_QUEUED_BYTES`] stopped reading: the frame is dropped and the
    /// connection closed.
    pub fn send_frame(&self, frame: Bytes) {
        if let Push::Overflow { first: true } = self.writer.push(frame) {
            tracing::warn!(
                "session {}: more than {} MiB queued for the client, closing the connection",
                self.id,
                MAX_QUEUED_BYTES / (1024 * 1024)
            );
            self.close();
        }
    }

    pub fn close(&self) {
        let _ = self.tx.send(ConnCommand::Close);
    }

    /// `ActiveChar = null` then `Shutdown()` on the connection task.
    pub fn detach_and_close(&self) {
        let _ = self.tx.send(ConnCommand::DetachAndClose);
    }

    /// Queues [`ConnCommand::ReturnToWorldList`] for the connection task.
    pub fn return_to_world_list(&self) {
        let _ = self.tx.send(ConnCommand::ReturnToWorldList);
    }
}

/// `GameConnection.SendPacket(GamePacket)`: packets with the placeholder opcode `0xFFF` (no
/// 1.2 opcode) are dropped (`None`); everything else is encoded (`GamePacket.Encode`).
pub fn encode_for_send(packet: &dyn ServerPacket) -> Option<Bytes> {
    if packet.opcode() == INVALID_OPCODE {
        tracing::error!("Dropping invalid game packet with opcode 0xFFF.");
        return None;
    }
    let bytes = encode(packet);
    tracing::debug!(
        "GamePacket: S->C type {:03X} {} ({} bytes)",
        packet.opcode(),
        packet.name(),
        bytes.len()
    );
    tracing::trace!("{:?}", packet);
    Some(Bytes::from(bytes))
}

/// One packet sent to many connections (`BroadcastPacket` and friends): encoded once, on the
/// first recipient, and the same frame queued for every connection.
pub struct Fanout<'a> {
    packet: &'a dyn ServerPacket,
    frame: Option<Option<Bytes>>,
}

impl<'a> Fanout<'a> {
    pub fn new(packet: &'a dyn ServerPacket) -> Self {
        Self {
            packet,
            frame: None,
        }
    }

    pub fn send(&mut self, conn: &ConnectionHandle) {
        let packet = self.packet;
        if let Some(frame) = self.frame.get_or_insert_with(|| encode_for_send(packet)) {
            conn.send_frame(frame.clone());
        }
    }
}

/// `GameConnectionTable`: connections keyed by session id.
#[derive(Debug, Default)]
pub struct ConnectionTable {
    connections: DashMap<u32, ConnectionHandle>,
}

impl ConnectionTable {
    pub fn add(&self, handle: ConnectionHandle) {
        self.connections.insert(handle.id, handle);
    }

    pub fn remove(&self, id: u32) -> Option<ConnectionHandle> {
        self.connections.remove(&id).map(|(_, h)| h)
    }

    pub fn get(&self, id: u32) -> Option<ConnectionHandle> {
        self.connections.get(&id).map(|h| h.clone())
    }

    /// `GetConnectionByAccount`: first connection with this account id.
    pub fn get_by_account(&self, account_id: u32) -> Option<ConnectionHandle> {
        self.connections
            .iter()
            .find(|h| h.account_id() == account_id)
            .map(|h| h.clone())
    }

    pub fn len(&self) -> usize {
        self.connections.len()
    }

    /// `GetConnections()`.
    pub fn all(&self) -> Vec<ConnectionHandle> {
        self.connections.iter().map(|h| h.clone()).collect()
    }

    pub fn is_empty(&self) -> bool {
        self.connections.is_empty()
    }
}

/// `GameConnection`.
pub struct GameConnection {
    pub id: u32,
    pub ip: SocketAddr,
    account_id: Arc<AtomicU32>,
    last_ping_ms: Arc<AtomicI64>,
    pub state: GameState,
    pub payment: AccountPayment,
    /// `Characters.Values` in insertion (load) order.
    pub characters: Vec<Character>,
    /// `ActiveChar`.
    pub active_char: Option<crate::world::PlayerRef>,
    /// `LeaveTask` (+ its `CancelTokenSource`): the pending logout timer.
    pub leave_task: Option<tokio::task::JoinHandle<()>>,
    pub last_ping: Instant,
    writer: Outbox,
    commands: mpsc::UnboundedSender<ConnCommand>,
    closing: bool,
}

impl std::fmt::Debug for GameConnection {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("GameConnection")
            .field("id", &self.id)
            .field("ip", &self.ip)
            .field("account_id", &self.account_id())
            .field("state", &self.state)
            .field("characters", &self.characters.len())
            .finish_non_exhaustive()
    }
}

impl GameConnection {
    /// Creates the connection state and its table handle. `writer` receives encoded frame payloads;
    /// `commands` is the sender half of the connection task's command channel.
    pub fn new(
        id: u32,
        ip: SocketAddr,
        writer: Outbox,
        commands: mpsc::UnboundedSender<ConnCommand>,
    ) -> (Self, ConnectionHandle) {
        let account_id = Arc::new(AtomicU32::new(0));
        let last_ping_ms = Arc::new(AtomicI64::new(i64::MIN));
        let handle = ConnectionHandle {
            id,
            ip,
            account_id: account_id.clone(),
            last_ping_ms: last_ping_ms.clone(),
            tx: commands.clone(),
            writer: writer.clone(),
        };
        let conn = Self {
            id,
            ip,
            account_id,
            last_ping_ms,
            state: GameState::Lobby,
            payment: AccountPayment::default(),
            characters: Vec::new(),
            active_char: None,
            leave_task: None,
            last_ping: Instant::now(),
            writer,
            commands,
            closing: false,
        };
        (conn, handle)
    }

    pub fn account_id(&self) -> u32 {
        self.account_id.load(Ordering::Relaxed)
    }

    pub fn set_account_id(&mut self, id: u32) {
        self.account_id.store(id, Ordering::Relaxed);
    }

    /// `Connection.LastPing = DateTime.UtcNow` (`PingPacket`).
    pub fn touch_ping(&mut self) {
        self.last_ping = Instant::now();
        self.last_ping_ms
            .store(chrono::Utc::now().timestamp_millis(), Ordering::Relaxed);
    }

    /// `GameConnection.SendPacket(GamePacket)`: packets with the placeholder opcode `0xFFF` (no
    /// 1.2 opcode) are dropped; everything else is encoded and queued for the writer task.
    pub fn send(&mut self, packet: &dyn ServerPacket) {
        let Some(bytes) = encode_for_send(packet) else {
            return;
        };
        match self.writer.push(bytes) {
            Push::Queued => {}
            Push::Closed => self.closing = true,
            Push::Overflow { first } => {
                if first {
                    tracing::warn!(
                        "session {}: more than {} MiB queued for the client, closing the connection",
                        self.id,
                        MAX_QUEUED_BYTES / (1024 * 1024)
                    );
                }
                self.closing = true;
            }
        }
    }

    /// A [`ConnectionHandle`] for this connection (same channels as the table entry).
    pub fn handle(&self) -> ConnectionHandle {
        ConnectionHandle {
            id: self.id,
            ip: self.ip,
            account_id: self.account_id.clone(),
            last_ping_ms: self.last_ping_ms.clone(),
            tx: self.commands.clone(),
            writer: self.writer.clone(),
        }
    }

    /// Queues a command for this connection's own task (e.g. the logout timer).
    pub fn command_sender(&self) -> mpsc::UnboundedSender<ConnCommand> {
        self.commands.clone()
    }

    /// `Shutdown()`: the connection task stops after the current packet.
    pub fn shutdown(&mut self) {
        self.closing = true;
        let _ = self.commands.send(ConnCommand::Close);
    }

    pub fn is_closing(&self) -> bool {
        self.closing
    }
}
