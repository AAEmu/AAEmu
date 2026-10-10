//! Read-only port of `AAEmu.Commons/Utils/AAPak/AAPak.cs` (+ `SubStream.cs`).
//!
//! Only the subset the game server needs to read client data is ported: opening a `game_pak`,
//! decrypting its header and file table (FAT), looking files up by name and reading their bytes.
//! Writing, CSV "virtual" paks, MD5 updates and custom keys are not ported.
//!
//! Layout of a pak file (all little endian):
//!
//! ```text
//! [file data ...][file info table: (fileCount + extraFileCount) * 0x150, AES encrypted per entry]
//! [padding up to a 0x200 boundary][header: 0x200 bytes, AES encrypted]
//! ```

use std::collections::HashMap;
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};

use aes::cipher::block_padding::NoPadding;
use aes::cipher::{BlockDecryptMut, KeyIvInit};

/// `AAPakFileHeader.XLGamesKey`: default AES-128 key used by XLGames for the header and FAT.
pub const XL_GAMES_KEY: [u8; 16] = [
    0x32, 0x1F, 0x2A, 0xEE, 0xAA, 0x58, 0x4A, 0xB4, 0x9A, 0x6C, 0x9E, 0x09, 0xD5, 0x9E, 0x9C, 0x6F,
];

/// `AAPakFileHeader.headerSize`.
pub const HEADER_SIZE: usize = 0x200;
/// `AAPakFileHeader.fileInfoSize`.
pub const FILE_INFO_SIZE: usize = 0x150;
/// Size of the file name field inside a file info block.
const NAME_SIZE: usize = 0x108;

type Aes128CbcDec = cbc::Decryptor<aes::Aes128>;

/// Errors raised while opening a pak. In C# every one of these makes `AAPak.isOpen` false
/// (the exceptions are swallowed by `OpenPak`).
#[derive(Debug, thiserror::Error)]
pub enum PakError {
    #[error("I/O error on pak {path}: {source}")]
    Io {
        path: PathBuf,
        #[source]
        source: std::io::Error,
    },
    #[error("pak {path} is too small ({len} bytes) to contain a header")]
    TooSmall { path: PathBuf, len: u64 },
    #[error("pak {path} has an unknown header (not WIBO/IDEJ/ZERO, or wrong key)")]
    InvalidHeader { path: PathBuf },
    #[error("pak {path} has an invalid file table offset {offset}")]
    InvalidFatOffset { path: PathBuf, offset: i64 },
    #[error("pak {path} contains the file name {name:?} more than once")]
    DuplicateFileName { path: PathBuf, name: String },
    #[error("AES error while decrypting pak {path}")]
    Crypto { path: PathBuf },
}

/// `PakFileType` (without the `CSV` virtual type, which is not ported).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PakFileType {
    /// Header starts with `WIBO`; files first, extra (deleted) files last.
    TypeA,
    /// Header has `IDEJ` at offset 8; extra files first, files last.
    TypeB,
    /// Header starts with `ZERO`; same ordering as TypeA, different entry layout.
    TypeF,
}

/// `AAPakFileInfo`: one entry of the file table.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PakFileInfo {
    pub name: String,
    pub offset: i64,
    pub size: i64,
    pub size_duplicate: i64,
    pub padding_size: i32,
    pub md5: [u8; 16],
    pub dummy1: u32,
    pub create_time: i64,
    pub modify_time: i64,
    pub dummy2: u64,
    /// Not part of the on-disk structure (`entryIndexNumber`, -1 when unset).
    pub entry_index_number: i32,
    /// Not part of the on-disk structure (`deletedIndexNumber`, -1 when unset).
    pub deleted_index_number: i32,
}

/// Port of the read-only parts of `AAPak`.
#[derive(Debug)]
pub struct Pak {
    path: PathBuf,
    pak_type: PakFileType,
    file_count: u32,
    extra_file_count: u32,
    first_file_info_offset: i64,
    add_file_offset: i64,
    /// `pakFiles` values, in table (= C# `Dictionary` insertion) order.
    files: Vec<PakFileInfo>,
    /// `pakFiles` keys -> index into `files`.
    index: HashMap<String, usize>,
    /// `extraFiles`.
    extra_files: Vec<PakFileInfo>,
}

/// `AAPakFileHeader.EncryptAES(message, key, false)`: AES-128-CBC, zero IV, no padding.
fn decrypt_aes(data: &mut [u8], key: &[u8; 16]) -> bool {
    let iv = [0u8; 16];
    Aes128CbcDec::new(key.into(), &iv.into())
        .decrypt_padded_mut::<NoPadding>(data)
        .is_ok()
}

/// `AAPak.ToPakSlashes`: replaces `Path.DirectorySeparatorChar` (`\` on Windows, `/` elsewhere)
/// with `/`.
pub fn to_pak_slashes(file_name: &str) -> String {
    file_name.replace(std::path::MAIN_SEPARATOR, "/")
}

/// Mirrors `Path.GetDirectoryName(name.ToLower().Replace('/', sep)).Replace(sep, '/')` as used
/// by `AAPak.GetFilesInDirectory`: the lower-cased directory part of a pak file name, `""` for
/// files in the root.
fn pak_directory_name(name: &str) -> String {
    let lower = name.to_lowercase();
    let is_sep = |c: char| c == '/' || c == std::path::MAIN_SEPARATOR;
    match lower.rfind(is_sep) {
        Some(pos) => {
            // GetDirectoryName also strips redundant trailing separators of the directory part.
            let dir = lower[..pos].trim_end_matches(is_sep);
            let dir = if dir.is_empty() { &lower[..pos] } else { dir };
            dir.replace(std::path::MAIN_SEPARATOR, "/")
        }
        None => String::new(),
    }
}

fn read_i32(b: &[u8], at: usize) -> i32 {
    i32::from_le_bytes(b[at..at + 4].try_into().expect("4 bytes"))
}
fn read_u32(b: &[u8], at: usize) -> u32 {
    u32::from_le_bytes(b[at..at + 4].try_into().expect("4 bytes"))
}
fn read_i64(b: &[u8], at: usize) -> i64 {
    i64::from_le_bytes(b[at..at + 8].try_into().expect("8 bytes"))
}
fn read_u64(b: &[u8], at: usize) -> u64 {
    u64::from_le_bytes(b[at..at + 8].try_into().expect("8 bytes"))
}

/// Reads a zero terminated name of at most `0x108` bytes, each byte mapped to a `char` (C#
/// `(char)byte`, i.e. Latin-1).
fn read_name(b: &[u8], at: usize) -> String {
    b[at..at + NAME_SIZE]
        .iter()
        .take_while(|&&c| c != 0)
        .map(|&c| c as char)
        .collect()
}

/// Decodes one decrypted `0x150` byte file info block (`ReadFileTable` field layouts).
fn parse_file_info(pak_type: PakFileType, d: &[u8]) -> PakFileInfo {
    let md5_at = |at: usize| -> [u8; 16] { d[at..at + 16].try_into().expect("16 bytes") };
    match pak_type {
        PakFileType::TypeA => PakFileInfo {
            name: read_name(d, 0),
            offset: read_i64(d, 0x108),
            size: read_i64(d, 0x110),
            size_duplicate: read_i64(d, 0x118),
            padding_size: read_i32(d, 0x120),
            md5: md5_at(0x124),
            dummy1: read_u32(d, 0x134),
            create_time: read_i64(d, 0x138),
            modify_time: read_i64(d, 0x140),
            dummy2: read_u64(d, 0x148),
            entry_index_number: -1,
            deleted_index_number: -1,
        },
        PakFileType::TypeB => PakFileInfo {
            padding_size: read_i32(d, 0),
            md5: md5_at(4),
            dummy1: read_u32(d, 0x14),
            size: read_i64(d, 0x18),
            name: read_name(d, 0x20),
            size_duplicate: read_i64(d, 0x128),
            offset: read_i64(d, 0x130),
            modify_time: read_i64(d, 0x138),
            create_time: read_i64(d, 0x140),
            dummy2: read_u64(d, 0x148),
            entry_index_number: -1,
            deleted_index_number: -1,
        },
        PakFileType::TypeF => PakFileInfo {
            dummy2: read_u64(d, 0),
            name: read_name(d, 8),
            offset: read_i64(d, 0x110),
            size: read_i64(d, 0x118),
            size_duplicate: read_i64(d, 0x120),
            padding_size: read_i32(d, 0x128),
            md5: md5_at(0x12C),
            dummy1: read_u32(d, 0x13C),
            create_time: read_i64(d, 0x140),
            modify_time: read_i64(d, 0x148),
            entry_index_number: -1,
            deleted_index_number: -1,
        },
    }
}

impl Pak {
    /// `new AAPak(filePath)` / `OpenPak(filePath, true)` + `ReadHeader()`: opens a pak read-only
    /// using the default XLGames key.
    pub fn open(path: impl AsRef<Path>) -> Result<Pak, PakError> {
        let path = path.as_ref().to_path_buf();
        let io = |source| PakError::Io {
            path: path.clone(),
            source,
        };
        let mut file = File::open(&path).map_err(io)?;
        let len = file.metadata().map_err(io)?.len();
        if len < HEADER_SIZE as u64 {
            return Err(PakError::TooSmall { path, len });
        }

        // ReadHeader: the last 512 bytes are the encrypted header.
        let mut header = [0u8; HEADER_SIZE];
        file.seek(SeekFrom::End(-(HEADER_SIZE as i64)))
            .map_err(io)?;
        file.read_exact(&mut header).map_err(io)?;
        // DecryptHeaderData
        if !decrypt_aes(&mut header, &XL_GAMES_KEY) {
            return Err(PakError::Crypto { path });
        }
        let (pak_type, file_count, extra_file_count) = if &header[0..4] == b"WIBO" {
            (
                PakFileType::TypeA,
                read_u32(&header, 8),
                read_u32(&header, 12),
            )
        } else if &header[8..12] == b"IDEJ" {
            (
                PakFileType::TypeB,
                read_u32(&header, 12),
                read_u32(&header, 0),
            )
        } else if &header[0..4] == b"ZERO" {
            (
                PakFileType::TypeF,
                read_u32(&header, 8),
                read_u32(&header, 12),
            )
        } else {
            return Err(PakError::InvalidHeader { path });
        };

        // LoadRawFAT. C# computes `(fileCount + extraFileCount)` in uint (wrapping), then widens.
        let total_count = file_count.wrapping_add(extra_file_count);
        let total_file_info_size = i64::from(total_count) * FILE_INFO_SIZE as i64;
        let mut first_file_info_offset = len as i64;
        first_file_info_offset -= HEADER_SIZE as i64;
        first_file_info_offset -= total_file_info_size;
        // Align to previous block of 512 bytes (C# `%` keeps the sign of the dividend).
        let dif = first_file_info_offset % 0x200;
        first_file_info_offset -= dif;
        if first_file_info_offset < 0 {
            // C#: setting a negative stream Position throws -> OpenPak fails.
            return Err(PakError::InvalidFatOffset {
                path,
                offset: first_file_info_offset,
            });
        }

        // The FAT is everything from `first_file_info_offset` to the end of the file; only the
        // entry part is needed. Reads past the end yield zeroes, like the C# MemoryStream.Read
        // into a fresh zeroed buffer.
        let fat_len = (len as i64 - first_file_info_offset) as u64;
        let wanted = (total_file_info_size as u64).min(fat_len);
        let mut fat = vec![0u8; total_file_info_size as usize];
        file.seek(SeekFrom::Start(first_file_info_offset as u64))
            .map_err(io)?;
        file.read_exact(&mut fat[..wanted as usize]).map_err(io)?;

        let mut pak = Pak {
            path,
            pak_type,
            file_count,
            extra_file_count,
            first_file_info_offset,
            add_file_offset: 0,
            files: Vec::with_capacity(file_count as usize),
            index: HashMap::with_capacity(file_count as usize),
            extra_files: Vec::with_capacity(extra_file_count as usize),
        };
        pak.read_file_table(&mut fat)?;
        Ok(pak)
    }

    /// `AAPakFileHeader.ReadFileTable`: decrypts every 0x150-byte entry on its own (fresh zero
    /// IV each time) and sorts it into files / extra files according to the pak type.
    fn read_file_table(&mut self, fat: &mut [u8]) -> Result<(), PakError> {
        let mut files_to_go = self.file_count;
        let mut extra_to_go = self.extra_file_count;
        let mut file_index_counter: i32 = -1;
        let mut deleted_index_counter: i32 = -1;

        let (chunks, _) = fat.as_chunks_mut::<FILE_INFO_SIZE>();
        for chunk in chunks {
            if !decrypt_aes(chunk, &XL_GAMES_KEY) {
                return Err(PakError::Crypto {
                    path: self.path.clone(),
                });
            }
            let mut pfi = parse_file_info(self.pak_type, chunk);

            // Update our "end of file data" location if needed (done after the add in C#, but
            // only depends on the entry itself).
            let end = pfi
                .offset
                .wrapping_add(pfi.size)
                .wrapping_add(i64::from(pfi.padding_size));
            if end > self.add_file_offset {
                self.add_file_offset = end;
            }

            match self.pak_type {
                PakFileType::TypeA | PakFileType::TypeF => {
                    // Files first, extra files last.
                    if files_to_go > 0 {
                        file_index_counter += 1;
                        pfi.entry_index_number = file_index_counter;
                        files_to_go -= 1;
                        self.add_file(pfi)?;
                    } else if extra_to_go > 0 {
                        deleted_index_counter += 1;
                        pfi.deleted_index_number = deleted_index_counter;
                        extra_to_go -= 1;
                        self.extra_files.push(pfi);
                    }
                }
                PakFileType::TypeB => {
                    // Extra files first, normal files last.
                    // NOTE: C# bug mirrored: for TypeB the counters are swapped, extra files get
                    // an `entryIndexNumber` and normal files a `deletedIndexNumber`.
                    if extra_to_go > 0 {
                        file_index_counter += 1;
                        pfi.entry_index_number = file_index_counter;
                        extra_to_go -= 1;
                        self.extra_files.push(pfi);
                    } else if files_to_go > 0 {
                        deleted_index_counter += 1;
                        pfi.deleted_index_number = deleted_index_counter;
                        files_to_go -= 1;
                        self.add_file(pfi)?;
                    }
                }
            }
        }
        Ok(())
    }

    /// `pakFiles.Add` + its duplicate-key `ArgumentException` (which makes `OpenPak` fail).
    fn add_file(&mut self, pfi: PakFileInfo) -> Result<(), PakError> {
        if self.index.contains_key(&pfi.name) {
            return Err(PakError::DuplicateFileName {
                path: self.path.clone(),
                name: pfi.name,
            });
        }
        self.index.insert(pfi.name.clone(), self.files.len());
        self.files.push(pfi);
        Ok(())
    }

    /// Path this pak was opened from (`_gpFilePath`).
    pub fn path(&self) -> &Path {
        &self.path
    }

    /// `PakType`.
    pub fn pak_type(&self) -> PakFileType {
        self.pak_type
    }

    /// `_header.fileCount`.
    pub fn file_count(&self) -> u32 {
        self.file_count
    }

    /// `_header.extraFileCount`.
    pub fn extra_file_count(&self) -> u32 {
        self.extra_file_count
    }

    /// `_header.FirstFileInfoOffset`.
    pub fn first_file_info_offset(&self) -> i64 {
        self.first_file_info_offset
    }

    /// `_header.AddFileOffset`.
    pub fn add_file_offset(&self) -> i64 {
        self.add_file_offset
    }

    /// `pakFiles.Values`, in file table order.
    pub fn files(&self) -> &[PakFileInfo] {
        &self.files
    }

    /// `pakFiles.Keys`, in file table order.
    pub fn file_names(&self) -> impl Iterator<Item = &str> + '_ {
        self.files.iter().map(|f| f.name.as_str())
    }

    /// `extraFiles` (normally all named `__unused__`).
    pub fn extra_files(&self) -> &[PakFileInfo] {
        &self.extra_files
    }

    /// `GetFileByName`: exact (case-sensitive) lookup after [`to_pak_slashes`].
    pub fn get_file_by_name(&self, file_name: &str) -> Option<&PakFileInfo> {
        self.index
            .get(&to_pak_slashes(file_name))
            .map(|&i| &self.files[i])
    }

    /// `FileExists`.
    pub fn file_exists(&self, file_name: &str) -> bool {
        self.get_file_by_name(file_name).is_some()
    }

    /// `GetFilesInDirectory(directoryName)`: files whose lower-cased directory part equals the
    /// lower-cased `directory_name` (no recursion).
    pub fn files_in_directory(&self, directory_name: &str) -> Vec<&PakFileInfo> {
        let directory_name = directory_name.to_lowercase();
        self.files
            .iter()
            .filter(|f| pak_directory_name(&f.name) == directory_name)
            .collect()
    }

    /// `ExportFileAsStream(AAPakFileInfo)` read to the end: opens a fresh handle (like
    /// `ExportFileAsStreamCloned`) and reads up to `size` bytes at `offset`. A short read at the
    /// end of the pak returns the available bytes (SubStream semantics). Returns `None` on I/O
    /// errors or a negative offset (SubStream throws).
    pub fn read_entry(&self, info: &PakFileInfo) -> Option<Vec<u8>> {
        if info.offset < 0 {
            return None;
        }
        let mut file = File::open(&self.path).ok()?;
        if file.metadata().ok()?.len() == 0 {
            // ExportFileAsStreamCloned returns an empty MemoryStream for an empty file.
            return Some(Vec::new());
        }
        if info.size <= 0 {
            return Some(Vec::new());
        }
        file.seek(SeekFrom::Start(info.offset as u64)).ok()?;
        let mut buf = Vec::new();
        file.take(info.size as u64).read_to_end(&mut buf).ok()?;
        Some(buf)
    }

    /// `ExportFileAsStreamCloned(fileName)` read to the end; `None` when the file is not in the
    /// pak (C# returns an empty `MemoryStream`).
    pub fn read_file(&self, file_name: &str) -> Option<Vec<u8>> {
        let info = self.get_file_by_name(file_name)?;
        self.read_entry(info)
    }
}

/// Test helpers to build synthetic paks (inverse of the reader, `WriteToFAT` layouts).
#[cfg(test)]
pub(crate) mod test_util {
    use super::*;
    use aes::cipher::BlockEncryptMut;

    type Aes128CbcEnc = cbc::Encryptor<aes::Aes128>;

    fn encrypt(data: &mut [u8]) {
        let iv = [0u8; 16];
        let len = data.len();
        Aes128CbcEnc::new(&XL_GAMES_KEY.into(), &iv.into())
            .encrypt_padded_mut::<NoPadding>(data, len)
            .expect("block aligned");
    }

    fn put_name(b: &mut [u8], at: usize, name: &str) {
        for (i, c) in name.bytes().take(NAME_SIZE).enumerate() {
            b[at + i] = c;
        }
    }

    fn entry(pak_type: PakFileType, name: &str, offset: i64, size: i64, pad: i32) -> Vec<u8> {
        let mut b = vec![0u8; FILE_INFO_SIZE];
        let md5 = [0xABu8; 16];
        match pak_type {
            PakFileType::TypeA => {
                put_name(&mut b, 0, name);
                b[0x108..0x110].copy_from_slice(&offset.to_le_bytes());
                b[0x110..0x118].copy_from_slice(&size.to_le_bytes());
                b[0x118..0x120].copy_from_slice(&size.to_le_bytes());
                b[0x120..0x124].copy_from_slice(&pad.to_le_bytes());
                b[0x124..0x134].copy_from_slice(&md5);
                b[0x138..0x140].copy_from_slice(&11i64.to_le_bytes());
                b[0x140..0x148].copy_from_slice(&22i64.to_le_bytes());
            }
            PakFileType::TypeB => {
                b[0..4].copy_from_slice(&pad.to_le_bytes());
                b[4..0x14].copy_from_slice(&md5);
                b[0x14..0x18].copy_from_slice(&0x8000_0000u32.to_le_bytes());
                b[0x18..0x20].copy_from_slice(&size.to_le_bytes());
                put_name(&mut b, 0x20, name);
                b[0x128..0x130].copy_from_slice(&size.to_le_bytes());
                b[0x130..0x138].copy_from_slice(&offset.to_le_bytes());
                b[0x138..0x140].copy_from_slice(&22i64.to_le_bytes());
                b[0x140..0x148].copy_from_slice(&11i64.to_le_bytes());
            }
            PakFileType::TypeF => {
                put_name(&mut b, 8, name);
                b[0x110..0x118].copy_from_slice(&offset.to_le_bytes());
                b[0x118..0x120].copy_from_slice(&size.to_le_bytes());
                b[0x120..0x128].copy_from_slice(&size.to_le_bytes());
                b[0x128..0x12C].copy_from_slice(&pad.to_le_bytes());
                b[0x12C..0x13C].copy_from_slice(&md5);
                b[0x140..0x148].copy_from_slice(&11i64.to_le_bytes());
            }
        }
        encrypt(&mut b);
        b
    }

    /// Builds a complete pak image containing `files` plus `extra` `__unused__` entries.
    pub(crate) fn build_pak(pak_type: PakFileType, files: &[(&str, &[u8])], extra: u32) -> Vec<u8> {
        let mut out = Vec::new();
        let mut entries = Vec::new();
        for (name, data) in files {
            let offset = out.len() as i64;
            out.extend_from_slice(data);
            let pad = (0x200 - out.len() % 0x200) % 0x200;
            out.resize(out.len() + pad, 0);
            entries.push(entry(pak_type, name, offset, data.len() as i64, pad as i32));
        }
        let extras: Vec<Vec<u8>> = (0..extra)
            .map(|_| {
                let offset = out.len() as i64;
                out.resize(out.len() + 0x200, 0);
                entry(pak_type, "__unused__", offset, 0, 0x200)
            })
            .collect();
        let ordered: Vec<&Vec<u8>> = match pak_type {
            PakFileType::TypeB => extras.iter().chain(entries.iter()).collect(),
            _ => entries.iter().chain(extras.iter()).collect(),
        };
        for e in ordered {
            out.extend_from_slice(e);
        }
        let pad = (0x200 - out.len() % 0x200) % 0x200;
        out.resize(out.len() + pad, 0);

        let mut header = [0u8; HEADER_SIZE];
        let fc = files.len() as u32;
        match pak_type {
            PakFileType::TypeA | PakFileType::TypeF => {
                header[0..4].copy_from_slice(if pak_type == PakFileType::TypeA {
                    b"WIBO"
                } else {
                    b"ZERO"
                });
                header[8..12].copy_from_slice(&fc.to_le_bytes());
                header[12..16].copy_from_slice(&extra.to_le_bytes());
            }
            PakFileType::TypeB => {
                header[0..4].copy_from_slice(&extra.to_le_bytes());
                header[8..12].copy_from_slice(b"IDEJ");
                header[12..16].copy_from_slice(&fc.to_le_bytes());
            }
        }
        encrypt(&mut header);
        out.extend_from_slice(&header);
        out
    }

    /// Unique temporary path for a test.
    pub(crate) fn temp_path(tag: &str) -> PathBuf {
        use std::sync::atomic::{AtomicU32, Ordering};
        static COUNTER: AtomicU32 = AtomicU32::new(0);
        let n = COUNTER.fetch_add(1, Ordering::Relaxed);
        let nanos = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(0);
        std::env::temp_dir().join(format!(
            "aaemu_test_{tag}_{}_{n}_{nanos}",
            std::process::id()
        ))
    }
}

#[cfg(test)]
mod tests {
    use super::test_util::{build_pak, temp_path};
    use super::*;

    fn write_temp(bytes: &[u8]) -> PathBuf {
        let p = temp_path("pak");
        std::fs::write(&p, bytes).unwrap();
        p
    }

    fn check_roundtrip(pak_type: PakFileType) {
        let world = b"<World name=\"main_world\"/>".to_vec();
        let big = vec![7u8; 1000];
        let files: [(&str, &[u8]); 3] = [
            ("game/worlds/main_world/world.xml", &world),
            ("game/big.bin", &big),
            ("readme.txt", b"hi"),
        ];
        let path = write_temp(&build_pak(pak_type, &files, 2));
        let pak = Pak::open(&path).unwrap();
        assert_eq!(pak.pak_type(), pak_type);
        assert_eq!(pak.file_count(), 3);
        assert_eq!(pak.extra_file_count(), 2);
        assert_eq!(pak.extra_files().len(), 2);
        assert!(pak.extra_files().iter().all(|f| f.name == "__unused__"));
        assert_eq!(
            pak.file_names().collect::<Vec<_>>(),
            vec![
                "game/worlds/main_world/world.xml",
                "game/big.bin",
                "readme.txt"
            ]
        );
        assert!(pak.file_exists("game/big.bin"));
        assert!(!pak.file_exists("game/BIG.bin"));
        assert_eq!(
            pak.read_file("game/worlds/main_world/world.xml"),
            Some(world)
        );
        assert_eq!(pak.read_file("game/big.bin"), Some(big));
        assert_eq!(pak.read_file("readme.txt"), Some(b"hi".to_vec()));
        assert_eq!(pak.read_file("missing"), None);
        let info = pak.get_file_by_name("game/big.bin").unwrap();
        assert_eq!(info.md5, [0xAB; 16]);
        assert_eq!(info.create_time, 11);
        assert_eq!(info.size_duplicate, 1000);
        assert_eq!(
            pak.files_in_directory("GAME")
                .iter()
                .map(|f| f.name.as_str())
                .collect::<Vec<_>>(),
            vec!["game/big.bin"]
        );
        assert_eq!(pak.files_in_directory("").len(), 1);
        std::fs::remove_file(&path).ok();
    }

    #[test]
    fn type_a_roundtrip() {
        check_roundtrip(PakFileType::TypeA);
    }

    #[test]
    fn type_b_roundtrip() {
        check_roundtrip(PakFileType::TypeB);
    }

    #[test]
    fn type_f_roundtrip() {
        check_roundtrip(PakFileType::TypeF);
    }

    #[test]
    fn type_a_index_numbers_and_add_offset() {
        let files: [(&str, &[u8]); 2] = [("a", b"1"), ("b", b"22")];
        let path = write_temp(&build_pak(PakFileType::TypeA, &files, 1));
        let pak = Pak::open(&path).unwrap();
        assert_eq!(pak.files()[0].entry_index_number, 0);
        assert_eq!(pak.files()[1].entry_index_number, 1);
        assert_eq!(pak.extra_files()[0].deleted_index_number, 0);
        assert_eq!(pak.add_file_offset(), 0x600);
        assert_eq!(pak.first_file_info_offset(), 0x600);
        std::fs::remove_file(&path).ok();
    }

    #[test]
    #[cfg(windows)]
    fn backslashes_are_converted_on_windows() {
        let files: [(&str, &[u8]); 1] = [("game/a.txt", b"x")];
        let path = write_temp(&build_pak(PakFileType::TypeA, &files, 0));
        let pak = Pak::open(&path).unwrap();
        assert!(pak.file_exists("game\\a.txt"));
        std::fs::remove_file(&path).ok();
    }

    #[test]
    fn rejects_garbage_and_small_files() {
        let small = write_temp(&[0u8; 10]);
        assert!(matches!(Pak::open(&small), Err(PakError::TooSmall { .. })));
        let garbage = write_temp(&[0x55u8; 0x400]);
        assert!(matches!(
            Pak::open(&garbage),
            Err(PakError::InvalidHeader { .. })
        ));
        std::fs::remove_file(&small).ok();
        std::fs::remove_file(&garbage).ok();
    }

    #[test]
    fn rejects_duplicate_names() {
        let files: [(&str, &[u8]); 2] = [("a", b"1"), ("a", b"2")];
        let path = write_temp(&build_pak(PakFileType::TypeA, &files, 0));
        assert!(matches!(
            Pak::open(&path),
            Err(PakError::DuplicateFileName { .. })
        ));
        std::fs::remove_file(&path).ok();
    }
}
