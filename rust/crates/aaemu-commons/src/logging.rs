//! Port of the servers' `NLog.config` (AAEmu.Game / AAEmu.Login).
//!
//! * console: `Debug` and above, `HH:mm:ss [LEVEL] logger - message`;
//! * `${basedir}/Logs/Server.log`: `Trace` through `Warn`;
//! * `${basedir}/Logs/Error.log`: `Error`.
//!
//! Both files are deleted on startup (`deleteOldFileOnStartup`) and archived daily as
//! `Server.yyyy-MM-dd.log` / `Error.yyyy-MM-dd.log`, keeping the newest 9 archives
//! (`archiveEvery="Day"`, `maxArchiveFiles="9"`). `RUST_LOG`, when set, replaces the level
//! rules (it filters every target).

use std::fmt;
use std::fs::{self, File, OpenOptions};
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use chrono::{Local, NaiveDate};
use tracing::{Event, Level, Subscriber};
use tracing_subscriber::filter::{filter_fn, EnvFilter, LevelFilter};
use tracing_subscriber::fmt::format::Writer;
use tracing_subscriber::fmt::{FmtContext, FormatEvent, FormatFields, MakeWriter};
use tracing_subscriber::layer::SubscriberExt;
use tracing_subscriber::registry::LookupSpan;
use tracing_subscriber::util::SubscriberInitExt;
use tracing_subscriber::Layer;

/// `maxArchiveFiles`.
pub const MAX_ARCHIVE_FILES: usize = 9;

/// NLog layout `${date:format=HH\:mm\:ss} [${level:uppercase=true}] ${logger:shortName=true} - ${message}`.
struct NlogFormat;

impl<S, N> FormatEvent<S, N> for NlogFormat
where
    S: Subscriber + for<'a> LookupSpan<'a>,
    N: for<'a> FormatFields<'a> + 'static,
{
    fn format_event(
        &self,
        ctx: &FmtContext<'_, S, N>,
        mut writer: Writer<'_>,
        event: &Event<'_>,
    ) -> fmt::Result {
        let meta = event.metadata();
        // `${logger:shortName=true}`: the last path segment of the logger (module) name.
        let logger = meta.target().rsplit("::").next().unwrap_or(meta.target());
        write!(
            writer,
            "{} [{}] {} - ",
            Local::now().format("%H:%M:%S"),
            meta.level().as_str(),
            logger
        )?;
        ctx.field_format().format_fields(writer.by_ref(), event)?;
        writeln!(writer)
    }
}

/// A file target with NLog's daily archiving.
pub struct DailyFile {
    dir: PathBuf,
    name: String,
    state: Mutex<(Option<File>, NaiveDate)>,
}

impl DailyFile {
    /// Opens `dir/name.log`, deleting a previous one (`deleteOldFileOnStartup`).
    pub fn new(dir: &Path, name: &str) -> io::Result<Self> {
        fs::create_dir_all(dir)?;
        let path = dir.join(format!("{name}.log"));
        if path.exists() {
            fs::remove_file(&path)?;
        }
        Ok(Self {
            dir: dir.to_path_buf(),
            name: name.to_string(),
            state: Mutex::new((None, Local::now().date_naive())),
        })
    }

    fn current(&self) -> PathBuf {
        self.dir.join(format!("{}.log", self.name))
    }

    /// Archives the current file under its day and trims old archives.
    fn roll(&self, day: NaiveDate) {
        let archive = self
            .dir
            .join(format!("{}.{}.log", self.name, day.format("%Y-%m-%d")));
        let _ = fs::rename(self.current(), archive);
        let prefix = format!("{}.", self.name);
        let mut archives: Vec<PathBuf> = fs::read_dir(&self.dir)
            .into_iter()
            .flatten()
            .flatten()
            .map(|e| e.path())
            .filter(|p| {
                p.file_name().and_then(|n| n.to_str()).is_some_and(|n| {
                    n.starts_with(&prefix) && n.ends_with(".log") && n.len() == prefix.len() + 14
                })
            })
            .collect();
        archives.sort();
        while archives.len() > MAX_ARCHIVE_FILES {
            let _ = fs::remove_file(archives.remove(0));
        }
    }

    fn write_all(&self, buf: &[u8]) -> io::Result<()> {
        let mut g = self.state.lock().unwrap_or_else(|e| e.into_inner());
        let today = Local::now().date_naive();
        if today != g.1 {
            g.0 = None;
            self.roll(g.1);
            g.1 = today;
        }
        if g.0.is_none() {
            g.0 = Some(
                OpenOptions::new()
                    .create(true)
                    .append(true)
                    .open(self.current())?,
            );
        }
        g.0.as_mut().expect("opened").write_all(buf)
    }
}

/// `MakeWriter` handle over a shared [`DailyFile`].
#[derive(Clone)]
pub struct DailyWriter(Arc<DailyFile>);

impl Write for DailyWriter {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        self.0.write_all(buf)?;
        Ok(buf.len())
    }

    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}

impl<'a> MakeWriter<'a> for DailyWriter {
    type Writer = DailyWriter;

    fn make_writer(&'a self) -> Self::Writer {
        self.clone()
    }
}

/// Installs the global subscriber with the NLog targets under `base_dir/Logs`. If the log
/// directory cannot be created, only the console target is installed (and a warning logged).
pub fn init(base_dir: &Path) {
    let env = std::env::var("RUST_LOG").ok().filter(|s| !s.is_empty());
    let filter = |default: LevelFilter| match &env {
        Some(s) => EnvFilter::new(s),
        None => EnvFilter::default().add_directive(default.into()),
    };

    let console = tracing_subscriber::fmt::layer()
        .event_format(NlogFormat)
        .with_filter(filter(LevelFilter::DEBUG));

    let logs = base_dir.join("Logs");
    let files = DailyFile::new(&logs, "Server")
        .and_then(|server| DailyFile::new(&logs, "Error").map(|error| (server, error)));
    match files {
        Ok((server, error)) => {
            let server_layer = tracing_subscriber::fmt::layer()
                .event_format(NlogFormat)
                .with_ansi(false)
                .with_writer(DailyWriter(Arc::new(server)))
                .with_filter(filter(LevelFilter::TRACE))
                .with_filter(filter_fn(|m| *m.level() != Level::ERROR));
            let error_layer = tracing_subscriber::fmt::layer()
                .event_format(NlogFormat)
                .with_ansi(false)
                .with_writer(DailyWriter(Arc::new(error)))
                .with_filter(filter(LevelFilter::ERROR));
            let _ = tracing_subscriber::registry()
                .with(console)
                .with(server_layer)
                .with(error_layer)
                .try_init();
        }
        Err(e) => {
            let _ = tracing_subscriber::registry().with(console).try_init();
            tracing::warn!("File logging disabled ({}): {e}", logs.display());
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn startup_deletes_old_file_and_roll_keeps_nine_archives() {
        let dir = std::env::temp_dir().join(format!("aaemu-log-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        fs::write(dir.join("Server.log"), "old").unwrap();
        for d in 1..=12 {
            fs::write(dir.join(format!("Server.2026-01-{d:02}.log")), "a").unwrap();
        }
        let f = DailyFile::new(&dir, "Server").unwrap();
        assert!(!dir.join("Server.log").exists(), "deleteOldFileOnStartup");
        f.write_all(b"line\n").unwrap();
        f.roll(NaiveDate::from_ymd_opt(2026, 2, 1).unwrap());
        let mut names: Vec<String> = fs::read_dir(&dir)
            .unwrap()
            .flatten()
            .map(|e| e.file_name().to_string_lossy().into_owned())
            .collect();
        names.sort();
        assert_eq!(names.len(), MAX_ARCHIVE_FILES);
        assert_eq!(names.last().unwrap(), "Server.2026-02-01.log");
        let _ = fs::remove_dir_all(&dir);
    }
}
