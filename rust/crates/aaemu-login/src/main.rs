//! `AAEmu.Login` entry point (`Program.Main`).
//!
//! Configuration is read from `Config.json` / `Config.Local.json` in the application directory,
//! then environment variables and command-line arguments. The application directory is
//! `AAEMU_APP_PATH`, else the executable's directory or the current directory when it holds a
//! `Config.json` (published layout), else this crate's `app/` folder (`cargo run` in a source
//! checkout). Logging is controlled with `RUST_LOG` (default `info`).

use std::path::{Path, PathBuf};
use std::time::Instant;

use aaemu_login::config::AppConfig;
use tokio_util::sync::CancellationToken;
use tracing_subscriber::EnvFilter;

fn config_dir() -> PathBuf {
    aaemu_commons::file_manager::resolve_app_dir(&Path::new(env!("CARGO_MANIFEST_DIR")).join("app"))
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")),
        )
        .init();

    let start = Instant::now();
    tracing::info!(
        "{} version {}",
        env!("CARGO_PKG_NAME"),
        env!("CARGO_PKG_VERSION")
    );
    tracing::info!(
        "Running as {}-bits on {} ({})",
        usize::BITS,
        std::env::consts::OS,
        std::env::consts::ARCH
    );
    if usize::BITS < 64 {
        tracing::warn!("Running in 32-bits mode is not recommended due to memory constraints");
    }

    let args: Vec<String> = std::env::args().skip(1).collect();
    let dir = config_dir();
    let config = AppConfig::load(&dir, &args)?;
    tracing::info!("Loaded configuration from {}", dir.display());

    let shutdown = CancellationToken::new();
    tokio::spawn({
        let shutdown = shutdown.clone();
        async move {
            let _ = tokio::signal::ctrl_c().await;
            shutdown.cancel();
        }
    });

    let result = aaemu_login::server::run(config, shutdown).await;
    tracing::info!("Uptime: {} s", start.elapsed().as_secs());
    result
}
