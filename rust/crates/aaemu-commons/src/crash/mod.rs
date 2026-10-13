//! Crash reports for the servers (not part of the C# NLog setup).
//!
//! * Every panic (also the ones a `catch_unwind` or tokio task swallows) is logged at `ERROR`
//!   with its thread, location and a backtrace, and appended to `Logs/Crash.log`.
//! * On Windows x64, fatal native exceptions (stack overflow, access violation, ...) never reach
//!   the panic machinery. An unhandled-exception filter hands them to a reporter thread that
//!   appends the exception, the unwound stack of the crashing thread (with a recursion summary
//!   for stack overflows) to `Logs/Crash.log` and writes a minidump to `Logs/Crash/`.
//!
//! `Crash.log` is appended to and never deleted on startup, so a report survives a restart.

use std::backtrace::Backtrace;
use std::collections::HashMap;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, OnceLock};

use chrono::Local;

#[cfg(all(windows, target_arch = "x86_64"))]
mod windows;

/// Full reports per panic location; later panics at the same place get one line each.
const FULL_REPORTS_PER_LOCATION: u32 = 3;
/// Minidumps kept in `Logs/Crash/`.
const MAX_DUMPS: usize = 5;

static LOGS_DIR: OnceLock<PathBuf> = OnceLock::new();

/// Installs the panic hook and (Windows x64) the native exception reporter. Reports go to
/// `logs_dir/Crash.log`. Only the first call has an effect.
pub fn install(logs_dir: &Path) {
    if LOGS_DIR.set(logs_dir.to_path_buf()).is_err() {
        return;
    }
    install_panic_hook();
    #[cfg(all(windows, target_arch = "x86_64"))]
    windows::install();
}

fn program_name() -> String {
    std::env::current_exe()
        .ok()
        .and_then(|p| p.file_stem().map(|s| s.to_string_lossy().into_owned()))
        .unwrap_or_else(|| "aaemu".into())
}

/// Appends a report with a timestamped header to `Crash.log`. Never panics.
fn append_report(kind: &str, body: &str) {
    let Some(dir) = LOGS_DIR.get() else { return };
    let _ = fs::create_dir_all(dir);
    let header = format!(
        "==== {} {} {} (pid {}) ====\n",
        Local::now().format("%Y-%m-%d %H:%M:%S%.3f"),
        program_name(),
        kind,
        std::process::id()
    );
    if let Ok(mut f) = OpenOptions::new()
        .create(true)
        .append(true)
        .open(dir.join("Crash.log"))
    {
        let _ = f.write_all(header.as_bytes());
        let _ = f.write_all(body.as_bytes());
        let _ = f.write_all(b"\n\n");
        let _ = f.sync_all();
    }
}

/// `Logs/Crash/<program>_<timestamp>.dmp`, dropping the oldest dumps beyond [`MAX_DUMPS`].
#[cfg_attr(not(all(windows, target_arch = "x86_64")), allow(dead_code))]
fn next_dump_path() -> Option<PathBuf> {
    let dir = LOGS_DIR.get()?.join("Crash");
    fs::create_dir_all(&dir).ok()?;
    let mut dumps: Vec<PathBuf> = fs::read_dir(&dir)
        .into_iter()
        .flatten()
        .flatten()
        .map(|e| e.path())
        .filter(|p| p.extension().is_some_and(|e| e == "dmp"))
        .collect();
    dumps.sort_by_key(|p| fs::metadata(p).and_then(|m| m.modified()).ok());
    while dumps.len() >= MAX_DUMPS {
        let _ = fs::remove_file(dumps.remove(0));
    }
    Some(dir.join(format!(
        "{}_{}.dmp",
        program_name(),
        Local::now().format("%Y-%m-%d_%H-%M-%S")
    )))
}

fn install_panic_hook() {
    static SEEN: OnceLock<Mutex<HashMap<String, u32>>> = OnceLock::new();
    let previous = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        let thread = std::thread::current();
        let name = thread.name().unwrap_or("<unnamed>");
        let payload = info
            .payload()
            .downcast_ref::<&str>()
            .copied()
            .or_else(|| info.payload().downcast_ref::<String>().map(String::as_str))
            .unwrap_or("Box<dyn Any>");
        let location = info
            .location()
            .map(ToString::to_string)
            .unwrap_or_else(|| "<unknown>".into());

        let count = {
            let mut seen = SEEN
                .get_or_init(Default::default)
                .lock()
                .unwrap_or_else(|e| e.into_inner());
            let n = seen.entry(location.clone()).or_insert(0);
            *n += 1;
            *n
        };
        if count <= FULL_REPORTS_PER_LOCATION {
            let report = format!(
                "thread '{name}' panicked at {location}:\n{payload}\nstack backtrace:\n{}",
                Backtrace::force_capture()
            );
            append_report("PANIC", &report);
            tracing::error!("{report}");
        } else {
            // A repeating panic (e.g. one NPC's AI every tick): keep the log readable.
            tracing::error!(
                "thread '{name}' panicked at {location} (#{count}, backtrace in Crash.log for the first {FULL_REPORTS_PER_LOCATION}): {payload}"
            );
        }
        previous(info);
    }));
}
