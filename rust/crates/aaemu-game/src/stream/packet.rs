//! Stream packet framing: port of `StreamPacket.Encode` and the header parsing in
//! `StreamProtocolHandler.OnReceive`.
//!
//! Wire format (both directions): `[u16 len][u16 opcode][body]`, `len` counting opcode + body.
//! The `u16` length prefix is handled by [`aaemu_commons::net::FrameCodec`]; the functions here
//! deal with the frame payload `[u16 opcode][body]`. There is no level byte, hash or encryption.

use std::fmt;

use aaemu_commons::{Marshal, PacketStream};

/// Static description of an S->C stream packet (`StreamPacket(typeId)`).
pub trait StreamPacketDef {
    const OPCODE: u16;
    const NAME: &'static str;
}

/// An S->C stream packet ready to be encoded. Blanket-implemented for every
/// `StreamPacketDef + Marshal`.
pub trait StreamServerPacket: Send + fmt::Debug {
    fn opcode(&self) -> u16;
    fn name(&self) -> &'static str;
    fn write_body(&self, stream: &mut PacketStream);
}

impl<T> StreamServerPacket for T
where
    T: StreamPacketDef + Marshal + Send + fmt::Debug,
{
    fn opcode(&self) -> u16 {
        T::OPCODE
    }
    fn name(&self) -> &'static str {
        T::NAME
    }
    fn write_body(&self, stream: &mut PacketStream) {
        self.write(stream);
    }
}

/// `StreamPacket.Encode` without the outer `u16` length: `[u16 opcode][body]`.
pub fn encode(packet: &dyn StreamServerPacket) -> Vec<u8> {
    let mut out = PacketStream::new();
    out.write_u16(packet.opcode());
    packet.write_body(&mut out);
    out.into_bytes()
}

/// Splits a frame payload into `(opcode, body)`.
///
/// A payload shorter than two bytes yields opcode `0` with the bytes left as the body: the C#
/// `ReadUInt16` returns `0` on a short read, which then lands in `HandleUnknownPacket`.
pub fn decode_frame(payload: &[u8]) -> (u16, PacketStream) {
    if payload.len() < 2 {
        return (0, PacketStream::from_bytes(payload.to_vec()));
    }
    let opcode = u16::from_le_bytes([payload[0], payload[1]]);
    (opcode, PacketStream::from_bytes(payload[2..].to_vec()))
}

/// Implements [`StreamPacketDef`] for a packet struct.
macro_rules! stream_packet_def {
    ($ty:ident, $op:expr) => {
        impl $crate::stream::packet::StreamPacketDef for $ty {
            const OPCODE: u16 = $op;
            const NAME: &'static str = stringify!($ty);
        }
    };
}
pub(crate) use stream_packet_def;

#[cfg(test)]
mod tests {
    use super::*;

    #[derive(Debug)]
    struct Dummy(u32);
    impl StreamPacketDef for Dummy {
        const OPCODE: u16 = 0x00c;
        const NAME: &'static str = "Dummy";
    }
    impl Marshal for Dummy {
        fn write(&self, s: &mut PacketStream) {
            s.write_u32(self.0);
        }
    }

    #[test]
    fn layout() {
        assert_eq!(
            encode(&Dummy(0xAABBCCDD)),
            vec![0x0C, 0x00, 0xDD, 0xCC, 0xBB, 0xAA]
        );
    }

    #[test]
    fn decode() {
        let (op, body) = decode_frame(&[0x0A, 0x00, 1, 2]);
        assert_eq!((op, body.unread()), (0x0A, &[1u8, 2][..]));
        let (op, body) = decode_frame(&[0x07]);
        assert_eq!((op, body.unread()), (0, &[7u8][..]));
        let (op, body) = decode_frame(&[]);
        assert_eq!((op, body.remaining()), (0, 0));
    }
}
