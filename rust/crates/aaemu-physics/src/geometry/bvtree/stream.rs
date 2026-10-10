//! `CMemStream`: the byte stream geometry `Save`/`Load` use (CGF physics blobs, chunk
//! 0xCCCC0018). Layout in the binary: `+8` buffer, `+20` position, `+24` capacity, `+32`
//! "measure only" flag (writes only advance the position; used to size a blob before saving).
//!
//! Values are little-endian and unaligned; floats are written as their raw bits.

use crate::math::{Mat33, Vec3};

/// A read error (the original reads past the end of the buffer instead).
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum StreamError {
    /// Not enough bytes left.
    Truncated { at: usize, need: usize },
    /// A value the reader cannot accept.
    BadValue(String),
}

impl std::fmt::Display for StreamError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            StreamError::Truncated { at, need } => {
                write!(f, "stream truncated: {need} bytes needed at offset {at}")
            }
            StreamError::BadValue(s) => write!(f, "bad stream value: {s}"),
        }
    }
}

impl std::error::Error for StreamError {}

/// Reading side of `CMemStream`.
#[derive(Clone, Debug)]
pub struct MemReader<'a> {
    buf: &'a [u8],
    /// `+20`.
    pub pos: usize,
}

impl<'a> MemReader<'a> {
    #[must_use]
    pub fn new(buf: &'a [u8]) -> Self {
        Self { buf, pos: 0 }
    }

    /// Remaining bytes.
    #[must_use]
    pub fn remaining(&self) -> usize {
        self.buf.len().saturating_sub(self.pos)
    }

    /// The whole buffer.
    #[must_use]
    pub fn buffer(&self) -> &'a [u8] {
        self.buf
    }

    /// Reads `n` raw bytes.
    ///
    /// # Errors
    /// [`StreamError::Truncated`].
    pub fn bytes(&mut self, n: usize) -> Result<&'a [u8], StreamError> {
        if self.remaining() < n {
            return Err(StreamError::Truncated {
                at: self.pos,
                need: n,
            });
        }
        let s = &self.buf[self.pos..self.pos + n];
        self.pos += n;
        Ok(s)
    }

    /// Skips `n` bytes.
    ///
    /// # Errors
    /// [`StreamError::Truncated`].
    pub fn skip(&mut self, n: usize) -> Result<(), StreamError> {
        self.bytes(n).map(|_| ())
    }

    /// # Errors
    /// [`StreamError::Truncated`].
    pub fn u8(&mut self) -> Result<u8, StreamError> {
        Ok(self.bytes(1)?[0])
    }

    /// # Errors
    /// [`StreamError::Truncated`].
    pub fn u16(&mut self) -> Result<u16, StreamError> {
        let b = self.bytes(2)?;
        Ok(u16::from_le_bytes([b[0], b[1]]))
    }

    /// # Errors
    /// [`StreamError::Truncated`].
    pub fn u32(&mut self) -> Result<u32, StreamError> {
        let b = self.bytes(4)?;
        Ok(u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
    }

    /// # Errors
    /// [`StreamError::Truncated`].
    pub fn i32(&mut self) -> Result<i32, StreamError> {
        Ok(self.u32()? as i32)
    }

    /// # Errors
    /// [`StreamError::Truncated`].
    pub fn f32(&mut self) -> Result<f32, StreamError> {
        Ok(f32::from_bits(self.u32()?))
    }

    /// # Errors
    /// [`StreamError::Truncated`].
    pub fn vec3(&mut self) -> Result<Vec3, StreamError> {
        Ok(Vec3::new(self.f32()?, self.f32()?, self.f32()?))
    }

    /// 9 floats, row-major.
    ///
    /// # Errors
    /// [`StreamError::Truncated`].
    pub fn mat33(&mut self) -> Result<Mat33, StreamError> {
        let mut a = [0f32; 9];
        for v in &mut a {
            *v = self.f32()?;
        }
        Ok(Mat33::from_array(a))
    }
}

/// Writing side of `CMemStream`.
#[derive(Clone, Debug, Default)]
pub struct MemWriter {
    /// `+8` (grown on demand, vtable slot 1).
    pub buf: Vec<u8>,
    /// `+20`.
    pub pos: usize,
    /// `+32`: only advance [`MemWriter::pos`].
    pub measure_only: bool,
}

impl MemWriter {
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// A writer that only counts bytes.
    #[must_use]
    pub fn measuring() -> Self {
        Self {
            measure_only: true,
            ..Self::default()
        }
    }

    /// Writes raw bytes at the position.
    pub fn bytes(&mut self, b: &[u8]) {
        if !self.measure_only {
            let end = self.pos + b.len();
            if self.buf.len() < end {
                self.buf.resize(end, 0);
            }
            self.buf[self.pos..end].copy_from_slice(b);
        }
        self.pos += b.len();
    }

    pub fn u8(&mut self, v: u8) {
        self.bytes(&[v]);
    }

    pub fn u16(&mut self, v: u16) {
        self.bytes(&v.to_le_bytes());
    }

    pub fn u32(&mut self, v: u32) {
        self.bytes(&v.to_le_bytes());
    }

    pub fn i32(&mut self, v: i32) {
        self.bytes(&v.to_le_bytes());
    }

    pub fn f32(&mut self, v: f32) {
        self.u32(v.to_bits());
    }

    pub fn vec3(&mut self, v: Vec3) {
        self.f32(v.x);
        self.f32(v.y);
        self.f32(v.z);
    }

    /// 36 bytes, row-major (sub_3513A0A0).
    pub fn mat33(&mut self, m: &Mat33) {
        for v in m.to_array() {
            self.f32(v);
        }
    }

    /// The written bytes.
    #[must_use]
    pub fn into_bytes(self) -> Vec<u8> {
        self.buf
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn roundtrip_and_measure() {
        let mut w = MemWriter::new();
        w.i32(-1);
        w.f32(1.5);
        w.vec3(Vec3::new(1.0, 2.0, 3.0));
        w.u8(7);
        let b = w.into_bytes();
        assert_eq!(b.len(), 21);
        let mut r = MemReader::new(&b);
        assert_eq!(r.i32().unwrap(), -1);
        assert_eq!(r.f32().unwrap(), 1.5);
        assert_eq!(r.vec3().unwrap(), Vec3::new(1.0, 2.0, 3.0));
        assert_eq!(r.u8().unwrap(), 7);
        assert!(r.u8().is_err());
        let mut m = MemWriter::measuring();
        m.mat33(&Mat33::IDENTITY);
        assert_eq!((m.pos, m.buf.len()), (36, 0));
    }
}
