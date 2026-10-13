//! Port of the servers' `NLog.config` (AAEmu.Game / AAEmu.Login).
//!
//! * console: `Info` and above, `HH:mm:ss [LEVEL] logger - message`;
//! * `${basedir}/Logs/Server.log`: `Info` through `Warn`;
//! * `${basedir}/Logs/Error.log`: `Error`.
//!
//! Both files are archived daily as `Server.yyyy-MM-dd.log` / `Error.yyyy-MM-dd.log`, keeping
//! the newest 9 archives (`archiveEvery="Day"`, `maxArchiveFiles="9"`). `RUST_LOG`, when set,
//! replaces the level rules (it filters every target; `Error.log` still only gets errors).
//!
//! Deviation: NLog deletes both files on startup (`deleteOldFileOnStartup`); here the previous
//! run's files move to `Logs/Previous/Server.<last write time>.log` (newest 9 kept), so the log
//! of a crashed run survives the restart. Panics and native crashes are also reported to
//! `Logs/Crash.log` ([`crate::crash`]).
//!
//! Deviation: NLog logs `Debug` to the console and `Trace` to `Server.log`. Both default to
//! `Info` here, because the game server logs every packet at `Debug` / `Trace` and that
//! formatting and I/O dominate the send path with many players in view
//! (`RUST_LOG=info,aaemu_game::net=trace` brings the packet log back). File targets are
//! written by a background thread ([`AsyncFile`]), so logging never blocks a tokio worker on
//! disk I/O; call [`flush`] before the process exits.

use std::fmt;
use std::fs::{self, File, OpenOptions};
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::{self, Receiver, SyncSender, TrySendError};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::Duration;

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
    /// Opens `dir/name.log`, moving a previous one to `dir/Previous/` (see the module docs).
    pub fn new(dir: &Path, name: &str) -> io::Result<Self> {
        fs::create_dir_all(dir)?;
        let path = dir.join(format!("{name}.log"));
        if path.exists() {
            keep_previous(dir, name, &path)?;
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

/// Moves the previous run's `path` to `dir/Previous/name.<last write time>.log` and keeps the
/// newest [`MAX_ARCHIVE_FILES`] of them.
fn keep_previous(dir: &Path, name: &str, path: &Path) -> io::Result<()> {
    let previous = dir.join("Previous");
    fs::create_dir_all(&previous)?;
    let written: chrono::DateTime<Local> = fs::metadata(path)?
        .modified()
        .map(Into::into)
        .unwrap_or_else(|_| Local::now());
    let stamp = written.format("%Y-%m-%d_%H-%M-%S");
    let mut target = previous.join(format!("{name}.{stamp}.log"));
    let mut n = 1;
    while target.exists() {
        target = previous.join(format!("{name}.{stamp}-{n}.log"));
        n += 1;
    }
    if fs::rename(path, &target).is_err() {
        // Still open elsewhere (another server sharing the directory): copy, then truncate.
        fs::copy(path, &target)?;
        OpenOptions::new().write(true).truncate(true).open(path)?;
    }
    let prefix = format!("{name}.");
    let mut kept: Vec<PathBuf> = fs::read_dir(&previous)?
        .flatten()
        .map(|e| e.path())
        .filter(|p| {
            p.file_name()
                .and_then(|n| n.to_str())
                .is_some_and(|n| n.starts_with(&prefix) && n.ends_with(".log"))
        })
        .collect();
    kept.sort();
    while kept.len() > MAX_ARCHIVE_FILES {
        let _ = fs::remove_file(kept.remove(0));
    }
    Ok(())
}

/// Lines queued per file before new ones are dropped (and counted) instead of blocking.
const QUEUED_LINES: usize = 65_536;
/// Bytes collected from the queue into one file write.
const WRITE_BATCH_BYTES: usize = 64 * 1024;

enum LogMsg {
    Line(Vec<u8>),
    Flush(SyncSender<()>),
}

/// A [`DailyFile`] written by its own thread: events are queued (bounded, lossy when full) and
/// written in batches, so the logging thread never waits for the disk.
pub struct AsyncFile {
    tx: SyncSender<LogMsg>,
    dropped: Arc<AtomicU64>,
}

/// Every [`AsyncFile`], for [`flush`].
static FILES: OnceLock<Mutex<Vec<SyncSender<LogMsg>>>> = OnceLock::new();

impl AsyncFile {
    pub fn new(file: DailyFile) -> io::Result<Self> {
        let (tx, rx) = mpsc::sync_channel(QUEUED_LINES);
        let dropped = Arc::new(AtomicU64::new(0));
        let worker_dropped = dropped.clone();
        let name = format!("log-{}", file.name);
        std::thread::Builder::new()
            .name(name)
            .spawn(move || write_loop(file, rx, worker_dropped))?;
        FILES
            .get_or_init(Default::default)
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .push(tx.clone());
        Ok(Self { tx, dropped })
    }

    fn queue(&self, buf: &[u8]) {
        match self.tx.try_send(LogMsg::Line(buf.to_vec())) {
            Ok(()) | Err(TrySendError::Disconnected(_)) => {}
            Err(TrySendError::Full(_)) => {
                self.dropped.fetch_add(1, Ordering::Relaxed);
            }
        }
    }
}

fn write_loop(file: DailyFile, rx: Receiver<LogMsg>, dropped: Arc<AtomicU64>) {
    let mut batch = Vec::with_capacity(WRITE_BATCH_BYTES);
    let mut acks = Vec::new();
    while let Ok(first) = rx.recv() {
        let mut next = Some(first);
        while let Some(msg) = next.take() {
            match msg {
                LogMsg::Line(line) => batch.extend_from_slice(&line),
                LogMsg::Flush(ack) => acks.push(ack),
            }
            if batch.len() < WRITE_BATCH_BYTES {
                next = rx.try_recv().ok();
            }
        }
        let lost = dropped.swap(0, Ordering::Relaxed);
        if lost > 0 {
            batch.extend_from_slice(
                format!(
                    "{} [WARN] logging - {lost} log lines dropped (log queue full)
",
                    Local::now().format("%H:%M:%S")
                )
                .as_bytes(),
            );
        }
        if !batch.is_empty() {
            let _ = file.write_all(&batch);
            batch.clear();
        }
        for ack in acks.drain(..) {
            let _ = ack.send(());
        }
    }
}

/// Waits (up to 2 s) until every line queued so far is written to the log files.
pub fn flush() {
    let Some(files) = FILES.get() else { return };
    let senders = files.lock().unwrap_or_else(|e| e.into_inner()).clone();
    for tx in senders {
        let (ack_tx, ack_rx) = mpsc::sync_channel(1);
        if tx.send(LogMsg::Flush(ack_tx)).is_ok() {
            let _ = ack_rx.recv_timeout(Duration::from_secs(2));
        }
    }
}

/// `MakeWriter` handle over a shared [`AsyncFile`].
#[derive(Clone)]
pub struct DailyWriter(Arc<AsyncFile>);

impl Write for DailyWriter {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        self.0.queue(buf);
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

/// Installs the global subscriber with the NLog targets under `base_dir/Logs`, and the crash
/// reporter ([`crate::crash`]). If the log directory cannot be created, only the console
/// target is installed (and a warning logged).
pub fn init(base_dir: &Path) {
    let env = std::env::var("RUST_LOG").ok().filter(|s| !s.is_empty());
    let filter = |default: LevelFilter| match &env {
        Some(s) => EnvFilter::new(s),
        None => EnvFilter::default().add_directive(default.into()),
    };

    let console = tracing_subscriber::fmt::layer()
        .event_format(NlogFormat)
        .with_filter(filter(LevelFilter::INFO));

    let logs = base_dir.join("Logs");
    let files = DailyFile::new(&logs, "Server")
        .and_then(AsyncFile::new)
        .and_then(|server| {
            DailyFile::new(&logs, "Error")
                .and_then(AsyncFile::new)
                .map(|error| (server, error))
        });
    match files {
        Ok((server, error)) => {
            let server_layer = tracing_subscriber::fmt::layer()
                .event_format(NlogFormat)
                .with_ansi(false)
                .with_writer(DailyWriter(Arc::new(server)))
                .with_filter(filter(LevelFilter::INFO))
                .with_filter(filter_fn(|m| *m.level() != Level::ERROR));
            let error_layer = tracing_subscriber::fmt::layer()
                .event_format(NlogFormat)
                .with_ansi(false)
                .with_writer(DailyWriter(Arc::new(error)))
                .with_filter(filter(LevelFilter::ERROR))
                .with_filter(LevelFilter::ERROR);
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
    crate::crash::install(&logs);
    tracing::info!("Logging to {}", logs.display());
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn startup_keeps_previous_file_and_roll_keeps_nine_archives() {
        let dir = std::env::temp_dir().join(format!("aaemu-log-test-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        fs::write(dir.join("Server.log"), "old").unwrap();
        for d in 1..=12 {
            fs::write(dir.join(format!("Server.2026-01-{d:02}.log")), "a").unwrap();
        }
        let f = DailyFile::new(&dir, "Server").unwrap();
        assert!(!dir.join("Server.log").exists(), "previous run moved away");
        let previous: Vec<_> = fs::read_dir(dir.join("Previous"))
            .unwrap()
            .flatten()
            .collect();
        assert_eq!(previous.len(), 1);
        assert_eq!(fs::read_to_string(previous[0].path()).unwrap(), "old");
        fs::remove_dir_all(dir.join("Previous")).unwrap();
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
