use crate::{hash, invalid, Result};
use std::path::Path;

pub const DEFAULT_CHUNK_SIZE: u32 = 262_144;
const HEADER_BYTES: usize = 128;
const MAX_BODY: u64 = 64 * 1024 * 1024;

pub(crate) fn load_bytes(path: &Path) -> Result<Vec<u8>> {
    use std::io::Read;
    let file = std::fs::File::open(path)?;
    let length = file.metadata()?.len();
    if length < HEADER_BYTES as u64 || length > MAX_BODY + HEADER_BYTES as u64 {
        return Err(invalid("manifest length is outside supported bounds"));
    }
    let mut bytes = Vec::new();
    file.take(length + 1).read_to_end(&mut bytes)?;
    if bytes.len() as u64 != length {
        return Err(invalid("manifest changed while reading"));
    }
    Ok(bytes)
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Segment {
    pub hash: [u8; 32],
    pub stored_length: u64,
}

impl Segment {
    pub fn filename(&self) -> String {
        format!("{}.aacb", hex::encode(self.hash))
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Chunk {
    pub segment: u32,
    /// 0: original bytes, 1: independently decodable zstd frame.
    pub codec: u32,
    pub offset: u64,
    pub stored: u32,
    pub decoded: u32,
    pub hash: [u8; 32],
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Manifest {
    pub chunk_size: u32,
    pub logical_size: u64,
    pub logical_hash: [u8; 32],
    pub segments: Vec<Segment>,
    pub chunks: Vec<Chunk>,
}

fn u32_at(b: &[u8], at: usize) -> u32 {
    u32::from_le_bytes(b[at..at + 4].try_into().expect("validated record length"))
}
fn u64_at(b: &[u8], at: usize) -> u64 {
    u64::from_le_bytes(b[at..at + 8].try_into().expect("validated record length"))
}

pub(crate) fn valid_chunk_size(size: u32) -> Result<()> {
    if !(4096..=4_194_304).contains(&size) || !size.is_power_of_two() {
        return Err(invalid(
            "chunk size must be a power of two from 4096 to 4194304",
        ));
    }
    Ok(())
}

impl Manifest {
    pub fn load(path: impl AsRef<Path>) -> Result<Self> {
        let bytes = load_bytes(path.as_ref())?;
        Self::decode(&bytes)
    }

    pub fn validate(&self) -> Result<()> {
        valid_chunk_size(self.chunk_size)?;
        if self.logical_size > i64::MAX as u64 {
            return Err(invalid("logical size exceeds signed client file offsets"));
        }
        let expected = self.logical_size.div_ceil(u64::from(self.chunk_size));
        if expected != self.chunks.len() as u64 || expected > u64::from(u32::MAX) {
            return Err(invalid("chunk count does not match logical size"));
        }
        if self.segments.len() > self.chunks.len() || self.segments.len() > u32::MAX as usize {
            return Err(invalid("segment count exceeds chunk count"));
        }
        let mut hashes = std::collections::HashSet::new();
        for segment in &self.segments {
            if segment.stored_length == 0 || !hashes.insert(segment.hash) {
                return Err(invalid("empty or duplicate segment"));
            }
        }
        let mut used = vec![false; self.segments.len()];
        for (i, chunk) in self.chunks.iter().enumerate() {
            let segment = self
                .segments
                .get(chunk.segment as usize)
                .ok_or_else(|| invalid("chunk segment is out of range"))?;
            used[chunk.segment as usize] = true;
            let remaining = self.logical_size - i as u64 * u64::from(self.chunk_size);
            let decoded = remaining.min(u64::from(self.chunk_size));
            if u64::from(chunk.decoded) != decoded
                || chunk.stored == 0
                || chunk.stored > chunk.decoded
            {
                return Err(invalid("chunk decoded/stored length is invalid"));
            }
            if chunk.codec > 1 || (chunk.codec == 0 && chunk.stored != chunk.decoded) {
                return Err(invalid("unknown codec or invalid raw chunk length"));
            }
            if chunk
                .offset
                .checked_add(u64::from(chunk.stored))
                .is_none_or(|end| end > segment.stored_length)
            {
                return Err(invalid("chunk extends beyond its segment"));
            }
        }
        if used.iter().any(|used| !used) {
            return Err(invalid("manifest contains unreferenced segments"));
        }
        Ok(())
    }

    pub fn encode(&self) -> Result<Vec<u8>> {
        self.validate()?;
        let mut body = Vec::new();
        for segment in &self.segments {
            body.extend_from_slice(&segment.hash);
            body.extend_from_slice(&segment.stored_length.to_le_bytes());
        }
        for chunk in &self.chunks {
            body.extend_from_slice(&chunk.segment.to_le_bytes());
            body.extend_from_slice(&chunk.codec.to_le_bytes());
            body.extend_from_slice(&chunk.offset.to_le_bytes());
            body.extend_from_slice(&chunk.stored.to_le_bytes());
            body.extend_from_slice(&chunk.decoded.to_le_bytes());
            body.extend_from_slice(&chunk.hash);
        }
        if body.len() as u64 > MAX_BODY {
            return Err(invalid("manifest body too large"));
        }
        let mut header = vec![0u8; HEADER_BYTES];
        header[..8].copy_from_slice(b"AAEMPK01");
        header[8..12].copy_from_slice(&1u32.to_le_bytes());
        header[12..16].copy_from_slice(&128u32.to_le_bytes());
        header[16..20].copy_from_slice(&self.chunk_size.to_le_bytes());
        header[20..24].copy_from_slice(&(self.chunks.len() as u32).to_le_bytes());
        header[24..32].copy_from_slice(&self.logical_size.to_le_bytes());
        header[32..36].copy_from_slice(&(self.segments.len() as u32).to_le_bytes());
        header[40..72].copy_from_slice(&self.logical_hash);
        header[72..80].copy_from_slice(&(body.len() as u64).to_le_bytes());
        header[80..112].copy_from_slice(&hash(&body));
        header.extend_from_slice(&body);
        Ok(header)
    }

    pub fn decode(bytes: &[u8]) -> Result<Self> {
        if bytes.len() < HEADER_BYTES || &bytes[..8] != b"AAEMPK01" {
            return Err(invalid("bad manifest magic/header"));
        }
        if u32_at(bytes, 8) != 1
            || u32_at(bytes, 12) != 128
            || u32_at(bytes, 36) != 0
            || bytes[112..128].iter().any(|b| *b != 0)
        {
            return Err(invalid("unsupported version, flags or reserved fields"));
        }
        let count = u32_at(bytes, 20) as u64;
        let segments = u32_at(bytes, 32) as u64;
        let body_length = segments * 40 + count * 56;
        if body_length > MAX_BODY
            || body_length != u64_at(bytes, 72)
            || body_length + 128 != bytes.len() as u64
        {
            return Err(invalid("invalid body/record lengths"));
        }
        if hash(&bytes[128..]) != bytes[80..112] {
            return Err(invalid("manifest body hash mismatch"));
        }
        let mut out = Self {
            chunk_size: u32_at(bytes, 16),
            logical_size: u64_at(bytes, 24),
            logical_hash: bytes[40..72].try_into().expect("validated header"),
            segments: Vec::new(),
            chunks: Vec::new(),
        };
        let mut at = HEADER_BYTES;
        for _ in 0..segments {
            out.segments.push(Segment {
                hash: bytes[at..at + 32].try_into().expect("validated segment"),
                stored_length: u64_at(bytes, at + 32),
            });
            at += 40;
        }
        for _ in 0..count {
            out.chunks.push(Chunk {
                segment: u32_at(bytes, at),
                codec: u32_at(bytes, at + 4),
                offset: u64_at(bytes, at + 8),
                stored: u32_at(bytes, at + 16),
                decoded: u32_at(bytes, at + 20),
                hash: bytes[at + 24..at + 56].try_into().expect("validated chunk"),
            });
            at += 56;
        }
        out.validate()?;
        Ok(out)
    }
}
