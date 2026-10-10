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
