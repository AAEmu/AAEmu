//! Wire packets. Every frame on both the client (C2L/L2C) and the internal (G2L/L2G) socket is
//! `[u16 LE length][u16 LE opcode][body]` where `length` counts opcode + body
//! (`LoginPacket.Encode` / `InternalPacket.Encode`: `ps.Write(new PacketStream().Write(TypeId).Write(this))`).
//!
//! The length prefix is added/stripped by [`aaemu_commons::net::FrameCodec`]; the helpers here
//! deal with the `[opcode][body]` payload.

pub mod c2l;
pub mod g2l;
pub mod l2c;
pub mod l2g;

use aaemu_commons::{MarshalError, PacketStream};

use crate::lenient::NegativeLength;

#[derive(Debug, thiserror::Error)]
pub enum PacketError {
    #[error(transparent)]
    Marshal(#[from] MarshalError),
    #[error(transparent)]
    NegativeLength(#[from] NegativeLength),
    #[error("negative collection count {0}")]
    NegativeCount(i32),
    #[error("invalid XML: {0}")]
    Xml(String),
    #[error("invalid IP address '{0}'")]
    InvalidIp(String),
    #[error("payload too short for an opcode ({0} bytes)")]
    MissingOpcode(usize),
}

/// A packet that can be serialized as `[opcode][body]`.
pub trait OutgoingPacket {
    const OPCODE: u16;

    /// Writes the body (everything after the opcode).
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError>;

    /// `[u16 opcode][body]` — the payload handed to `FrameCodec`.
    fn encode_payload(&self) -> Result<Vec<u8>, PacketError> {
        let mut s = PacketStream::new();
        s.write_u16(Self::OPCODE);
        self.write_body(&mut s)?;
        Ok(s.into_bytes())
    }

    /// `[u16 len][u16 opcode][body]` — the full frame as it appears on the wire.
    fn encode_frame(&self) -> Result<Vec<u8>, PacketError> {
        let payload = self.encode_payload()?;
        let mut out = Vec::with_capacity(payload.len() + 2);
        out.extend_from_slice(&(payload.len() as u16).to_le_bytes());
        out.extend_from_slice(&payload);
        Ok(out)
    }
}

/// Splits a frame payload into `(opcode, body stream)`.
pub fn split_opcode(payload: Vec<u8>) -> Result<(u16, PacketStream), PacketError> {
    if payload.len() < 2 {
        return Err(PacketError::MissingOpcode(payload.len()));
    }
    let opcode = u16::from_le_bytes([payload[0], payload[1]]);
    let mut s = PacketStream::from_bytes(payload);
    s.set_pos(2);
    Ok((opcode, s))
}

/// Space-separated hex dump of the unread part of a stream (for unknown packet logging).
pub fn dump_unread(s: &PacketStream) -> String {
    s.unread().iter().map(|b| format!("{b:02x} ")).collect()
}

#[cfg(test)]
pub(crate) fn hex_bytes(s: &str) -> Vec<u8> {
    hex::decode(s).expect("valid hex")
}

#[cfg(test)]
mod protocol_tests {
    //! Port of `AAEmu.UnitTests/Login/Core/Network/Login/LoginProtocolHandlerTests.cs`.
    //! `LoginProtocolHandler.TryParsePacket` is `FrameCodec::decode` (length prefix) followed by
    //! [`split_opcode`] (packet type + payload).

    use aaemu_commons::net::FrameCodec;
    use bytes::BytesMut;
    use tokio_util::codec::Decoder;

    use super::*;

    /// `TryParsePacket(ref buffer, out type, out packet)`: `None` when the buffer holds no
    /// complete packet; the buffer then is left untouched.
    fn try_parse_packet(buffer: &mut BytesMut) -> Option<(u16, Vec<u8>)> {
        let frame = FrameCodec.decode(buffer).unwrap()?;
        let (opcode, s) = split_opcode(frame).unwrap();
        Some((opcode, s.unread().to_vec()))
    }

    #[test]
    fn try_parse_packet_valid_data_returns_true() {
        let mut buffer = BytesMut::from(
            &[
                0x06, 0x00, // Packet length (6 bytes)
                0x2A, 0x00, // Packet type (42)
                0x01, 0x02, 0x03, 0x04, // Packet payload
            ][..],
        );

        let (parsed_type, packet) = try_parse_packet(&mut buffer).expect("parsed");

        assert_eq!(parsed_type, 42);
        assert_eq!(packet, vec![1, 2, 3, 4]);
        // The whole buffer (8 bytes) was consumed.
        assert!(buffer.is_empty());
    }

    #[test]
    fn try_parse_packet_valid_data_followed_by_partial_data_mutates_buffer() {
        let mut buffer = BytesMut::from(
            &[
                0x06, 0x00, // Packet length (6 bytes)
                0x2A, 0x00, // Packet type (42)
                0x01, 0x02, 0x03, 0x04, // Packet payload
                0x08, 0x00, // Partial data for the next packet (only length)
            ][..],
        );

        let (parsed_type, packet) = try_parse_packet(&mut buffer).expect("parsed");

        assert_eq!(parsed_type, 42);
        assert_eq!(packet.len(), 4);
        // Start advanced to 8; the 2 bytes of the next packet remain.
        assert_eq!(&buffer[..], &[0x08, 0x00]);
    }

    #[test]
    fn try_parse_packet_insufficient_data_returns_false() {
        // only 2 bytes: a length, but no packet
        let mut buffer = BytesMut::from(&[0x02, 0x00][..]);
        assert!(try_parse_packet(&mut buffer).is_none());
    }

    #[test]
    fn try_parse_packet_insufficient_data_does_not_mutate_buffer() {
        let original = [0x02u8, 0x00];
        let mut buffer = BytesMut::from(&original[..]);

        let _ = try_parse_packet(&mut buffer);

        assert_eq!(&buffer[..], &original[..]);
    }

    #[test]
    fn try_parse_packet_empty_buffer_returns_false() {
        let mut buffer = BytesMut::new();
        assert!(try_parse_packet(&mut buffer).is_none());
    }

    #[test]
    fn try_parse_packet_empty_buffer_does_not_mutate_buffer() {
        let mut buffer = BytesMut::new();

        let _ = try_parse_packet(&mut buffer);

        assert!(buffer.is_empty());
    }

    #[test]
    fn try_parse_packet_large_packet_size_returns_true() {
        // length 65535: 2 bytes of type + 65533 bytes of payload
        let mut data = vec![0u8; 65537];
        data[0] = 0xFF;
        data[1] = 0xFF;
        data[2] = 0x2A;
        data[3] = 0x00;
        let mut buffer = BytesMut::from(&data[..]);

        let (parsed_type, packet) = try_parse_packet(&mut buffer).expect("parsed");

        assert_eq!(parsed_type, 42);
        assert_eq!(packet.len(), 65533);
    }
}
