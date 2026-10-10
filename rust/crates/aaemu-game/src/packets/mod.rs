//! Packet definitions. One sub-module per C# packet namespace:
//! `c2g` (`Core/Packets/C2G`), `g2c` (`Core/Packets/G2C`), `proxy` (`Core/Packets/Proxy`).
//! Internal login-server packets live in [`crate::login`].

pub mod c2g;
pub mod c2g_world;
pub mod g2c;
pub mod proxy;
pub mod world;
pub mod world_objects;

/// Implements [`crate::net::packet::PacketDef`] for a packet struct.
macro_rules! packet_def {
    ($ty:ident, level = $level:expr, opcode = $op:expr) => {
        impl $crate::net::packet::PacketDef for $ty {
            const OPCODE: u16 = $op;
            const LEVEL: u8 = $level;
            const NAME: &'static str = stringify!($ty);
        }
    };
}
pub(crate) use packet_def;

/// A level-1 packet whose body was encoded up front (packets whose C# `Write` reads live game
/// objects are built by the object's owner while it holds the object's lock).
macro_rules! raw_body_packet {
    ($ty:ident, $op:expr) => {
        #[derive(Debug, Clone, PartialEq, Eq)]
        pub struct $ty {
            pub body: Vec<u8>,
        }
        $crate::packets::packet_def!($ty, level = $crate::net::packet::LEVEL_PLAIN, opcode = $op);
        impl aaemu_commons::Marshal for $ty {
            fn write(&self, s: &mut aaemu_commons::PacketStream) {
                s.write_raw(&self.body);
            }
        }
    };
}
pub(crate) use raw_body_packet;

use crate::net::registry::PacketRegistry;

/// `GameNetwork()` registrations: one `register` call per module that owns C->S packets (this
/// is the central list; a phase-3 subsystem registers through `systems::register_packets`).
/// Every other C# registration is logged as "not ported yet" by the dispatcher (see
/// `opcodes::cs::NAMES`).
pub fn build_registry() -> PacketRegistry {
    let mut r = PacketRegistry::new();
    c2g::register(&mut r); // X2EnterWorld, leave world, character list
    c2g_world::register(&mut r); // character lifecycle, world entry, movement, chat
    proxy::register(&mut r); // level-2 proxy packets
    crate::systems::register_packets(&mut r);
    for (level, opcode, old, new) in r.duplicates() {
        tracing::warn!("Packet 0x{opcode:03x}({level}) registered twice: {old} replaced by {new}");
    }
    r
}

#[cfg(test)]
mod tests {
    #[test]
    fn no_duplicate_registrations() {
        let r = super::build_registry();
        assert!(r.duplicates().is_empty(), "{:?}", r.duplicates());
        let mut systems = crate::net::registry::PacketRegistry::new();
        crate::systems::register_packets(&mut systems);
        assert_eq!(r.len(), 13 + 22 + systems.len());
    }
}
