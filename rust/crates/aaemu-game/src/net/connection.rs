//! Per-connection state (`GameConnection`) and the connection table (`GameConnectionTable`).
//!
//! Each client connection is driven by one tokio task that owns its [`GameConnection`] (so its
//! state needs no locking) plus a writer task fed through an mpsc channel. Other tasks (e.g. the
//! login-server link) reach a connection through its [`ConnectionHandle`].

use std::net::SocketAddr;
use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::Arc;
use std::time::Instant;

use dashmap::DashMap;
use tokio::sync::mpsc;

use crate::models::character::Character;
use crate::models::item::UnixTime;
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
}

/// Cloneable handle stored in the [`ConnectionTable`].
#[derive(Debug, Clone)]
pub struct ConnectionHandle {
    pub id: u32,
    pub ip: SocketAddr,
    account_id: Arc<AtomicU32>,
    tx: mpsc::UnboundedSender<ConnCommand>,
}

impl ConnectionHandle {
    pub fn account_id(&self) -> u32 {
        self.account_id.load(Ordering::Relaxed)
    }

    /// Queues a packet; it is encoded and sent by the connection task, in queue order.
    pub fn send(&self, packet: impl ServerPacket + 'static) {
        let _ = self.tx.send(ConnCommand::Send(Box::new(packet)));
    }

    pub fn close(&self) {
        let _ = self.tx.send(ConnCommand::Close);
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

    pub fn is_empty(&self) -> bool {
        self.connections.is_empty()
    }
}

/// `GameConnection`.
pub struct GameConnection {
    pub id: u32,
    pub ip: SocketAddr,
    account_id: Arc<AtomicU32>,
    pub state: GameState,
    pub payment: AccountPayment,
    /// `Characters.Values` in insertion (load) order.
    pub characters: Vec<Character>,
    pub last_ping: Instant,
    writer: mpsc::UnboundedSender<Vec<u8>>,
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
        writer: mpsc::UnboundedSender<Vec<u8>>,
        commands: mpsc::UnboundedSender<ConnCommand>,
    ) -> (Self, ConnectionHandle) {
        let account_id = Arc::new(AtomicU32::new(0));
        let handle = ConnectionHandle {
            id,
            ip,
            account_id: account_id.clone(),
            tx: commands.clone(),
        };
        let conn = Self {
            id,
            ip,
            account_id,
            state: GameState::Lobby,
            payment: AccountPayment::default(),
            characters: Vec::new(),
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

    /// `GameConnection.SendPacket(GamePacket)`: packets with the placeholder opcode `0xFFF` (no
    /// 1.2 opcode) are dropped; everything else is encoded and queued for the writer task.
    pub fn send(&mut self, packet: &dyn ServerPacket) {
        if packet.opcode() == INVALID_OPCODE {
            tracing::error!("Dropping invalid game packet with opcode 0xFFF.");
            return;
        }
        let bytes = encode(packet);
        tracing::debug!(
            "GamePacket: S->C type {:03X} {} ({} bytes)",
            packet.opcode(),
            packet.name(),
            bytes.len()
        );
        tracing::trace!("{:?}", packet);
        if self.writer.send(bytes).is_err() {
            self.closing = true;
        }
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
