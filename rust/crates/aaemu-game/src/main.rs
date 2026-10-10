//! `aaemu-game [APP_PATH]` — APP_PATH holds `Config.json`, `Configurations/`, `Config.Local.json`
//! and `Data/compact.sqlite3` (defaults to the current directory, the C# server uses its exe dir).

use std::path::PathBuf;
use std::sync::Arc;

use aaemu_game::config::AppConfig;
use aaemu_game::data::GameData;
use aaemu_game::{login, net, GameServer};
use anyhow::Context;
use sqlx::mysql::MySqlPoolOptions;
use tracing_subscriber::EnvFilter;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")),
        )
        .init();
    tracing::info!("Starting daemon: AAEmu.Game (Rust)");

    let app_path = std::env::args()
        .nth(1)
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("."));
    let config = AppConfig::load(&app_path).context("loading configuration")?;

    // Program.cs: "MySQL connection failed, check your configuration!" is fatal.
    let db = MySqlPoolOptions::new()
        .max_connections(16)
        .connect_with(config.connections.mysql_provider.connect_options())
        .await
        .context("MySQL connection failed, check your configuration!")?;

    // Program.cs: "Failed to load compact.sqlite3 database check if it exists!" is fatal, and so
    // is any failing loader (the 1.2 ManagerOrchestrator / GameDataManager are not tolerant).
    let game_data = GameData::load(&config.app_path)
        .context("Failed to load compact.sqlite3 database check if it exists!")?;

    let server = Arc::new(GameServer::new(config, db, game_data));
    tracing::info!("{} packet handlers registered", server.registry.len());

    tokio::spawn(login::run(server.clone()));
    let network = tokio::spawn(net::listener::run(server.clone()));

    tokio::select! {
        res = network => { res.context("network task panicked")?.context("game network")?; }
        _ = tokio::signal::ctrl_c() => tracing::info!("Stopping daemon ..."),
    }
    Ok(())
}
