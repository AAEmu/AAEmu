//! Port of `AAEmu.Commons.Utils.AAPak.SubStream`: a read-only window onto part of another stream.

use std::io::{self, Read, Seek, SeekFrom};

/// Exposes `length` bytes of `base`, starting at `offset`, as its own stream.
///
/// Differences from the C# class:
/// * The base stream is re-positioned before **every** read, so several `SubStream`s (or other
///   users) can share one base handle without corrupting each other's position. The C# version
///   only seeks once, in the constructor.
/// * [`Seek`] is implemented (the C# `CanSeek` is `false`, but its `Position` setter works). The
///   position is clamped to `0..=length`; the C# setter compared the *old* position against
///   `length` (bug).
#[derive(Debug)]
pub struct SubStream<S> {
    base: S,
    base_offset: u64,
    length: u64,
    position: u64,
}

impl<S: Read + Seek> SubStream<S> {
    /// Creates the window. Fails if the base stream cannot seek to `offset`.
    pub fn new(mut base: S, offset: u64, length: u64) -> io::Result<Self> {
        base.seek(SeekFrom::Start(offset))?;
        Ok(Self {
            base,
            base_offset: offset,
            length,
            position: 0,
        })
    }
}

impl<S> SubStream<S> {
    /// Length of the window (C# `Length`).
    pub fn len(&self) -> u64 {
        self.length
    }

    pub fn is_empty(&self) -> bool {
        self.length == 0
    }

    /// Current position inside the window (C# `Position`).
    pub fn position(&self) -> u64 {
        self.position
    }

    /// Offset of the window inside the base stream.
    pub fn base_offset(&self) -> u64 {
        self.base_offset
    }

    pub fn into_inner(self) -> S {
        self.base
    }
}

impl<S: Read + Seek> Read for SubStream<S> {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        let remaining = self.length.saturating_sub(self.position);
        if remaining == 0 || buf.is_empty() {
            return Ok(0);
        }
        let count = usize::try_from(remaining).map_or(buf.len(), |r| r.min(buf.len()));
        self.base
            .seek(SeekFrom::Start(self.base_offset + self.position))?;
        let read = self.base.read(&mut buf[..count])?;
        self.position += read as u64;
        Ok(read)
    }
}

impl<S: Read + Seek> Seek for SubStream<S> {
    fn seek(&mut self, pos: SeekFrom) -> io::Result<u64> {
        let target: i128 = match pos {
            SeekFrom::Start(p) => i128::from(p),
            SeekFrom::Current(d) => i128::from(self.position) + i128::from(d),
            SeekFrom::End(d) => i128::from(self.length) + i128::from(d),
        };
        if target < 0 {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                "seek before start of SubStream",
            ));
        }
        self.position = u64::try_from(target).unwrap_or(u64::MAX).min(self.length);
        Ok(self.position)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;

    #[test]
    fn reads_only_window() {
        let data: Vec<u8> = (0u8..100).collect();
        let mut s = SubStream::new(Cursor::new(data), 10, 5).unwrap();
        let mut out = Vec::new();
        s.read_to_end(&mut out).unwrap();
        assert_eq!(out, vec![10, 11, 12, 13, 14]);
        assert_eq!(s.position(), 5);
    }

    #[test]
    fn seek_is_clamped_and_shared_base_is_safe() {
        let data: Vec<u8> = (0u8..100).collect();
        let mut base = Cursor::new(data);
        {
            let mut s = SubStream::new(&mut base, 50, 10).unwrap();
            assert_eq!(s.seek(SeekFrom::End(100)).unwrap(), 10);
            assert_eq!(s.seek(SeekFrom::Start(8)).unwrap(), 8);
            let mut b = [0u8; 4];
            assert_eq!(s.read(&mut b).unwrap(), 2);
            assert_eq!(&b[..2], &[58, 59]);
            assert!(s.seek(SeekFrom::Current(-20)).is_err());
        }
        // Moving the base between reads doesn't matter.
        let mut s = SubStream::new(&mut base, 20, 4).unwrap();
        let mut b = [0u8; 2];
        s.read_exact(&mut b).unwrap();
        s.base.set_position(0);
        s.read_exact(&mut b).unwrap();
        assert_eq!(b, [22, 23]);
    }
}
