//! Port of `AAEmu.Commons.Utils.AAPak` (originally <https://github.com/ZeromusXYZ/AAEmu-Packer>):
//! reader **and writer** for ArcheAge `game_pak` archives.
//!
//! Layout of a pak file (all little endian):
//!
//! ```text
//! [file data ...]
//! [file info table: (fileCount + extraFileCount) entries of 0x150 bytes, each AES-CBC encrypted
//!  on its own with a zero IV]
//! [zero padding up to a 0x200 boundary]
//! [header: 0x200 bytes, AES-CBC encrypted (only the first 0x20 bytes are meaningful)]
//! ```
//!
//! The header identifies the layout: `WIBO` (TypeA), `IDEJ` at offset 8 (TypeB) or `ZERO`
//! (TypeF). A `.csv` file listing (`name;size;offset;md5;...`) can also be opened as a read-only
//! "virtual" pak.
//!
//! Differences from the C# class (behaviour is otherwise kept, including on-disk formats):
//! * Opening returns `Result<AAPak, PakError>` instead of an object with `isOpen == false`;
//!   `AAPakFileHeader.LastAESError` (a global) is replaced by [`PakError::Aes`].
//! * Files are addressed by name instead of passing `AAPakFileInfo` references around; the
//!   C# `Dictionary` enumeration order (including slot reuse after deletes) is emulated so the
//!   file table is written in the same order.
//! * `ReadCSVData` accepted a CSV only when its header line did **not** match the expected
//!   header (inverted check); here a matching header is required.
//! * Reads through [`AAPak::read_file`] / [`AAPak::export_file_as_stream_cloned`] open their own
//!   file handle (C# `ExportFileAsStreamCloned`), so `&AAPak` can be shared between threads.
//! * Both `\` and `/` are accepted as directory separators in lookups (C# only converts the
//!   platform separator).

mod sub_stream;

use std::collections::{HashMap, HashSet};
use std::fs::{File, OpenOptions};
use std::io::{self, Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use aes::cipher::generic_array::GenericArray;
use aes::cipher::{BlockDecrypt, BlockEncrypt, KeyInit};
use chrono::{DateTime, NaiveDate, Utc};
use md5::{Digest, Md5};

pub use sub_stream::SubStream;

/// `AAPakFileHeader.XLGamesKey`: default AES-128 key used by XLGames for the header and FAT.
pub const XL_GAMES_KEY: [u8; 16] = [
    0x32, 0x1F, 0x2A, 0xEE, 0xAA, 0x58, 0x4A, 0xB4, 0x9A, 0x6C, 0x9E, 0x09, 0xD5, 0x9E, 0x9C, 0x6F,
];
/// `AAPakFileHeader.headerSize`.
pub const HEADER_SIZE: usize = 0x200;
/// `AAPakFileHeader.fileInfoSize`.
pub const FILE_INFO_SIZE: usize = 0x150;
/// Size of the file name field inside a file info block.
pub const NAME_SIZE: usize = 0x108;
/// Name used for the "extra" (free space) entries.
pub const UNUSED_FILE_NAME: &str = "__unused__";
/// Expected first line of a CSV virtual pak.
pub const CSV_HEADER: &str =
    "name;size;offset;md5;createTime;modifyTime;sizeDuplicate;paddingSize;dummy1;dummy2";

/// Difference between the FILETIME epoch (1601-01-01) and the Unix epoch, in 100 ns ticks.
const FILETIME_UNIX_DIFF: i64 = 116_444_736_000_000_000;

#[derive(Debug, thiserror::Error)]
pub enum PakError {
    #[error("I/O error: {0}")]
    Io(#[from] io::Error),
    #[error("AES error: {0}")]
    Aes(String),
    #[error("file not found: {0}")]
    NotFound(PathBuf),
    #[error("not a valid pak (unknown header, wrong key, or corrupted)")]
    InvalidHeader,
    #[error("invalid file table offset {0}")]
    InvalidFatOffset(i64),
    #[error("pak contains the file name {0:?} more than once")]
    DuplicateFileName(String),
    #[error("invalid CSV pak: {0}")]
    InvalidCsv(String),
    #[error("pak is not open")]
    NotOpen,
    #[error("pak is read-only")]
    ReadOnly,
    #[error("operation not supported on this pak type: {0:?}")]
    Unsupported(PakFileType),
    #[error("invalid hex string: {0}")]
    InvalidHex(String),
}

pub type Result<T> = std::result::Result<T, PakError>;

/// `PakFileType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum PakFileType {
    /// Header starts with `WIBO`; files first, extra (deleted) files last.
    #[default]
    TypeA,
    /// Header has `IDEJ` at offset 8; extra files first, files last.
    TypeB,
    /// Virtual pak loaded from a CSV listing (read-only, no data).
    Csv,
    /// Header starts with `ZERO`; same ordering as TypeA, different entry layout.
    TypeF,
}

/// `AAPakFileInfo`: one entry of the file table.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PakFileInfo {
    pub name: String,
    pub offset: i64,
    pub size: i64,
    /// Maybe a compressed size; observed always equal to `size`.
    pub size_duplicate: i64,
    /// Free space after the file until the next file (or 0x200 boundary).
    pub padding_size: i32,
    pub md5: [u8; 16],
    /// Mostly 0, or 0x80000000 on TypeB paks; possibly file flags.
    pub dummy1: u32,
    /// Windows FILETIME (UTC).
    pub create_time: i64,
    /// Windows FILETIME (UTC).
    pub modify_time: i64,
    pub dummy2: u64,
    /// Not part of the on-disk structure.
    pub entry_index_number: i32,
    /// Not part of the on-disk structure.
    pub deleted_index_number: i32,
}

impl Default for PakFileInfo {
    fn default() -> Self {
        Self {
            name: String::new(),
            offset: 0,
            size: 0,
            size_duplicate: 0,
            padding_size: 0,
            md5: [0; 16],
            dummy1: 0,
            create_time: 0,
            modify_time: 0,
            dummy2: 0,
            entry_index_number: -1,
            deleted_index_number: -1,
        }
    }
}

impl PakFileInfo {
    /// Decodes a decrypted 0x150-byte entry.
    pub fn from_decrypted(data: &[u8], pak_type: PakFileType) -> Self {
        let mut r = Le { data, pos: 0 };
        let mut pfi = PakFileInfo::default();
        match pak_type {
            PakFileType::TypeA => {
                pfi.name = read_name(&data[..NAME_SIZE]);
                r.pos = 0x108;
                pfi.offset = r.i64();
                pfi.size = r.i64();
                pfi.size_duplicate = r.i64();
                pfi.padding_size = r.i32();
                pfi.md5 = r.md5();
                pfi.dummy1 = r.u32();
                pfi.create_time = r.i64();
                pfi.modify_time = r.i64();
                pfi.dummy2 = r.u64();
            }
            PakFileType::TypeB => {
                pfi.padding_size = r.i32();
                pfi.md5 = r.md5();
                pfi.dummy1 = r.u32();
                pfi.size = r.i64();
                pfi.name = read_name(&data[0x20..0x20 + NAME_SIZE]);
                r.pos = 0x128;
                pfi.size_duplicate = r.i64();
                pfi.offset = r.i64();
                pfi.modify_time = r.i64();
                pfi.create_time = r.i64();
                pfi.dummy2 = r.u64();
            }
            PakFileType::TypeF => {
                pfi.dummy2 = r.u64();
                pfi.name = read_name(&data[8..8 + NAME_SIZE]);
                r.pos = 0x110;
                pfi.offset = r.i64();
                pfi.size = r.i64();
                pfi.size_duplicate = r.i64();
                pfi.padding_size = r.i32();
                pfi.md5 = r.md5();
                pfi.dummy1 = r.u32();
                pfi.create_time = r.i64();
                pfi.modify_time = r.i64();
            }
            PakFileType::Csv => {}
        }
        pfi
    }

    /// Encodes the entry (unencrypted, 0x150 bytes). `None` for [`PakFileType::Csv`].
    pub fn to_decrypted(&self, pak_type: PakFileType) -> Option<[u8; FILE_INFO_SIZE]> {
        let mut w = Vec::with_capacity(FILE_INFO_SIZE);
        let name = |w: &mut Vec<u8>| {
            let mut chars = self.name.chars();
            for _ in 0..NAME_SIZE {
                // C#: (byte)pfi.name[c]  (UTF-16 code unit truncated to a byte)
                w.push(chars.next().map_or(0, |c| c as u32 as u8));
            }
        };
        match pak_type {
            PakFileType::TypeA => {
                name(&mut w);
                w.extend_from_slice(&self.offset.to_le_bytes());
                w.extend_from_slice(&self.size.to_le_bytes());
                w.extend_from_slice(&self.size_duplicate.to_le_bytes());
                w.extend_from_slice(&self.padding_size.to_le_bytes());
                w.extend_from_slice(&self.md5);
                w.extend_from_slice(&self.dummy1.to_le_bytes());
                w.extend_from_slice(&self.create_time.to_le_bytes());
                w.extend_from_slice(&self.modify_time.to_le_bytes());
                w.extend_from_slice(&self.dummy2.to_le_bytes());
            }
            PakFileType::TypeB => {
                w.extend_from_slice(&self.padding_size.to_le_bytes());
                w.extend_from_slice(&self.md5);
                w.extend_from_slice(&self.dummy1.to_le_bytes());
                w.extend_from_slice(&self.size.to_le_bytes());
                name(&mut w);
                w.extend_from_slice(&self.size_duplicate.to_le_bytes());
                w.extend_from_slice(&self.offset.to_le_bytes());
                w.extend_from_slice(&self.modify_time.to_le_bytes());
                w.extend_from_slice(&self.create_time.to_le_bytes());
                w.extend_from_slice(&self.dummy2.to_le_bytes());
            }
            PakFileType::TypeF => {
                w.extend_from_slice(&self.dummy2.to_le_bytes());
                name(&mut w);
                w.extend_from_slice(&self.offset.to_le_bytes());
                w.extend_from_slice(&self.size.to_le_bytes());
                w.extend_from_slice(&self.size_duplicate.to_le_bytes());
                w.extend_from_slice(&self.padding_size.to_le_bytes());
                w.extend_from_slice(&self.md5);
                w.extend_from_slice(&self.dummy1.to_le_bytes());
                w.extend_from_slice(&self.create_time.to_le_bytes());
                w.extend_from_slice(&self.modify_time.to_le_bytes());
            }
            PakFileType::Csv => return None,
        }
        debug_assert_eq!(w.len(), FILE_INFO_SIZE);
        w.try_into().ok()
    }
}

/// Reads a zero-terminated name, mapping each byte to the char with the same code point
/// (C#: `pfi.name += (char)ch`).
fn read_name(bytes: &[u8]) -> String {
    bytes
        .iter()
        .take_while(|&&b| b != 0)
        .map(|&b| char::from(b))
        .collect()
}

struct Le<'a> {
    data: &'a [u8],
    pos: usize,
}

impl Le<'_> {
    fn take<const N: usize>(&mut self) -> [u8; N] {
        let mut out = [0u8; N];
        let end = (self.pos + N).min(self.data.len());
        if self.pos < end {
            out[..end - self.pos].copy_from_slice(&self.data[self.pos..end]);
        }
        self.pos += N;
        out
    }
    fn i32(&mut self) -> i32 {
        i32::from_le_bytes(self.take())
    }
    fn u32(&mut self) -> u32 {
        u32::from_le_bytes(self.take())
    }
    fn i64(&mut self) -> i64 {
        i64::from_le_bytes(self.take())
    }
    fn u64(&mut self) -> u64 {
        u64::from_le_bytes(self.take())
    }
    fn md5(&mut self) -> [u8; 16] {
        self.take()
    }
}

// ---- AES ------------------------------------------------------------------------------------

enum AnyAes {
    A128(aes::Aes128),
    A192(aes::Aes192),
    A256(aes::Aes256),
}

impl AnyAes {
    fn new(key: &[u8]) -> Result<Self> {
        let bad = |_| PakError::Aes("Specified key is not a valid size for this algorithm.".into());
        Ok(match key.len() {
            16 => Self::A128(aes::Aes128::new_from_slice(key).map_err(bad)?),
            24 => Self::A192(aes::Aes192::new_from_slice(key).map_err(bad)?),
            32 => Self::A256(aes::Aes256::new_from_slice(key).map_err(bad)?),
            _ => {
                return Err(PakError::Aes(
                    "Specified key is not a valid size for this algorithm.".into(),
                ))
            }
        })
    }

    fn encrypt(&self, block: &mut [u8]) {
        let b = GenericArray::from_mut_slice(block);
        match self {
            Self::A128(c) => c.encrypt_block(b),
            Self::A192(c) => c.encrypt_block(b),
            Self::A256(c) => c.encrypt_block(b),
        }
    }

    fn decrypt(&self, block: &mut [u8]) {
        let b = GenericArray::from_mut_slice(block);
        match self {
            Self::A128(c) => c.decrypt_block(b),
            Self::A192(c) => c.decrypt_block(b),
            Self::A256(c) => c.decrypt_block(b),
        }
    }
}

/// `AAPakFileHeader.EncryptAES`: AES-CBC, zero IV, no padding. `do_encryption == false`
/// decrypts. The input length must be a multiple of 16; the key 16, 24 or 32 bytes.
pub fn encrypt_aes(message: &[u8], key: &[u8], do_encryption: bool) -> Result<Vec<u8>> {
    encrypt_aes_using_iv(message, key, &[0u8; 16], do_encryption)
}

/// `AAPakFileHeader.EncryptAESUsingIV`: like [`encrypt_aes`] with a custom IV.
pub fn encrypt_aes_using_iv(
    message: &[u8],
    key: &[u8],
    iv: &[u8],
    do_encryption: bool,
) -> Result<Vec<u8>> {
    let cipher = AnyAes::new(key)?;
    let iv: [u8; 16] = iv
        .try_into()
        .map_err(|_| PakError::Aes("Specified initialization vector (IV) does not match the block size for this algorithm.".into()))?;
    if !message.len().is_multiple_of(16) {
        return Err(PakError::Aes(
            "The input data is not a complete block.".into(),
        ));
    }
    let mut out = message.to_vec();
    let mut prev = iv;
    for block in out.as_chunks_mut::<16>().0.iter_mut() {
        if do_encryption {
            block.iter_mut().zip(prev).for_each(|(b, p)| *b ^= p);
            cipher.encrypt(block);
            prev.copy_from_slice(block);
        } else {
            let cipher_text: [u8; 16] = *block;
            cipher.decrypt(block);
            block.iter_mut().zip(prev).for_each(|(b, p)| *b ^= p);
            prev = cipher_text;
        }
    }
    Ok(out)
}

/// `AAPakFileHeader.EncryptStreamAes`: processes all of `source` into `target` (zero IV).
pub fn encrypt_stream_aes(
    source: &mut impl Read,
    target: &mut impl Write,
    key: &[u8],
    do_encryption: bool,
) -> Result<()> {
    encrypt_stream_aes_with_iv(source, target, key, &[0u8; 16], do_encryption)
}

/// `AAPakFileHeader.EncryptStreamAESWithIV`.
pub fn encrypt_stream_aes_with_iv(
    source: &mut impl Read,
    target: &mut impl Write,
    key: &[u8],
    iv: &[u8],
    do_encryption: bool,
) -> Result<()> {
    let mut data = Vec::new();
    source.read_to_end(&mut data)?;
    let out = encrypt_aes_using_iv(&data, key, iv, do_encryption)?;
    target.write_all(&out)?;
    Ok(())
}

/// `AAPakFileHeader.ByteArrayToHexString`: debug hex dump (`X2` + spacing, extra spacing every
/// 4 and 8 bytes, `line_feed` every 16 bytes).
pub fn byte_array_to_hex_string(bytes: &[u8], spacing_text: &str, line_feed: &str) -> String {
    let mut s = String::new();
    for (i, b) in bytes.iter().enumerate() {
        s.push_str(&format!("{b:02X}"));
        s.push_str(spacing_text);
        if i % 16 == 15 {
            s.push_str(line_feed);
        } else {
            if i % 4 == 3 {
                s.push_str(spacing_text);
            }
            if i % 8 == 7 {
                s.push_str(spacing_text);
            }
        }
    }
    s
}

// ---- time helpers ---------------------------------------------------------------------------

/// `DateTime.ToFileTimeUtc()`.
pub fn date_time_to_filetime(time: DateTime<Utc>) -> i64 {
    let ticks = time.timestamp() * 10_000_000 + i64::from(time.timestamp_subsec_nanos() / 100);
    ticks + FILETIME_UNIX_DIFF
}

/// Converts a [`SystemTime`] to a Windows FILETIME.
pub fn system_time_to_filetime(time: SystemTime) -> i64 {
    match time.duration_since(UNIX_EPOCH) {
        Ok(d) => FILETIME_UNIX_DIFF + (d.as_nanos() / 100) as i64,
        Err(e) => FILETIME_UNIX_DIFF - (e.duration().as_nanos() / 100) as i64,
    }
}

/// `DateTime.FromFileTimeUtc()`; `None` when out of range.
pub fn filetime_to_date_time(filetime: i64) -> Option<DateTime<Utc>> {
    let ticks = filetime.checked_sub(FILETIME_UNIX_DIFF)?;
    let secs = ticks.div_euclid(10_000_000);
    let nanos = (ticks.rem_euclid(10_000_000) * 100) as u32;
    DateTime::from_timestamp(secs, nanos)
}

/// `AAPak.DateTimeToDateTimeStr`: `yyyyMMdd-HHmmss`.
pub fn date_time_to_date_time_str(time: DateTime<Utc>) -> String {
    time.format("%Y%m%d-%H%M%S").to_string()
}

/// `AAPak.DateTimeStrToFILETIME`: parses `yyyy?MM?dd?HH?mm?ss` (fields at character offsets
/// 0, 5, 8, 11, 14 and 17; unparsable fields count as 0) and returns the FILETIME, or 0 if the
/// result is not a valid date.
///
/// Note: this does **not** read the format written by [`date_time_to_date_time_str`]
/// (`yyyyMMdd-HHmmss`); the two C# functions don't round-trip either.
pub fn date_time_str_to_filetime(encoded: &str) -> i64 {
    let field = |start: usize, len: usize| -> Option<i32> {
        encoded
            .get(start..start + len)
            .map(|s| s.parse().unwrap_or(0))
    };
    // C# `AsSpan(start, len)` throws when the string is too short -> result 0.
    let (Some(y), Some(mo), Some(d), Some(h), Some(mi), Some(s)) = (
        field(0, 4),
        field(5, 2),
        field(8, 2),
        field(11, 2),
        field(14, 2),
        field(17, 2),
    ) else {
        return 0;
    };
    let date = u32::try_from(mo)
        .ok()
        .zip(u32::try_from(d).ok())
        .and_then(|(mo, d)| NaiveDate::from_ymd_opt(y, mo, d))
        .zip(u32::try_from(h).ok())
        .zip(u32::try_from(mi).ok().zip(u32::try_from(s).ok()))
        .and_then(|((date, h), (mi, s))| date.and_hms_opt(h, mi, s));
    match date {
        Some(dt) if (1..=9999).contains(&y) && date_time_to_filetime(dt.and_utc()) >= 0 => {
            date_time_to_filetime(dt.and_utc())
        }
        _ => 0,
    }
}

/// `AAPak.StringToByteArray`: hex string to bytes.
pub fn string_to_byte_array(hex: &str) -> Result<Vec<u8>> {
    let bytes = hex.as_bytes();
    let mut out = Vec::with_capacity(bytes.len() / 2);
    let mut i = 0;
    while i < bytes.len() {
        let pair = hex
            .get(i..i + 2)
            .ok_or_else(|| PakError::InvalidHex(hex.to_string()))?;
        out.push(u8::from_str_radix(pair, 16).map_err(|_| PakError::InvalidHex(hex.to_string()))?);
        i += 2;
    }
    Ok(out)
}

/// `AAPak.ToPakSlashes`.
pub fn to_pak_slashes(file_name: &str) -> String {
    file_name.replace('\\', "/")
}

/// `AAPak.StreamToString`: reads a stream as text (UTF-8, honouring byte order marks).
pub fn stream_to_string(stream: &mut impl Read) -> io::Result<String> {
    let mut data = Vec::new();
    stream.read_to_end(&mut data)?;
    Ok(crate::file_manager::decode_text(&data))
}

// ---- header ---------------------------------------------------------------------------------

/// `AAPakFileHeader`.
#[derive(Debug, Clone)]
pub struct PakHeader {
    key: Vec<u8>,
    /// Encrypted file table + header, exactly as stored at the end of the pak.
    pub fat: Vec<u8>,
    pub first_file_info_offset: i64,
    /// End of the last file's data (incl. padding).
    pub add_file_offset: i64,
    /// Encrypted header.
    pub raw_data: [u8; HEADER_SIZE],
    /// Decrypted header.
    pub data: [u8; HEADER_SIZE],
    pub is_valid: bool,
    /// Number of used files.
    pub file_count: u32,
    /// Number of unused ("deleted") entries.
    pub extra_file_count: u32,
}

impl Default for PakHeader {
    fn default() -> Self {
        Self {
            key: XL_GAMES_KEY.to_vec(),
            fat: Vec::new(),
            first_file_info_offset: 0,
            add_file_offset: 0,
            raw_data: [0; HEADER_SIZE],
            data: [0; HEADER_SIZE],
            is_valid: false,
            file_count: 0,
            extra_file_count: 0,
        }
    }
}

impl PakHeader {
    /// `SetCustomKey`.
    pub fn set_custom_key(&mut self, key: &[u8]) {
        self.key = key.to_vec();
    }

    /// `SetDefaultKey`. (The C# version copies the 16-byte default key *over* the current key
    /// buffer, so a longer custom key would keep its tail; here the key is simply reset.)
    pub fn set_default_key(&mut self) {
        self.key = XL_GAMES_KEY.to_vec();
    }

    pub fn key(&self) -> &[u8] {
        &self.key
    }

    /// `DecryptHeaderData`: decrypts `raw_data`, detects the pak type and file counts.
    pub fn decrypt_header_data(&mut self, debug_mode: bool) -> Result<Option<PakFileType>> {
        let data = encrypt_aes(&self.raw_data, &self.key, false)?;
        self.data.copy_from_slice(&data);
        let d = &self.data;
        let u32_at = |o: usize| u32::from_le_bytes([d[o], d[o + 1], d[o + 2], d[o + 3]]);
        let detected = if &d[0..4] == b"WIBO" {
            self.file_count = u32_at(8);
            self.extra_file_count = u32_at(12);
            Some(PakFileType::TypeA)
        } else if &d[8..12] == b"IDEJ" {
            self.file_count = u32_at(12);
            self.extra_file_count = u32_at(0);
            Some(PakFileType::TypeB)
        } else if &d[0..4] == b"ZERO" {
            self.file_count = u32_at(8);
            self.extra_file_count = u32_at(12);
            Some(PakFileType::TypeF)
        } else {
            self.file_count = 0;
            self.extra_file_count = 0;
            if debug_mode {
                let hex = byte_array_to_hex_string(&self.key, "", "");
                let _ = std::fs::write(format!("game_pak_failed_header_{hex}.key"), self.data);
            }
            None
        };
        self.is_valid = detected.is_some();
        Ok(detected)
    }

    /// `EncryptHeaderData`: writes magic + counts into `data` and encrypts it into `raw_data`.
    pub fn encrypt_header_data(&mut self, pak_type: PakFileType) -> Result<()> {
        let fc = self.file_count.to_le_bytes();
        let efc = self.extra_file_count.to_le_bytes();
        match pak_type {
            PakFileType::TypeA => {
                self.data[0..4].copy_from_slice(b"WIBO");
                self.data[8..12].copy_from_slice(&fc);
                self.data[12..16].copy_from_slice(&efc);
            }
            PakFileType::TypeB => {
                self.data[0..4].copy_from_slice(&efc);
                self.data[8..12].copy_from_slice(b"IDEJ");
                self.data[12..16].copy_from_slice(&fc);
            }
            PakFileType::TypeF => {
                self.data[0..4].copy_from_slice(b"ZERO");
                self.data[8..12].copy_from_slice(&fc);
                self.data[12..16].copy_from_slice(&efc);
            }
            PakFileType::Csv => {}
        }
        let raw = encrypt_aes(&self.data, &self.key, true)?;
        self.raw_data.copy_from_slice(&raw);
        Ok(())
    }
}

// ---- file table storage (emulates Dictionary<string, AAPakFileInfo> ordering) --------------

#[derive(Debug, Default, Clone)]
struct FileTable {
    slots: Vec<Option<PakFileInfo>>,
    free: Vec<usize>,
    index: HashMap<String, usize>,
}

impl FileTable {
    fn clear(&mut self) {
        self.slots.clear();
        self.free.clear();
        self.index.clear();
    }

    fn len(&self) -> usize {
        self.index.len()
    }

    fn insert(&mut self, pfi: PakFileInfo) -> Result<()> {
        if self.index.contains_key(&pfi.name) {
            return Err(PakError::DuplicateFileName(pfi.name));
        }
        let name = pfi.name.clone();
        let slot = match self.free.pop() {
            Some(s) => {
                self.slots[s] = Some(pfi);
                s
            }
            None => {
                self.slots.push(Some(pfi));
                self.slots.len() - 1
            }
        };
        self.index.insert(name, slot);
        Ok(())
    }

    fn remove(&mut self, name: &str) -> Option<PakFileInfo> {
        let slot = self.index.remove(name)?;
        self.free.push(slot);
        self.slots[slot].take()
    }

    fn get(&self, name: &str) -> Option<&PakFileInfo> {
        self.index.get(name).and_then(|&s| self.slots[s].as_ref())
    }

    fn get_mut(&mut self, name: &str) -> Option<&mut PakFileInfo> {
        let s = *self.index.get(name)?;
        self.slots[s].as_mut()
    }

    fn iter(&self) -> impl Iterator<Item = &PakFileInfo> {
        self.slots.iter().flatten()
    }
}

// ---- AAPak ----------------------------------------------------------------------------------

/// Options for [`AAPak::open_with`].
#[derive(Debug, Clone)]
pub struct PakOpenOptions {
    /// Open read-only (default `true`).
    pub read_only: bool,
    /// Custom AES key for the header and file table (default: [`XL_GAMES_KEY`]).
    pub key: Option<Vec<u8>>,
    /// When the header can't be decrypted, dump it as `game_pak_failed_header_<key>.key` in the
    /// working directory (C# `DebugMode`).
    pub debug_mode: bool,
}

impl Default for PakOpenOptions {
    fn default() -> Self {
        Self {
            read_only: true,
            key: None,
            debug_mode: false,
        }
    }
}

/// `AAPak`: an opened `game_pak` file (or CSV virtual pak).
#[derive(Debug)]
pub struct AAPak {
    path: PathBuf,
    file: Option<File>,
    /// Header / file table state (`_header`).
    pub header: PakHeader,
    is_open: bool,
    /// Changes were made that require rewriting the file table on close.
    pub is_dirty: bool,
    is_virtual: bool,
    files: FileTable,
    /// Unused entries, normally all named `__unused__`.
    pub extra_files: Vec<PakFileInfo>,
    /// Virtual folder list, filled by [`AAPak::generate_folder_list`].
    pub folders: Vec<String>,
    read_only: bool,
    /// When `true`, a deleted file's space is added to the previous file's padding instead of
    /// becoming an extra (reusable) entry.
    pub padding_delete_mode: bool,
    pak_type: PakFileType,
    pub debug_mode: bool,
}

impl AAPak {
    fn empty(path: &Path, read_only: bool) -> Self {
        Self {
            path: path.to_path_buf(),
            file: None,
            header: PakHeader::default(),
            is_open: false,
            is_dirty: false,
            is_virtual: false,
            files: FileTable::default(),
            extra_files: Vec::new(),
            folders: Vec::new(),
            read_only,
            padding_delete_mode: false,
            pak_type: PakFileType::TypeA,
            debug_mode: false,
        }
    }

    /// `new AAPak(filePath, openAsReadOnly)`: opens an existing pak (or `.csv` listing).
    pub fn open(path: impl AsRef<Path>, read_only: bool) -> Result<Self> {
        Self::open_with(
            path,
            PakOpenOptions {
                read_only,
                ..Default::default()
            },
        )
    }

    /// `OpenPak` with a custom key / debug mode.
    pub fn open_with(path: impl AsRef<Path>, options: PakOpenOptions) -> Result<Self> {
        let path = path.as_ref();
        if !path.is_file() {
            return Err(PakError::NotFound(path.to_path_buf()));
        }
        let is_csv = path
            .extension()
            .is_some_and(|e| e.to_string_lossy().eq_ignore_ascii_case("csv"));
        if is_csv {
            return Self::open_virtual_csv(path);
        }
        let mut pak = Self::empty(path, options.read_only);
        pak.debug_mode = options.debug_mode;
        if let Some(key) = &options.key {
            pak.header.set_custom_key(key);
        }
        let file = if options.read_only {
            File::open(path)?
        } else {
            OpenOptions::new().read(true).write(true).open(path)?
        };
        pak.file = Some(file);
        pak.is_open = true;
        if !pak.read_header()? {
            pak.is_open = false;
            return Err(PakError::InvalidHeader);
        }
        Ok(pak)
    }

    /// `NewPak` / `new AAPak(path, createAsNewPak: true)`: creates (overwrites!) an empty TypeA
    /// pak opened read/write.
    pub fn create(path: impl AsRef<Path>) -> Result<Self> {
        let path = path.as_ref();
        let mut pak = Self::empty(path, false);
        pak.file = Some(
            OpenOptions::new()
                .read(true)
                .write(true)
                .create(true)
                .truncate(true)
                .open(path)?,
        );
        pak.is_open = true;
        pak.is_dirty = true;
        pak.save_header()?;
        if !pak.read_header()? {
            return Err(PakError::InvalidHeader);
        }
        Ok(pak)
    }

    /// `OpenVirtualCSVPak`: loads a CSV listing as a read-only pak without data.
    pub fn open_virtual_csv(path: impl AsRef<Path>) -> Result<Self> {
        let path = path.as_ref();
        if !path.is_file() {
            return Err(PakError::NotFound(path.to_path_buf()));
        }
        let mut pak = Self::empty(path, true);
        pak.is_virtual = true;
        pak.is_open = true;
        pak.pak_type = PakFileType::Csv;
        let text = std::fs::read_to_string(path)?;
        pak.read_csv_data(&text)?;
        Ok(pak)
    }

    fn read_csv_data(&mut self, text: &str) -> Result<()> {
        self.files.clear();
        self.extra_files.clear();
        self.folders.clear();
        let mut lines = text.lines();
        let header = lines.next().unwrap_or_default();
        // C# compares with CurrentCultureIgnoreCase, but the check was inverted (bug).
        self.header.is_valid = header.to_lowercase() == CSV_HEADER.to_lowercase();
        if !self.header.is_valid {
            return Err(PakError::InvalidCsv("unexpected header line".into()));
        }
        for line in lines {
            let fields: Vec<&str> = line.split(';').collect();
            if fields.len() != 10 {
                continue;
            }
            let bad = |what: &str| PakError::InvalidCsv(format!("{what} in line {line:?}"));
            let num = |s: &str| s.trim().parse::<i64>();
            let mut md5 = [0u8; 16];
            let hash = string_to_byte_array(fields[3]).map_err(|_| bad("md5"))?;
            let n = hash.len().min(16);
            md5[..n].copy_from_slice(&hash[..n]);
            let pfi = PakFileInfo {
                name: fields[0].to_string(),
                size: num(fields[1]).map_err(|_| bad("size"))?,
                offset: num(fields[2]).map_err(|_| bad("offset"))?,
                md5,
                create_time: date_time_str_to_filetime(fields[4]),
                modify_time: date_time_str_to_filetime(fields[5]),
                size_duplicate: num(fields[6]).map_err(|_| bad("sizeDuplicate"))?,
                padding_size: fields[7].trim().parse().map_err(|_| bad("paddingSize"))?,
                dummy1: fields[8].trim().parse().map_err(|_| bad("dummy1"))?,
                dummy2: u64::from(fields[9].trim().parse::<u32>().map_err(|_| bad("dummy2"))?),
                ..Default::default()
            };
            if let Err(e) = self.files.insert(pfi) {
                self.header.is_valid = false;
                return Err(e);
            }
        }
        Ok(())
    }

    /// `ClosePak`: saves the file table if dirty (and writable) and closes the file.
    pub fn close(&mut self) -> Result<()> {
        if !self.is_open {
            return Ok(());
        }
        let res = if self.is_dirty && !self.read_only && !self.is_virtual {
            self.save_header()
        } else {
            Ok(())
        };
        self.file = None;
        self.is_open = false;
        self.header.set_default_key();
        res
    }

    /// `SaveHeader`: encrypts and writes the file table + header back at
    /// `first_file_info_offset` and truncates the file there.
    pub fn save_header(&mut self) -> Result<()> {
        if self.is_virtual || self.pak_type == PakFileType::Csv {
            return Err(PakError::Unsupported(self.pak_type));
        }
        self.write_to_fat()?;
        let file = self.file.as_mut().ok_or(PakError::NotOpen)?;
        file.seek(SeekFrom::Start(
            self.header.first_file_info_offset.max(0) as u64
        ))?;
        file.write_all(&self.header.fat)?;
        let pos = file.stream_position()?;
        file.set_len(pos)?;
        file.flush()?;
        self.is_dirty = false;
        Ok(())
    }

    /// `WriteToFAT`: rebuilds `header.fat` (encrypted entries, padding, header).
    fn write_to_fat(&mut self) -> Result<()> {
        let pak_type = self.pak_type;
        let mut ordered: Vec<&PakFileInfo> =
            Vec::with_capacity(self.file_count() + self.extra_files.len());
        match pak_type {
            PakFileType::TypeA | PakFileType::TypeF => {
                ordered.extend(self.files.iter());
                ordered.extend(self.extra_files.iter());
            }
            PakFileType::TypeB => {
                ordered.extend(self.extra_files.iter());
                ordered.extend(self.files.iter());
            }
            PakFileType::Csv => return Err(PakError::Unsupported(pak_type)),
        }
        let mut fat = Vec::with_capacity(ordered.len() * FILE_INFO_SIZE + 2 * HEADER_SIZE);
        for pfi in ordered {
            let plain = pfi
                .to_decrypted(pak_type)
                .ok_or(PakError::Unsupported(pak_type))?;
            fat.extend_from_slice(&encrypt_aes(&plain, &self.header.key, true)?);
        }
        let dif = fat.len() % HEADER_SIZE;
        if dif > 0 {
            fat.resize(fat.len() + HEADER_SIZE - dif, 0);
        }
        self.header.file_count = self.files.len() as u32;
        self.header.extra_file_count = self.extra_files.len() as u32;
        self.header.encrypt_header_data(pak_type)?;
        // C# stretches the FAT by headerSize but only writes the first 0x20 encrypted bytes.
        let header_start = fat.len();
        fat.resize(header_start + HEADER_SIZE, 0);
        fat[header_start..header_start + 0x20].copy_from_slice(&self.header.raw_data[..0x20]);
        self.header.fat = fat;
        Ok(())
    }

    /// `ReadHeader`: reads and decrypts the header and file table. Returns `header.is_valid`.
    fn read_header(&mut self) -> Result<bool> {
        self.files.clear();
        self.extra_files.clear();
        self.folders.clear();
        let file = self.file.as_mut().ok_or(PakError::NotOpen)?;
        file.seek(SeekFrom::End(-(HEADER_SIZE as i64)))?;
        file.read_exact(&mut self.header.raw_data)?;
        match self.header.decrypt_header_data(self.debug_mode)? {
            Some(t) => {
                self.pak_type = t;
                self.load_raw_fat()?;
                self.read_file_table()?;
            }
            None => self.header.fat.clear(),
        }
        Ok(self.header.is_valid)
    }

    /// `LoadRawFAT`.
    fn load_raw_fat(&mut self) -> Result<()> {
        let file = self.file.as_mut().ok_or(PakError::NotOpen)?;
        let total_info_size = (i64::from(self.header.file_count)
            + i64::from(self.header.extra_file_count))
            * FILE_INFO_SIZE as i64;
        let len = file.seek(SeekFrom::End(0))? as i64;
        let mut first = len - HEADER_SIZE as i64 - total_info_size;
        first -= first % 0x200;
        if first < 0 {
            return Err(PakError::InvalidFatOffset(first));
        }
        self.header.first_file_info_offset = first;
        file.seek(SeekFrom::Start(first as u64))?;
        let mut fat = Vec::with_capacity((len - first) as usize);
        file.read_to_end(&mut fat)?;
        self.header.fat = fat;
        Ok(())
    }

    /// `ReadFileTable`.
    fn read_file_table(&mut self) -> Result<()> {
        self.files.clear();
        self.extra_files.clear();
        let pak_type = self.pak_type;
        let total = u64::from(self.header.file_count) + u64::from(self.header.extra_file_count);
        let mut files_to_go = self.header.file_count;
        let mut extra_to_go = self.header.extra_file_count;
        let mut file_index_counter = -1;
        let mut deleted_index_counter = -1;
        let mut raw = [0u8; FILE_INFO_SIZE];
        for i in 0..total {
            let start = (i as usize).saturating_mul(FILE_INFO_SIZE);
            raw.fill(0);
            if start < self.header.fat.len() {
                let end = (start + FILE_INFO_SIZE).min(self.header.fat.len());
                raw[..end - start].copy_from_slice(&self.header.fat[start..end]);
            }
            let dec = encrypt_aes(&raw, &self.header.key, false)?;
            let mut pfi = PakFileInfo::from_decrypted(&dec, pak_type);
            let end_of_data = pfi.offset + pfi.size + i64::from(pfi.padding_size);
            match pak_type {
                PakFileType::TypeA | PakFileType::TypeF => {
                    if files_to_go > 0 {
                        file_index_counter += 1;
                        pfi.entry_index_number = file_index_counter;
                        files_to_go -= 1;
                        self.files.insert(pfi)?;
                    } else if extra_to_go > 0 {
                        deleted_index_counter += 1;
                        pfi.deleted_index_number = deleted_index_counter;
                        extra_to_go -= 1;
                        self.extra_files.push(pfi);
                    }
                }
                PakFileType::TypeB => {
                    // Note: the C# assigns entryIndexNumber to the extra files and
                    // deletedIndexNumber to the normal files here; kept as-is.
                    if extra_to_go > 0 {
                        file_index_counter += 1;
                        pfi.entry_index_number = file_index_counter;
                        extra_to_go -= 1;
                        self.extra_files.push(pfi);
                    } else if files_to_go > 0 {
                        deleted_index_counter += 1;
                        pfi.deleted_index_number = deleted_index_counter;
                        files_to_go -= 1;
                        self.files.insert(pfi)?;
                    }
                }
                PakFileType::Csv => {}
            }
            if end_of_data > self.header.add_file_offset {
                self.header.add_file_offset = end_of_data;
            }
        }
        Ok(())
    }

    // ---- accessors ----------------------------------------------------------------------

    /// `_gpFilePath`.
    pub fn path(&self) -> &Path {
        &self.path
    }

    /// `isOpen`.
    pub fn is_open(&self) -> bool {
        self.is_open
    }

    /// `isVirtual` (CSV pak).
    pub fn is_virtual(&self) -> bool {
        self.is_virtual
    }

    /// `readOnly`.
    pub fn read_only(&self) -> bool {
        self.read_only
    }

    /// `PakType`.
    pub fn pak_type(&self) -> PakFileType {
        self.pak_type
    }

    /// Number of used files (`pakFiles.Count`).
    pub fn file_count(&self) -> usize {
        self.files.len()
    }

    /// All used files, in file-table order (`pakFiles.Values`).
    pub fn files(&self) -> impl Iterator<Item = &PakFileInfo> {
        self.files.iter()
    }

    /// All used file names (`pakFiles.Keys`).
    pub fn file_names(&self) -> impl Iterator<Item = &str> {
        self.files.iter().map(|f| f.name.as_str())
    }

    /// `GetFileByName`.
    pub fn get_file_by_name(&self, file_name: &str) -> Option<&PakFileInfo> {
        self.files.get(&to_pak_slashes(file_name))
    }

    /// Mutable access to a file entry (changes are written on the next [`AAPak::save_header`];
    /// set [`AAPak::is_dirty`] yourself).
    pub fn get_file_by_name_mut(&mut self, file_name: &str) -> Option<&mut PakFileInfo> {
        self.files.get_mut(&to_pak_slashes(file_name))
    }

    /// `GetFileByIndex`.
    pub fn get_file_by_index(&self, file_index: i32) -> Option<&PakFileInfo> {
        self.files
            .iter()
            .find(|f| f.entry_index_number == file_index)
    }

    /// `FileExists`.
    pub fn file_exists(&self, file_name: &str) -> bool {
        self.get_file_by_name(file_name).is_some()
    }

    /// `FindFileByOffset`: the used file whose data (incl. padding) contains `offset`.
    pub fn find_file_by_offset(&self, offset: i64) -> Option<&PakFileInfo> {
        self.files
            .iter()
            .find(|f| offset >= f.offset && offset <= f.offset + f.size + i64::from(f.padding_size))
    }

    /// `GenerateFolderList`: fills [`AAPak::folders`] with the (lower-case) directory names of
    /// all files. Sorting is ordinal (C# used the current culture's comparer).
    pub fn generate_folder_list(&mut self, sort_the_list: bool) {
        self.folders.clear();
        if !self.is_open || !self.header.is_valid {
            return;
        }
        let mut seen = HashSet::new();
        for pfi in self.files.iter() {
            if pfi.name.is_empty() {
                continue;
            }
            let n = directory_name(&pfi.name.to_lowercase());
            if seen.insert(n.clone()) {
                self.folders.push(n);
            }
        }
        if sort_the_list {
            self.folders.sort();
        }
    }

    /// `GetFilesInDirectory`: files directly inside `directory_name` (case-insensitive).
    pub fn get_files_in_directory(&self, directory_name: &str) -> Vec<&PakFileInfo> {
        let dir = to_pak_slashes(&directory_name.to_lowercase());
        self.files
            .iter()
            .filter(|f| directory_name_of(&f.name.to_lowercase()) == dir)
            .collect()
    }

    // ---- reading ------------------------------------------------------------------------

    /// `ExportFileAsStream(AAPakFileInfo)`: a stream over the file's data using the pak's own
    /// file handle.
    pub fn export_file_as_stream(&mut self, file: &PakFileInfo) -> Result<SubStream<&mut File>> {
        let handle = self.file.as_mut().ok_or(PakError::NotOpen)?;
        Ok(SubStream::new(
            handle,
            file.offset.max(0) as u64,
            file.size.max(0) as u64,
        )?)
    }

    /// `ExportFileAsStreamCloned`: opens a new read-only handle to the pak and returns a stream
    /// over the file's data, or `None` if the file is not in the pak.
    pub fn export_file_as_stream_cloned(&self, file_name: &str) -> Result<Option<SubStream<File>>> {
        let Some(file) = self.get_file_by_name(file_name) else {
            return Ok(None);
        };
        if self.is_virtual || !self.is_open {
            return Err(PakError::NotOpen);
        }
        let fs = File::open(&self.path)?;
        Ok(Some(SubStream::new(
            fs,
            file.offset.max(0) as u64,
            file.size.max(0) as u64,
        )?))
    }

    /// Reads a whole file, or `None` if it is not in the pak. Thread-safe (own handle).
    pub fn read_file(&self, file_name: &str) -> Result<Option<Vec<u8>>> {
        let Some(mut s) = self.export_file_as_stream_cloned(file_name)? else {
            return Ok(None);
        };
        let mut out = Vec::with_capacity(usize::try_from(s.len()).unwrap_or(0));
        s.read_to_end(&mut out)?;
        Ok(Some(out))
    }

    // ---- writing ------------------------------------------------------------------------

    fn ensure_writable(&self) -> Result<()> {
        if self.read_only {
            Err(PakError::ReadOnly)
        } else if !self.is_open || self.file.is_none() {
            Err(PakError::NotOpen)
        } else {
            Ok(())
        }
    }

    fn md5_of(&mut self, file: &PakFileInfo) -> Result<[u8; 16]> {
        let mut s = self.export_file_as_stream(file)?;
        let mut hasher = Md5::new();
        let mut buf = vec![0u8; 64 * 1024];
        loop {
            let n = s.read(&mut buf)?;
            if n == 0 {
                break;
            }
            hasher.update(&buf[..n]);
        }
        Ok(hasher.finalize().into())
    }

    /// `UpdateMD5`: recalculates a file's MD5 (marks the pak dirty if it changed) and returns it
    /// as an upper-case hex string.
    pub fn update_md5(&mut self, file_name: &str) -> Result<String> {
        let info = self
            .get_file_by_name(file_name)
            .cloned()
            .ok_or_else(|| PakError::NotFound(PathBuf::from(file_name)))?;
        let hash = self.md5_of(&info)?;
        if info.md5 != hash {
            if let Some(f) = self.get_file_by_name_mut(file_name) {
                f.md5 = hash;
            }
            self.is_dirty = true;
        }
        Ok(hash.iter().map(|b| format!("{b:02X}")).collect())
    }

    /// `SetMD5`: returns `false` if the file doesn't exist or `new_hash` isn't 16 bytes.
    pub fn set_md5(&mut self, file_name: &str, new_hash: &[u8]) -> bool {
        let Ok(hash) = <[u8; 16]>::try_from(new_hash) else {
            return false;
        };
        let Some(f) = self.get_file_by_name_mut(file_name) else {
            return false;
        };
        f.md5 = hash;
        self.is_dirty = true;
        true
    }

    fn copy_into_pak<R: Read + Seek>(&mut self, offset: i64, source: &mut R) -> Result<()> {
        let file = self.file.as_mut().ok_or(PakError::NotOpen)?;
        file.seek(SeekFrom::Start(offset.max(0) as u64))?;
        source.seek(SeekFrom::Start(0))?;
        io::copy(source, file)?;
        Ok(())
    }

    /// `ReplaceFile`: overwrites a file's data in place. Returns `Ok(false)` if the pak is
    /// read-only, the file doesn't exist, or the new data doesn't fit in size + padding.
    pub fn replace_file<R: Read + Seek>(
        &mut self,
        file_name: &str,
        source: &mut R,
        modify_time: DateTime<Utc>,
    ) -> Result<bool> {
        if self.ensure_writable().is_err() {
            return Ok(false);
        }
        let Some(mut pfi) = self.get_file_by_name(file_name).cloned() else {
            return Ok(false);
        };
        let source_len = source.seek(SeekFrom::End(0))? as i64;
        if source_len > pfi.size + i64::from(pfi.padding_size) {
            return Ok(false);
        }
        let end_pos = pfi.offset + pfi.size + i64::from(pfi.padding_size);
        if self.copy_into_pak(pfi.offset, source).is_err() {
            return Ok(false);
        }
        pfi.size = source_len;
        pfi.size_duplicate = pfi.size;
        pfi.padding_size = (end_pos - pfi.size - pfi.offset) as i32;
        pfi.md5 = self.md5_of(&pfi)?;
        pfi.modify_time = date_time_to_filetime(modify_time);
        if self.pak_type == PakFileType::TypeB {
            pfi.dummy1 = 0x8000_0000;
        }
        if let Some(f) = self.files.get_mut(&pfi.name) {
            *f = pfi;
        }
        self.is_dirty = true;
        Ok(true)
    }

    /// `DeleteFile(string)`: returns `true` on success or if the file didn't exist, `false` if
    /// the pak is read-only.
    pub fn delete_file(&mut self, file_name: &str) -> bool {
        if self.read_only {
            return false;
        }
        let name = to_pak_slashes(file_name);
        let Some(pfi) = self.files.get(&name).cloned() else {
            return true;
        };
        if self.padding_delete_mode {
            let prev_offset = self
                .find_file_by_offset(pfi.offset - 1)
                .map(|p| p.name.clone());
            if let Some(prev) = prev_offset.and_then(|n| self.files.get_mut(&n)) {
                prev.padding_size += pfi.size as i32 + pfi.padding_size;
            }
        } else {
            let mut e = PakFileInfo {
                name: UNUSED_FILE_NAME.to_string(),
                offset: pfi.offset,
                size: pfi.size + i64::from(pfi.padding_size),
                ..Default::default()
            };
            e.size_duplicate = e.size;
            if self.pak_type == PakFileType::TypeB {
                e.dummy1 = 0x8000_0000;
            }
            self.extra_files.push(e);
        }
        self.files.remove(&name);
        self.is_dirty = true;
        true
    }

    /// `AddAsNewFile`: adds a new file, reusing the first big-enough extra entry or appending
    /// at the end of the data (with optional 25% spare space). Fails if the name exists.
    pub fn add_as_new_file<R: Read + Seek>(
        &mut self,
        file_name: &str,
        source: &mut R,
        create_time: DateTime<Utc>,
        modify_time: DateTime<Utc>,
        auto_spare_space: bool,
    ) -> Result<PakFileInfo> {
        self.ensure_writable()?;
        let size = source.seek(SeekFrom::End(0))? as i64;
        let mut new_file = PakFileInfo {
            name: file_name.to_string(),
            offset: self.header.first_file_info_offset,
            size,
            size_duplicate: size,
            create_time: date_time_to_filetime(create_time),
            modify_time: date_time_to_filetime(modify_time),
            ..Default::default()
        };
        if self.pak_type == PakFileType::TypeB {
            new_file.dummy1 = 0x8000_0000;
        }
        let mut added_at_the_end = true;
        if let Some(i) = self
            .extra_files
            .iter()
            .position(|e| new_file.size <= e.size)
        {
            let e = self.extra_files.remove(i);
            new_file.offset = e.offset;
            new_file.padding_size = (e.size - new_file.size) as i32;
            added_at_the_end = false;
        }
        if added_at_the_end {
            let dif = new_file.size % 0x200;
            if dif > 0 {
                new_file.padding_size = (0x200 - dif) as i32;
            }
            if auto_spare_space {
                let mut spare = new_file.size / 4;
                spare -= spare % 0x200;
                new_file.padding_size += spare as i32;
            }
        }
        self.files.insert(new_file.clone())?;
        self.is_dirty = true;
        self.copy_into_pak(new_file.offset, source)?;
        if added_at_the_end {
            self.header.first_file_info_offset =
                new_file.offset + new_file.size + i64::from(new_file.padding_size);
        }
        new_file.md5 = self.md5_of(&new_file)?;
        if let Some(f) = self.files.get_mut(&new_file.name) {
            f.md5 = new_file.md5;
        }
        Ok(new_file)
    }

    /// `AddFileFromStream`: replaces the file in place if it fits, otherwise deletes it (if
    /// present) and adds it as new.
    pub fn add_file_from_stream<R: Read + Seek>(
        &mut self,
        file_name: &str,
        source: &mut R,
        create_time: DateTime<Utc>,
        modify_time: DateTime<Utc>,
        auto_spare_space: bool,
    ) -> Result<PakFileInfo> {
        self.ensure_writable()?;
        let mut add_as_new = true;
        if let Some(existing) = self.get_file_by_name(file_name).cloned() {
            let len = source.seek(SeekFrom::End(0))? as i64;
            add_as_new = len > existing.size + i64::from(existing.padding_size);
            if add_as_new {
                self.delete_file(&existing.name);
            }
        }
        if add_as_new {
            self.add_as_new_file(
                file_name,
                source,
                create_time,
                modify_time,
                auto_spare_space,
            )
        } else if self.replace_file(file_name, source, modify_time)? {
            self.get_file_by_name(file_name)
                .cloned()
                .ok_or_else(|| PakError::NotFound(PathBuf::from(file_name)))
        } else {
            Err(PakError::Io(io::Error::other(
                "failed to replace file data",
            )))
        }
    }

    /// `AddFileFromFile`: adds a file from disk using its creation / modification times.
    pub fn add_file_from_file(
        &mut self,
        source_file_name: impl AsRef<Path>,
        as_file_name: &str,
        auto_spare_space: bool,
    ) -> Result<PakFileInfo> {
        let path = source_file_name.as_ref();
        if !path.is_file() {
            return Err(PakError::NotFound(path.to_path_buf()));
        }
        let meta = std::fs::metadata(path)?;
        let to_dt = |t: io::Result<SystemTime>| -> DateTime<Utc> {
            t.ok()
                .and_then(|t| filetime_to_date_time(system_time_to_filetime(t)))
                .unwrap_or_else(Utc::now)
        };
        let create_time = to_dt(meta.created());
        let modify_time = to_dt(meta.modified());
        let mut fs = File::open(path)?;
        self.add_file_from_stream(
            as_file_name,
            &mut fs,
            create_time,
            modify_time,
            auto_spare_space,
        )
    }
}

impl Drop for AAPak {
    /// C# finalizer: `if (isOpen) ClosePak();`
    fn drop(&mut self) {
        let _ = self.close();
    }
}

/// `Path.GetDirectoryName(name)` for pak names (`/` separated): everything before the last
/// separator, or `""`.
fn directory_name(name: &str) -> String {
    directory_name_of(name).to_string()
}

fn directory_name_of(name: &str) -> &str {
    match name.rfind(['/', '\\']) {
        Some(i) => &name[..i],
        None => "",
    }
}

#[cfg(test)]
mod tests;
