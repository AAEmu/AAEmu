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
    #[test]
    fn no_duplicate_registrations() {
        let r = super::build_registry();
        assert!(r.duplicates().is_empty(), "{:?}", r.duplicates());
        let mut systems = crate::net::registry::PacketRegistry::new();
        crate::systems::register_packets(&mut systems);
        assert_eq!(r.len(), 13 + 22 + 2 + systems.len());
    }

    /// Parses `public const ushort Name = 0x...;` lines of a C# offsets class.
    fn cs_consts(src: &str) -> std::collections::HashMap<String, u16> {
        let re = regex::Regex::new(r"const\s+ushort\s+(\w+)\s*=\s*0x([0-9A-Fa-f]+)").unwrap();
        re.captures_iter(src)
            .map(|c| (c[1].to_string(), u16::from_str_radix(&c[2], 16).unwrap()))
            .collect()
    }

    /// Every (uncommented) `RegisterPacket` call of the C# `GameNetwork()` constructor is
    /// registered in Rust under the same (level, opcode) with the same packet class name, and
    /// Rust registers nothing the C# does not.
    #[test]
    fn registry_matches_csharp_game_network() {
        let root = concat!(env!("CARGO_MANIFEST_DIR"), "/../../../AAEmu.Game/Core");
        let Ok(net) = std::fs::read_to_string(format!("{root}/Network/Game/GameNetwork.cs")) else {
            eprintln!("C# sources not found next to the workspace; skipped");
            return;
        };
        let offsets = cs_consts(
            &std::fs::read_to_string(format!("{root}/Packets/C2G/CSOffsets.cs")).unwrap(),
        );
        let re = regex::Regex::new(
            r"RegisterPacket\(\s*(?:CSOffsets\.(\w+)|0x([0-9A-Fa-f]+))\s*,\s*(\d+)\s*,\s*typeof\((\w+)\)\s*\)",
        )
        .unwrap();
        let r = super::build_registry();
        let mut expected = std::collections::HashMap::new();
        for line in net.lines() {
            let code = line.split("//").next().unwrap();
            let Some(c) = re.captures(code) else { continue };
            let opcode = match (c.get(1), c.get(2)) {
                (Some(name), _) => offsets[name.as_str()],
                (_, Some(hex)) => u16::from_str_radix(hex.as_str(), 16).unwrap(),
                _ => unreachable!(),
            };
            let level: u8 = c[3].parse().unwrap();
            // C# `_packets[level][type] = classType`: a later registration wins.
            expected.insert((level, opcode), c[4].to_string());
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
            if !expected.contains_key(&(level, opcode)) && opcode != 0xfff {
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
