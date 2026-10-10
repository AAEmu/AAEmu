//! `AAEmu.Login` entry point (`Program.Main`).
//!
//! Configuration is read from `Config.json` / `Config.Local.json` next to the executable (or, if
//! there is no `Config.json` there, from the current directory), then environment variables and
//! command-line arguments. Logging is controlled with `RUST_LOG` (default `info`).

use std::path::PathBuf;
use std::time::Instant;

use aaemu_login::config::AppConfig;
use tokio_util::sync::CancellationToken;
use tracing_subscriber::EnvFilter;

fn config_dir() -> PathBuf {
    let exe_dir = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(PathBuf::from));
    match exe_dir {
        Some(dir) if dir.join("Config.json").exists() => dir,
        _ => std::env::current_dir().unwrap_or_else(|_| PathBuf::from(".")),
    }
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
