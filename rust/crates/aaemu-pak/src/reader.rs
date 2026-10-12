use crate::{hash, invalid, Manifest, Result};
use sha2::{Digest, Sha256};
use std::collections::HashMap;
use std::fs::File;
use std::io;
use std::path::Path;
use std::sync::{Arc, Mutex};

pub const CACHE_BYTES: usize = 32 * 1024 * 1024;

#[derive(Default)]
struct Cache {
    entries: HashMap<usize, (Arc<Vec<u8>>, u64)>,
    bytes: usize,
    tick: u64,
}
impl Cache {
    fn get(&mut self, index: usize) -> Option<Arc<Vec<u8>>> {
        self.tick = self.tick.wrapping_add(1);
        self.entries.get_mut(&index).map(|(data, tick)| {
            *tick = self.tick;
            Arc::clone(data)
        })
    }
    fn publish(&mut self, index: usize, data: Arc<Vec<u8>>) -> Result<Arc<Vec<u8>>> {
        // Concurrent duplicate decodes never duplicate cache entries.
        if let Some(existing) = self.get(index) {
            return Ok(existing);
        }
        while self.bytes + data.len() > CACHE_BYTES {
            let oldest = self
                .entries
                .iter()
                .min_by_key(|(_, (_, tick))| *tick)
                .map(|(index, _)| *index)
                .ok_or_else(|| invalid("cache accounting failed"))?;
            if let Some((removed, _)) = self.entries.remove(&oldest) {
                self.bytes -= removed.len();
            }
        }
        self.bytes += data.len();
        self.entries.insert(index, (Arc::clone(&data), self.tick));
        Ok(data)
    }
}

/// Immutable segment handles and positional I/O let independent game threads decode in
/// parallel. Cache locks cover lookup/publication only, never disk, SHA or zstd work.
pub struct Pak {
    pub manifest: Manifest,
    files: Vec<File>,
    cache: Mutex<Cache>,
}

fn read_some_at(file: &File, buffer: &mut [u8], offset: u64) -> io::Result<usize> {
    #[cfg(windows)]
    {
        use std::os::windows::fs::FileExt;
        file.seek_read(buffer, offset)
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::FileExt;
        file.read_at(buffer, offset)
    }
    #[cfg(not(any(windows, unix)))]
    {
        let _ = (file, buffer, offset);
        Err(io::Error::new(
            io::ErrorKind::Unsupported,
            "positional pak I/O requires Windows or Unix",
        ))
    }
}
fn read_exact_at(file: &File, mut buffer: &mut [u8], mut offset: u64) -> io::Result<()> {
    while !buffer.is_empty() {
        match read_some_at(file, buffer, offset) {
            Ok(0) => return Err(io::ErrorKind::UnexpectedEof.into()),
            Ok(n) => {
                offset = offset.checked_add(n as u64).ok_or_else(|| {
                    io::Error::new(io::ErrorKind::InvalidInput, "positional read overflow")
                })?;
                buffer = &mut buffer[n..];
            }
            Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
            Err(error) => return Err(error),
        }
    }
    Ok(())
}
impl Pak {
    pub fn open(path: impl AsRef<Path>) -> Result<Self> {
        let path = path.as_ref();
        let manifest = Manifest::load(path)?;
        Self::from_manifest(manifest, path.parent().unwrap_or_else(|| Path::new(".")))
    }
    pub(crate) fn from_manifest(manifest: Manifest, parent: &Path) -> Result<Self> {
        manifest.validate()?;
        let mut files = Vec::new();
        for segment in &manifest.segments {
            let file = File::open(parent.join(segment.filename()))?;
            if file.metadata()?.len() != segment.stored_length {
                return Err(invalid("segment file length mismatch"));
            }
            files.push(file);
        }
        Ok(Self {
            manifest,
            files,
            cache: Mutex::new(Cache::default()),
        })
    }
    fn decoded_chunk(&self, index: usize) -> Result<Arc<Vec<u8>>> {
        if let Some(data) = self
            .cache
            .lock()
            .map_err(|_| invalid("pak cache lock poisoned"))?
            .get(index)
        {
            return Ok(data);
        }
        let chunk = &self.manifest.chunks[index];
        let mut stored = vec![0; chunk.stored as usize];
        read_exact_at(
            &self.files[chunk.segment as usize],
            &mut stored,
            chunk.offset,
        )?;
        let decoded = if chunk.codec == 0 {
            stored
        } else {
            zstd::bulk::decompress(&stored, chunk.decoded as usize)?
        };
        if decoded.len() != chunk.decoded as usize || hash(&decoded) != chunk.hash {
            return Err(invalid("decoded chunk length/hash mismatch"));
        }
        self.cache
            .lock()
            .map_err(|_| invalid("pak cache lock poisoned"))?
            .publish(index, Arc::new(decoded))
    }
    pub fn read_at(&self, offset: u64, buffer: &mut [u8]) -> Result<usize> {
        if offset >= self.manifest.logical_size || buffer.is_empty() {
            return Ok(0);
        }
        let count = buffer
            .len()
            .min(usize::try_from(self.manifest.logical_size - offset).unwrap_or(usize::MAX));
        let mut copied = 0;
        while copied < count {
            let position = offset + copied as u64;
            let index = (position / u64::from(self.manifest.chunk_size)) as usize;
            let within = (position % u64::from(self.manifest.chunk_size)) as usize;
            let chunk = self.decoded_chunk(index)?;
            let take = (count - copied).min(chunk.len() - within);
            buffer[copied..copied + take].copy_from_slice(&chunk[within..within + take]);
            copied += take;
        }
        Ok(copied)
    }
    /// Verify stored segments and logical bytes without sharing a file cursor.
    pub fn verify(&self) -> Result<()> {
        *self
            .cache
            .lock()
            .map_err(|_| invalid("pak cache lock poisoned"))? = Cache::default();
        for (segment, file) in self.manifest.segments.iter().zip(&self.files) {
            if file.metadata()?.len() != segment.stored_length {
                return Err(invalid("segment file length changed"));
            }
            let mut sha = Sha256::new();
            let mut buffer = [0u8; 65536];
            let mut offset = 0;
            while offset < segment.stored_length {
                let n = (segment.stored_length - offset).min(buffer.len() as u64) as usize;
                read_exact_at(file, &mut buffer[..n], offset)?;
                sha.update(&buffer[..n]);
                offset += n as u64;
            }
            if <[u8; 32]>::from(sha.finalize()) != segment.hash {
                return Err(invalid("segment content hash mismatch"));
            }
        }
        let mut sha = Sha256::new();
        for index in 0..self.manifest.chunks.len() {
            sha.update(&*self.decoded_chunk(index)?);
        }
        if <[u8; 32]>::from(sha.finalize()) != self.manifest.logical_hash {
            return Err(invalid("whole logical pak hash mismatch"));
        }
        Ok(())
    }
    pub fn cached_bytes(&self) -> Result<usize> {
        Ok(self
            .cache
            .lock()
            .map_err(|_| invalid("pak cache lock poisoned"))?
            .bytes)
    }
}
