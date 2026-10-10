//! The C->S and S->C packets of the social subsystem (`Core/Packets/C2G`, `Core/Packets/G2C`),
//! one file per area. C->S packets read their body and call the managers; S->C packets are
//! plain structs whose `Marshal` impl is the C# `Write`.

pub mod chat;
pub mod duel;
pub mod expedition;
pub mod faction;
pub mod family;
pub mod friend;
pub mod misc;
pub mod team;
pub mod trial;

use crate::net::registry::PacketRegistry;

/// Defines a level-1 S->C packet: `sc_packet!(Name { field: Type, .. } |self, s| { body })`
/// (the opcode is `opcodes::sc::Name`).
macro_rules! sc_packet {
    ($(#[$m:meta])* $name:ident { $($(#[$fm:meta])* $f:ident : $t:ty),* $(,)? } |$self:ident, $s:ident| $body:block) => {
        $(#[$m])*
        #[derive(Debug, Clone, PartialEq)]
        pub struct $name { $($(#[$fm])* pub $f: $t),* }
        $crate::packets::packet_def!(
            $name,
            level = $crate::net::packet::LEVEL_PLAIN,
            opcode = $crate::opcodes::sc::$name
        );
        impl aaemu_commons::Marshal for $name {
            #[allow(unused_variables)]
            fn write(&$self, $s: &mut aaemu_commons::PacketStream) $body
        }
    };
}
pub(crate) use sc_packet;

/// Defines a C->S packet: `cs_packet!(Name { field: Type, .. } read |s| { Ok(Self {..}) }
/// handle |self, server, conn| { async body })`.
macro_rules! cs_packet {
    ($(#[$m:meta])* $name:ident { $($(#[$fm:meta])* $f:ident : $t:ty),* $(,)? }
        read |$rs:ident| $read:block
        handle |$self:ident, $server:ident, $conn:ident| $handle:block) => {
        $(#[$m])*
        #[derive(Debug, Clone, PartialEq)]
        pub struct $name { $($(#[$fm])* pub $f: $t),* }
        impl $crate::net::registry::ClientPacket for $name {
            const NAME: &'static str = stringify!($name);
            #[allow(unused_variables)]
            fn read($rs: &mut aaemu_commons::PacketStream) -> $crate::error::Result<Self> $read
            #[allow(unused_variables)]
            fn handle<'a>(
                $self,
                $server: &'a $crate::server::GameServer,
                $conn: &'a mut $crate::net::connection::GameConnection,
            ) -> futures::future::BoxFuture<'a, $crate::error::Result<()>> {
                Box::pin(async move $handle)
            }
        }
    };
}
pub(crate) use cs_packet;

/// `Connection.ActiveChar` of a handler (returns from the handler when there is none; the C#
/// would throw a `NullReferenceException`).
macro_rules! active_char {
    ($conn:ident) => {
        match $conn.active_char.clone() {
            Some(p) => p,
            None => return Ok(()),
        }
    };
}
pub(crate) use active_char;

/// Registers every social C->S packet.
pub fn register(reg: &mut PacketRegistry) {
    chat::register(reg);
    duel::register(reg);
    expedition::register(reg);
    faction::register(reg);
    family::register(reg);
    friend::register(reg);
    misc::register(reg);
    team::register(reg);
    trial::register(reg);
}

/// Encodes a packet body (tests).
#[cfg(test)]
pub(crate) fn body(p: &impl aaemu_commons::Marshal) -> Vec<u8> {
    let mut s = aaemu_commons::PacketStream::new();
    p.write(&mut s);
    s.into_bytes()
}
