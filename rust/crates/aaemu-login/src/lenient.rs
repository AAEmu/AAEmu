//! Lenient reads with the semantics of the C# `AAEmu.Commons.Network.PacketStream`.
//!
//! The C# stream never throws on an out-of-bounds read: it logs an error and returns a default
//! value (0 / empty array / empty string) without advancing. Packet `Read` methods rely on that,
//! e.g. a client that sends a truncated `CARequestAuth` still gets processed. To stay
//! behaviour-compatible, C2L/G2L decoders read through this trait instead of the strict
//! `aaemu_commons::PacketStream` API.
//!
//! Candidate for hoisting into `aaemu-commons`.

use aaemu_commons::{MarshalError, PacketStream};

fn eos<T: Default>(e: MarshalError) -> T {
    tracing::error!("Attempted to read beyond the end of the stream. ({e})");
    T::default()
}

/// C#-compatible reads that return defaults instead of failing.
pub trait LenientRead {
    fn l_u8(&mut self) -> u8;
    /// `ReadBoolean`: true only if the byte is exactly 1.
    fn l_bool(&mut self) -> bool;
    fn l_i16(&mut self) -> i16;
    fn l_u16(&mut self) -> u16;
    fn l_i32(&mut self) -> i32;
    fn l_u32(&mut self) -> u32;
    fn l_u64(&mut self) -> u64;
    /// `ReadBytes(count)`: returns an empty array (without advancing) if not enough data.
    fn l_bytes(&mut self, count: usize) -> Vec<u8>;
    /// `ReadString()`: `i16` length + UTF-8, NULs trimmed; empty on any failure.
    fn l_string(&mut self) -> String;
    /// `ReadBytes()`: `i16` length + bytes. A negative length makes the C# code throw
    /// (`new byte[negative]`), so that is an error here too.
    fn l_bytes_sized(&mut self) -> Result<Vec<u8>, NegativeLength>;
}

/// A negative `i16` length prefix (the C# `ReadBytes()` throws `OverflowException`).
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[error("negative length prefix {0}")]
pub struct NegativeLength(pub i16);

impl LenientRead for PacketStream {
    fn l_u8(&mut self) -> u8 {
        self.read_u8().unwrap_or_else(eos)
    }
    fn l_bool(&mut self) -> bool {
        self.l_u8() == 1
    }
    fn l_i16(&mut self) -> i16 {
        self.read_i16().unwrap_or_else(eos)
    }
    fn l_u16(&mut self) -> u16 {
        self.read_u16().unwrap_or_else(eos)
    }
    fn l_i32(&mut self) -> i32 {
        self.read_i32().unwrap_or_else(eos)
    }
    fn l_u32(&mut self) -> u32 {
        self.read_u32().unwrap_or_else(eos)
    }
    fn l_u64(&mut self) -> u64 {
        self.read_u64().unwrap_or_else(eos)
    }
    fn l_bytes(&mut self, count: usize) -> Vec<u8> {
        self.read_bytes(count).unwrap_or_else(eos)
    }
    fn l_string(&mut self) -> String {
        let len = self.l_i16();
        if len < 0 {
            // C#: `new byte[negative]` throws inside ReadString's try/catch -> "".
            return String::new();
        }
        let bytes = self.l_bytes(len as usize);
        String::from_utf8_lossy(&bytes)
            .trim_matches('\0')
            .to_string()
    }
    fn l_bytes_sized(&mut self) -> Result<Vec<u8>, NegativeLength> {
        let len = self.l_i16();
        if len < 0 {
            return Err(NegativeLength(len));
        }
        Ok(self.l_bytes(len as usize))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_on_eof_without_advancing() {
        let mut s = PacketStream::from_bytes(vec![1, 2, 3]);
        assert_eq!(s.l_u32(), 0);
        assert_eq!(s.pos(), 0);
        assert_eq!(s.l_u16(), 0x0201);
        assert_eq!(s.l_bytes(5), Vec::<u8>::new());
        assert_eq!(s.l_u8(), 3);
        assert_eq!(s.l_string(), "");
        assert!(!s.l_bool());
    }

    #[test]
    fn string_truncated_payload_is_empty() {
        // length 5 but only 2 bytes follow
        let mut s = PacketStream::from_bytes(vec![5, 0, b'a', b'b']);
        assert_eq!(s.l_string(), "");
        assert_eq!(s.pos(), 2);
    }

    #[test]
    fn negative_sized_bytes_is_error() {
        let mut s = PacketStream::from_bytes(vec![0xFF, 0xFF]);
        assert_eq!(s.l_bytes_sized(), Err(NegativeLength(-1)));
    }
}
