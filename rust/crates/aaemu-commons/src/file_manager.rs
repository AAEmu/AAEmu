//! Port of `AAEmu.Commons.IO.FileManager` plus shared path / text helpers.

use std::collections::VecDeque;
use std::fs::OpenOptions;
use std::io::{self, Write};
use std::path::{Path, PathBuf, MAIN_SEPARATOR};
use std::sync::OnceLock;

/// `FileManager.AppPath`: directory of the running executable, **with** a trailing separator.
/// Falls back to the current directory.
pub fn app_path() -> &'static str {
    static APP_PATH: OnceLock<String> = OnceLock::new();
    APP_PATH.get_or_init(|| {
        let dir = std::env::current_exe()
            .ok()
            .and_then(|p| p.parent().map(Path::to_path_buf))
            .or_else(|| std::env::current_dir().ok())
            .unwrap_or_default();
        let mut s = dir.to_string_lossy().into_owned();
        if !s.ends_with(MAIN_SEPARATOR) {
            s.push(MAIN_SEPARATOR);
        }
        s
    })
}

/// `FileManager.DirectorySeparator`.
pub const DIRECTORY_SEPARATOR: char = MAIN_SEPARATOR;

/// `FileManager.SaveFile(string, ...)`: saves UTF-8 text.
pub fn save_file_str(content: &str, file: impl AsRef<Path>, append: bool) -> io::Result<()> {
    save_file(content.as_bytes(), file, append)
}

/// `FileManager.SaveFile(byte[], ...)`: creates the parent directory if needed, then creates /
/// truncates (or appends to) the file. The file name must contain a directory component.
pub fn save_file(content: &[u8], file: impl AsRef<Path>, append: bool) -> io::Result<()> {
    let file = file.as_ref();
    let s = file.to_string_lossy();
    // C#: LastIndexOf(separator) <= 0 -> DirectoryNotFoundException
    let index = s.rfind(MAIN_SEPARATOR).filter(|&i| i > 0).or_else(|| {
        s.rfind(if MAIN_SEPARATOR == '/' { '\\' } else { '/' })
            .filter(|&i| i > 0)
    });
    let Some(index) = index else {
        return Err(io::Error::new(
            io::ErrorKind::NotFound,
            "Directory must be specified for the file",
        ));
    };
    let directory = &s[..index];
    if !directory_exists(directory) {
        create_directory(directory)?;
    }
    let mut writer = OpenOptions::new()
        .write(true)
        .create(true)
        .append(append)
        .truncate(!append)
        .open(file)?;
    writer.write_all(content)
}

/// `FileManager.DirectoryExists`.
pub fn directory_exists(directory: impl AsRef<Path>) -> bool {
    directory.as_ref().is_dir()
}

/// `FileManager.FileExists`.
pub fn file_exists(file: impl AsRef<Path>) -> bool {
    file.as_ref().is_file()
}

/// `FileManager.CreateDirectory` (recursive).
pub fn create_directory(directory: impl AsRef<Path>) -> io::Result<()> {
    std::fs::create_dir_all(directory)
}

/// `FileManager.GetFileContents`: the file as text, or `""` if it doesn't exist. The C#
/// timeout parameter only guarded a loop that could never repeat and is not ported.
pub fn get_file_contents(file: impl AsRef<Path>) -> io::Result<String> {
    let file = file.as_ref();
    if !file_exists(file) {
        return Ok(String::new());
    }
    Ok(decode_text(&std::fs::read(file)?))
}

/// `FileManager.GetMatchingDirectories`: directories under `path` whose name matches the
/// `*`/`?` wildcard `mask`. Empty if `path` doesn't exist.
pub fn get_matching_directories(
    path: impl AsRef<Path>,
    mask: &str,
    recursive: bool,
) -> Vec<PathBuf> {
    walk(path.as_ref(), recursive, true, |p| {
        p.file_name()
            .is_some_and(|n| wildcard_match(mask, &n.to_string_lossy(), FS_IGNORE_CASE))
    })
}

/// `FileManager.GetMatchingFilesInDirectory(path, string mask, ...)`.
pub fn get_matching_files_in_directory(
    path: impl AsRef<Path>,
    mask: &str,
    recursive: bool,
) -> Vec<PathBuf> {
    walk(path.as_ref(), recursive, false, |p| {
        p.file_name()
            .is_some_and(|n| wildcard_match(mask, &n.to_string_lossy(), FS_IGNORE_CASE))
    })
}

/// `FileManager.GetMatchingFilesInDirectory(path, Regex, ...)`: the C# overload takes a
/// `Regex` matched against the full path; here any predicate on the full path is accepted
/// (e.g. `|p| re.is_match(&p.to_string_lossy())`).
pub fn get_matching_files_in_directory_by(
    path: impl AsRef<Path>,
    predicate: impl Fn(&Path) -> bool,
    recursive: bool,
) -> Vec<PathBuf> {
    walk(path.as_ref(), recursive, false, predicate)
}

/// Whether file name matching ignores case on this platform (Windows: yes).
pub const FS_IGNORE_CASE: bool = cfg!(windows);

/// Enumerates `root` like `Directory.GetFiles/GetDirectories`: entries of a directory first
/// (sorted by name), sub-directories queued breadth-first.
fn walk(root: &Path, recursive: bool, dirs: bool, keep: impl Fn(&Path) -> bool) -> Vec<PathBuf> {
    let mut out = Vec::new();
    if !root.is_dir() {
        return out;
    }
    let mut pending = VecDeque::from([root.to_path_buf()]);
    while let Some(dir) = pending.pop_front() {
        let Ok(rd) = std::fs::read_dir(&dir) else {
            continue;
        };
        let mut entries: Vec<_> = rd.filter_map(Result::ok).collect();
        entries.sort_by_key(|e| e.file_name());
        for e in entries {
            let p = e.path();
            let is_dir = e.file_type().map(|t| t.is_dir()).unwrap_or(false);
            if is_dir == dirs && keep(&p) {
                out.push(p.clone());
            }
            if is_dir && recursive {
                pending.push_back(p);
            }
        }
    }
    out
}

/// Matches `text` against a wildcard `pattern` (`*` = any run, `?` = any single char).
/// This is the semantics of the AAEmu `ClientSource.WildcardToRegex` and (minus legacy DOS
/// quirks) of .NET directory search masks.
pub fn wildcard_match(pattern: &str, text: &str, ignore_case: bool) -> bool {
    let fold = |s: &str| -> Vec<char> {
        if ignore_case {
            s.to_lowercase().chars().collect()
        } else {
            s.chars().collect()
        }
    };
    let p = fold(pattern);
    let t = fold(text);
    let (mut pi, mut ti) = (0usize, 0usize);
    let mut star: Option<(usize, usize)> = None;
    while ti < t.len() {
        if pi < p.len() && (p[pi] == '?' || (p[pi] != '*' && p[pi] == t[ti])) {
            pi += 1;
            ti += 1;
        } else if pi < p.len() && p[pi] == '*' {
            star = Some((pi, ti));
            pi += 1;
        } else if let Some((sp, st)) = star {
            pi = sp + 1;
            ti = st + 1;
            star = Some((sp, st + 1));
        } else {
            return false;
        }
    }
    p[pi..].iter().all(|&c| c == '*')
}

/// Decodes bytes the way .NET's `StreamReader` does by default: UTF-8, but honouring a UTF-8,
/// UTF-16 LE/BE or UTF-32 LE byte order mark. Invalid sequences become U+FFFD.
pub fn decode_text(data: &[u8]) -> String {
    let utf16 = |rest: &[u8], be: bool| -> String {
        let units: Vec<u16> = rest
            .as_chunks::<2>()
            .0
            .iter()
            .map(|&c| {
                if be {
                    u16::from_be_bytes(c)
                } else {
                    u16::from_le_bytes(c)
                }
            })
            .collect();
        String::from_utf16_lossy(&units)
    };
    match data {
        [0xEF, 0xBB, 0xBF, rest @ ..] => String::from_utf8_lossy(rest).into_owned(),
        [0xFF, 0xFE, 0x00, 0x00, rest @ ..] => rest
            .as_chunks::<4>()
            .0
            .iter()
            .map(|&c| char::from_u32(u32::from_le_bytes(c)).unwrap_or('\u{FFFD}'))
            .collect(),
        [0xFF, 0xFE, rest @ ..] => utf16(rest, false),
        [0xFE, 0xFF, rest @ ..] => utf16(rest, true),
        _ => String::from_utf8_lossy(data).into_owned(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_util::TempDir;

    #[test]
    fn wildcards() {
        assert!(wildcard_match("*.xml", "world.xml", false));
        assert!(!wildcard_match("*.xml", "world.xmlx", false));
        assert!(wildcard_match(
            "*world.xml*",
            "game/worlds/main_world/world.xml",
            false
        ));
        assert!(wildcard_match("a?c", "abc", false));
        assert!(!wildcard_match("a?c", "ac", false));
        assert!(wildcard_match("*", "", false));
        assert!(wildcard_match(
            "*aaemu_game*.sql",
            "2024_aaemu_game_x.sql",
            false
        ));
        assert!(!wildcard_match("*.XML", "a.xml", false));
        assert!(wildcard_match("*.XML", "a.xml", true));
        assert!(wildcard_match("**a**b", "xxaYYb", false));
    }

    #[test]
    fn text_decoding() {
        assert_eq!(decode_text(b"\xEF\xBB\xBFabc"), "abc");
        assert_eq!(decode_text(&[0xFF, 0xFE, b'h', 0, b'i', 0]), "hi");
        assert_eq!(decode_text(&[0xFE, 0xFF, 0, b'h', 0, b'i']), "hi");
        assert_eq!(decode_text("ü".as_bytes()), "ü");
    }

    #[test]
    fn save_and_enumerate() {
        let dir = TempDir::new("fm");
        let file = dir.path().join("a").join("b").join("x.txt");
        save_file_str("hello", &file, false).unwrap();
        save_file_str(" world", &file, true).unwrap();
        assert_eq!(get_file_contents(&file).unwrap(), "hello world");
        save_file_str("new", &file, false).unwrap();
        assert_eq!(get_file_contents(&file).unwrap(), "new");
        assert_eq!(get_file_contents(dir.path().join("missing")).unwrap(), "");
        assert!(save_file(b"x", "nodir.txt", false).is_err());
        save_file(b"1", dir.path().join("a").join("y.txt"), false).unwrap();
        save_file(b"1", dir.path().join("z.dat"), false).unwrap();

        let all = get_matching_files_in_directory(dir.path(), "*.txt", true);
        let names: Vec<_> = all
            .iter()
            .map(|p| p.file_name().unwrap().to_string_lossy().into_owned())
            .collect();
        assert_eq!(names, ["y.txt", "x.txt"]); // breadth-first
        assert!(get_matching_files_in_directory(dir.path(), "*.txt", false).is_empty());
        assert_eq!(get_matching_directories(dir.path(), "*", true).len(), 2);
        assert_eq!(get_matching_directories(dir.path(), "b", true).len(), 1);
        let by = get_matching_files_in_directory_by(
            dir.path(),
            |p| p.to_string_lossy().ends_with(".dat"),
            true,
        );
        assert_eq!(by.len(), 1);
        assert!(get_matching_files_in_directory(dir.path().join("nope"), "*", true).is_empty());
        assert!(app_path().ends_with(MAIN_SEPARATOR));
    }
}
