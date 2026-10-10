//! Port of AAEmu.UnitTests/Game/Core/Packets/G2C/SCCharacterListPacketTests.cs
//!
//! The C# tests only assert that the packet can be constructed (its `Write` needs a
//! `PacketStream`, and `new Character[3]` holds nulls); the Rust port also checks the body:
//! `last`, the character count and each `Character.Write`.

use aaemu_commons::{Marshal, PacketStream};

use super::SCCharacterListPacket;
use crate::models::character::Character;
use crate::net::packet::ServerPacket;
use crate::test_support::test_character;

fn body(p: &SCCharacterListPacket) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write_body(&mut s);
    s.into_bytes()
}

#[test]
fn write_with_empty_character_list_writes_correct_data() {
    let packet = SCCharacterListPacket {
        last: true,
        characters: Vec::new(),
    };

    assert_eq!(body(&packet), vec![1, 0]);
}

#[test]
fn write_with_multiple_characters_writes_correct_data() {
    let characters: Vec<Character> = (1..=3)
        .map(|i| test_character(i, &format!("Char{i}"), 10.0, 10.0))
        .collect();
    let packet = SCCharacterListPacket {
        last: true,
        characters: characters.clone(),
    };

    let mut want = PacketStream::new();
    want.write_bool(true).write_u8(3);
    for c in &characters {
        c.write(&mut want);
    }
    assert_eq!(body(&packet), want.into_bytes());
}

#[test]
fn constructor_sets_last_flag() {
    let packet_last = SCCharacterListPacket {
        last: true,
        characters: Vec::new(),
    };
    let packet_not_last = SCCharacterListPacket {
        last: false,
        characters: Vec::new(),
    };

    assert!(packet_last.last);
    assert!(!packet_not_last.last);
    assert_eq!(body(&packet_last)[0], 1);
    assert_eq!(body(&packet_not_last)[0], 0);
}
