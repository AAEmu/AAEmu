//! Packet definitions. One sub-module per C# packet namespace:
//! `c2g` (`Core/Packets/C2G`), `g2c` (`Core/Packets/G2C`), `proxy` (`Core/Packets/Proxy`).
//! Internal login-server packets live in [`crate::login`].

pub mod c2g;
pub mod c2g_misc;
pub mod c2g_world;
pub mod g2c;
pub mod g2c_misc;
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
    c2g_misc::register(&mut r); // log-only packets (CSEditCharacter, CSTurretState)
    proxy::register(&mut r); // level-2 proxy packets
    crate::systems::register_packets(&mut r);
    for (level, opcode, old, new) in r.duplicates() {
        tracing::warn!("Packet 0x{opcode:03x}({level}) registered twice: {old} replaced by {new}");
    }
    r
}

#[cfg(test)]
mod tests {
    /// Cover the target client's complete non-reserved inbound table independently
    /// of the historical C# registration fixture. Behavior still needs feature flows.
    #[test]
    fn every_supported_1_2_inbound_opcode_has_a_handler() {
        let registry = super::build_registry();
        let supported: Vec<_> = crate::opcodes::cs::NAMES
            .iter()
            .filter(|(opcode, _)| *opcode != 0xfff)
            .collect();
        assert_eq!(supported.len(), 245);
        for (opcode, name) in supported {
            assert!(
                registry
                    .iter()
                    .any(|((_, registered), _)| registered == *opcode),
                "missing handler for {name} (0x{opcode:03x})"
            );
        }
    }

    #[test]
    fn no_duplicate_registrations() {
        let r = super::build_registry();
        assert!(r.duplicates().is_empty(), "{:?}", r.duplicates());
        let mut systems = crate::net::registry::PacketRegistry::new();
        crate::systems::register_packets(&mut systems);
        assert_eq!(r.len(), 13 + 22 + 2 + systems.len());
    }

    /// Every `RegisterPacket` call of the C# `GameNetwork()` constructor is registered in Rust
    /// under the same (level, opcode) with the same packet class name, and Rust registers
    /// nothing the C# does not. The C# table is a checked-in snapshot
    /// (`tests/fixtures/csharp_game_network_registry.tsv`) taken while the C# sources existed.
    #[test]
    fn registry_matches_csharp_game_network() {
        let fixture = include_str!("../../tests/fixtures/csharp_game_network_registry.tsv");
        let r = super::build_registry();
        let mut expected = std::collections::HashMap::new();
        for line in fixture.lines() {
            if line.starts_with('#') || line.trim().is_empty() {
                continue;
            }
            let mut cols = line.split('\t');
            let level: u8 = cols.next().unwrap().parse().unwrap();
            let opcode =
                u16::from_str_radix(cols.next().unwrap().trim_start_matches("0x"), 16).unwrap();
            let name = cols.next().unwrap().to_string();
            expected.insert((level, opcode), name);
        }
        assert!(
            expected.len() > 250,
            "parsed only {} registrations",
            expected.len()
        );
        let mut missing = Vec::new();
        for ((level, opcode), name) in &expected {
            match r.get(*level, *opcode) {
                // Level-2 proxy packets that have both directions are `XClientPacket` in Rust.
                Some(reg)
                    if reg.name == name
                        || reg.name.strip_suffix("ClientPacket") == name.strip_suffix("Packet") => {
                }
                other => missing.push(format!(
                    "0x{opcode:03x}({level}) {name}: rust {:?}",
                    other.map(|o| o.name)
                )),
            }
        }
        for ((level, opcode), reg) in r.iter() {
            // `CSReturnMailPacket` is commented out in C# (opcode 0xfff, never sent by 1.2):
            // registering it is harmless.
            // `CSTakeScheduleItemPacket` is a 1.2 client packet the C# never registered
            // (`systems::npcs::schedule_item_packets`, opcode read off the client).
            // `CSRankSnapshotPacket` (0x130) is not named by the 1.2 opcode tables and the C#
            // never handled it; the real client sends it.
            let rust_only =
                opcode == crate::opcodes::cs::CSTakeScheduleItemPacket || opcode == 0x130;
            if !expected.contains_key(&(level, opcode)) && opcode != 0xfff && !rust_only {
                missing.push(format!(
                    "0x{opcode:03x}({level}) {} not registered in C#",
                    reg.name
                ));
            }
        }
        missing.sort();
        assert!(missing.is_empty(), "{missing:#?}");
    }
}
