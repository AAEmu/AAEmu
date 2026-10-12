use crate::format::{load_bytes, valid_chunk_size};
use crate::{hash, invalid, Chunk, Manifest, Pak, Result, Segment};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::HashMap;
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Write};
use std::path::{Path, PathBuf};

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Receipt {
    pub version: u32,
    pub base_manifest_sha256: String,
    pub new_manifest_sha256: String,
    pub new_segments: Vec<String>,
}

pub(crate) const MAX_RECEIPT_BYTES: u64 = 16 * 1024 * 1024;

pub(crate) fn read_receipt(reader: impl Read) -> Result<Receipt> {
    let mut bytes = Vec::new();
    reader.take(MAX_RECEIPT_BYTES + 1).read_to_end(&mut bytes)?;
    if bytes.len() as u64 > MAX_RECEIPT_BYTES {
        return Err(invalid("receipt too large"));
    }
    Ok(serde_json::from_slice(&bytes)?)
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ApplyResult {
    Applied,
    AlreadyApplied,
}

struct Temporary {
    path: PathBuf,
    file: File,
}
impl Temporary {
    fn create(dir: &Path) -> Result<Self> {
        for _ in 0..16 {
            let path = dir.join(format!(".aaemu-pak-{:016x}.tmp", rand::random::<u64>()));
            match OpenOptions::new()
                .read(true)
                .write(true)
                .create_new(true)
                .open(&path)
            {
                Ok(file) => return Ok(Self { path, file }),
                Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => continue,
                Err(e) => return Err(e.into()),
            }
        }
        Err(invalid("cannot allocate unique staging file"))
    }
}
impl Drop for Temporary {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.path);
    }
}

pub(crate) struct UpdateLock {
    _file: File,
}
impl UpdateLock {
    pub(crate) fn acquire(dir: &Path) -> Result<Self> {
        let path = dir.join(".aaemu-pak-update.lock");
        let mut options = OpenOptions::new();
        options.read(true).write(true).create(true).truncate(false);
        #[cfg(windows)]
        {
            use std::os::windows::fs::OpenOptionsExt;
            options.share_mode(0);
        }
        let file = options.open(path)?;
        #[cfg(unix)]
        {
            use std::os::fd::AsRawFd;
            extern "C" {
                fn flock(fd: i32, operation: i32) -> i32;
            }
            // LOCK_EX | LOCK_NB: fail immediately if another process owns the lock.
            if unsafe { flock(file.as_raw_fd(), 2 | 4) } != 0 {
                return Err(std::io::Error::last_os_error().into());
            }
        }
        #[cfg(not(any(windows, unix)))]
        return Err(invalid("OS writer locking requires Windows or Unix"));
        #[cfg(any(windows, unix))]
        Ok(Self { _file: file })
    }
}

fn reader_hash(mut reader: impl Read) -> Result<([u8; 32], u64)> {
    let mut sha = Sha256::new();
    let mut bytes = 0;
    let mut buffer = [0u8; 65536];
    loop {
        let n = reader.read(&mut buffer)?;
        if n == 0 {
            break;
        }
        bytes += n as u64;
        sha.update(&buffer[..n]);
    }
    Ok((sha.finalize().into(), bytes))
}

fn file_hash(path: &Path) -> Result<([u8; 32], u64)> {
    reader_hash(File::open(path)?)
}

fn segment_read_limit(segment: &Segment) -> Result<u64> {
    segment
        .stored_length
        .checked_add(1)
        .ok_or_else(|| invalid("segment length overflow"))
}

pub(crate) fn check_segment_reader(reader: impl Read, segment: &Segment) -> Result<()> {
    let limit = segment_read_limit(segment)?;
    if reader_hash(reader.take(limit))? != (segment.hash, segment.stored_length) {
        return Err(invalid("segment content/length hash mismatch"));
    }
    Ok(())
}

fn check_segment(path: &Path, segment: &Segment) -> Result<()> {
    check_segment_reader(File::open(path)?, segment)
}

fn publish_segment(temp: &mut Temporary, dir: &Path) -> Result<Segment> {
    publish_segment_expected(temp, dir, None)
}

fn publish_segment_expected(
    temp: &mut Temporary,
    dir: &Path,
    expected: Option<&Segment>,
) -> Result<Segment> {
    temp.file.flush()?;
    temp.file.sync_all()?;
    let segment = if let Some(expected) = expected {
        check_segment(&temp.path, expected)?;
        expected.clone()
    } else {
        let (sha, length) = file_hash(&temp.path)?;
        Segment {
            hash: sha,
            stored_length: length,
        }
    };
    let target = dir.join(segment.filename());
    if target.exists() {
        check_segment(&target, &segment)?;
    } else {
        // A hard link atomically publishes the complete staging file without replacing
        // any immutable blob that another writer may have published concurrently.
        match fs::hard_link(&temp.path, &target) {
            Ok(()) => (),
            Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {
                check_segment(&target, &segment)?
            }
            Err(e) => return Err(e.into()),
        }
    }
    Ok(segment)
}

pub(crate) fn stage_segment(source: impl Read, dir: &Path, segment: &Segment) -> Result<Segment> {
    let limit = segment_read_limit(segment)?;
    let mut temp = Temporary::create(dir)?;
    let copied = std::io::copy(&mut source.take(limit), &mut temp.file)?;
    if copied != segment.stored_length {
        return Err(invalid("segment changed during staging"));
    }
    // Check the exact staged bytes before publishing any content-addressed blob.
    publish_segment_expected(&mut temp, dir, Some(segment))
}

#[cfg(windows)]
fn replace(source: &Path, target: &Path) -> Result<()> {
    use std::os::windows::ffi::OsStrExt;
    #[link(name = "kernel32")]
    extern "system" {
        fn MoveFileExW(source: *const u16, target: *const u16, flags: u32) -> i32;
    }
    let source: Vec<u16> = source.as_os_str().encode_wide().chain(Some(0)).collect();
    let target: Vec<u16> = target.as_os_str().encode_wide().chain(Some(0)).collect();
    // Same-directory move, replacement plus WRITE_THROUGH. Blob files are never replaced.
    if unsafe { MoveFileExW(source.as_ptr(), target.as_ptr(), 1 | 8) } == 0 {
        return Err(std::io::Error::last_os_error().into());
    }
    Ok(())
}
#[cfg(not(windows))]
fn replace(source: &Path, target: &Path) -> Result<()> {
    fs::rename(source, target)?;
    Ok(())
}

fn atomic_write(path: &Path, bytes: &[u8]) -> Result<()> {
    let dir = path
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
        .unwrap_or_else(|| Path::new("."));
    let mut temp = Temporary::create(dir)?;
    temp.file.write_all(bytes)?;
    temp.file.flush()?;
    temp.file.sync_all()?;
    replace(&temp.path, path)?;
    #[cfg(unix)]
    File::open(dir)?.sync_all()?;
    Ok(())
}

fn encode_source(
    source: &Path,
    dir: &Path,
    chunk_size: u32,
    base: Option<&Manifest>,
) -> Result<(Manifest, Vec<String>)> {
    valid_chunk_size(chunk_size)?;
    let mut file = File::open(source)?;
    let length = file.metadata()?.len();
    let count = length.div_ceil(u64::from(chunk_size));
    if length > i64::MAX as u64 || count > u64::from(u32::MAX) || count * 56 + 40 > 64 * 1024 * 1024
    {
        return Err(invalid("source exceeds supported format/manifest bounds"));
    }
    let mut temp = Temporary::create(dir)?;
    let mut segments = Vec::new();
    let mut segment_map = HashMap::new();
    let mut chunks = Vec::new();
    // Prefer an unchanged logical offset below. For moved or duplicated content,
    // retain the first matching base chunk in logical order, independent of map order.
    let mut base_chunks = HashMap::new();
    if let Some(base) = base {
        for chunk in &base.chunks {
            base_chunks
                .entry((chunk.hash, chunk.decoded))
                .or_insert(chunk);
        }
    }
    let mut whole = Sha256::new();
    let mut remaining = length;
    let mut stored_offset = 0u64;
    while remaining > 0 {
        let decoded = remaining.min(u64::from(chunk_size)) as u32;
        let mut bytes = vec![0; decoded as usize];
        file.read_exact(&mut bytes)?;
        whole.update(&bytes);
        let sha = hash(&bytes);
        let previous = base
            .and_then(|m| m.chunks.get(chunks.len()))
            .filter(|c| c.decoded == decoded && c.hash == sha)
            .or_else(|| base_chunks.get(&(sha, decoded)).copied());
        if let Some(previous) = previous {
            let base = base.ok_or_else(|| invalid("missing base manifest"))?;
            let segment = &base.segments[previous.segment as usize];
            let mapped = *segment_map.entry(segment.hash).or_insert_with(|| {
                let index = segments.len() as u32;
                segments.push(segment.clone());
                index
            });
            let mut chunk = previous.clone();
            chunk.segment = mapped;
            chunks.push(chunk);
        } else {
            let compressed = zstd::bulk::compress(&bytes, 3)?;
            let (codec, stored) = if compressed.len() < bytes.len() {
                (1, compressed)
            } else {
                (0, bytes)
            };
            temp.file.write_all(&stored)?;
            chunks.push(Chunk {
                segment: u32::MAX,
                codec,
                offset: stored_offset,
                stored: stored.len() as u32,
                decoded,
                hash: sha,
            });
            stored_offset += stored.len() as u64;
        }
        remaining -= u64::from(decoded);
    }
    if file.read(&mut [0u8; 1])? != 0 || file.metadata()?.len() != length {
        return Err(invalid("source size changed while building"));
    }
    let mut new_segments = Vec::new();
    if stored_offset > 0 {
        let segment = publish_segment(&mut temp, dir)?;
        let index = *segment_map.entry(segment.hash).or_insert_with(|| {
            let index = segments.len() as u32;
            segments.push(segment.clone());
            index
        });
        for chunk in &mut chunks {
            if chunk.segment == u32::MAX {
                chunk.segment = index;
            }
        }
        if base.is_none_or(|m| !m.segments.iter().any(|s| s.hash == segment.hash)) {
            new_segments.push(segment.filename());
        }
    }
    let logical_hash = whole.finalize().into();
    if let Some(base) = base.filter(|base| {
        stored_offset == 0 && base.logical_size == length && base.logical_hash == logical_hash
    }) {
        return Ok((base.clone(), new_segments));
    }
    let manifest = Manifest {
        chunk_size,
        logical_size: length,
        logical_hash,
        segments,
        chunks,
    };
    manifest.validate()?;
    Ok((manifest, new_segments))
}

/// Converts a raw source without changing any logical bytes. Refuses an existing manifest.
pub fn build(
    source: impl AsRef<Path>,
    directory: impl AsRef<Path>,
    chunk_size: u32,
) -> Result<PathBuf> {
    let dir = directory.as_ref();
    fs::create_dir_all(dir)?;
    let _lock = UpdateLock::acquire(dir)?;
    let path = dir.join("game.aacm");
    if path.exists() {
        return Err(invalid("manifest already exists; use diff/apply"));
    }
    let (manifest, _) = encode_source(source.as_ref(), dir, chunk_size, None)?;
    atomic_write(&path, &manifest.encode()?)?;
    Ok(path)
}

/// Packages new chunks only. Identical base chunks retain their old blobs, preferring
/// the same logical offset, then the first matching hash and decoded length anywhere.
pub fn diff(
    source: impl AsRef<Path>,
    base_manifest: impl AsRef<Path>,
    directory: impl AsRef<Path>,
) -> Result<PathBuf> {
    let base_path = base_manifest.as_ref();
    let base_bytes = load_bytes(base_path)?;
    let base = Manifest::decode(&base_bytes)?;
    Pak::open(base_path)?.verify()?;
    let dir = directory.as_ref();
    fs::create_dir_all(dir)?;
    let _lock = UpdateLock::acquire(dir)?;
    let path = dir.join("game.aacm");
    if path.exists() || dir.join("receipt.json").exists() {
        return Err(invalid("patch destination already contains a package"));
    }
    let (manifest, new_segments) =
        encode_source(source.as_ref(), dir, base.chunk_size, Some(&base))?;
    write_package(dir, &base_bytes, &manifest, new_segments)
}

fn write_package(
    dir: &Path,
    base_bytes: &[u8],
    manifest: &Manifest,
    new_segments: Vec<String>,
) -> Result<PathBuf> {
    let bytes = manifest.encode()?;
    let receipt = Receipt {
        version: 1,
        base_manifest_sha256: hex::encode(hash(base_bytes)),
        new_manifest_sha256: hex::encode(hash(&bytes)),
        new_segments,
    };
    let path = dir.join("game.aacm");
    atomic_write(&path, &bytes)?;
    atomic_write(
        &dir.join("receipt.json"),
        &serde_json::to_vec_pretty(&receipt)?,
    )?;
    Ok(path)
}

pub(crate) const MAX_TRACE_BYTES: u64 = 64 * 1024 * 1024;
pub(crate) const MAX_TRACE_ROWS: usize = 1_000_000;

fn trace_integer(text: &str) -> Result<u64> {
    if text.is_empty() || !text.bytes().all(|b| b.is_ascii_digit()) {
        return Err(invalid("trace integer must contain only decimal digits"));
    }
    text.parse().map_err(|_| invalid("trace integer overflow"))
}

fn traced_chunks(path: &Path, manifest: &Manifest) -> Result<Vec<bool>> {
    let file = File::open(path)?;
    let length = file.metadata()?.len();
    if length > MAX_TRACE_BYTES {
        return Err(invalid("trace exceeds 64 MiB limit"));
    }
    let mut bytes = Vec::new();
    file.take(MAX_TRACE_BYTES + 1).read_to_end(&mut bytes)?;
    if bytes.len() as u64 != length {
        return Err(invalid("trace size changed while reading"));
    }
    let text = std::str::from_utf8(&bytes).map_err(|_| invalid("trace must be UTF-8 CSV"))?;
    let mut lines = text.lines();
    if lines.next() != Some("offset,bytes,microseconds,thread") {
        return Err(invalid(
            "trace header must be offset,bytes,microseconds,thread",
        ));
    }
    // Range endpoints make selection linear in trace rows plus chunk count, even
    // when many large reads overlap the same chunks.
    let mut endpoints = vec![0i32; manifest.chunks.len() + 1];
    for (row, line) in lines.enumerate() {
        if row >= MAX_TRACE_ROWS || line.len() > 256 {
            return Err(invalid("trace exceeds row count or line length limit"));
        }
        let fields: Vec<_> = line.split(',').collect();
        if fields.len() != 4 {
            return Err(invalid("trace row must have four unquoted numeric fields"));
        }
        let offset = trace_integer(fields[0])?;
        let count = trace_integer(fields[1])?;
        let duration = fields[2];
        if duration.is_empty()
            || !duration.bytes().all(|b| b.is_ascii_digit() || b == b'.')
            || duration
                .parse::<f64>()
                .ok()
                .is_none_or(|v| !v.is_finite() || v < 0.0)
            || trace_integer(fields[3])? > u64::from(u32::MAX)
            || count > u64::from(u32::MAX)
        {
            return Err(invalid("invalid trace duration, byte count or thread id"));
        }
        if count == 0 {
            continue;
        }
        let end = offset
            .checked_add(count)
            .filter(|end| *end <= manifest.logical_size)
            .ok_or_else(|| invalid("trace read exceeds logical pak bounds"))?;
        let first = (offset / u64::from(manifest.chunk_size)) as usize;
        let after = ((end - 1) / u64::from(manifest.chunk_size)) as usize + 1;
        endpoints[first] += 1;
        endpoints[after] -= 1;
    }
    let mut active = 0;
    Ok(endpoints[..manifest.chunks.len()]
        .iter()
        .map(|endpoint| {
            active += endpoint;
            active > 0
        })
        .collect())
}

/// Packages raw storage for traced compressed chunks without changing logical bytes.
/// Cold chunks retain their immutable segments. Trace input is bounded native CSV;
/// applying the resulting receipt uses the same offline verification as any update.
pub fn profile(
    base_manifest: impl AsRef<Path>,
    read_trace: impl AsRef<Path>,
    directory: impl AsRef<Path>,
) -> Result<PathBuf> {
    let base_path = base_manifest.as_ref();
    let base_bytes = load_bytes(base_path)?;
    let base = Manifest::decode(&base_bytes)?;
    let selected = traced_chunks(read_trace.as_ref(), &base)?;
    let pak = Pak::from_manifest(
        base.clone(),
        base_path.parent().unwrap_or_else(|| Path::new(".")),
    )?;
    pak.verify()?;
    let dir = directory.as_ref();
    fs::create_dir_all(dir)?;
    let _lock = UpdateLock::acquire(dir)?;
    if dir.join("game.aacm").exists() || dir.join("receipt.json").exists() {
        return Err(invalid("patch destination already contains a package"));
    }
    if !base
        .chunks
        .iter()
        .zip(&selected)
        .any(|(chunk, hot)| *hot && chunk.codec != 0)
    {
        return write_package(dir, &base_bytes, &base, Vec::new());
    }
    let mut temp = Temporary::create(dir)?;
    let mut manifest = base.clone();
    let mut stored_offset = 0;
    for (index, (chunk, hot)) in manifest.chunks.iter_mut().zip(selected).enumerate() {
        if !hot || chunk.codec == 0 {
            continue;
        }
        let mut decoded = vec![0; chunk.decoded as usize];
        if pak.read_at(index as u64 * u64::from(base.chunk_size), &mut decoded)? != decoded.len() {
            return Err(invalid("short profile chunk read"));
        }
        temp.file.write_all(&decoded)?;
        chunk.segment = u32::MAX;
        chunk.codec = 0;
        chunk.offset = stored_offset;
        chunk.stored = chunk.decoded;
        stored_offset += u64::from(chunk.decoded);
    }
    let raw_segment = publish_segment(&mut temp, dir)?;
    let mut segments = Vec::new();
    let mut segment_map = HashMap::new();
    for chunk in &mut manifest.chunks {
        let segment = if chunk.segment == u32::MAX {
            &raw_segment
        } else {
            &base.segments[chunk.segment as usize]
        };
        chunk.segment = *segment_map.entry(segment.hash).or_insert_with(|| {
            let index = segments.len() as u32;
            segments.push(segment.clone());
            index
        });
    }
    manifest.segments = segments;
    let new_segments = if base.segments.iter().any(|s| s.hash == raw_segment.hash) {
        Vec::new()
    } else {
        vec![raw_segment.filename()]
    };
    write_package(dir, &base_bytes, &manifest, new_segments)
}

fn digest_text(text: &str) -> Result<[u8; 32]> {
    if text.len() != 64
        || !text
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    {
        return Err(invalid("receipt digest must be lowercase SHA256 hex"));
    }
    let bytes = hex::decode(text).map_err(|_| invalid("invalid receipt digest"))?;
    bytes
        .try_into()
        .map_err(|_| invalid("invalid receipt digest length"))
}

/// Stages and verifies immutable blobs, then atomically switches the manifest. Old blobs
/// remain for rollback. Windows rejects native clients holding the manifest with read-only
/// sharing. Callers on other platforms must stop clients before applying updates.
pub fn apply(
    installed_manifest: impl AsRef<Path>,
    package: impl AsRef<Path>,
) -> Result<ApplyResult> {
    apply_before_publication(installed_manifest.as_ref(), package.as_ref(), || Ok(()))
}

// The callback is internal; tests use it to force both publication/client-open race
// outcomes at the exact boundary, without depending on scheduler timing.
pub(crate) fn apply_before_publication(
    installed: &Path,
    package: &Path,
    before_publication: impl FnOnce() -> Result<()>,
) -> Result<ApplyResult> {
    apply_with_hooks(installed, package, || Ok(()), before_publication)
}

#[cfg(test)]
pub(crate) fn apply_before_staging(
    installed: &Path,
    package: &Path,
    before_staging: impl FnOnce() -> Result<()>,
) -> Result<ApplyResult> {
    apply_with_hooks(installed, package, before_staging, || Ok(()))
}

fn apply_with_hooks(
    installed: &Path,
    package: &Path,
    before_staging: impl FnOnce() -> Result<()>,
    before_publication: impl FnOnce() -> Result<()>,
) -> Result<ApplyResult> {
    let dir = installed
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
        .unwrap_or_else(|| Path::new("."));
    // Native clients pin this file with GENERIC_READ/FILE_SHARE_READ. Requesting write
    // access fails while any client is alive, and blocks a new client during verification.
    #[cfg(windows)]
    let manifest_guard = OpenOptions::new().read(true).write(true).open(installed)?;
    let _lock = UpdateLock::acquire(dir)?;
    let receipt = read_receipt(File::open(package.join("receipt.json"))?)?;
    if receipt.version != 1 {
        return Err(invalid("unsupported receipt version"));
    }
    let base_sha = digest_text(&receipt.base_manifest_sha256)?;
    let new_sha = digest_text(&receipt.new_manifest_sha256)?;
    let new_path = package.join("game.aacm");
    let new_bytes = load_bytes(&new_path)?;
    let candidate = Manifest::decode(&new_bytes)?;
    if hash(&new_bytes) != new_sha {
        return Err(invalid("package manifest identity mismatch"));
    }
    let current_bytes = load_bytes(installed)?;
    let current_sha = hash(&current_bytes);
    if current_sha != base_sha && current_sha != new_sha {
        return Err(invalid("update base manifest identity mismatch"));
    }
    let mut seen = std::collections::HashSet::new();
    for name in &receipt.new_segments {
        let digest = name
            .strip_suffix(".aacb")
            .ok_or_else(|| invalid("invalid receipt segment filename"))?;
        let sha = digest_text(digest)?;
        if !seen.insert(sha) {
            return Err(invalid("duplicate receipt segment"));
        }
        let segment = candidate
            .segments
            .iter()
            .find(|s| s.hash == sha)
            .ok_or_else(|| invalid("receipt segment absent from manifest"))?;
        check_segment(&package.join(name), segment)?;
    }
    if current_sha == new_sha {
        Pak::open(installed)?.verify()?;
        return Ok(ApplyResult::AlreadyApplied);
    }
    before_staging()?;
    for name in &receipt.new_segments {
        let sha = digest_text(
            name.strip_suffix(".aacb")
                .ok_or_else(|| invalid("invalid segment filename"))?,
        )?;
        let segment = candidate
            .segments
            .iter()
            .find(|s| s.hash == sha)
            .ok_or_else(|| invalid("missing receipt segment"))?;
        let target = dir.join(name);
        if target.exists() {
            check_segment(&target, segment)?;
        } else {
            stage_segment(File::open(package.join(name))?, dir, segment)?;
        }
    }
    Pak::from_manifest(candidate, dir)?.verify()?;
    // Recheck under our exclusive update lock before the sole publication point.
    if hash(&load_bytes(installed)?) != base_sha {
        return Err(invalid("installed manifest changed during update"));
    }
    let rollback = dir.join(format!("{}.aacm", hex::encode(base_sha)));
    if rollback.exists() {
        if load_bytes(&rollback)? != current_bytes {
            return Err(invalid("rollback manifest content mismatch"));
        }
    } else {
        atomic_write(&rollback, &current_bytes)?;
    }
    // Windows refuses replacement while this read/write handle is open, even with
    // share-delete. Release only after verification, identity recheck and rollback save.
    // A native client winning this narrow gap denies delete/rename on the old manifest,
    // so publication fails unchanged; if replacement wins, it opens the verified new one.
    #[cfg(windows)]
    drop(manifest_guard);
    before_publication()?;
    atomic_write(installed, &new_bytes)?;
    Ok(ApplyResult::Applied)
}
