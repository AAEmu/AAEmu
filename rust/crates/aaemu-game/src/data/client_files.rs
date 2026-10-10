//! Port of `AAEmu.Game/IO/ClientFileManager.cs`, `ClientSource.cs`, `ClientSourceType.cs` and
//! `ClientDataConfig.cs`.
//!
//! Client data (world.xml files, heightmaps, ...) is read from an ordered list of sources, each
//! either a directory with unpacked client files or a `game_pak` file. The first source that has a
//! file wins. Unlike C#, [`ClientFileManager`] is a plain value instead of a static class.

use std::path::{Component, Path, PathBuf, MAIN_SEPARATOR, MAIN_SEPARATOR_STR};

use regex::Regex;
use tracing::{debug, error, info, trace, warn};

use super::pak::Pak;

/// `ClientDataConfig` (the `ClientData` section of `Configurations/ClientData.json`).
#[derive(Debug, Clone, Default, PartialEq, Eq, serde::Deserialize, serde::Serialize)]
pub struct ClientDataConfig {
    #[serde(rename = "Sources", default)]
    pub sources: Vec<String>,
}

/// `ClientSourceType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClientSourceType {
    Directory,
    GamePak,
}

impl std::fmt::Display for ClientSourceType {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            ClientSourceType::Directory => f.write_str("Directory"),
            ClientSourceType::GamePak => f.write_str("GamePak"),
        }
    }
}

/// `ClientSource`: one directory or opened `game_pak`.
#[derive(Debug)]
pub struct ClientSource {
    source_type: ClientSourceType,
    /// `PathName`: directory name or game_pak file name (full path).
    path_name: String,
    /// `GamePak` (only for [`ClientSourceType::GamePak`]).
    game_pak: Option<Pak>,
}

/// `ClientSource.WildcardToRegex`: `^` + escaped pattern with `*` -> `.*` and `?` -> `.` + `$`.
pub fn wildcard_to_regex(pattern: &str) -> String {
    format!(
        "^{}$",
        regex::escape(pattern)
            .replace("\\*", ".*")
            .replace("\\?", ".")
    )
}

/// Approximation of the .NET `Directory.GetFiles` search pattern matching
/// (`EnumerationOptions.Compatible` = `MatchType.Win32`, `MatchCasing.PlatformDefault`):
/// the pattern is translated like `FileSystemName.TranslateWin32Expression` (`?` -> DOS_QM,
/// `.` before `?`/`*` -> DOS_DOT, trailing `*.` -> DOS_STAR) and matched against the file name
/// only. Case-insensitive on Windows, case-sensitive elsewhere. An empty pattern matches nothing.
pub fn win32_name_matches(pattern: &str, name: &str) -> bool {
    #[derive(Clone, Copy, PartialEq)]
    enum Tok {
        Lit(char),
        Star,
        DosStar,
        DosQm,
        DosDot,
    }

    if pattern.is_empty() {
        return false;
    }
    if pattern == "*" || pattern == "*.*" {
        return true;
    }
    let ignore_case = cfg!(windows);
    let norm = |c: char| -> char {
        if ignore_case {
            c.to_lowercase().next().unwrap_or(c)
        } else {
            c
        }
    };

    let p: Vec<char> = pattern.chars().collect();
    let mut toks: Vec<Tok> = Vec::with_capacity(p.len());
    for (i, &c) in p.iter().enumerate() {
        match c {
            '.' => {
                if i >= 1 && i == p.len() - 1 && p[i - 1] == '*' {
                    if let Some(last) = toks.last_mut() {
                        *last = Tok::DosStar;
                    }
                } else if i < p.len() - 1 && (p[i + 1] == '?' || p[i + 1] == '*') {
                    toks.push(Tok::DosDot);
                } else {
                    toks.push(Tok::Lit('.'));
                }
            }
            '?' => toks.push(Tok::DosQm),
            '*' => toks.push(Tok::Star),
            c => toks.push(Tok::Lit(norm(c))),
        }
    }

    let n: Vec<char> = name.chars().map(norm).collect();
    let last_dot = n.iter().rposition(|&c| c == '.');

    fn rec(t: &[Tok], n: &[char], pos: usize, last_dot: Option<usize>) -> bool {
        let Some((&first, rest)) = t.split_first() else {
            return pos == n.len();
        };
        match first {
            Tok::Lit(c) => pos < n.len() && n[pos] == c && rec(rest, n, pos + 1, last_dot),
            Tok::Star => (pos..=n.len()).any(|k| rec(rest, n, k, last_dot)),
            Tok::DosStar => {
                // Zero or more characters, but never past the final '.' of the name.
                let limit = match last_dot {
                    Some(d) if d >= pos => d,
                    _ => n.len(),
                };
                (pos..=limit).any(|k| rec(rest, n, k, last_dot))
            }
            Tok::DosQm => {
                if pos >= n.len() || n[pos] == '.' {
                    rec(rest, n, pos, last_dot)
                } else {
                    rec(rest, n, pos + 1, last_dot)
                }
            }
            Tok::DosDot => {
                if pos < n.len() && n[pos] == '.' {
                    rec(rest, n, pos + 1, last_dot)
                } else {
                    pos >= n.len() && rec(rest, n, pos, last_dot)
                }
            }
        }
    }

    rec(&toks, &n, 0, last_dot)
}

/// Lexical equivalent of `Path.GetFullPath(path)` with `base_dir` playing the role of the
/// current directory: joins relative paths onto `base_dir` and resolves `.` / `..` components.
pub fn full_path(path: &str, base_dir: &Path) -> PathBuf {
    let joined = base_dir.join(path);
    let mut out = PathBuf::new();
    for comp in joined.components() {
        match comp {
            Component::CurDir => {}
            Component::ParentDir => {
                // Never pop past the root/prefix.
                if matches!(out.components().next_back(), Some(Component::Normal(_))) {
                    out.pop();
                }
            }
            other => out.push(other.as_os_str()),
        }
    }
    out
}

/// `Directory.GetFiles(dir, pattern, option)`: breadth-first like .NET's `FileSystemEnumerator`
/// (a directory's files are returned before those of its sub directories, which are queued).
/// Entries of each directory are sorted by upper-cased name to mimic NTFS ordering and keep the
/// result deterministic on every platform. Symlinked directories are not followed.
fn directory_get_files(dir: &Path, pattern: &str, recursive: bool) -> Vec<String> {
    let mut result = Vec::new();
    let mut pending = std::collections::VecDeque::from([dir.to_path_buf()]);
    while let Some(current) = pending.pop_front() {
        let entries = match std::fs::read_dir(&current) {
            Ok(rd) => rd,
            Err(e) => {
                // .NET would throw (e.g. UnauthorizedAccessException) here.
                warn!("Could not enumerate {}: {e}", current.display());
                continue;
            }
        };
        let mut entries: Vec<_> = entries.filter_map(Result::ok).collect();
        entries.sort_by_key(|e| e.file_name().to_string_lossy().to_uppercase());
        for entry in entries {
            let Ok(file_type) = entry.file_type() else {
                continue;
            };
            let path = entry.path();
            if file_type.is_dir() {
                if recursive {
                    pending.push_back(path);
                }
            } else if win32_name_matches(pattern, &entry.file_name().to_string_lossy()) {
                result.push(path.to_string_lossy().into_owned());
            }
        }
    }
    result
}

/// Decodes text like `StreamReader.ReadToEnd` (UTF-8 default, BOM detection for UTF-8 and
/// UTF-16 LE/BE, invalid sequences replaced with U+FFFD).
pub fn decode_text(bytes: &[u8]) -> String {
    let utf16 = |rest: &[u8], le: bool| -> String {
        let units: Vec<u16> = rest
            .chunks(2)
            .map(|c| {
                let pair = [c[0], *c.get(1).unwrap_or(&0)];
                if le {
                    u16::from_le_bytes(pair)
                } else {
                    u16::from_be_bytes(pair)
                }
            })
            .collect();
        String::from_utf16_lossy(&units)
    };
    match bytes {
        [0xEF, 0xBB, 0xBF, rest @ ..] => String::from_utf8_lossy(rest).into_owned(),
        [0xFF, 0xFE, rest @ ..] => utf16(rest, true),
        [0xFE, 0xFF, rest @ ..] => utf16(rest, false),
        _ => String::from_utf8_lossy(bytes).into_owned(),
    }
}

impl ClientSource {
    /// `new ClientSource { ... }` + `Open()`. Returns `None` when `Open()` would return false.
    pub fn open(source_type: ClientSourceType, path_name: &str) -> Option<ClientSource> {
        match source_type {
            ClientSourceType::Directory => {
                if Path::new(path_name).is_dir() {
                    Some(ClientSource {
                        source_type,
                        path_name: path_name.to_string(),
                        game_pak: None,
                    })
                } else {
                    None
                }
            }
            ClientSourceType::GamePak => match Pak::open(path_name) {
                Ok(pak) => Some(ClientSource {
                    source_type,
                    path_name: path_name.to_string(),
                    game_pak: Some(pak),
                }),
                Err(e) => {
                    // C# swallows the reason (AAPak.isOpen = false).
                    debug!("Failed to open game_pak {path_name}: {e}");
                    None
                }
            },
        }
    }

    /// `SourceType`.
    pub fn source_type(&self) -> ClientSourceType {
        self.source_type
    }

    /// `PathName`.
    pub fn path_name(&self) -> &str {
        &self.path_name
    }

    /// `GamePak`.
    pub fn game_pak(&self) -> Option<&Pak> {
        self.game_pak.as_ref()
    }

    /// `FileExists`: `File.Exists(Path.Combine(PathName, fileName))` or `GamePak.FileExists`.
    pub fn file_exists(&self, file_name: &str) -> bool {
        match (&self.source_type, &self.game_pak) {
            (ClientSourceType::Directory, _) => {
                Path::new(&self.path_name).join(file_name).is_file()
            }
            (ClientSourceType::GamePak, Some(pak)) => pak.file_exists(file_name),
            (ClientSourceType::GamePak, None) => false,
        }
    }

    /// `GetFileStream` read to the end. `None` on any error (C# throws for directories, returns an
    /// empty stream for a missing pak file).
    pub fn read_file(&self, file_name: &str) -> Option<Vec<u8>> {
        match (&self.source_type, &self.game_pak) {
            (ClientSourceType::Directory, _) => {
                std::fs::read(Path::new(&self.path_name).join(file_name)).ok()
            }
            (ClientSourceType::GamePak, Some(pak)) => pak.read_file(file_name),
            (ClientSourceType::GamePak, None) => None,
        }
    }

    /// `GetFileAsString`: empty string on any error.
    pub fn read_to_string(&self, file_name: &str) -> String {
        self.read_file(file_name)
            .map(|b| decode_text(&b))
            .unwrap_or_default()
    }

    /// `GetFilesInDirectory`.
    ///
    /// * Directory: `Directory.GetFiles(Path.Combine(PathName, directory), searchPattern, ...)`,
    ///   returning full paths (see [`win32_name_matches`] for the pattern semantics).
    /// * GamePak: with `include_sub_directories`, every pak name that starts with the lower-cased
    ///   `directory` (case-insensitive, no trailing separator check) and whose lower-cased name
    ///   matches `WildcardToRegex("*" + pattern.ToLower() + "*")`. Without it, the files of exactly
    ///   that directory (`AAPak.GetFilesInDirectory`) whose *original-case* name matches that regex.
    ///   Pak names are returned as-is. A blank pattern matches everything.
    pub fn files_in_directory(
        &self,
        directory: &str,
        search_pattern: &str,
        include_sub_directories: bool,
    ) -> Vec<String> {
        match (&self.source_type, &self.game_pak) {
            (ClientSourceType::Directory, _) => {
                let root_dir = directory.replace('/', MAIN_SEPARATOR_STR);
                let dir = Path::new(&self.path_name).join(root_dir);
                if dir.is_dir() {
                    directory_get_files(&dir, search_pattern, include_sub_directories)
                } else {
                    Vec::new()
                }
            }
            (ClientSourceType::GamePak, Some(pak)) => {
                let root_dir = directory
                    .replace('/', MAIN_SEPARATOR_STR)
                    .replace(MAIN_SEPARATOR, "/")
                    .to_lowercase();
                let blank = search_pattern.trim().is_empty();
                // "Hopefully this behaves the same as the Directory.GetFiles pattern" (it does not:
                // it is a "contains" match).
                let wild_card = Regex::new(&wildcard_to_regex(&format!(
                    "*{}*",
                    search_pattern.to_lowercase()
                )))
                .ok();
                let matches = |name: &str| -> bool {
                    blank || wild_card.as_ref().is_some_and(|re| re.is_match(name))
                };
                if include_sub_directories {
                    pak.file_names()
                        .filter(|name| name.to_lowercase().starts_with(&root_dir))
                        .filter(|name| matches(&name.to_lowercase()))
                        .map(str::to_string)
                        .collect()
                } else {
                    // NOTE: C# quirk mirrored: here the regex (built from the lower-cased pattern)
                    // is matched against the original-case name, so it is case-sensitive.
                    pak.files_in_directory(&root_dir)
                        .into_iter()
                        .filter(|pfi| matches(&pfi.name))
                        .map(|pfi| pfi.name.clone())
                        .collect()
                }
            }
            (ClientSourceType::GamePak, None) => Vec::new(),
        }
    }

    /// `GetBaseName`: a source-independent name used to de-duplicate file lists.
    /// GamePak: `/` replaced by the OS separator. Directory: the path relative to `PathName`
    /// (leading separators trimmed), or the input unchanged when it is not under `PathName`.
    pub fn get_base_name(&self, file_name: &str) -> String {
        match self.source_type {
            ClientSourceType::GamePak => file_name.replace('/', MAIN_SEPARATOR_STR),
            ClientSourceType::Directory => match file_name.strip_prefix(&self.path_name) {
                Some(rest) => rest.trim_start_matches(MAIN_SEPARATOR).to_string(),
                None => file_name.to_string(),
            },
        }
    }
}

/// `ClientFileManager`: ordered list of client data sources.
#[derive(Debug, Default)]
pub struct ClientFileManager {
    sources: Vec<ClientSource>,
}

impl ClientFileManager {
    /// Creates a manager without sources.
    pub fn new() -> Self {
        Self::default()
    }

    /// `Initialize()`: adds every configured source (`AppConfiguration.ClientData.Sources`),
    /// resolving relative paths against `app_path`.
    pub fn from_sources(sources: &[String], app_path: &Path) -> Self {
        let mut manager = Self::new();
        for source in sources {
            if !manager.add_source(source, app_path) {
                warn!("{source} is not a valid source for client data");
            }
        }
        if manager.sources.is_empty() {
            error!(
                "No valid client sources have been defined or found, some features will not work !"
            );
        }
        manager
    }

    /// `AddSource(pathName)`: adds a directory or game_pak source. Relative paths are resolved
    /// against `base_dir` (C# uses the process working directory via `Path.GetFullPath`).
    pub fn add_source(&mut self, path_name: &str, base_dir: &Path) -> bool {
        if path_name.trim().is_empty() {
            return false;
        }
        let full = full_path(path_name, base_dir);
        let path_name = full.to_string_lossy().into_owned();

        // Source is a directory of unpacked client files ?
        if full.is_dir() {
            if let Some(source) = ClientSource::open(ClientSourceType::Directory, &path_name) {
                let source_type = source.source_type;
                self.sources.push(source);
                info!(
                    "Using Source [{}:{source_type}]: {path_name}",
                    self.sources.len()
                );
                return true;
            }
        }

        // Source is a packed file ?
        if full.is_file() {
            return match ClientSource::open(ClientSourceType::GamePak, &path_name) {
                Some(source) => {
                    let source_type = source.source_type;
                    self.sources.push(source);
                    info!(
                        "Using Source [{}:{source_type}]: {path_name}",
                        self.sources.len()
                    );
                    true
                }
                None => {
                    error!("Source could not be opened, or is invalid: {path_name}");
                    false
                }
            };
        }

        warn!("Client Source not found {path_name}");
        false
    }

    /// `ClearSources()`.
    pub fn clear_sources(&mut self) {
        self.sources.clear();
    }

    /// `Sources`.
    pub fn sources(&self) -> &[ClientSource] {
        &self.sources
    }

    /// `GetFileSource`: first source that has `file_name`.
    pub fn get_file_source(&self, file_name: &str) -> Option<&ClientSource> {
        self.sources.iter().find(|s| s.file_exists(file_name))
    }

    /// `FileExists`.
    pub fn file_exists(&self, file_name: &str) -> bool {
        self.get_file_source(file_name).is_some()
    }

    /// `GetFileStream` read to the end; `None` when no source has the file.
    pub fn read_file(&self, file_name: &str) -> Option<Vec<u8>> {
        match self.get_file_source(file_name) {
            Some(source) => source.read_file(file_name),
            None => {
                trace!("GetFileStream({file_name}) not found");
                None
            }
        }
    }

    /// `GetFileAsString`: empty string when not found or unreadable.
    pub fn read_to_string(&self, file_name: &str) -> String {
        self.get_file_source(file_name)
            .map(|s| s.read_to_string(file_name))
            .unwrap_or_default()
    }

    /// `GetFilesInDirectory`: concatenates every source's list, skipping names whose
    /// [`ClientSource::get_base_name`] was already returned by an earlier source (case-sensitive).
    /// Directory sources yield full paths, pak sources pak names.
    pub fn files_in_directory(
        &self,
        directory: &str,
        search_pattern: &str,
        include_sub_directories: bool,
    ) -> Vec<String> {
        let mut list = Vec::new();
        let mut base_list = std::collections::HashSet::new();
        for source in &self.sources {
            for f_name in
                source.files_in_directory(directory, search_pattern, include_sub_directories)
            {
                let base_name = source.get_base_name(&f_name);
                if base_list.insert(base_name) {
                    list.push(f_name);
                }
            }
        }
        list
    }
}

#[cfg(test)]
mod tests {
    use super::super::pak::test_util::{build_pak, temp_path};
    use super::super::pak::PakFileType;
    use super::*;

    fn write(root: &Path, rel: &str, content: &str) {
        let p = root.join(rel);
        std::fs::create_dir_all(p.parent().unwrap()).unwrap();
        std::fs::write(p, content).unwrap();
    }

    #[test]
    fn wildcard_regex() {
        assert_eq!(wildcard_to_regex("*a.b?*"), r"^.*a\.b..*$");
        let re = Regex::new(&wildcard_to_regex("*world.xml*")).unwrap();
        assert!(re.is_match("game/worlds/x/world.xml"));
        assert!(re.is_match("game/worlds/x/oldworld.xml.bak"));
        assert!(!re.is_match("game/worlds/x/worldaxml"));
    }

    #[test]
    fn win32_patterns() {
        assert!(win32_name_matches("world.xml", "world.xml"));
        assert!(!win32_name_matches("world.xml", "oldworld.xml"));
        assert!(win32_name_matches("*.bai", "a.bai"));
        assert!(!win32_name_matches("*.bai", "a.bai2"));
        assert!(win32_name_matches("*", "anything"));
        assert!(win32_name_matches("*.*", "noext"));
        assert!(win32_name_matches("a?c", "abc"));
        assert!(win32_name_matches("ab?", "ab"));
        assert!(!win32_name_matches("", "ab"));
        assert!(win32_name_matches("*.", "noext"));
        assert!(win32_name_matches("a.*", "a"));
        if cfg!(windows) {
            assert!(win32_name_matches("WORLD.xml", "world.XML"));
        }
    }

    #[test]
    fn full_path_resolution() {
        let base = std::env::temp_dir().join("x").join("y");
        assert_eq!(
            full_path("../z", &base),
            std::env::temp_dir().join("x").join("z")
        );
        assert_eq!(full_path("./ClientData", &base), base.join("ClientData"));
    }

    #[test]
    fn directory_and_pak_sources() {
        let root = temp_path("cfm");
        let client = root.join("ClientData");
        write(
            &client,
            "game/worlds/main_world/world.xml",
            "<World name=\"main_world\"/>",
        );
        write(
            &client,
            "game/worlds/foo/world.xml",
            "<World name=\"foo\"/>",
        );
        write(&client, "game/worlds/foo/other.xml", "x");
        let pak_bytes = build_pak(
            PakFileType::TypeA,
            &[
                ("game/worlds/foo/world.xml", b"<World name=\"pakfoo\"/>"),
                (
                    "game/worlds/bar/world.xml",
                    b"\xEF\xBB\xBF<World name=\"bar\"/>",
                ),
                ("game/worlds/bar/oldworld.xml", b"old"),
            ],
            0,
        );
        std::fs::write(root.join("game_pak"), pak_bytes).unwrap();

        let fm = ClientFileManager::from_sources(
            &[
                "ClientData".to_string(),
                "game_pak".to_string(),
                "missing".to_string(),
                " ".to_string(),
            ],
            &root,
        );
        assert_eq!(fm.sources().len(), 2);
        assert_eq!(fm.sources()[0].source_type(), ClientSourceType::Directory);
        assert_eq!(fm.sources()[1].source_type(), ClientSourceType::GamePak);

        // Directory wins over the pak.
        assert_eq!(
            fm.read_to_string("game/worlds/foo/world.xml"),
            "<World name=\"foo\"/>"
        );
        // BOM is stripped like StreamReader does.
        assert_eq!(
            fm.read_to_string("game/worlds/bar/world.xml"),
            "<World name=\"bar\"/>"
        );
        assert!(fm.file_exists("game/worlds/bar/oldworld.xml"));
        assert!(!fm.file_exists("game/worlds/nope.xml"));
        assert_eq!(fm.read_file("game/worlds/nope.xml"), None);
        assert_eq!(fm.read_to_string("game/worlds/nope.xml"), "");

        let files = fm.files_in_directory("game/worlds", "world.xml", true);
        let sep = MAIN_SEPARATOR;
        let dir_prefix = client.to_string_lossy().into_owned();
        assert_eq!(
            files,
            vec![
                format!("{dir_prefix}{sep}game{sep}worlds{sep}foo{sep}world.xml"),
                format!("{dir_prefix}{sep}game{sep}worlds{sep}main_world{sep}world.xml"),
                // foo/world.xml from the pak is a duplicate base name (on every OS, since the
                // directory source joins with the OS separator).
                "game/worlds/bar/world.xml".to_string(),
                // pak pattern is a "contains" match
                "game/worlds/bar/oldworld.xml".to_string(),
            ]
        );

        // Non-recursive pak lookups are case-sensitive on the name.
        let pak_src = &fm.sources()[1];
        assert_eq!(
            pak_src.files_in_directory("GAME/worlds/bar", "WORLD", false),
            vec!["game/worlds/bar/world.xml", "game/worlds/bar/oldworld.xml"]
        );
        assert_eq!(
            pak_src
                .files_in_directory("game/worlds/bar", "", false)
                .len(),
            2
        );
        assert!(pak_src
            .files_in_directory("game/worlds", "world", false)
            .is_empty());

        std::fs::remove_dir_all(&root).ok();
    }
}
