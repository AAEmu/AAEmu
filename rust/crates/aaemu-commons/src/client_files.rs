//! Port of `AAEmu.Game/IO` (`ClientFileManager`, `ClientSource`, `ClientSourceType`,
//! `ClientDataConfig`): locates client data files in `game_pak` archives and/or folders of
//! extracted client files.
//!
//! Sources are searched in the order they were added; the first source containing a file wins.
//! File names are relative client paths such as `game/worlds/main_world/world.xml` (`/` or `\`).
//!
//! Differences from the C#:
//! * `ClientFileManager` is a value instead of a static class; the game server owns one (e.g. in
//!   an `Arc` or `OnceLock`). All lookups take `&self` and pak reads open their own file
//!   handle, so it can be shared between threads.
//! * Streams are returned as `Option` (`None` = not found) instead of `null` / empty streams.
//! * Wildcard matching against pak names is case-insensitive in both the recursive and
//!   non-recursive branches (the C# non-recursive branch matched the lower-cased pattern against
//!   the original-case name).

use std::fs::File;
use std::io::{self, Read, Seek};
use std::path::{Path, PathBuf, MAIN_SEPARATOR};

use serde::Deserialize;

use crate::aapak::AAPak;
use crate::file_manager::{decode_text, get_matching_files_in_directory, wildcard_match};

/// `ClientDataConfig` (the `ClientData` section of the game server configuration).
#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
#[serde(default)]
pub struct ClientDataConfig {
    #[serde(rename = "Sources", alias = "sources")]
    pub sources: Vec<String>,
}

/// `ClientSourceType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClientSourceType {
    /// A folder of extracted client files.
    Directory,
    /// A `game_pak` archive.
    GamePak,
}

/// A readable, seekable client file stream.
pub trait ReadSeek: Read + Seek + Send {}
impl<T: Read + Seek + Send> ReadSeek for T {}

/// `ClientSource`: one directory or `game_pak`.
#[derive(Debug)]
pub struct ClientSource {
    pub source_type: ClientSourceType,
    /// Directory name or `game_pak` file name.
    pub path_name: PathBuf,
    pub game_pak: Option<AAPak>,
}

impl ClientSource {
    /// `new ClientSource { ... }.Open()`: `None` if the directory doesn't exist or the pak can't
    /// be opened.
    pub fn open(source_type: ClientSourceType, path_name: impl Into<PathBuf>) -> Option<Self> {
        let path_name = path_name.into();
        match source_type {
            ClientSourceType::Directory if path_name.is_dir() => Some(Self {
                source_type,
                path_name,
                game_pak: None,
            }),
            ClientSourceType::Directory => None,
            ClientSourceType::GamePak => match AAPak::open(&path_name, true) {
                Ok(pak) => Some(Self {
                    source_type,
                    path_name,
                    game_pak: Some(pak),
                }),
                Err(e) => {
                    tracing::debug!("Failed to open game_pak {}: {e}", path_name.display());
                    None
                }
            },
        }
    }

    /// `Close`.
    pub fn close(&mut self) {
        if let Some(pak) = self.game_pak.as_mut() {
            let _ = pak.close();
        }
    }

    /// `FileExists`.
    pub fn file_exists(&self, file_name: &str) -> bool {
        match self.source_type {
            ClientSourceType::Directory => self.path_name.join(file_name).is_file(),
            ClientSourceType::GamePak => self
                .game_pak
                .as_ref()
                .is_some_and(|p| p.is_open() && p.file_exists(file_name)),
        }
    }

    /// `WildcardToRegex`: the regex the C# built from a wildcard pattern (kept for reference;
    /// matching itself uses [`wildcard_match`]).
    pub fn wildcard_to_regex(pattern: &str) -> String {
        let mut s = String::from("^");
        for c in pattern.chars() {
            match c {
                '*' => s.push_str(".*"),
                '?' => s.push('.'),
                '\\' | '.' | '+' | '(' | ')' | '[' | ']' | '{' | '}' | '^' | '$' | '|' | '#'
                | ' ' => {
                    s.push('\\');
                    s.push(c);
                }
                _ => s.push(c),
            }
        }
        s.push('$');
        s
    }

    /// `GetFilesInDirectory`: file names in `directory` matching `search_pattern`.
    ///
    /// * Directory sources return full paths (`<path_name>/<directory>/...`), using normal
    ///   directory mask semantics.
    /// * Pak sources return pak names; the pattern is applied as `*pattern*` (case-insensitive)
    ///   and a blank pattern matches everything. With `include_sub_directories`, every file whose
    ///   name *starts with* `directory` is considered.
    pub fn get_files_in_directory(
        &self,
        directory: &str,
        search_pattern: &str,
        include_sub_directories: bool,
    ) -> Vec<String> {
        match self.source_type {
            ClientSourceType::Directory => {
                let root_dir = directory.replace(['/', '\\'], std::path::MAIN_SEPARATOR_STR);
                let dir = self.path_name.join(root_dir);
                if !dir.is_dir() {
                    return Vec::new();
                }
                get_matching_files_in_directory(&dir, search_pattern, include_sub_directories)
                    .into_iter()
                    .map(|p| p.to_string_lossy().into_owned())
                    .collect()
            }
            ClientSourceType::GamePak => {
                let Some(pak) = self.game_pak.as_ref() else {
                    return Vec::new();
                };
                let root_dir = directory.replace('\\', "/").to_lowercase();
                let wild = format!("*{}*", search_pattern.to_lowercase());
                let blank = search_pattern.trim().is_empty();
                let matches =
                    |name: &str| blank || wildcard_match(&wild, &name.to_lowercase(), false);
                if include_sub_directories {
                    pak.file_names()
                        .filter(|n| n.to_lowercase().starts_with(&root_dir) && matches(n))
                        .map(str::to_string)
                        .collect()
                } else {
                    pak.get_files_in_directory(&root_dir)
                        .into_iter()
                        .filter(|f| matches(&f.name))
                        .map(|f| f.name.clone())
                        .collect()
                }
            }
        }
    }

    /// `GetFileStream`: `Ok(None)` if the file is not in this source.
    pub fn get_file_stream(&self, file_name: &str) -> io::Result<Option<Box<dyn ReadSeek>>> {
        match self.source_type {
            ClientSourceType::Directory => {
                let p = self.path_name.join(file_name);
                if !p.is_file() {
                    return Ok(None);
                }
                Ok(Some(Box::new(File::open(p)?)))
            }
            ClientSourceType::GamePak => {
                let Some(pak) = self.game_pak.as_ref() else {
                    return Ok(None);
                };
                match pak.export_file_as_stream_cloned(file_name) {
                    Ok(Some(s)) => Ok(Some(Box::new(s))),
                    Ok(None) => Ok(None),
                    Err(e) => Err(io::Error::other(e)),
                }
            }
        }
    }

    /// Reads a whole file; `Ok(None)` if it is not in this source.
    pub fn get_file_bytes(&self, file_name: &str) -> io::Result<Option<Vec<u8>>> {
        let Some(mut s) = self.get_file_stream(file_name)? else {
            return Ok(None);
        };
        let mut out = Vec::new();
        s.read_to_end(&mut out)?;
        Ok(Some(out))
    }

    /// `GetFileAsString`: the file as text (BOM-aware UTF-8), or `""` on any error.
    pub fn get_file_as_string(&self, file_name: &str) -> String {
        match self.get_file_bytes(file_name) {
            Ok(Some(b)) => decode_text(&b),
            _ => String::new(),
        }
    }

    /// `GetBaseName`: a source-independent name used to de-duplicate listings (pak names with
    /// platform separators; directory paths relative to `path_name`).
    pub fn get_base_name(&self, file_name: &str) -> String {
        match self.source_type {
            ClientSourceType::GamePak => file_name.replace('/', std::path::MAIN_SEPARATOR_STR),
            ClientSourceType::Directory => {
                let base = self.path_name.to_string_lossy();
                match file_name.strip_prefix(base.as_ref()) {
                    Some(rest) => rest.trim_start_matches(MAIN_SEPARATOR).to_string(),
                    None => file_name.to_string(),
                }
            }
        }
    }
}

impl Drop for ClientSource {
    fn drop(&mut self) {
        self.close();
    }
}

/// `ClientFileManager`.
#[derive(Debug, Default)]
pub struct ClientFileManager {
    sources: Vec<ClientSource>,
    initialized: bool,
}

impl ClientFileManager {
    pub fn new() -> Self {
        Self::default()
    }

    /// Creates a manager and calls [`ClientFileManager::initialize`].
    pub fn from_config(config: &ClientDataConfig) -> Self {
        let mut m = Self::new();
        m.initialize(config);
        m
    }

    /// `Sources`.
    pub fn sources(&self) -> &[ClientSource] {
        &self.sources
    }

    /// `AddSource`: adds a directory of unpacked client files or a `game_pak` file.
    pub fn add_source(&mut self, path_name: &str) -> bool {
        if path_name.trim().is_empty() {
            return false;
        }
        let path = std::path::absolute(path_name).unwrap_or_else(|_| PathBuf::from(path_name));
        if path.is_dir() {
            if let Some(src) = ClientSource::open(ClientSourceType::Directory, &path) {
                self.sources.push(src);
                tracing::info!(
                    "Using Source [{}:{:?}]: {}",
                    self.sources.len(),
                    ClientSourceType::Directory,
                    path.display()
                );
                return true;
            }
        }
        if path.is_file() {
            return match ClientSource::open(ClientSourceType::GamePak, &path) {
                Some(src) => {
                    self.sources.push(src);
                    tracing::info!(
                        "Using Source [{}:{:?}]: {}",
                        self.sources.len(),
                        ClientSourceType::GamePak,
                        path.display()
                    );
                    true
                }
                None => {
                    tracing::error!(
                        "Source could not be opened, or is invalid: {}",
                        path.display()
                    );
                    false
                }
            };
        }
        tracing::warn!("Client Source not found {}", path.display());
        false
    }

    /// `ClearSources`.
    pub fn clear_sources(&mut self) {
        while let Some(mut s) = self.sources.pop() {
            s.close();
        }
    }

    /// `GetFileSource`: the first source containing `file_name`.
    pub fn get_file_source(&self, file_name: &str) -> Option<&ClientSource> {
        self.sources.iter().find(|s| s.file_exists(file_name))
    }

    /// `FileExists`.
    pub fn file_exists(&self, file_name: &str) -> bool {
        self.get_file_source(file_name).is_some()
    }

    /// `GetFileStream`: `Ok(None)` if no source has the file.
    pub fn get_file_stream(&self, file_name: &str) -> io::Result<Option<Box<dyn ReadSeek>>> {
        match self.get_file_source(file_name) {
            Some(s) => s.get_file_stream(file_name),
            None => {
                tracing::trace!("GetFileStream({file_name}) not found");
                Ok(None)
            }
        }
    }

    /// Reads a whole file from the first source that has it.
    pub fn get_file_bytes(&self, file_name: &str) -> io::Result<Option<Vec<u8>>> {
        match self.get_file_source(file_name) {
            Some(s) => s.get_file_bytes(file_name),
            None => Ok(None),
        }
    }

    /// `GetFileAsString`: `""` if not found or unreadable.
    pub fn get_file_as_string(&self, file_name: &str) -> String {
        self.get_file_source(file_name)
            .map(|s| s.get_file_as_string(file_name))
            .unwrap_or_default()
    }

    /// `Initialize`: (once) replaces the sources with the configured ones.
    pub fn initialize(&mut self, config: &ClientDataConfig) {
        if self.initialized {
            return;
        }
        self.clear_sources();
        for source in &config.sources {
            if !self.add_source(source) {
                tracing::warn!("{source} is not a valid source for client data");
            }
        }
        if self.sources.is_empty() {
            tracing::error!(
                "No valid client sources have been defined or found, some features will not work !"
            );
        }
        self.initialized = true;
    }

    /// `GetFilesInDirectory`: union over all sources, de-duplicated by
    /// [`ClientSource::get_base_name`] (earlier sources win). Returned names can be passed back
    /// to the other lookup functions.
    pub fn get_files_in_directory(
        &self,
        directory: &str,
        search_pattern: &str,
        include_sub_directories: bool,
    ) -> Vec<String> {
        let mut list = Vec::new();
        let mut seen = std::collections::HashSet::new();
        for source in &self.sources {
            for name in
                source.get_files_in_directory(directory, search_pattern, include_sub_directories)
            {
                if seen.insert(source.get_base_name(&name)) {
                    list.push(name);
                }
            }
        }
        list
    }
}

/// Default location of the 1.2 client used by the env-gated integration tests.
pub fn default_test_client_pak() -> Option<PathBuf> {
    std::env::var_os("AAEMU_CLIENT_PAK")
        .map(PathBuf::from)
        .or_else(|| Some(PathBuf::from(r"D:\AA1.2\game_pak")))
        .filter(|p: &PathBuf| Path::new(p).is_file())
}

#[cfg(test)]
mod tests {
    use std::io::Cursor;

    use chrono::Utc;

    use super::*;
    use crate::test_util::TempDir;

    fn make_pak(path: &Path, files: &[(&str, &[u8])]) {
        let mut pak = AAPak::create(path).unwrap();
        for (n, d) in files {
            pak.add_as_new_file(
                n,
                &mut Cursor::new(d.to_vec()),
                Utc::now(),
                Utc::now(),
                false,
            )
            .unwrap();
        }
        pak.close().unwrap();
    }

    #[test]
    fn dir_and_pak_sources() {
        let tmp = TempDir::new("cfm");
        let unpacked = tmp.path().join("unpacked");
        let worlds = unpacked.join("game").join("worlds").join("w1");
        std::fs::create_dir_all(&worlds).unwrap();
        std::fs::write(worlds.join("world.xml"), "\u{FEFF}<World name=\"dir\"/>").unwrap();
        let pak_path = tmp.path().join("game_pak");
        make_pak(
            &pak_path,
            &[
                ("game/worlds/w1/world.xml", b"<World name=\"pak\"/>"),
                ("game/worlds/w2/world.xml", b"<World name=\"pak2\"/>"),
                ("game/worlds/w2/cells/a.bai", b"x"),
                ("game/worlds/w2/b.BAI", b"y"),
            ],
        );

        let config: ClientDataConfig = serde_json::from_str(&format!(
            r#"{{"Sources": [{:?}, {:?}, "does/not/exist", ""]}}"#,
            unpacked.to_string_lossy(),
            pak_path.to_string_lossy()
        ))
        .unwrap();
        let mut m = ClientFileManager::from_config(&config);
        assert_eq!(m.sources().len(), 2);
        assert_eq!(m.sources()[0].source_type, ClientSourceType::Directory);
        assert_eq!(m.sources()[1].source_type, ClientSourceType::GamePak);
        // initialize() only runs once
        m.initialize(&ClientDataConfig::default());
        assert_eq!(m.sources().len(), 2);

        // Directory source wins for w1, pak provides w2.
        assert_eq!(
            m.get_file_as_string("game/worlds/w1/world.xml"),
            "<World name=\"dir\"/>"
        );
        assert_eq!(
            m.get_file_as_string("game\\worlds\\w2\\world.xml"),
            "<World name=\"pak2\"/>"
        );
        assert_eq!(m.get_file_as_string("missing.xml"), "");
        assert!(m.get_file_stream("missing.xml").unwrap().is_none());
        let mut s = m
            .get_file_stream("game/worlds/w2/world.xml")
            .unwrap()
            .unwrap();
        let mut buf = String::new();
        s.read_to_string(&mut buf).unwrap();
        assert!(buf.contains("pak2"));

        let list = m.get_files_in_directory("game/worlds", "world.xml", true);
        assert_eq!(list.len(), 2, "{list:?}");
        assert!(list[0].ends_with("world.xml") && Path::new(&list[0]).is_absolute());
        assert_eq!(list[1], "game/worlds/w2/world.xml");
        // Returned names can be fed back.
        assert!(list.iter().all(|n| m.file_exists(n)));

        let pak = &m.sources()[1];
        let mut bai = pak.get_files_in_directory("game/worlds/w2", "*.bai", false);
        bai.sort();
        assert_eq!(bai, ["game/worlds/w2/b.BAI"]);
        assert_eq!(
            pak.get_files_in_directory("game/worlds/w2", "", true).len(),
            3
        );
        assert_eq!(pak.get_base_name("a/b"), format!("a{MAIN_SEPARATOR}b"));
        let dir_src = &m.sources()[0];
        let full = dir_src.path_name.join("x").to_string_lossy().into_owned();
        assert_eq!(dir_src.get_base_name(&full), "x");

        m.clear_sources();
        assert!(m.sources().is_empty());
        assert!(!m.add_source("   "));
        assert!(!m.add_source(
            &tmp.path()
                .join("unpacked")
                .join("game")
                .join("worlds")
                .join("w1")
                .join("world.xml")
                .to_string_lossy()
        ));
    }

    #[test]
    fn wildcard_regex_text() {
        assert_eq!(
            ClientSource::wildcard_to_regex("*world.xml*"),
            "^.*world\\.xml.*$"
        );
    }
}
