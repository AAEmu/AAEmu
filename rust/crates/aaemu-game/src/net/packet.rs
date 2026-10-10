//! Game packet framing: port of the 1.2 `GamePacket.Encode` and the header parsing in
//! `GameProtocolHandler.OnReceive`.
//!
//! Every frame payload (after the `u16` length, which [`aaemu_commons::net::FrameCodec`] handles)
//! starts with `[0xDD][level]`. The 1.2 client only uses two levels:
//!
//! | level | S->C payload                          | C->S payload                              |
//! |-------|---------------------------------------|-------------------------------------------|
//! | 1     | `[dd][01][00][00][u16 opcode][body]`  | `[dd][01][hash][count][u16 opcode][body]` |
//! | 2     | `[dd][02][u16 opcode][body]` (proxy)  | same                                      |
//!
//! The C# server writes `hash = 0` / `count = 0` on every level-1 packet it sends and reads but
//! never verifies the client's values (`// TODO: verify 1.2 crc` / `counter`). Any level other
//! than 1 is encoded/decoded without those two bytes.

use std::fmt;

use aaemu_commons::{Marshal, PacketStream};

use crate::error::Result;

/// Header byte that starts every game frame payload. The C# reader ignores it (`//unk`).
pub const FRAME_MAGIC: u8 = 0xDD;

/// Game (C2G / G2C) channel.
pub const LEVEL_PLAIN: u8 = 1;
/// Proxy (CryNetwork) channel.
pub const LEVEL_PROXY: u8 = 2;

/// Opcode the C# offset tables use for packets that do not exist in 1.2.
/// `GameConnection.SendPacket` drops any packet with this type id.
pub const INVALID_OPCODE: u16 = 0xFFF;

/// Static description of a game packet type (the `GamePacket(typeId, level)` constructor
/// arguments in C#).
pub trait PacketDef {
    const OPCODE: u16;
    const LEVEL: u8;
    const NAME: &'static str;
}

/// An S->C packet ready to be encoded. Blanket-implemented for every `PacketDef + Marshal`.
pub trait ServerPacket: Send + fmt::Debug {
    fn opcode(&self) -> u16;
    fn level(&self) -> u8;
    fn name(&self) -> &'static str;
    fn write_body(&self, stream: &mut PacketStream);
}

impl<T> ServerPacket for T
where
    T: PacketDef + Marshal + Send + fmt::Debug,
{
    fn opcode(&self) -> u16 {
        T::OPCODE
    }
    fn level(&self) -> u8 {
        T::LEVEL
    }
    fn name(&self) -> &'static str {
        T::NAME
    }
    fn write_body(&self, stream: &mut PacketStream) {
        self.write(stream);
    }
}

/// `GamePacket.Encode`: builds the frame payload (without the `u16` length prefix).
pub fn encode(packet: &dyn ServerPacket) -> Vec<u8> {
    let mut body = PacketStream::new();
    packet.write_body(&mut body);
    encode_raw(packet.level(), packet.opcode(), body.as_bytes())
}

/// Encodes an already-serialised body. See [`encode`].
pub fn encode_raw(level: u8, opcode: u16, body: &[u8]) -> Vec<u8> {
    let mut out = PacketStream::new();
    out.write_u8(FRAME_MAGIC).write_u8(level);
    if level == LEVEL_PLAIN {
        out.write_u8(0) // hash
            .write_u8(0); // count
    }
    out.write_u16(opcode).write_raw(body);
    out.into_bytes()
}

/// A decoded C->S packet: channel level, opcode and a stream positioned at the body.
#[derive(Debug)]
pub struct RawPacket {
    pub level: u8,
    /// Level-1 `hash` byte (read, never verified — C# TODO).
    pub hash: u8,
    /// Level-1 `count` byte (read, never verified — C# TODO).
    pub count: u8,
    pub opcode: u16,
    pub body: PacketStream,
}

/// Parses one frame payload (starting at the `0xDD` byte) the way
/// `GameProtocolHandler.OnReceive` does.
pub fn decode_frame(payload: &[u8]) -> Result<RawPacket> {
    let mut s = PacketStream::from_bytes(payload.to_vec());
    let _magic = s.read_u8()?;
    let level = s.read_u8()?;
    let (hash, count) = if level == LEVEL_PLAIN {
        (s.read_u8()?, s.read_u8()?)
    } else {
        (0, 0)
    };
    let opcode = s.read_u16()?;
    Ok(RawPacket {
        level,
        hash,
        count,
        opcode,
        body: s,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[derive(Debug)]
    struct Dummy(u32);
    impl PacketDef for Dummy {
        const OPCODE: u16 = 0x1234;
        const LEVEL: u8 = 1;
        const NAME: &'static str = "Dummy";
    }
    impl Marshal for Dummy {
        fn write(&self, s: &mut PacketStream) {
            s.write_u32(self.0);
        }
    }

    #[test]
    fn level1_layout() {
        assert_eq!(
            encode(&Dummy(0xAABBCCDD)),
            vec![0xDD, 0x01, 0x00, 0x00, 0x34, 0x12, 0xDD, 0xCC, 0xBB, 0xAA]
        );
    }

    #[test]
    fn level2_and_other_levels_have_no_hash_count() {
        assert_eq!(
            encode_raw(2, 0x000F, &[1, 2]),
            vec![0xDD, 0x02, 0x0F, 0x00, 1, 2]
        );
        assert_eq!(encode_raw(3, 0x0001, &[]), vec![0xDD, 0x03, 0x01, 0x00]);
    }

    #[test]
    fn decodes_plain_and_proxy_headers() {
        let p = decode_frame(&[0xDD, 0x01, 0xAA, 0xBB, 0x20, 0x00, 9]).unwrap();
        assert_eq!(
            (p.level, p.hash, p.count, p.opcode, p.body.unread()),
            (1, 0xAA, 0xBB, 0x20, &[9u8][..])
        );
        let p = decode_frame(&[0xDD, 0x02, 0x01, 0x00, 0, 0, 0, 0]).unwrap();
        assert_eq!((p.level, p.opcode, p.body.remaining()), (2, 0x01, 4));
        // Any other level is read like level 2 (the C# table lookup then throws).
        let p = decode_frame(&[0xDD, 0x05, 0x07, 0x00]).unwrap();
        assert_eq!((p.level, p.opcode), (5, 7));
    }

    #[test]
    fn truncated_headers_are_errors() {
        assert!(decode_frame(&[0xDD]).is_err());
        assert!(decode_frame(&[0xDD, 0x01, 0x00, 0x00, 0x01]).is_err());
        assert!(decode_frame(&[0xDD, 0x02, 0x01]).is_err());
    }
}
