//! Packet registry: port of `GameProtocolHandler._packets` / `GameNetwork.RegisterPacket`,
//! keyed by `(level, opcode)`.

use std::collections::HashMap;

use aaemu_commons::PacketStream;
use futures::future::BoxFuture;

use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::server::GameServer;

/// Type-erased handler: reads the packet body and runs its behaviour (`Read` + `Execute`).
pub type HandlerFn =
    for<'a> fn(&'a GameServer, &'a mut GameConnection, PacketStream) -> BoxFuture<'a, Result<()>>;

/// A C->S packet: `Read` is [`ClientPacket::read`], the side effects are [`ClientPacket::handle`].
pub trait ClientPacket: Sized + Send + 'static {
    const NAME: &'static str;

    fn read(stream: &mut PacketStream) -> Result<Self>;

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>>;
}

fn dispatch<'a, P: ClientPacket>(
    server: &'a GameServer,
    conn: &'a mut GameConnection,
    mut stream: PacketStream,
) -> BoxFuture<'a, Result<()>> {
    match P::read(&mut stream) {
        Ok(packet) => {
            tracing::debug!("GamePacket: C->S {} conn={}", P::NAME, conn.id);
            packet.handle(server, conn)
        }
        Err(e) => {
            tracing::error!("GamePacket: C->S {} failed to read: {e}", P::NAME);
            Box::pin(async move { Err(e) })
        }
    }
}

#[derive(Clone, Copy)]
pub struct Registration {
    pub name: &'static str,
    pub handler: HandlerFn,
}

impl std::fmt::Debug for Registration {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(self.name)
    }
}

#[derive(Debug, Default)]
pub struct PacketRegistry {
    packets: HashMap<(u8, u16), Registration>,
    /// (level, opcode, replaced name, new name) of every registration that replaced another.
    duplicates: Vec<(u8, u16, &'static str, &'static str)>,
}

impl PacketRegistry {
    pub fn new() -> Self {
        Self::default()
    }

    /// `RegisterPacket(type, level, typeof(P))`. A later registration replaces an earlier one
    /// (C# `_packets[level][type] = classType`).
    pub fn register<P: ClientPacket>(&mut self, level: u8, opcode: u16) -> &mut Self {
        let old = self.packets.insert(
            (level, opcode),
            Registration {
                name: P::NAME,
                handler: dispatch::<P>,
            },
        );
        if let Some(old) = old {
            self.duplicates.push((level, opcode, old.name, P::NAME));
        }
        self
    }

    /// Registrations that replaced an earlier one (two modules claiming the same opcode is a
    /// porting mistake; `packets::tests::no_duplicate_registrations` guards against it).
    pub fn duplicates(&self) -> &[(u8, u16, &'static str, &'static str)] {
        &self.duplicates
    }

    pub fn get(&self, level: u8, opcode: u16) -> Option<Registration> {
        self.packets.get(&(level, opcode)).copied()
    }

    pub fn len(&self) -> usize {
        self.packets.len()
    }

    /// Every `((level, opcode), registration)` pair (unordered).
    pub fn iter(&self) -> impl Iterator<Item = ((u8, u16), Registration)> + '_ {
        self.packets.iter().map(|(k, v)| (*k, *v))
    }

    pub fn is_empty(&self) -> bool {
        self.packets.is_empty()
    }
}
