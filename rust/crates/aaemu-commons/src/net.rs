//! TCP framing shared by every AAEmu protocol (replaces NetCoreServer + the hand-rolled
//! reassembly in each `*ProtocolHandler.OnReceive`).
//!
//! Every frame on the wire is `[u16 LE length][payload]`, where `length` counts the payload only.

use bytes::{Buf, BufMut, BytesMut};
use tokio_util::codec::{Decoder, Encoder};

/// Splits a byte stream into length-prefixed frames. Yields the payload with the length removed.
#[derive(Debug, Default, Clone, Copy)]
pub struct FrameCodec;

impl Decoder for FrameCodec {
    type Item = Vec<u8>;
    type Error = std::io::Error;

    fn decode(&mut self, src: &mut BytesMut) -> Result<Option<Self::Item>, Self::Error> {
        if src.len() < 2 {
            return Ok(None);
        }
        let len = usize::from(u16::from_le_bytes([src[0], src[1]]));
        if src.len() < 2 + len {
            src.reserve(2 + len - src.len());
            return Ok(None);
        }
        src.advance(2);
        Ok(Some(src.split_to(len).to_vec()))
    }
}

impl Encoder<Vec<u8>> for FrameCodec {
    type Error = std::io::Error;

    /// Prepends the `u16` length to `payload`.
    fn encode(&mut self, payload: Vec<u8>, dst: &mut BytesMut) -> Result<(), Self::Error> {
        let len = u16::try_from(payload.len()).map_err(|_| {
            std::io::Error::new(
                std::io::ErrorKind::InvalidInput,
                "frame exceeds 65535 bytes",
            )
        })?;
        dst.reserve(2 + payload.len());
        dst.put_u16_le(len);
        dst.extend_from_slice(&payload);
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decodes_split_frames() {
        let mut c = FrameCodec;
        let mut buf = BytesMut::from(&[3u8, 0, 1, 2][..]);
        assert_eq!(c.decode(&mut buf).unwrap(), None);
        buf.extend_from_slice(&[3, 1, 0, 9]);
        assert_eq!(c.decode(&mut buf).unwrap(), Some(vec![1, 2, 3]));
        assert_eq!(c.decode(&mut buf).unwrap(), Some(vec![9]));
        assert_eq!(c.decode(&mut buf).unwrap(), None);
    }
}
