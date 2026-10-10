//! `StreamConnection` and `StreamConnectionTable` (`Core/Network/Connections`).
//!
//! Like the game connections, each stream connection is driven by one tokio task that owns its
//! [`StreamConnection`] (doodad request table, game-connection link) plus a writer task fed by
//! an mpsc channel. The table keeps a [`StreamConnectionHandle`] per connection.

use std::collections::HashMap;
use std::net::SocketAddr;

use dashmap::DashMap;
use tokio::sync::mpsc;

use crate::stream::packet::{encode, StreamServerPacket};
use crate::stream::world::StreamDoodad;

/// Commands other tasks can send to a stream connection task.
#[derive(Debug)]
pub enum StreamCommand {
    Send(Box<dyn StreamServerPacket>),
    Close,
}

#[derive(Debug, Clone)]
pub struct StreamConnectionHandle {
    pub id: u32,
    pub ip: SocketAddr,
    tx: mpsc::UnboundedSender<StreamCommand>,
}

impl StreamConnectionHandle {
    pub fn send(&self, packet: impl StreamServerPacket + 'static) {
        let _ = self.tx.send(StreamCommand::Send(Box::new(packet)));
    }

    pub fn close(&self) {
        let _ = self.tx.send(StreamCommand::Close);
    }
}

/// `StreamConnectionTable`.
#[derive(Debug, Default)]
pub struct StreamConnectionTable {
    connections: DashMap<u32, StreamConnectionHandle>,
}

impl StreamConnectionTable {
    pub fn add(&self, handle: StreamConnectionHandle) {
        // ConcurrentDictionary.TryAdd: an existing id is kept.
        self.connections.entry(handle.id).or_insert(handle);
    }

    pub fn get(&self, id: u32) -> Option<StreamConnectionHandle> {
        self.connections.get(&id).map(|h| h.clone())
    }

    pub fn remove(&self, id: u32) -> Option<StreamConnectionHandle> {
        self.connections.remove(&id).map(|(_, h)| h)
    }

    pub fn connections(&self) -> Vec<StreamConnectionHandle> {
        self.connections.iter().map(|h| h.clone()).collect()
    }

    pub fn len(&self) -> usize {
        self.connections.len()
    }

    pub fn is_empty(&self) -> bool {
        self.connections.is_empty()
    }
}

/// `StreamConnection`.
pub struct StreamConnection {
    pub id: u32,
    pub ip: SocketAddr,
    /// `GameConnection` (by id), set by a successful `CTJoinPacket`.
    pub game_connection_id: Option<u32>,
    request_id: i32,
    requests: HashMap<i32, Vec<StreamDoodad>>,
    writer: mpsc::UnboundedSender<Vec<u8>>,
    commands: mpsc::UnboundedSender<StreamCommand>,
    closing: bool,
}

impl std::fmt::Debug for StreamConnection {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("StreamConnection")
            .field("id", &self.id)
            .field("ip", &self.ip)
            .field("game_connection_id", &self.game_connection_id)
            .field("requests", &self.requests.len())
            .finish_non_exhaustive()
    }
}

impl StreamConnection {
    /// `writer` receives encoded frame payloads (`[u16 opcode][body]`); `commands` is the sender
    /// half of the connection task's command channel.
    pub fn new(
        id: u32,
        ip: SocketAddr,
        writer: mpsc::UnboundedSender<Vec<u8>>,
        commands: mpsc::UnboundedSender<StreamCommand>,
    ) -> (Self, StreamConnectionHandle) {
        let handle = StreamConnectionHandle {
            id,
            ip,
            tx: commands.clone(),
        };
        let conn = Self {
            id,
            ip,
            game_connection_id: None,
            request_id: -1,
            requests: HashMap::new(),
            writer,
            commands,
            closing: false,
        };
        (conn, handle)
    }

    /// `GetNextRequestId(doodads)`: ids start at 0.
    pub fn next_request_id(&mut self, doodads: Vec<StreamDoodad>) -> i32 {
        self.request_id = self.request_id.wrapping_add(1);
        self.requests.insert(self.request_id, doodads);
        self.request_id
    }

    pub fn request(&self, request_id: i32) -> Option<&Vec<StreamDoodad>> {
        self.requests.get(&request_id)
    }

    pub fn remove_request(&mut self, request_id: i32) -> Option<Vec<StreamDoodad>> {
        self.requests.remove(&request_id)
    }

    /// `SendPacket(StreamPacket)`.
    pub fn send(&mut self, packet: &dyn StreamServerPacket) {
        let bytes = encode(packet);
        tracing::debug!(
            "StreamPacket: S->C type {:03X} {} ({} bytes)",
            packet.opcode(),
            packet.name(),
            bytes.len()
        );
        tracing::trace!("{:?}", packet);
        if self.writer.send(bytes).is_err() {
            self.closing = true;
        }
    }

    /// `Shutdown()`.
    pub fn shutdown(&mut self) {
        self.closing = true;
        let _ = self.commands.send(StreamCommand::Close);
    }

    pub fn is_closing(&self) -> bool {
        self.closing
    }
}
